using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CustomTypes;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class RaceManager : MonoBehaviour
{
    #region Variables

    private static AppConfig APP_CONFIG => AppConfig.Singleton;
    
    [SerializeField] public SemaphoreController semaphoreController;

    [SerializeField] [HideInInspector] public bool hasFinished;
    [SerializeField] [HideInInspector] public List<NetworkPlayer> waitList = new();
    [SerializeField] [HideInInspector] public List<NetworkPlayer> players = new();
    [SerializeField] [HideInInspector] public CircuitController circuitController;

    [SerializeField] [HideInInspector] private NetworkPlayer[] _cachedPos = new NetworkPlayer[APP_CONFIG.GAME.MAX_PLAYERS_PER_ROOM];
    [SerializeField] [HideInInspector] private GameObject[] _debugSpheres;
    [SerializeField] [HideInInspector] private string _debugRaceOrder;

    private bool IsRacing => UIManager.Instance.State is AppScreen.Game or AppScreen.EndGame;
    
    public static RaceManager Instance { get; private set; }

    #endregion

    #region Delegates and Events

    public delegate void PlayerLeft(NetworkPlayer networkPlayer);
    public event PlayerLeft OnPlayerLeft;

    public void OnPlayerHasFinished(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            var finished = true;
            foreach (var player in players) finished &= player.HasFinished;

            if (finished)
            {
                hasFinished = true;
                GameManager.Instance.State = GameState.Finished;
                StartCoroutine(RaceEndCountdown());
            }
        }
    }
    
    private IEnumerator RaceEndCountdown()
    {
        var playersRacing = waitList.Where(p => p.StartPos != -1).ToList();
        playersRacing.ForEach(p => { if (p.IsOwner) p.IsReady = false; });

        
        UIManager.Instance.SetNotificationCanvas(true, "RACE ENDS IN", "SECONDS");
        
        for (var i = 3; i > 0; i--)
        {
            UIManager.Instance.notificationTime.text = i.ToString();
            semaphoreController.lights[Mathf.Abs(i - 3)].color = Color.red;
            yield return new WaitForSeconds(1);
        }

        UIManager.Instance.SetNotificationCanvas(false);

        players.Clear();

        EventManager.Instance.RaiseScreenChange(AppScreen.Game);
    }

    private void OnGameStateChange(GameState oldState, GameState newState)
    {
        if (newState == GameState.Started) foreach (var player in waitList) AddToPlayers(player);
    }

    #endregion

    #region Unity Callbacks

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        if (circuitController == null) circuitController = GetComponent<CircuitController>();

        _debugSpheres = new GameObject[APP_CONFIG.GAME.MAX_PLAYERS_PER_ROOM];

        for (var i = 0; i < APP_CONFIG.GAME.MAX_PLAYERS_PER_ROOM; ++i)
        {
            _debugSpheres[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _debugSpheres[i].GetComponent<MeshRenderer>().enabled = false;
            _debugSpheres[i].GetComponent<SphereCollider>().enabled = false;
        }

        GameManager.Instance.OnGameStateChange += OnGameStateChange;

        // Debug coroutine
        // StartCoroutine(PrintDebugRaceOrder());
    }

    private void Update()
    {
        if (players.Count > 0 && IsRacing)
            try
            {
                UpdateRaceProgress();
            }
            catch (Exception)
            {
                // Host has left the game
                StartCoroutine(LeaveRace());
            }

        if (waitList.Count > 0 && !AppScreen.Menu.Equals(UIManager.Instance.State))
            try
            {
                // Low-cost operation to test if the host has left the game
                var pos = waitList[^1].car.transform;
            }
            catch (Exception)
            {
                // Host has left the game
                StartCoroutine(LeaveRace());
            }
    }

    #endregion

    #region Update race progress

    private void UpdateRaceProgress()
    {
        // Update car arc-lengths
        var arcLengths = new float[players.Count];

        for (var i = 0; i < players.Count; ++i) arcLengths[i] = ComputeCarArcLength(i);

        var sortedPos = hasFinished
            ? SortPlayersByTime(players.ToArray())
            : SortPlayersByLengths(players.ToArray(), arcLengths);

        var sb = new StringBuilder();
        sb.Append("Race order");

        for (var i = 0; i < sortedPos.Length && !hasFinished; i++)
        {
            if (_cachedPos[i] != sortedPos[i])
            {
                _cachedPos[i] = sortedPos[i];
                sortedPos[i].car.GetComponent<CarController>().SetPlayerTag(i + 1, sortedPos[i].Name);
                if (sortedPos[i].IsOwner) sortedPos[i].StartPos = i;
            }

            if (sortedPos[i].IsOwner) UIManager.Instance.gamePosition.text = $"{i + 1}/{players.Count}";

            sb.Append($"\n{sortedPos[i].Name}\\> {i + 1}º:{sortedPos[i].CurrentPos:F2}" +
                      $"|Lap:{sortedPos[i].CurrentLap}" +
                      $"|Checkpoint:{sortedPos[i].checkpointAchieved}" +
                      $"|RB_K:{sortedPos[i].RubberBandCoefficient:F2}" +
                      $"|Race:{sortedPos[i].CurrentRace}");
        }

        UIManager.Instance.matchSummaryController.UpdateMatchSummary(sortedPos);

        _debugRaceOrder = sb.ToString();
    }

    private float ComputeCarArcLength(int id)
    {
        // Compute the projection of the car position to the closest circuit 
        // path segment and accumulate the arc-length along of the car along
        // the circuit.
        var carPos = players[id].car.transform.position;

        var minArcL = circuitController.ComputeClosestPointArcLength(carPos, out _, out var carProj, out _);

        _debugSpheres[id].transform.position = carProj;
        players[id].projPos = carProj;

        if (players[id].CurrentLap == 0)
            minArcL -= circuitController.CircuitLength;
        else
            minArcL += circuitController.CircuitLength *
                       (players[id].CurrentLap - 1);

        return minArcL;
    }

    // Sorts players by their distances to the finish line.
    private NetworkPlayer[] SortPlayersByLengths(NetworkPlayer[] p, float[] len)
    {
        var playerData = new List<Tuple<NetworkPlayer, float, float>>();

        // Check if player has passed first the checkpoint
        for (var i = 0; i < p.Length; i++)
        {
            var dist = len[i];
            var finishTime = p[i].FinishRawTime;
            if (dist > p[i].lastLapPos + APP_CONFIG.GAME.CHECKPOINT_DISTANCE * 1.05f && !p[i].checkpointAchieved) dist = p[i].lastLapPos;
            playerData.Add(new Tuple<NetworkPlayer, float, float>(p[i], dist, finishTime));
        }

        // Sort players by finish time and distance to finish line, unless no one has finished yet
        var sortedPlayerData = playerData.Any(p => p.Item1.FinishRawTime != 0f) ?
            playerData.OrderByDescending(t => t.Item3)
                .ThenByDescending(t => t.Item2).ToList() : 
            playerData.OrderByDescending(t => t.Item2).ToList();
        
        var sortPlayers = sortedPlayerData.Select(t => t.Item1).ToArray();
        var sortPos = sortedPlayerData.Select(t => t.Item2).ToArray();

        // Update player positions
        for (var i = 0; i < sortPlayers.Length; i++)
            if (sortPlayers[i].IsOwner)
                sortPlayers[i].CurrentPos = sortPos[i];

        // Apply rubber banding and catch-up mechanics
        if (sortPlayers.Length > 1)
        {
            var range = sortPos[1] - sortPos[0];
            var sigmoid = Sigmoid(range);
            sortPlayers[0].RubberBandCoefficient = sigmoid;
            for (var i = sortPlayers.Length - 1; i > 0; i--)
            {
                range = sortPos[0] - sortPos[i];
                sigmoid = Sigmoid(range);
                sortPlayers[i].RubberBandCoefficient = sigmoid;
            }
        }

        return sortPlayers;
    }

    // Sigmoid function to apply rubber banding and catch-up mechanics
    private float Sigmoid(float value, float min = 0.8f, float max = 1.2f)
    {
        // Example Sigmoid values in double precision
        // Sigmoid -45 = 0,9625701546562504
        // Sigmoid -55 = 0,9542946124155942
        // Sigmoid -195 = 0,8429895373265013
        // Sigmoid -205 = 0,8355177436521295
        var s = value switch
        {
            < 50f => 300f,
            < 200f => 290f,
            _ => 270f
        };
        var k = Mathf.Exp(value / s);
        return Mathf.Clamp(k / (1f + k) + 0.5f, min, max);
    }

    private NetworkPlayer[] SortPlayersByTime(NetworkPlayer[] playerList)
    {
        var playerData =
            (from player in playerList let time = player.FinishRawTime select new Tuple<NetworkPlayer, float>(player, time)).ToList();

        var sortedPlayersData = playerData.OrderBy(t => t.Item2).ToList();
        var sortedPlayers = sortedPlayersData.Select(t => t.Item1).ToArray();

        return sortedPlayers;
    }

    private IEnumerator PrintDebugRaceOrder()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f);
            if (GameManager.Instance.State is GameState.Started or GameState.Finished) Debug.Log(_debugRaceOrder);
        }
    }

    #endregion

    #region Race management

    public bool AllPlayersRacing()
    {
        var currentPlayers = waitList.Where(p => p.IsRacing).ToList();
        return currentPlayers.Count > 0;
    }
    
    public void AddToWaitList(NetworkPlayer networkPlayer)
    {
        waitList.Add(networkPlayer);
        UIManager.Instance.debugController.SetRoomPlayers(waitList.Count, players.Count);
    }

    public void RemoveFromWaitList(NetworkPlayer networkPlayer)
    {
        waitList.Remove(networkPlayer);
        if (players.Exists((p) => p.Equals(networkPlayer))) RemoveFromPlayers(networkPlayer);
        UIManager.Instance.debugController.SetRoomPlayers(waitList.Count, players.Count);
    }

    public void AddToPlayers(NetworkPlayer networkPlayer)
    {
        players.Add(networkPlayer);
        UIManager.Instance.debugController.SetRoomPlayers(waitList.Count, players.Count);
    }

    public void RemoveFromPlayers(NetworkPlayer networkPlayer)
    {
        players.Remove(networkPlayer);
        UIManager.Instance.debugController.SetRoomPlayers(waitList.Count, players.Count);
        OnPlayerLeft?.Invoke(networkPlayer);
    }

    public void UpdateRaceState(NetworkPlayer player)
    {
        var currentPlayers = waitList.Where(p => p.IsReady && !p.Equals(player)).ToList();
        var raceStarted = currentPlayers.TrueForAll(p => p.IsReady);
        
        if (currentPlayers.Count > 0 && raceStarted)
        {
            currentPlayers.ForEach(AddToPlayers);
            player.StartPos = currentPlayers.Count + (waitList.Count - (currentPlayers.Count + 1));
            player.CurrentRace = currentPlayers[0].CurrentRace + 1;
            GameManager.Instance.currentRace = player.CurrentRace;
        }
    }
    
    public IEnumerator LeaveRace()
    {
        waitList.Clear();
        players.Clear();

        circuitController.SetNextCircuit(true);

        EventManager.Instance.RaiseScreenChange(AppScreen.Menu);
        GameManager.Instance.State = GameState.Idle;

        UIManager.Instance.chatController.chatBody.text = "";
        UIManager.Instance.debugController.debugConsole.text = "";
        UIManager.Instance.debugController.statFpsCounter.text = "";
        UIManager.Instance.debugController.statRoomProperties.text = "";
        UIManager.Instance.debugController.statRtt.text = "";

        UIManager.Instance.SetNotificationCanvas(true, "ROOM'S BEEN CLOSED");
        for (var i = 3; i > 0; i--) yield return new WaitForSeconds(1);
        UIManager.Instance.SetNotificationCanvas(false);
    }

    #endregion
}
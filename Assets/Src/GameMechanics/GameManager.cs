using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CustomTypes;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class GameManager : MonoBehaviour
{
    #region Enablers, Collisions or Triggers

    private void OnEnable()
    {
        Application.targetFrameRate = 60;
    }

    private void OnDisable()
    {
        Application.targetFrameRate = -1;
    }

    #endregion

    #region Variables
    
    private static AppConfig APP_CONFIG => AppConfig.Singleton;
    
    private const float OFFSET_MULTIPLIER = 12.5f;

    public readonly Vector3 ROOM_POS = new(40f, -25f, -135f);
    public readonly Vector3 CLASSIF_POS = new(10.5f, 0f, -80f);
    public readonly List<Vector3> RACE_POS = new()
    {
        new Vector3(7, 0, -80),
        new Vector3(14, 0, -85),
        new Vector3(7, 0, -90),
        new Vector3(14, 0, -95)
    };
    public readonly List<RaceState> CLASSIF_STATES = new()
    {
        RaceState.Classification1,
        RaceState.Classification2,
        RaceState.Classification3
    };
    public readonly List<RaceState> RACE_STATES = new()
    {
        RaceState.Race1,
        RaceState.Race2,
        RaceState.Race3
    };

    [Header("Game Properties")] [SerializeField]
    public Camera mainCamera;

    [SerializeField] [HideInInspector] public RaceState currentRace = RaceState.Schedule;
    [SerializeField] [HideInInspector] public float raceTime;
    
    [SerializeField] [HideInInspector] private GameState _state = GameState.Idle;
    

    private GameObject _playerPrefab;
    private GameObject _carPrefab;
    private GameObject _rocketPrefab;
    private GameObject _itemBoxPrefab;
    private GameObject m_PrefabInstance;
    private NetworkObject m_SpawnedNetworkObject;
    private List<GameObject> m_ItemboxInstances = new();
    
    public GameState State
    {
        get => _state;
        set
        {
            OnGameStateChange?.Invoke(_state, value);
            _state = value;
        }
    }
    public string PlayerName { get; set; } = "default";
    public int NumLaps { get; set; } = 1;
    
    public static GameManager Instance { get; private set; }

    #endregion

    #region Delegates and Events

    public delegate void GameStateChange(GameState oldState, GameState newState);
    public event GameStateChange OnGameStateChange;

    private void OnClientConnected(ulong obj)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            var id = (int)obj;

            // Spawn player
            m_PrefabInstance = Instantiate(_playerPrefab, GetPlayerPosById(id), Quaternion.identity);
            m_PrefabInstance.GetComponent<NetworkPlayer>().ID = id;
            m_SpawnedNetworkObject = m_PrefabInstance.GetComponent<NetworkObject>();
            m_SpawnedNetworkObject.SpawnAsPlayerObject(obj);

            // Spawn players' car
            m_PrefabInstance = Instantiate(_carPrefab, GetPlayerPosById(id), Quaternion.identity);
            m_SpawnedNetworkObject = m_PrefabInstance.GetComponent<NetworkObject>();
            m_SpawnedNetworkObject.SpawnWithOwnership(obj);
        }
    }
    
    private void OnScreenChange(AppScreen screen)
    {
        mainCamera.cullingMask = !screen.Equals(AppScreen.Game)
            ? LayerMask.GetMask("UI")
            : LayerMask.GetMask("Default", "Player", "UI");
    }

    private void OnStateChange(GameState oldState, GameState newState)
    {
        if (newState == GameState.Started)
        {
            StartCoroutine(RaceTimer());
        }
        
        if (newState == GameState.Finished)
        {
            StopCoroutine(RaceTimer());
            StartCoroutine(ResetTimer());
            if (RACE_STATES.Contains(currentRace)) RaceManager.Instance.circuitController.SetNextCircuit();
        }
    }

    private IEnumerator RaceTimer()
    {
        RaceManager.Instance.hasFinished = false;
        while (!RaceManager.Instance.hasFinished)
        {
            raceTime += Time.deltaTime;
            UIManager.Instance.gameOverallTime.text = ConvertTimeToString(raceTime);
            yield return null;
        }
    }

    private IEnumerator ResetTimer()
    {
        yield return new WaitForSeconds(0.25f);
        raceTime = 0;
    }
    
    public void OnPlayerReady(bool oldValue, bool newValue)
    {
        var allReady = RaceManager.Instance.waitList.Count > 1 &&
                       RaceManager.Instance.waitList.TrueForAll(player => player.IsReady);
        if (allReady) StartCoroutine(RaceStartCountdown());
    }

    private IEnumerator RaceStartCountdown()
    {
        UIManager.Instance.SetNotificationCanvas(true, "RACE STARTS IN", "SECONDS");
        
        for (var i = 3; i > 0; i--)
        {
            UIManager.Instance.notificationTime.text = i.ToString();
            RaceManager.Instance.semaphoreController.lights[Mathf.Abs(i - 3)].color = Color.green;
            yield return new WaitForSeconds(1);
        }
        
        UIManager.Instance.SetNotificationCanvas(false);

        var playersStarting = RaceManager.Instance.waitList.Where(p => p.CurrentRace != RaceState.Schedule).ToList();
        SetCurrentRace(playersStarting);

        if (CLASSIF_STATES.Contains(currentRace)) NumLaps = APP_CONFIG.GAME.LAPS_PER_CLASSIFICATION;
        else if (RACE_STATES.Contains(currentRace)) NumLaps = APP_CONFIG.GAME.LAPS_PER_RACE_EVENT;

        State = GameState.Started;
    }
    
    #endregion

    #region Unity Callbacks

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        State = GameState.Idle;
        
        _playerPrefab = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs[0].Prefab;
        _carPrefab = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs[1].Prefab;
        _rocketPrefab = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs[2].Prefab;
        _itemBoxPrefab = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs[3].Prefab;

        OnGameStateChange += OnStateChange;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        EventManager.Instance.ScreenChange.AddListener(OnScreenChange);
    }

    #endregion

    #region Positioning methods

    public void SpawnRocket(Vector3 position, Quaternion rotation, string playerName)
    {
        // Spawn rocket
        m_PrefabInstance = Instantiate(_rocketPrefab, position, rotation);
        m_PrefabInstance.GetComponent<RocketController>().PlayerName = playerName;
        m_SpawnedNetworkObject = m_PrefabInstance.GetComponent<NetworkObject>();
        m_SpawnedNetworkObject.Spawn();
    }

    public void SpawnItemBox(int num)
    {
        // Spawn item box
        var positions = RaceManager.Instance.circuitController.GetRandomCircuitProjPos(num);
        foreach (var p in positions)
        {
            m_PrefabInstance = Instantiate(_itemBoxPrefab, p, Quaternion.identity);
            m_ItemboxInstances.Add(m_PrefabInstance);
            m_SpawnedNetworkObject = m_PrefabInstance.GetComponent<NetworkObject>();
            m_SpawnedNetworkObject.Spawn();
        }
    }

    public void DespawnItemBox()
    {
        foreach (var instance in m_ItemboxInstances)
            try
            {
                instance.GetComponent<NetworkObject>().Despawn();
            }
            catch (Exception)
            {
                // Object already despawned
            }

        m_ItemboxInstances.Clear();
    }

    #endregion

    #region Setters and getters

    private void SetCurrentRace(List<NetworkPlayer> pStart)
    {
        RaceState state;
        state = pStart.Count == 0 ? RaceState.Classification1 : SetNextState(pStart[0].CurrentRace);

        currentRace = state;
        RaceManager.Instance.waitList.ForEach(p =>
        {
            p.CurrentRace = currentRace;
            p.car.GetComponent<Rigidbody>().isKinematic = false;
        });
    }

    private RaceState SetNextState(RaceState state)
    {
        return (RaceState) (((int) state + 1) % (RaceManager.Instance.circuitController.NumCircuits * 2 + 1));
    }
    
    public string ConvertTimeToString(float time)
    {
        var min = Mathf.Clamp(Mathf.FloorToInt(time / 60), 0, 60);
        var sec = Mathf.Clamp(Mathf.FloorToInt(time % 60), 0, 60);
        var mil = Mathf.Clamp(Mathf.FloorToInt(time % 1 * 1000), 0, 999);
        return $"{min:00}:{sec:00}.{mil:000}";
    }
    
    public Vector3 GetPlayerPosById(int id)
    {
        return ROOM_POS + new Vector3(id * OFFSET_MULTIPLIER, 0, 0);
    }
    
    #endregion
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CustomTypes;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class UIManager : MonoBehaviour
{
    #region Variables

    private static AppConfig APP_CONFIG => AppConfig.Singleton;
    
    [Header("Menu")] [SerializeField] public GameObject menuCanvas;
    [SerializeField] public TMP_InputField menuNickname;
    [SerializeField] public TMP_InputField menuRoomCode;
    [SerializeField] public Button menuJoin;
    [SerializeField] public Button menuHost;

    [Header("Load screen")] [SerializeField]
    public GameObject loadCanvas;
    [SerializeField] public Button loadHide;

    [Header("Room")] [SerializeField] public GameObject roomCanvas;
    [SerializeField] public Image roomCarSkin;
    [SerializeField] public Slider roomColorSlider;
    [SerializeField] public Button roomReady;
    [SerializeField] public Button roomExit;

    [Header("Players Hub")] [SerializeField]
    public GameObject playersHubCanvas;
    [SerializeField] public List<GameObject> playersHubPanels;
    [SerializeField] public List<TMP_Text> playersHubNames;
    
    [Header("Notification")] [SerializeField]
    public GameObject notificationCanvas;
    [SerializeField] public TMP_Text notificationHeader;
    [SerializeField] public TMP_Text notificationTime;
    [SerializeField] public TMP_Text notificationBody;

    [Header("Game")] [SerializeField] public GameObject gameCanvas;
    [SerializeField] public TMP_Text gameTitle;
    [SerializeField] public TMP_Text gameKph;
    [SerializeField] public TMP_Text gamePosition;
    [SerializeField] public TMP_Text gameLaps;
    [SerializeField] public TMP_Text gameOverallTime;
    [SerializeField] public TMP_Text gameLapTime;
    [SerializeField] public Button gameRespawn;
    [SerializeField] public Toggle gameInterpolation;
    [SerializeField] public Button gameExit;

    [Header("End Game")] [SerializeField] public GameObject endGameCanvas;
    
    [SerializeField] [HideInInspector] public MatchSummaryController matchSummaryController;
    [SerializeField] [HideInInspector] public ChatController chatController;
    [SerializeField] [HideInInspector] public DebugController debugController;
    
    [SerializeField] [HideInInspector] private string _joinCode;

    public AppScreen State { get; private set; } = AppScreen.Menu;

    private bool IsAuthenticated => NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer ||
                                    NetworkManager.Singleton.IsHost;

    public static UIManager Instance { get; private set; }

    #endregion

    #region Delegates and Events

    private IEnumerator OnConnection()
    {
        loadCanvas.SetActive(true);

        yield return new WaitUntil(() => NetworkManager.Singleton.IsConnectedClient);
        yield return new WaitForSeconds(1f);
        // wait for Network Objects to finish OnNetworkSpawn and avoid race conditions

        loadCanvas.SetActive(false);

        var mode = NetworkManager.Singleton.IsHost ? "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client";
        debugController.statRoomProperties.text = $"@{_joinCode} @{mode}";

        EventManager.Instance.RaiseScreenChange(State = AppScreen.Room);

        StartCoroutine(debugController.ShowClientRTT());
    }

    private void OnScreenChange(AppScreen screen)
    {
        switch (screen)
        {
            case AppScreen.Menu:
                State = AppScreen.Menu;
                menuCanvas.SetActive(true);
                chatController.chatCanvas.SetActive(false);
                playersHubCanvas.SetActive(false);
                roomCanvas.SetActive(false);
                gameCanvas.SetActive(false);
                endGameCanvas.SetActive(false);
                break;
            case AppScreen.Room:
                State = AppScreen.Room;
                menuCanvas.SetActive(false);
                chatController.chatCanvas.SetActive(true);
                playersHubCanvas.SetActive(true);
                roomCanvas.SetActive(true);
                gameCanvas.SetActive(false);
                endGameCanvas.SetActive(false);
                break;
            case AppScreen.Game:
                State = AppScreen.Game;
                menuCanvas.SetActive(false);
                chatController.chatCanvas.SetActive(true);
                playersHubCanvas.SetActive(true);
                roomCanvas.SetActive(false);
                gameCanvas.SetActive(true);
                endGameCanvas.SetActive(false);
                break;
            case AppScreen.EndGame:
                State = AppScreen.EndGame;
                menuCanvas.SetActive(false);
                chatController.chatCanvas.SetActive(true);
                playersHubCanvas.SetActive(true);
                roomCanvas.SetActive(false);
                gameCanvas.SetActive(false);
                endGameCanvas.SetActive(true);

                gameKph.text = "";
                gamePosition.text = "";
                gameLaps.text = "";
                gameOverallTime.text = "--:--.---";
                gameLapTime.text = "--:--.---";
                break;
        }
    }
    
    private async void OnStartHost()
    {
        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();

        var allocation = await RelayService.Instance.CreateAllocationAsync(APP_CONFIG.GAME.MAX_PLAYERS_PER_ROOM);

        NetworkManager.Singleton.GetComponent<UnityTransport>()
            .SetRelayServerData(new RelayServerData(allocation, "dtls"));

        _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        GameManager.Instance.PlayerName = menuNickname.text;
        menuNickname.text = "";

        NetworkManager.Singleton.StartHost();
    }

    private async void OnStartClient()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();

        _joinCode = menuRoomCode.text;
        menuRoomCode.text = "";

        try
        {
            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(_joinCode);
            NetworkManager.Singleton.GetComponent<UnityTransport>()
                .SetRelayServerData(new RelayServerData(joinAllocation, "dtls"));

            GameManager.Instance.PlayerName = menuNickname.text;
            menuNickname.text = "";

            NetworkManager.Singleton.StartClient();
        }
        catch (Exception)
        {
            // 404 Not Found
            Debug.LogErrorFormat($"StartClient: Room code \"{_joinCode}\" Not Found (404)");
            loadCanvas.SetActive(false);
        }
    }

    private void OnExitClient()
    {
        if (IsAuthenticated) NetworkManager.Singleton.Shutdown();

        StopCoroutine(debugController.ShowClientRTT());

        debugController.statRoomProperties.text = "";
        debugController.statRtt.text = "";
        debugController.statRoomPlayers.text = "";

        StartCoroutine(RaceManager.Instance.LeaveRace());
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
        if (debugController == null) debugController = GetComponent<DebugController>();
        if (matchSummaryController == null) matchSummaryController = GetComponent<MatchSummaryController>();
        if (chatController == null) chatController = GetComponent<ChatController>();

        EventManager.Instance.ScreenChange.AddListener(OnScreenChange);

        loadHide.onClick.AddListener(() => loadCanvas.SetActive(false));

        menuNickname.text = "";
        menuRoomCode.text = "";
        menuJoin.onClick.AddListener(OnStartClient);
        menuJoin.onClick.AddListener(() => StartCoroutine(OnConnection()));
        menuHost.onClick.AddListener(OnStartHost);
        menuHost.onClick.AddListener(() => StartCoroutine(OnConnection()));

        roomColorSlider.value = 0;
        roomReady.onClick.AddListener(StartGame);
        roomExit.onClick.AddListener(OnExitClient);

        notificationHeader.text = "";
        notificationTime.text = "";
        notificationBody.text = "";

        gameKph.text = "";
        gamePosition.text = "";
        gameLaps.text = "";
        gameOverallTime.text = "--:--.---";
        gameLapTime.text = "--:--.---";
        gameExit.onClick.AddListener(OnExitClient);
    }

    #endregion
    
    #region UI properties setters

    public void SetNotificationCanvas(bool active, string header = "", string body = "", string seconds = "")
    {
        notificationHeader.text = header;
        notificationBody.text = body;
        notificationTime.text = seconds;
        notificationCanvas.SetActive(active);
    }
    
    public void SetPlayerHub(int pos, string username, Color color)
    {
        playersHubPanels[pos].SetActive(true);
        playersHubNames[pos].text = username;
        playersHubNames[pos].color = color;
    }

    public void ClearPlayerHub(int pos)
    {
        playersHubPanels[pos].SetActive(false);
        playersHubNames[pos].text = "";
    }

    #endregion

    #region Game Handling

    private void StartGame()
    {
        // If the game has already started, show a notification and return
        if (RaceManager.Instance.AllPlayersRacing()) StartCoroutine(FailedStartGame());
        else EventManager.Instance.RaiseScreenChange(State = AppScreen.Game);
    }

    private IEnumerator FailedStartGame()
    {
        SetNotificationCanvas(true, "GAME ALREADY STARTED", "WILL START THE NEXT RACE\nWAITING...", "SOON");
        
        yield return new WaitForSeconds(3);
        
        SetNotificationCanvas(false);
    }

    #endregion
}
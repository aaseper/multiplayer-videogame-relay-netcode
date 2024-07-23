using System;
using System.Collections;
using Cinemachine;
using CustomTypes;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkPlayer : NetworkBehaviour
{
    #region Variables

    private static AppConfig APP_CONFIG => AppConfig.Singleton;
    
    private static readonly Color
        DEFAULT_COLOR = new(1f, 0.227f, 0.36f, 1f);  // Default color is 'Folly', a reddish pink
    
    [SerializeField] public GameObject playerCameraPrefab;

    [SerializeField] [HideInInspector] public GameObject playerCamera;
    [SerializeField] [HideInInspector] public GameObject car;
    [SerializeField] [HideInInspector] public string carName = "carName";

    [SerializeField] [HideInInspector] public float lastLapPos;
    [SerializeField] [HideInInspector] public Vector3 projPos;
    [SerializeField] [HideInInspector] public bool checkpointAchieved;
    [SerializeField] [HideInInspector] public Vector3 cameraSpawnPos = new(.0f, 3f, -5f);
    [SerializeField] [HideInInspector] public Vector3 cameraSpawnRot = new(14.45f, .0f, 0.0f);
    
    private readonly NetworkVariable<FixedString64Bytes> _networkName = new(value:"_networkName",
        writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<FixedString64Bytes> _networkLocation = new(value:"_networkLocation",
        writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Color> _networkCarColor = new(value:DEFAULT_COLOR,
        writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<int> _networkID = new();
    private readonly NetworkVariable<int> _networkKills = new();
    private readonly NetworkVariable<int> _networkStartPos = new(value:-1, writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<int> _networkDeaths = new(writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<int> _networkRockets = new(writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> _networkCurrentPos = new(writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> _networkFinishTime = new(writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> _networkHasFinished = new(writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> _networkReady = new(writePerm:NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> _networkRacing = new(writePerm:NetworkVariableWritePermission.Owner);
    
    [SerializeField] [HideInInspector] private TMP_InputField _chatMessage;
    [SerializeField] [HideInInspector] private float _rubberBandCoefficient = 1f;  // Catch up coefficient too
    
    public RaceState CurrentRace { get; set; } = RaceState.Schedule;
    public Color CarColor
    {
        get => _networkCarColor.Value;
        private set
        {
            _networkCarColor.Value = value;
            OnColorChangeEvent?.Invoke(value);
        }
    }
    public string Name
    {
        get => _networkName.Value.ToString();
        private set => _networkName.Value = value;
    }
    public string Location
    {
        get => _networkLocation.Value.ToString();
        set => _networkLocation.Value = value;
    }
    public int ID
    {
        get => _networkID.Value;
        set => _networkID.Value = value;
    }
    public int Kills
    {
        get => _networkKills.Value;
        set => _networkKills.Value = value;
    }
    public int Deaths
    {
        get => _networkDeaths.Value;
        set => _networkDeaths.Value = value;
    }
    public int Rockets
    {
        get => _networkRockets.Value;
        set
        {
            _networkRockets.Value = value;
            OnRocketChangeEvent?.Invoke(value);
        }
    }
    public int StartPos
    {
        get => _networkStartPos.Value;
        set => _networkStartPos.Value = value;
    }
    public int CurrentLap { get; private set; }
    public float CurrentPos
    {
        get => _networkCurrentPos.Value;
        set
        {
            _networkCurrentPos.Value = value;
            OnCurrentPosChangeEvent?.Invoke(value);
        }
    }
    public float FinishRawTime
    {
        get => _networkFinishTime.Value;
        set => _networkFinishTime.Value = value;
    }
    public float RubberBandCoefficient
    {
        get => _rubberBandCoefficient;
        set => _rubberBandCoefficient = value;
    }
    public bool IsReady
    {
        get => _networkReady.Value;
        set => _networkReady.Value = value;
    }
    public bool IsRacing
    {
        get => _networkRacing.Value;
        set => _networkRacing.Value = value;
    }
    public bool HasFinished
    {
        get => _networkHasFinished.Value;
        set => _networkHasFinished.Value = value;
    }
    private bool IsInsideCheckpoint => 
        CurrentPos > lastLapPos + APP_CONFIG.GAME.CHECKPOINT_DISTANCE && CurrentPos < lastLapPos + 
        APP_CONFIG.GAME.CHECKPOINT_DISTANCE * 1.1f;

    #endregion

    #region Delegates and Events

    public delegate void OnColorChangeDelegate(Color newVal);
    public event OnColorChangeDelegate OnColorChangeEvent;
    
    public delegate void OnCurrentPosChangeDelegate(float newVal);
    public event OnCurrentPosChangeDelegate OnCurrentPosChangeEvent;
    
    public delegate void OnRocketChangeDelegate(int newVal);
    public event OnRocketChangeDelegate OnRocketChangeEvent;

    public void OnCurrentPosChange(float newVal)
    {
        if (IsInsideCheckpoint)
        {
            checkpointAchieved = true;
            OnCurrentPosChangeEvent -= OnCurrentPosChange;
        }
    }

    private void OnNameChange(FixedString64Bytes oldValue, FixedString64Bytes newValue)
    {
        try
        {
            car.GetComponent<CarController>().SetPlayerTag(-1, newValue.ToString());
        }
        catch (Exception)
        {
            // Car has not been linked yet
        }
    }

    private void OnColorChange(float newValue)
    {
        if (IsOwner)
        {
            // Convert slider value to Hue (0-360) and set the car color
            var hue = newValue * 360f;
            CarColor = Color.HSVToRGB(hue / 360f, 1f, 1f);
            UIManager.Instance.roomCarSkin.color = CarColor;
        }
    }

    private void OnColorChange(Color previousValue, Color newValue)
    {
        try
        {
            car.GetComponent<CarController>().SetMainMeshMaterialColor(newValue);
        }
        catch (Exception)
        {
            // Car has not been linked yet
        }
    }

    public void OnLapsChange(int lap)
    {
        CurrentLap = lap;
    }

    private void OnCarSpawn(int id)
    {
        if (id == ID)
        {
            var vCam = playerCamera.GetComponent<CinemachineVirtualCamera>();
            vCam.Follow = car.transform;
            vCam.LookAt = car.transform;
            EventManager.Instance.CarSpawn.RemoveListener(OnCarSpawn);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void OnMessageSubmitRpc(string message)
    {
        UIManager.Instance.chatController.SubmitChatMessage(Name, Location, message);
    }

    #endregion

    #region Unity Callbacks

    public override void OnNetworkDespawn()
    {
        try
        {
            OnMessageSubmitRpc("has left the room");
            UIManager.Instance.ClearPlayerHub(ID);
            RaceManager.Instance.RemoveFromWaitList(this);
            
            _networkName.OnValueChanged -= OnNameChange;
            _networkCarColor.OnValueChanged -= OnColorChange;
            _networkReady.OnValueChanged -= GameManager.Instance.OnPlayerReady;
            _networkReady.OnValueChanged -= SetPlayerHub;
            _networkHasFinished.OnValueChanged -= RaceManager.Instance.OnPlayerHasFinished;
            UIManager.Instance.roomColorSlider.onValueChanged.RemoveListener(OnColorChange);
            
            if (IsOwner)
            {
                _chatMessage.onSubmit.RemoveListener(OnMessageSubmitRpc);
            }
            
            car.GetComponent<NetworkObject>().Despawn();
        }
        catch (Exception)
        {
            // Game's been closed
        }
    }

    public override void OnNetworkSpawn()
    {
        RaceManager.Instance.AddToWaitList(this);

        _networkName.OnValueChanged += OnNameChange;
        _networkCarColor.OnValueChanged += OnColorChange;
        _networkReady.OnValueChanged += GameManager.Instance.OnPlayerReady;
        _networkReady.OnValueChanged += SetPlayerHub;
        _networkHasFinished.OnValueChanged += RaceManager.Instance.OnPlayerHasFinished;
        UIManager.Instance.roomColorSlider.onValueChanged.AddListener(OnColorChange);
        
        if (IsOwner)
        {
            Name = GameManager.Instance.PlayerName;
            Location = "/room";
            _chatMessage = UIManager.Instance.chatController.chatMessage;
            
            playerCamera = Instantiate(playerCameraPrefab, transform);
            playerCamera.transform.localPosition = cameraSpawnPos;
            playerCamera.transform.localEulerAngles = cameraSpawnRot;

            GetComponent<PlayerInput>().enabled = true;
            GetComponentInChildren<CinemachineVirtualCamera>().enabled = true;

            _chatMessage.onSubmit.AddListener(OnMessageSubmitRpc);
            EventManager.Instance.CarSpawn.AddListener(OnCarSpawn);
            
            UIManager.Instance.chatController.SetPlayerName(Name);
            RaceManager.Instance.UpdateRaceState(this);
        }

        StartCoroutine(SetInitialChatAndHub());
    }

    #endregion

    #region Player properties setters
    
    private void SetPlayerHub(bool oldValue, bool newValue)
    {
        if (newValue) UIManager.Instance.SetPlayerHub(ID, Name, Color.green);
        else UIManager.Instance.SetPlayerHub(ID, Name, Color.red);
    }

    private IEnumerator SetInitialChatAndHub()
    {
        yield return new WaitForSeconds(0.25f);

        if (IsOwner) OnMessageSubmitRpc("has joined the room");
        UIManager.Instance.SetPlayerHub(ID, Name, Color.red);
    }

    #endregion
}
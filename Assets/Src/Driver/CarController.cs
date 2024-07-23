using System;
using System.Collections;
using System.Collections.Generic;
using CustomTypes;
using TMPro;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class CarController : NetworkBehaviour
{
    #region Enablers, Collisions or Triggers

    private void OnTriggerEnter(Collider other)
    {
        // Check if the player has passed the finish line and has not completed the race
        if (other.gameObject.CompareTag("Finish") && Laps <= GameManager.Instance.NumLaps)
        {
            // Check if the player has completed a lap
            if (NetworkPlayer.CurrentLap.Equals(0))
            {
                NetworkPlayer.lastLapPos = NetworkPlayer.CurrentPos;
                NetworkPlayer.OnCurrentPosChangeEvent += NetworkPlayer.OnCurrentPosChange;
                Laps++;
            }
            // Or if the player has advanced to the next checkpoint before completing the lap
            else if (NetworkPlayer.checkpointAchieved)
            {
                NetworkPlayer.checkpointAchieved = false;
                NetworkPlayer.lastLapPos = NetworkPlayer.CurrentPos;
                NetworkPlayer.OnCurrentPosChangeEvent += NetworkPlayer.OnCurrentPosChange;
                Laps++;
            }
        }
    }

    #endregion

    #region Variables

    private static AppConfig APP_CONFIG => AppConfig.Singleton;
    
    private const float STEER_HELPER = 0.8f;
    
    [Header("Car Properties")] [SerializeField]
    public string carName;

    [SerializeField] public Material translucentMaterial;
    [SerializeField] public GameObject PlayerPanel;
    [SerializeField] public GameObject RocketPanel;
    [SerializeField] public TMP_Text playerTag;
    [SerializeField] public TMP_Text rocketTag;
    [SerializeField] public List<MeshRenderer> carMeshes;

    private readonly NetworkVariable<int> _networkSpeed = new();
    private readonly NetworkVariable<PosAndRotNetworkData> _networkData = new();
    // private readonly NetworkVariable<float> _networkWheelYRot = new();
    
    private List<Material[]> _originalMaterials;

    [SerializeField] [HideInInspector] private int _laps;
    [SerializeField] [HideInInspector] private float _lapTime;
    [SerializeField] [HideInInspector] private Vector3 _vel;
    [SerializeField] [HideInInspector] private Vector3 _velRot;
    [SerializeField] [HideInInspector] private bool _isOnTrack;
    [SerializeField] [HideInInspector] private Rigidbody _rigidbody;

    [Header("Movement")] [SerializeField] public List<AxleInfo> axleInfos;

    [SerializeField] [HideInInspector] public float inputAcceleration;
    [SerializeField] [HideInInspector] public float inputSteering;
    [SerializeField] [HideInInspector] public float inputBrake;
    [SerializeField] [HideInInspector] private float currentRotation;
    
    [SerializeField] [HideInInspector] private float _forwardMotorTorque = 100000f;
    [SerializeField] [HideInInspector] private float _backwardMotorTorque = 50000f;
    [SerializeField] [HideInInspector] private float _maxSteeringAngle = 15f;
    [SerializeField] [HideInInspector] private float _engineBrake = 1e+12f;
    [SerializeField] [HideInInspector] private float _footBrake = 1e+24f;
    [SerializeField] [HideInInspector] private float _topSpeed = 200f;
    [SerializeField] [HideInInspector] private float _downForce = 350f;
    [SerializeField] [HideInInspector] private float _slipLimit = 0.2f;
    [SerializeField] [HideInInspector] private float _forwardMotorTorqueRB = 100000f;
    [SerializeField] [HideInInspector] private float _backwardMotorTorqueRB = 50000f;

    public NetworkPlayer NetworkPlayer { get; private set; }
    public CarState State { get; private set; }
    public int ID { get; private set; }
    public bool RubberBand { get; private set; } = true;
    public bool Interpolation { get; private set; } = true;
    
    private int Laps
    {
        get => _laps;
        set
        {
            _laps = value;
            OnLapsChangeEvent?.Invoke(value);
        }
    }
    private int Speed
    {
        get => _networkSpeed.Value;
        set => _networkSpeed.Value = value;
    }
    private bool IsRacing => State != CarState.Dead && State != CarState.Idle;
    private bool IsRace => GameManager.Instance.CLASSIF_STATES.Contains(NetworkPlayer.CurrentRace);
    private bool IsClassif => GameManager.Instance.RACE_STATES.Contains(NetworkPlayer.CurrentRace);

    #endregion Variables

    #region Delegates and Events

    public delegate void OnLapsChangeDelegate(int newVal);

    public event OnLapsChangeDelegate OnLapsChangeEvent;

    private void OnSpeedChange(int oldVal, int newVal)
    {
        UIManager.Instance.gameKph.text = $"{newVal:0}";
    }

    private void OnLapsChange(int newVal)
    {
        if (newVal == 1) _lapTime = 0f;

        // Get the lap time and display it
        _lapTime = GameManager.Instance.raceTime - _lapTime;
        UIManager.Instance.gameLapTime.text = GameManager.Instance.ConvertTimeToString(_lapTime);

        // Check if the player has finished the race
        if (newVal.Equals(GameManager.Instance.NumLaps + 1)) SetPlayerEndGame();
        // Or update the lap counter
        else if (newVal <= GameManager.Instance.NumLaps + 1)
            UIManager.Instance.gameLaps.text = $"{newVal}/{GameManager.Instance.NumLaps}";
    }

    private void OnPlayerLeft(NetworkPlayer networkPlayer)
    {
        // Win by default if the player is the only one left
        if (NetworkPlayer.Equals(networkPlayer)) return;
        if (RaceManager.Instance.players.Count == 1) SetPlayerEndGame();
    }

    private void OnScreenChange(AppScreen screen)
    {
        if (screen.Equals(AppScreen.Game))
        {
            if (IsServer && IsRace) GameManager.Instance.SpawnItemBox(APP_CONFIG.GAME.ITEM_BOXES_PER_RACE);
            else if (IsServer && IsClassif) GameManager.Instance.DespawnItemBox();

            if (IsClassif || NetworkPlayer.CurrentRace.Equals(RaceState.Schedule))
            {
                UIManager.Instance.gameTitle.text = "Classification";
                RocketPanel.SetActive(false);
                SwitchToInvisibleExceptMeRpc();
                MoveToPositionRpc(GameManager.Instance.CLASSIF_POS);
            }
            else
            {
                UIManager.Instance.gameTitle.text = "Race";
                RocketPanel.SetActive(true);
                SwitchVisibilityRpc();
                MoveToPositionRpc(GameManager.Instance.RACE_POS[NetworkPlayer.StartPos]);
            }

            ResetStatsRpc();

            NetworkPlayer.Location = "/game";
            NetworkPlayer.FinishRawTime = 0f;
            NetworkPlayer.Rockets = 0;
            NetworkPlayer.HasFinished = false;
            UIManager.Instance.SetNotificationCanvas(true, "WAITING FOR PLAYERS");
            UIManager.Instance.gameLaps.text = "";
            UIManager.Instance.matchSummaryController.HasFinished = false;
            OnRocketChange(NetworkPlayer.Rockets);
            StartCoroutine(CheckIsOnTrack());
            
            NetworkPlayer.IsReady = true;
        }
        
        if (screen.Equals(AppScreen.Room)) NetworkPlayer.Location = "/room";
    }

    [Rpc(SendTo.Everyone)]
    private void MoveToPositionRpc(Vector3 pos)
    {
        _rigidbody.isKinematic = true;
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    }

    [Rpc(SendTo.Everyone)]
    private void ResetStatsRpc()
    {
        Laps = 0;
        NetworkPlayer.lastLapPos = 0f;
        NetworkPlayer.checkpointAchieved = false;
        NetworkPlayer.RubberBandCoefficient = 1f;
    }
    
    private void OnRocketChange(int newVal)
    {
        rocketTag.text = newVal.ToString();
    }
    
    private void OnGameStateChange(GameState oldState, GameState newState)
    {
        if (newState.Equals(GameState.Started))
        {
            if (IsOwner) NetworkPlayer.IsRacing = true;
            State = CarState.Vulnerable;
            _rigidbody.isKinematic = false;
        }
    }
    
    public void OnRocketHit()
    {
        StartCoroutine(InitiateDeath());
        StartCoroutine(SwitchVulnerability(3.25f, toVulnerable: false));
        StartCoroutine(SwitchVulnerability(8f));
    }

    #endregion

    #region Unity Callbacks

    public override void OnNetworkDespawn()
    {
        OnLapsChangeEvent -= NetworkPlayer.OnLapsChange;
        GameManager.Instance.OnGameStateChange -= OnGameStateChange;
                
        if (IsOwner)
        {
            NetworkPlayer.OnRocketChangeEvent -= OnRocketChange;
            _networkSpeed.OnValueChanged -= OnSpeedChange;
            OnLapsChangeEvent -= OnLapsChange;
            RaceManager.Instance.OnPlayerLeft -= OnPlayerLeft;
            UIManager.Instance.gameRespawn.onClick.RemoveListener(RespawnInProjPosRpc);
            UIManager.Instance.gameInterpolation.onValueChanged.RemoveListener(value => Interpolation = value);
            EventManager.Instance.ScreenChange.RemoveListener(OnScreenChange);
        }
    }

    public override void OnNetworkSpawn()
    {
        State = CarState.Idle;

        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.isKinematic = true;

        _originalMaterials = new List<Material[]>();
        foreach (var mesh in carMeshes) _originalMaterials.Add(mesh.materials);

        var networkObjects = FindObjectsOfType<NetworkObject>();
        foreach (var networkObject in networkObjects)
            // Find the car owner to assign the player to the car
            if (networkObject.IsPlayerObject && networkObject.OwnerClientId == OwnerClientId)
            {
                NetworkPlayer = networkObject.gameObject.GetComponent<NetworkPlayer>();

                NetworkPlayer.car = gameObject;
                NetworkPlayer.carName = carName;
                ID = NetworkPlayer.ID;

                OnLapsChangeEvent += NetworkPlayer.OnLapsChange;
                GameManager.Instance.OnGameStateChange += OnGameStateChange;
                
                if (IsOwner)
                {
                    RocketPanel.SetActive(true);
                    NetworkPlayer.OnRocketChangeEvent += OnRocketChange;
                    _networkSpeed.OnValueChanged += OnSpeedChange;
                    OnLapsChangeEvent += OnLapsChange;
                    RaceManager.Instance.OnPlayerLeft += OnPlayerLeft;
                    UIManager.Instance.gameRespawn.onClick.AddListener(RespawnInProjPosRpc);
                    UIManager.Instance.gameInterpolation.onValueChanged.AddListener(value => Interpolation = value);
                    EventManager.Instance.ScreenChange.AddListener(OnScreenChange);
                }

                // Set default values
                SetPlayerTag(-1, NetworkPlayer.Name);
                OnRocketChange(NetworkPlayer.Rockets);
                SetMainMeshMaterialColor(NetworkPlayer.CarColor);
                EventManager.Instance.RaisePlayersCarFound(ID);
            }

        if (NetworkPlayer == null) throw new Exception("Player not found!");
    }

    public void FixedUpdate()
    {
        // If the car is the server, move the car
        if (IsServer)
        {
            if (!_rigidbody.isKinematic)
            {
                inputSteering = Mathf.Clamp(inputSteering, -1, 1);
                inputAcceleration = Mathf.Clamp(inputAcceleration, -1, 1);
                inputBrake = Mathf.Clamp(inputBrake, 0, 1);

                var steering = _maxSteeringAngle * inputSteering;

                foreach (var axleInfo in axleInfos)
                {
                    if (axleInfo.steering)
                    {
                        axleInfo.leftWheel.steerAngle = steering;
                        axleInfo.rightWheel.steerAngle = steering;
                    }

                    if (axleInfo.motor)
                    {
                        if (inputAcceleration > float.Epsilon)
                        {
                            _forwardMotorTorqueRB = RubberBand
                                ? _forwardMotorTorque * NetworkPlayer.RubberBandCoefficient
                                : _forwardMotorTorque;
                            axleInfo.leftWheel.motorTorque = _forwardMotorTorqueRB;
                            axleInfo.leftWheel.brakeTorque = 0f;
                            axleInfo.rightWheel.motorTorque = _forwardMotorTorqueRB;
                            axleInfo.rightWheel.brakeTorque = 0f;
                        }

                        if (inputAcceleration < -float.Epsilon)
                        {
                            _backwardMotorTorqueRB = RubberBand
                                ? -_backwardMotorTorque * NetworkPlayer.RubberBandCoefficient
                                : -_backwardMotorTorque;
                            axleInfo.leftWheel.motorTorque = _backwardMotorTorqueRB;
                            axleInfo.leftWheel.brakeTorque = 0f;
                            axleInfo.rightWheel.motorTorque = _backwardMotorTorqueRB;
                            axleInfo.rightWheel.brakeTorque = 0f;
                        }

                        if (Math.Abs(inputAcceleration) < float.Epsilon)
                        {
                            axleInfo.leftWheel.motorTorque = 0f;
                            axleInfo.leftWheel.brakeTorque = _engineBrake;
                            axleInfo.rightWheel.motorTorque = 0f;
                            axleInfo.rightWheel.brakeTorque = _engineBrake;
                        }

                        if (inputBrake > 0f)
                        {
                            axleInfo.leftWheel.brakeTorque = _footBrake;
                            axleInfo.rightWheel.brakeTorque = _footBrake;
                        }
                    }

                    ApplyLocalPositionToVisuals(axleInfo.leftWheel);
                    ApplyLocalPositionToVisuals(axleInfo.rightWheel);
                }

                SteerHelper();
                SpeedLimiter();
                AddDownForce();
                TractionControl();

                // Save the car position and rotation for the client
                _networkData.Value = new PosAndRotNetworkData()
                {
                    Position = transform.position,
                    Rotation = transform.rotation.eulerAngles
                };

                /*
                 * TODO: rotate the wheels in the client
                 * This approach is not working properly, the wheels are not rotating around the y-axis as expected
                 *
                Quaternion rot;
                axleInfos[0].rightWheel.GetWorldPose(out _, out rot);
                _networkWheelYRot.Value = rot.eulerAngles.y;
                 */
            }
            // If the car is kinematic, reset the car position and rotation: it may give random values
            else
            {
                _networkData.Value = new PosAndRotNetworkData()
                {
                    Position = Vector3.zero,
                    Rotation = Vector3.zero
                };

                // _networkWheelYRot.Value = 0f;
            }
        }
        // If the car is in the client, interpolate the car position and rotation
        else if (!_rigidbody.isKinematic && !(_networkData.Value.Position == Vector3.zero))
        {
            if (Interpolation)
            {
                var targetPosition =
                    Vector3.SmoothDamp(transform.position, _networkData.Value.Position, ref _vel, APP_CONFIG.GAME.SMOOTH_INTERPOLATION_TIME);
                var targetRotation = Quaternion.Euler(
                    Mathf.SmoothDampAngle(transform.eulerAngles.x, _networkData.Value.Rotation.x, ref _velRot.x,
                        APP_CONFIG.GAME.SMOOTH_INTERPOLATION_TIME),
                    Mathf.SmoothDampAngle(transform.eulerAngles.y, _networkData.Value.Rotation.y, ref _velRot.y,
                        APP_CONFIG.GAME.SMOOTH_INTERPOLATION_TIME),
                    Mathf.SmoothDampAngle(transform.eulerAngles.z, _networkData.Value.Rotation.z, ref _velRot.z,
                        APP_CONFIG.GAME.SMOOTH_INTERPOLATION_TIME));

                transform.position = targetPosition;
                transform.rotation = targetRotation;

                /*
                 * TODO: rotate the wheels in the client
                 * This approach is not working properly, the wheels are not rotating around the y-axis as expected
                 *
                axleInfos[0].rightWheel.transform.rotation = Quaternion.Euler(0f, _networkWheelYRot.Value, 0f);
                axleInfos[0].leftWheel.transform.rotation = Quaternion.Euler(0f, _networkWheelYRot.Value, 0f);
                 */
            }
            else
            {
                transform.position = _networkData.Value.Position;
                transform.rotation = Quaternion.Euler(_networkData.Value.Rotation);
            }
        }
    }
    
    public void Update()
    {
        if (IsServer && IsSpawned)
        {
            var iSpeed = Mathf.FloorToInt(_rigidbody.velocity.magnitude);
            if (iSpeed != Speed) Speed = iSpeed;
        }
    }

    #endregion

    #region States

    public IEnumerator SwitchVulnerability(float time, bool toVulnerable = true)
    {
        yield return new WaitForSeconds(time);
        
        State = toVulnerable ? CarState.Vulnerable : CarState.Invincible;
        _rigidbody.excludeLayers = toVulnerable ? 0 : LayerMask.GetMask("Player");

        if (toVulnerable)
        {
            for (var i = 0; i < carMeshes.Count; i++) carMeshes[i].materials = _originalMaterials[i];
        }
        else
        {
            foreach (var mesh in carMeshes)
            {
                var materials = mesh.materials;
                for (var i = 0; i < materials.Length; i++) materials[i] = translucentMaterial;
                mesh.materials = materials;
            }

            StartCoroutine(ShineWhileInvincible());
        }
    }

    // Car meshes will shine and transparency will change while the car is invincible.
    private IEnumerator ShineWhileInvincible()
    {
        const float DURATION = 0.25f;
        const float MAX_ALPHA = 0.85f;
        const float MIN_ALPHA = 0.15f;

        while (CarState.Invincible.Equals(State))
        {
            var newAlpha = MIN_ALPHA + (MAX_ALPHA - MIN_ALPHA) * Mathf.Abs(Mathf.Sin(Time.time / DURATION));

            foreach (var mesh in carMeshes)
            {
                var materials = mesh.materials;
                foreach (var material in materials)
                {
                    var color = material.color;
                    color.a = newAlpha;
                    material.color = color;
                }

                mesh.materials = materials;
            }

            yield return null;
        }

        foreach (var mesh in carMeshes)
        {
            var materials = mesh.materials;
            foreach (var material in materials)
            {
                var color = material.color;
                color.a = MAX_ALPHA;
                material.color = color;
            }

            mesh.materials = materials;
        }
    }

    [Rpc(SendTo.Everyone)]
    private void SwitchVisibilityRpc(bool toVisible = true)
    {
        PlayerPanel.SetActive(toVisible);
        _rigidbody.excludeLayers = toVisible ? 0: LayerMask.GetMask("Player");
        foreach (var mesh in carMeshes) mesh.enabled = toVisible;
    }

    [Rpc(SendTo.NotMe)]
    private void SwitchToInvisibleExceptMeRpc()
    {
        PlayerPanel.SetActive(false);
        _rigidbody.excludeLayers = LayerMask.GetMask("Player");
        foreach (var mesh in carMeshes) mesh.enabled = false;
    }

    #endregion

    #region Car properties setters

    private void SetPlayerEndGame()
    {
        // Set player end game state adn disable UI elements
        State = CarState.Idle;
        StopCoroutine(CheckIsOnTrack());
        MoveToPositionRpc(GameManager.Instance.GetPlayerPosById(ID));

        NetworkPlayer.FinishRawTime = GameManager.Instance.raceTime;
        NetworkPlayer.HasFinished = true;
        UIManager.Instance.gameOverallTime.text = "--:--.---";
        UIManager.Instance.gameLapTime.text = "--:--.---";
        UIManager.Instance.matchSummaryController.HasFinished = true;
        EventManager.Instance.RaiseScreenChange(AppScreen.EndGame);
    }
    
    public void SetMainMeshMaterialColor(Color color)
    {
        carMeshes[0].materials[1].color = color;
    }

    public void SetPlayerTag(int pos, string playerName)
    {
        playerTag.text = pos == -1 ? $"Ready | {NetworkPlayer.Name}" : $"{pos} | {playerName}";
    }

    #endregion
    
    #region Death

    private IEnumerator InitiateDeath()
    {
        State = CarState.Dead;
        _rigidbody.isKinematic = true;
        SwitchVisibilityRpc(toVisible: false);
        RespawnInProjPosRpc();

        if (IsOwner)
        {
            NetworkPlayer.Deaths++;
            UIManager.Instance.SetNotificationCanvas(true, "YOU DIED", "SECONDS UNTIL REAPPEARANCE");
        }

        // Wait for 3 seconds before switching to visible
        for (var i = 3; i > 0; i--)
        {
            if (IsOwner) UIManager.Instance.notificationTime.text = $"{i}";
            yield return new WaitForSeconds(1);
        }

        if (IsOwner) UIManager.Instance.SetNotificationCanvas(false);

        _rigidbody.isKinematic = false;
        SwitchVisibilityRpc();
    }

    [Rpc(SendTo.Server)]
    private void RespawnInProjPosRpc()
    {
        transform.position = NetworkPlayer.projPos == Vector3.zero ? transform.position : NetworkPlayer.projPos;
        transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
    }

    #endregion

    #region IsOnTrack

    /// <summary>
    /// Checks if the car is not out track.
    /// </summary>
    private IEnumerator CheckIsOnTrack()
    {
        var cachedIsOnTrack = true;

        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            _isOnTrack = IsOnTrack();
            if (!_isOnTrack && !cachedIsOnTrack && IsRacing)
            {
                if (IsOwner)
                    UIManager.Instance.SetNotificationCanvas(true, "YOU ARE OUT OF TRACK", "SECONDS UNTIL RESPAWN");

                for (var i = 3; i > 0 && !IsOnTrack() && IsRacing; i--)
                {
                    if (IsOwner) UIManager.Instance.notificationTime.text = $"{i}";
                    yield return new WaitForSeconds(1);
                }

                if (IsRacing)
                {
                    if (IsOwner) UIManager.Instance.SetNotificationCanvas(false);
                    if (!IsOnTrack()) RespawnInProjPosRpc();
                }
            }

            cachedIsOnTrack = _isOnTrack;
        }
    }

    private bool IsOnTrack()
    {
        var isOnTrack = true;
        var cachedIsOnTrack = false;

        foreach (var axleInfo in axleInfos)
        {
            isOnTrack = cachedIsOnTrack || IsNotOffPiste(axleInfo.leftWheel) || IsNotOffPiste(axleInfo.rightWheel);
            cachedIsOnTrack = isOnTrack;
        }

        return isOnTrack;
    }

    private bool IsNotOffPiste(WheelCollider wheel)
    {
        var hit = new WheelHit();

        try
        {
            wheel.GetGroundHit(out hit);
            if (hit.collider.CompareTag("Off-piste")) return false;
        }
        catch (Exception)
        {
            // car is not on the ground
            return true;
        }

        return true;
    }

    #endregion

    #region Movement

    // Crude traction control that reduces the power to wheel if the car is wheel spinning too much
    private void TractionControl()
    {
        foreach (var axleInfo in axleInfos)
        {
            WheelHit wheelHitLeft;
            WheelHit wheelHitRight;
            axleInfo.leftWheel.GetGroundHit(out wheelHitLeft);
            axleInfo.rightWheel.GetGroundHit(out wheelHitRight);

            if (wheelHitLeft.forwardSlip >= _slipLimit)
            {
                var howMuchSlip = (wheelHitLeft.forwardSlip - _slipLimit) / (1 - _slipLimit);
                axleInfo.leftWheel.motorTorque -= axleInfo.leftWheel.motorTorque * howMuchSlip * _slipLimit;
            }

            if (wheelHitRight.forwardSlip >= _slipLimit)
            {
                var howMuchSlip = (wheelHitRight.forwardSlip - _slipLimit) / (1 - _slipLimit);
                axleInfo.rightWheel.motorTorque -= axleInfo.rightWheel.motorTorque * howMuchSlip * _slipLimit;
            }
        }
    }

    // This is used to add more grip in relation to speed
    private void AddDownForce()
    {
        foreach (var axleInfo in axleInfos)
            axleInfo.leftWheel.attachedRigidbody.AddForce(
                -transform.up * (_downForce * axleInfo.leftWheel.attachedRigidbody.velocity.magnitude));
    }

    private void SpeedLimiter()
    {
        var speed = _rigidbody.velocity.magnitude;
        if (speed > _topSpeed)
            _rigidbody.velocity = _topSpeed * _rigidbody.velocity.normalized;
    }

    // Finds the corresponding visual wheel and correctly applies the transform
    private void ApplyLocalPositionToVisuals(WheelCollider col)
    {
        if (col.transform.childCount == 0) return;

        var visualWheel = col.transform.GetChild(0);

        Vector3 position;
        Quaternion rotation;
        col.GetWorldPose(out position, out rotation);
        var myTransform = visualWheel.transform;
        myTransform.position = position;
        myTransform.rotation = rotation;
    }

    private void SteerHelper()
    {
        foreach (var axleInfo in axleInfos)
        {
            var wheelHit = new WheelHit[2];
            axleInfo.leftWheel.GetGroundHit(out wheelHit[0]);
            axleInfo.rightWheel.GetGroundHit(out wheelHit[1]);
            foreach (var wh in wheelHit)
                if (wh.normal == Vector3.zero)
                    return;  // Wheels aren't on the ground so don't realign the rigidbody velocity
        }

        // This if is needed to avoid gimbal lock problems that will make the car suddenly shift direction
        if (Mathf.Abs(currentRotation - transform.eulerAngles.y) < 10f)
        {
            var turnAdjust = (transform.eulerAngles.y - currentRotation) * STEER_HELPER;
            var velRotation = Quaternion.AngleAxis(turnAdjust, Vector3.up);
            _rigidbody.velocity = velRotation * _rigidbody.velocity;
        }

        currentRotation = transform.eulerAngles.y;
    }

    #endregion
}
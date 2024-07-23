using System;
using System.Collections;
using CustomTypes;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class RocketController : NetworkBehaviour
{
    #region Enablers, Collisions or Triggers

    private void OnCollisionEnter()
    {
        Explode();
    }

    private void OnTriggerEnter(Collider other)
    {
        var car = other.gameObject.GetComponent<CarController>();
        if (other.gameObject.CompareTag("Player") && car.State is CarState.Vulnerable)
        {
            if (IsServer) StartCoroutine(UpdateKills());
            car.OnRocketHit();
        }
    }

    #endregion

    #region Variables

    private static AppConfig APP_CONFIG => AppConfig.Singleton;
    
    private const int TRAJECTORY_DURATION = 1;
    private const int SPEED = 3;
    private const int START_ROTATION = 65;
    private const int END_ROTATION = 115;
    private const int TRAJECTORY_LENGTH = 60;

    private readonly NetworkVariable<FixedString64Bytes> _playerName = new("default");
    private readonly NetworkVariable<PosAndRotNetworkData> _networkData = new();
    
    [SerializeField] [HideInInspector] private Rigidbody _rigidbody;
    [SerializeField] [HideInInspector] private Vector3 _vel;
    [SerializeField] [HideInInspector] private Vector3 _velRot;
    [SerializeField] [HideInInspector] private Vector3 _endPosition;
    [SerializeField] [HideInInspector] private Vector3 _startPosition;
    [SerializeField] [HideInInspector] private Quaternion _endRotation;
    [SerializeField] [HideInInspector] private Quaternion _startRotation;
    [SerializeField] [HideInInspector] private float _elapsedTime;
    [SerializeField] [HideInInspector] private bool _hasCollision;

    public string PlayerName
    {
        get => _playerName.Value.ToString();
        set => _playerName.Value = value;
    }

    private bool NameIsDefault => PlayerName.Equals("default");

    #endregion

    #region Unity Callbacks

    public override void OnNetworkSpawn()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.isKinematic = false;

        _startRotation = Quaternion.Euler(START_ROTATION,
            transform.rotation.eulerAngles.y,
            transform.rotation.eulerAngles.z);
        _endRotation = Quaternion.Euler(END_ROTATION,
            transform.rotation.eulerAngles.y,
            transform.rotation.eulerAngles.z);

        _startPosition = transform.position;
        _endPosition = transform.position + transform.forward * TRAJECTORY_LENGTH;

        Invoke(nameof(Explode), 1f);
    }

    private void FixedUpdate()
    {
        if (!_hasCollision && IsServer)
        {
            transform.rotation = Quaternion.Lerp(_startRotation, _endRotation, _elapsedTime / TRAJECTORY_DURATION);
            transform.Rotate(Vector3.up, 360 * Time.deltaTime * SPEED, Space.Self);
            transform.position = Vector3.Lerp(_startPosition, _endPosition, _elapsedTime / TRAJECTORY_DURATION);

            _elapsedTime += Time.deltaTime;

            _networkData.Value = new PosAndRotNetworkData()
            {
                Position = transform.position,
                Rotation = transform.rotation.eulerAngles
            };
        }
        else if (!_hasCollision && !_rigidbody.isKinematic)
        {
            var targetPosition =
                Vector3.SmoothDamp(transform.position, _networkData.Value.Position, ref _vel, APP_CONFIG.GAME.SMOOTH_INTERPOLATION_TIME);
            transform.position = targetPosition;

            var targetRotation = Quaternion.Euler(
                Mathf.SmoothDampAngle(transform.eulerAngles.x, _networkData.Value.Rotation.x, ref _velRot.x,
                    APP_CONFIG.GAME.SMOOTH_INTERPOLATION_TIME),
                Mathf.SmoothDampAngle(transform.eulerAngles.y, _networkData.Value.Rotation.y, ref _velRot.y,
                    APP_CONFIG.GAME.SMOOTH_INTERPOLATION_TIME),
                Mathf.SmoothDampAngle(transform.eulerAngles.z, _networkData.Value.Rotation.z, ref _velRot.z,
                    APP_CONFIG.GAME.SMOOTH_INTERPOLATION_TIME));
            transform.rotation = targetRotation;
        }
    }

    #endregion

    #region Rocket logic

    private IEnumerator UpdateKills()
    {
        yield return new WaitUntil(() => !NameIsDefault);

        var player = RaceManager.Instance.players.Find(p => p.Name == PlayerName);
        player.Kills++;
    }

    private void Explode()
    {
        if (!_hasCollision)
        {
            _hasCollision = true;

            GetComponent<SphereCollider>().enabled = true;
            GetComponent<MeshRenderer>().enabled = false;
            GetComponentInChildren<ParticleSystem>().Play();

            StartCoroutine(Disappear());
        }
    }

    private IEnumerator Disappear()
    {
        yield return new WaitForSeconds(0.25f);

        GetComponent<SphereCollider>().enabled = false;
        GetComponent<BoxCollider>().enabled = false;

        yield return new WaitUntil(() => !NameIsDefault);
        yield return new WaitForSeconds(5);

        DespawnRocketRpc();
    }

    [Rpc(SendTo.Server)]
    private void DespawnRocketRpc()
    {
        NetworkObject.Despawn();
    }

    #endregion
}
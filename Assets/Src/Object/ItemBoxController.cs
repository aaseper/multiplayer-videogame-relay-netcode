using System;
using CustomTypes;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class ItemBoxController : NetworkBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        var car = other.gameObject.GetComponent<CarController>();
        if (other.gameObject.CompareTag("Player") && car.IsOwner)
        {
            car.NetworkPlayer.Rockets++;
            DespawnItemBoxRpc();
        }
    }

    private static AppConfig APP_CONFIG => AppConfig.Singleton;

    private readonly NetworkVariable<PosAndRotNetworkData> _networkData = new();

    [SerializeField] [HideInInspector] private Vector3 _vel;
    [SerializeField] [HideInInspector] private Vector3 _velRot;

    [Rpc(SendTo.Server)]
    private void DespawnItemBoxRpc()
    {
        NetworkObject.Despawn();
    }

    private void FixedUpdate()
    {
        if (IsServer)
        {
            // Rotate the item box around all its axes
            transform.Rotate(new Vector3(15, 30, 45) * Time.deltaTime);

            _networkData.Value = new PosAndRotNetworkData()
            {
                Position = transform.position,
                Rotation = transform.rotation.eulerAngles
            };
        }
        else
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
}
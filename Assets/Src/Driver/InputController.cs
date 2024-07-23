using System;
using CustomTypes;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[Serializable]
public class InputController : NetworkBehaviour
{
    [SerializeField] [HideInInspector] private CarController _carController;
    
    private void OnCarSpawn(int id)
    {
        var player = GetComponent<NetworkPlayer>();
        if (id == player.ID)
        {
            _carController = player.car.GetComponent<CarController>();
            EventManager.Instance.CarSpawn.RemoveListener(OnCarSpawn);
        }
    }

    public override void OnNetworkSpawn()
    {
        EventManager.Instance.CarSpawn.AddListener(OnCarSpawn);
    }
    
    public void OnMove(InputAction.CallbackContext context)
    {
        OnMoveRpc(context.ReadValue<Vector2>());
    }

    [Rpc(SendTo.Server)]
    private void OnMoveRpc(Vector2 input)
    {
        _carController.inputAcceleration = input.y;
        _carController.inputSteering = input.x;
    }

    public void OnBrake(InputAction.CallbackContext context)
    {
        OnBrakeRpc(context.ReadValue<float>());
    }

    [Rpc(SendTo.Server)]
    public void OnBrakeRpc(float input)
    {
        _carController.inputBrake = input;
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.performed && GetComponent<NetworkPlayer>().Rockets > 0 && _carController.State != CarState.Idle &&
            _carController.State != CarState.Dead)
        {
            GetComponent<NetworkPlayer>().Rockets--;
            OnAttackRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void OnAttackRpc()
    {
        // Spawn rocket above the car and aiming the car forward
        var spawnPos = _carController.transform.position + new Vector3(0, 2);
        var spawnRot = _carController.transform.rotation;

        GameManager.Instance.SpawnRocket(spawnPos, spawnRot, GetComponent<NetworkPlayer>().Name);
    }

    public void OnSummary(InputAction.CallbackContext context)
    {
        if (context.performed && GameManager.Instance.State == GameState.Started)
            EventManager.Instance.RaiseSummaryDisplay();
        else if (context.canceled && GameManager.Instance.State == GameState.Started)
            EventManager.Instance.RaiseSummaryHid();
    }

    public void OnWrite(InputAction.CallbackContext context)
    {
        if (context.performed) UIManager.Instance.chatController.WriteChatMessage();
    }
}
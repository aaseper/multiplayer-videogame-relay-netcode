using System;
using UnityEngine;

[Serializable]
public class AntiRollBar : MonoBehaviour
{
    private const float ANTI_ROLL = 5000f;
    
    [SerializeField] public WheelCollider wheelLeft, wheelRight;

    [SerializeField] [HideInInspector] private Rigidbody _carRigidbody;

    private void Awake()
    {
        _carRigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        var hit = new WheelHit();
        var travelL = 1.0f;
        var travelR = 1.0f;

        var groundedL = wheelLeft.GetGroundHit(out hit);

        if (groundedL)
            travelL = (-wheelLeft.transform.InverseTransformPoint(hit.point).y - wheelLeft.radius) /
                      wheelLeft.suspensionDistance;

        var groundedR = wheelRight.GetGroundHit(out hit);

        if (groundedR)
            travelR = (-wheelRight.transform.InverseTransformPoint(hit.point).y - wheelRight.radius) /
                      wheelRight.suspensionDistance;

        var antiRollForce = (travelL - travelR) * ANTI_ROLL;

        if (groundedL)
            _carRigidbody.AddForceAtPosition(wheelLeft.transform.up * -antiRollForce, wheelLeft.transform.position);
        if (groundedR)
            _carRigidbody.AddForceAtPosition(wheelRight.transform.up * antiRollForce, wheelRight.transform.position);
    }
}
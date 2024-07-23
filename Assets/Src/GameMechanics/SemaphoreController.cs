using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SemaphoreController : MonoBehaviour
{
    [Header("Lights")] [SerializeField] public List<Light> lights;

    private void Awake()
    {
        foreach (var led in lights) led.color = Color.red;
    }
}
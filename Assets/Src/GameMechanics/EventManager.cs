using System;
using CustomTypes;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class EventManager : MonoBehaviour
{
    [SerializeField] [HideInInspector] public UnityEvent<int> CarSpawn = new();
    [SerializeField] [HideInInspector] public UnityEvent<AppScreen> ScreenChange = new();
    [SerializeField] [HideInInspector] public UnityEvent SummaryDisplay = new();
    [SerializeField] [HideInInspector] public UnityEvent SummaryHid = new();

    public static EventManager Instance { get; private set; }

    public void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void RaisePlayersCarFound(int id)
    {
        CarSpawn?.Invoke(id);
    }

    public void RaiseScreenChange(AppScreen screen)
    {
        ScreenChange?.Invoke(screen);
    }

    public void RaiseSummaryDisplay()
    {
        SummaryDisplay?.Invoke();
    }

    public void RaiseSummaryHid()
    {
        SummaryHid?.Invoke();
    }
}
using System;
using System.Collections.Generic;
using CustomTypes;
using TMPro;
using UnityEngine;

[Serializable]
public class MatchSummaryController : MonoBehaviour
{
    private static AppConfig APP_CONFIG => AppConfig.Singleton;
    
    [Header("Match Summary")] [SerializeField]
    public GameObject Classification;

    [SerializeField] public List<TMP_Text> playerZeroStats;
    [SerializeField] public List<TMP_Text> playerOneStats;
    [SerializeField] public List<TMP_Text> playerTwoStats;
    [SerializeField] public List<TMP_Text> playerThreeStats;
    
    private List<List<TMP_Text>> _playerStats;

    [SerializeField] [HideInInspector] private bool _hasFinished;

    public bool HasFinished
    {
        get => _hasFinished;
        set
        {
            _hasFinished = value;
            IsSummaryDisplayed = value;
        }
    }
    public bool IsSummaryDisplayed
    {
        get => Classification.activeSelf;
        private set => Classification.SetActive(value);
    }
    
    private void OnSummaryDisplay()
    {
        if (!HasFinished) Classification.SetActive(true);
    }

    private void OnSummaryHid()
    {
        if (!HasFinished) Classification.SetActive(false);
    }
    
    private void Awake()
    {
        _playerStats = new List<List<TMP_Text>>
        {
            playerZeroStats,
            playerOneStats,
            playerTwoStats,
            playerThreeStats
        };
    }

    private void Start()
    {
        EventManager.Instance.SummaryDisplay.AddListener(OnSummaryDisplay);
        EventManager.Instance.SummaryHid.AddListener(OnSummaryHid);
    }
    
    public void UpdateMatchSummary(NetworkPlayer[] players)
    {
        for (var i = 0; i < APP_CONFIG.GAME.MAX_PLAYERS_PER_ROOM; i++)
            // If there are players to update
            if (players.Length > i) SetPlayerStats(i, players[i]);
            // If there are no players to update
            else SetPlayerStats(i);
    }

    private void SetPlayerStats(int pos, NetworkPlayer p = null)
    {
        var isNull = p == null;
        
        _playerStats[pos][0].text = isNull ? "" : $"{pos + 1}º {p.Name}";
        _playerStats[pos][1].text = isNull ? "" : p.carName;
        
        if (isNull) _playerStats[pos][2].text = "--:--.---";
        else _playerStats[pos][2].text = p.HasFinished ?
            GameManager.Instance.ConvertTimeToString(p.FinishRawTime)
            : "--:--.---"; 
        
        _playerStats[pos][3].text = isNull ? "" : p.Kills.ToString();
        _playerStats[pos][4].text = isNull ? "" : p.Deaths.ToString();
    }
}
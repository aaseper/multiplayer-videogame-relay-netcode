using System;
using System.Collections;
using System.Collections.Generic;
using CustomTypes;
using TMPro;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class DebugController : MonoBehaviour
{
    private void OnEnable()
    {
        Application.logMessageReceived += Log;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= Log;
    }
    
    private static AppConfig APP_CONFIG => AppConfig.Singleton;

    private const int AVERAGE_FPS = 30;
    private const int MAX_CACHE_FPS = 300;
    
    [Header("Debug")] [SerializeField] public TMP_Text debugConsole;

    [Header("Stats")] [SerializeField] public TMP_Text statFpsCounter;
    [SerializeField] public TMP_Text statRoomProperties;
    [SerializeField] public TMP_Text statRtt;
    [SerializeField] public TMP_Text statRoomPlayers;

    private Dictionary<int, string> _cachedNumberStrings = new();

    [SerializeField] [HideInInspector] private int _averageCounter;
    [SerializeField] [HideInInspector] private int _currentAveraged;
    [SerializeField] [HideInInspector] private int[] _frameRateSamples;

    private void Awake()
    {
        // Cache strings and create array
        for (var i = 0; i < MAX_CACHE_FPS; i++) _cachedNumberStrings[i] = i.ToString();

        _frameRateSamples = new int[AVERAGE_FPS];

        debugConsole.text = "";

        statFpsCounter.text = "";
        statRoomProperties.text = "";
        statRtt.text = "";
    }

    private void Update()
    {
        var currentFrame = (int)Mathf.Round(1f / Time.smoothDeltaTime);
        _frameRateSamples[_averageCounter] = currentFrame;

        var average = 0f;

        foreach (var frameRate in _frameRateSamples) average += frameRate;

        _currentAveraged = (int)Mathf.Round(average / AVERAGE_FPS);
        _averageCounter = (_averageCounter + 1) % AVERAGE_FPS;

        try
        {
            statFpsCounter.text = _currentAveraged switch
            {
                var x when x >= 0 && x < MAX_CACHE_FPS => _cachedNumberStrings[x],
                var x when x >= MAX_CACHE_FPS => $"> {MAX_CACHE_FPS}",
                var x when x < 0 => "0",
                _ => "?"
            } + " FPS";
        }
        catch (Exception)
        {
            // lack of memory/no framerate
            statFpsCounter.text = "0 FPS";
        }
    }

    private void Log(string logString, string stackTrace, LogType type)
    {
        if (type != LogType.Warning) debugConsole.text = $"{type}: {logString}\n{debugConsole.text}";
    }

    public IEnumerator ShowClientRTT()
    {
        while (true)
        {
            try
            {
                var currentRtt = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(APP_CONFIG.GAME.SERVER_ID);
                statRtt.text = $"{currentRtt.ToString()}ms Ping";
            }
            catch (Exception)
            {
                // Game is not running
            }

            yield return new WaitForSeconds(3);
        }
    }

    public void SetRoomPlayers(int players, int racing)
    {
        statRoomPlayers.text = $"{players}/{APP_CONFIG.GAME.MAX_PLAYERS_PER_ROOM} @Players " +
                               $"{racing}/{APP_CONFIG.GAME.MAX_PLAYERS_PER_ROOM} @Racing";
    }
}
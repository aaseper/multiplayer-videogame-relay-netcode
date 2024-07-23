using System;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;

namespace CustomTypes
{
    // This class is used to access the most relevant const values of the game from a single place
    // It is expandable, and it can be exported to a JSON file
    [Serializable]
    public class GameConfig
    {
        public int ITEM_BOXES_PER_RACE = 10;
        public int CHECKPOINT_DISTANCE = 200;
        public int LAPS_PER_CLASSIFICATION = 1;
        public int LAPS_PER_RACE_EVENT = 2;
        public int MAX_PLAYERS_PER_ROOM = 4;
        public ulong SERVER_ID;
        public float SMOOTH_INTERPOLATION_TIME = 0.1f;
        
        public string Stringify()
        {
            return JsonUtility.ToJson(this);
        }

        public static GameConfig Parse(string json)
        {
            return JsonUtility.FromJson<GameConfig>(json);
        }
    }
    
    [Serializable]
    public class AppConfig
    {
        [SerializeField] [HideInInspector] public GameConfig GAME = new(); 
        
        private static AppConfig singleton;

        public static AppConfig Singleton
        {
            get { return singleton ??= new AppConfig(); }
        }
    }
}
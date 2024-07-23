using System;

namespace CustomTypes
{
    [Serializable]
    public enum AppScreen
    {
        Menu,
        Room,
        Game,
        EndGame
    }

    [Serializable]
    public enum CarState
    {
        Idle,
        Vulnerable,
        Dead,
        Invincible
    }

    [Serializable]
    public enum GameState
    {
        Idle,
        Started,
        Finished
    }

    [Serializable]
    public enum RaceState
    {
        Schedule,
        Classification1,
        Race1,
        Classification2,
        Race2,
        Classification3,
        Race3
    }
}
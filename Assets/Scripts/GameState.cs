using System;
using UnityEngine;

public enum GameMode { Menu, Playing }

/// <summary>Shared game state for an endless run: mode, pause, score, time, player health.</summary>
public static class GameState
{
    public const string TopLayer = "Layer 3";   // sorting layer above the whole map
    public const string ActorLayer = "Layer 1"; // same sorting layer as the player

    public static GameMode Mode = GameMode.Menu;
    public static bool Paused;
    public static bool Over;

    public static int Score, Kills;
    public static float RunTime;
    public static int PlayerHp = 5, PlayerMaxHp = 5;
    public static float RunStartedAt;
    public static int RunId;                 // co-op: bumps every time a run starts

    /// <summary>True while the run is actually being played (not in a menu, paused, or dead).</summary>
    public static bool Active { get { return Mode == GameMode.Playing && !Paused && !Over; } }

    public static event Action OnRunStart;      // a new run begins (player respawns at the entrance)
    public static event Action OnReturnToMenu;  // back to the main menu

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Mode = GameMode.Menu; Paused = false; Over = false;
        Score = Kills = 0; RunTime = 0f; PlayerHp = PlayerMaxHp = 5;
        OnRunStart = null; OnReturnToMenu = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        GameSettings.Load();
        Time.timeScale = 0f;       // sit in the main menu until PLAY
        MusicPlayer.Start();
    }

    public static void AddScore(int n) { Score += Difficulty.Frenzy ? n * 2 : n; Kills++; }
    public static void Heal(int n) { PlayerHp = Mathf.Min(PlayerMaxHp, PlayerHp + n); }

    public static void StartRun()
    {
        Over = false; Paused = false; RunId++;
        Score = 0; Kills = 0; RunTime = 0f;
        PlayerHp = PlayerMaxHp;
        Mode = GameMode.Playing;
        RunStartedAt = Time.unscaledTime;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (OnRunStart != null) OnRunStart();
    }

    public static void ReturnToMenu(bool submitScore)
    {
        if (submitScore && !Tutorial.Active && Mode == GameMode.Playing && !Over && !Coop.IsGuest) { Leaderboard.Submit(Score, RunTime); Net.QueueScore(Score, Mathf.FloorToInt(RunTime), Kills); }
        Mode = GameMode.Menu; Paused = false; Over = false;
        Time.timeScale = 0f;
        AudioListener.pause = false;
        if (OnReturnToMenu != null) OnReturnToMenu();
    }

    public static void SetPaused(bool pause)
    {
        if (Mode != GameMode.Playing) return;
        if (Coop.InRun) { Paused = false; Time.timeScale = 1f; AudioListener.pause = false; return; }   // co-op never freezes the world: your partner is still playing
        Paused = pause;
        Time.timeScale = pause ? 0f : 1f;
        AudioListener.pause = pause;
    }

    /// <summary>Co-op guest: the host's team died.</summary>
    public static void RemoteOver()
    {
        if (Over) return;
        Over = true;
        Sfx.Play("gameover", 0.9f, 1f);
    }

    public static void GameOver()
    {
        if (Over) return;
        Over = true;
        if (!Tutorial.Active) { Leaderboard.Submit(Score, RunTime); Net.QueueScore(Score, Mathf.FloorToInt(RunTime), Kills); }
        Sfx.Play("gameover", 0.9f, 1f);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>Temporary leaderboard: this session's runs only (no demo data) (resets when the game closes).</summary>
public static class Leaderboard
{
    public struct Entry
    {
        public string name; public int score; public float seconds; public bool mine;
    }

    static readonly List<Entry> entries = new List<Entry>();
    static bool seeded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { entries.Clear(); seeded = false; best = -1; LastWasBest = false; }

    static void Seed() { }

    // ---- personal best (saved on this device, merged with the server's best after login) ----
    static int best = -1;
    public static bool LastWasBest;
    public static int Best { get { if (best < 0) best = PlayerPrefs.GetInt("rr_best", 0); return best; } }
    public static int Total { get { return PlayerPrefs.GetInt("rr_total", 0); } }
    public static int Runs { get { return PlayerPrefs.GetInt("rr_runs", 0); } }
    public static void RaiseBest(int s) { if (s > Best) { best = s; PlayerPrefs.SetInt("rr_best", s); PlayerPrefs.Save(); } }

    static void Add(string name, int score, float seconds, bool mine)
    {
        entries.Add(new Entry { name = name, score = score, seconds = seconds, mine = mine });
        entries.Sort((a, b) => b.score.CompareTo(a.score));
    }

    public static void Submit(int score, float seconds)
    {
        Seed();
        LastWasBest = score > Best;
        RaiseBest(score);
        if (score > 0) { PlayerPrefs.SetInt("rr_total", Total + score); PlayerPrefs.SetInt("rr_runs", Runs + 1); PlayerPrefs.Save(); }
        if (score <= 0) return;
        Add(GameSettings.ProfileName.Length == 0 ? "PLAYER" : GameSettings.ProfileName, score, seconds, true);
    }

    public static List<Entry> Top(int count)
    {
        Seed();
        return entries.GetRange(0, Mathf.Min(count, entries.Count));
    }
}

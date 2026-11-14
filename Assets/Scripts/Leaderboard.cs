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
    static void ResetStatics() { entries.Clear(); seeded = false; }

    static void Seed() { }

    static void Add(string name, int score, float seconds, bool mine)
    {
        entries.Add(new Entry { name = name, score = score, seconds = seconds, mine = mine });
        entries.Sort((a, b) => b.score.CompareTo(a.score));
    }

    public static void Submit(int score, float seconds)
    {
        Seed();
        if (score <= 0) return;
        Add(GameSettings.ProfileName.Length == 0 ? "PLAYER" : GameSettings.ProfileName, score, seconds, true);
    }

    public static List<Entry> Top(int count)
    {
        Seed();
        return entries.GetRange(0, Mathf.Min(count, entries.Count));
    }
}

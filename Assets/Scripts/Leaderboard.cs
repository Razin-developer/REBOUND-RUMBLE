using System.Collections.Generic;
using UnityEngine;

/// <summary>Temporary leaderboard: a few fake rivals plus this session's runs (resets when the game closes).</summary>
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

    static void Seed()
    {
        if (seeded) return;
        seeded = true;
        Add("SHANG", 4200, 412f, false);
        Add("XIALING", 3650, 366f, false);
        Add("KATY", 2980, 301f, false);
        Add("WENWU", 2410, 255f, false);
        Add("TRAINEE", 1720, 190f, false);
        Add("MORRIS", 1260, 148f, false);
        Add("RING_FAN", 840, 102f, false);
        Add("NEWBIE", 310, 51f, false);
    }

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

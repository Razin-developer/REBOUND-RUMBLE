using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Game flow helpers: the real asset preloader (splash -> loading -> menu) and the story text.</summary>
public static class Flow
{
    public static bool Loaded;
    public static float Progress;
    public static string Step = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Loaded = false; Progress = 0f; Step = ""; }

    public static readonly string[] Tips =
    {
        "A ring that finds no enemy comes home after a moment.",
        "Walk near a Rune Pillar to attune it, then press E to teleport.",
        "Right click or Q throws 5 rings at once - then wait for the cooldown.",
        "Chests open when you walk close to them. No click needed.",
        "Follow the golden arrow to reach the exit portal.",
        "Soul Eaters fly straight at you. Wasps wind up before they dash.",
    };

    public static readonly string[] StoryTitles =
    {
        "THE TEN RINGS", "THE ARMY", "THE SON", "THE HUNT", "THE RINGS ANSWER"
    };

    public static readonly string[] StoryText =
    {
        "Ten ancient rings of power. Whoever wears them cannot be stopped, and cannot be forgiven.",
        "An army has carried them across a thousand years, swallowing kingdoms and hunting everyone who stood in its way.",
        "You are Shang-Chi. You ran from your father's empire to live a quiet life. The quiet is over.",
        "His soldiers have found you. Behind them, ancient Soul Eaters circle, hungry for every spirit they can drag out of this world.",
        "Ten rings. One will. Throw them and they find their own prey. Hold the line, then follow the golden arrow to the portal and get out alive.",
    };

    static string StoryKey { get { return "rr_story_" + (Net.State == AccountState.LoggedIn ? Net.User.ToLowerInvariant() : "offline"); } }
    public static bool StorySeen() { return PlayerPrefs.GetInt(StoryKey, 0) == 1; }
    public static void MarkStorySeen() { PlayerPrefs.SetInt(StoryKey, 1); PlayerPrefs.Save(); }

    /// <summary>Actually loads every sprite sheet, clip and costume texture, and warms the world/effects, with progress.</summary>
    public static IEnumerator Preload()
    {
        Loaded = false; Progress = 0f;
        var steps = new List<KeyValuePair<string, System.Action>>();
        steps.Add(new KeyValuePair<string, System.Action>("FONTS", () => { var f = GameAssets.PixelFont; }));
        foreach (string n in new[] { "enemy_slime", "enemy_ant", "enemy_beetle", "enemy_wasp", "enemy_golem", "enemy_bat" })
        {
            string name = n;
            steps.Add(new KeyValuePair<string, System.Action>("ENEMIES", () => GameAssets.Frames(name, 4)));
        }
        steps.Add(new KeyValuePair<string, System.Action>("WORLD ART", () => { GameAssets.Frames("chest", 2); GameAssets.Frames("portal", 4); }));
        steps.Add(new KeyValuePair<string, System.Action>("RINGS", () => { GameAssets.Single("ring"); GameAssets.Texture("ring"); var g = GameAssets.Glow; var w = GameAssets.WhiteLeftPivot; }));
        for (int c = 0; c < GameSettings.CostumeNames.Length; c++)
        {
            string suf = c == 0 ? "" : c.ToString();
            steps.Add(new KeyValuePair<string, System.Action>("HERO COSTUMES", () => { GameAssets.Texture("player_s" + suf); GameAssets.Texture("player_n" + suf); GameAssets.Texture("player_e" + suf); }));
        }
        foreach (string n in new[] { "shoot", "bounce", "pop", "tick", "ui_open", "ui_close", "hit", "enemy_die", "hurt", "wave", "gameover", "ring_clang", "ring_throw", "catch", "click", "chest_open", "portal", "spawn", "music_loop" })
        {
            string name = n;
            steps.Add(new KeyValuePair<string, System.Action>("SOUNDS", () => GameAssets.Clip(name)));
        }
        steps.Add(new KeyValuePair<string, System.Action>("MAP", () =>
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) Nav.EnsureInit(p.transform.position);
        }));
        steps.Add(new KeyValuePair<string, System.Action>("EFFECTS", () => { var m = Vfx.ParticleMaterial; }));
        steps.Add(new KeyValuePair<string, System.Action>("SETTINGS", () => { GameSettings.Load(); GameSettings.Apply(); }));

        for (int i = 0; i < steps.Count; i++)
        {
            Step = steps[i].Key;
            try { steps[i].Value(); } catch (System.Exception e) { Debug.LogWarning("[Preload] " + steps[i].Key + ": " + e.Message); }
            Progress = (i + 1f) / steps.Count;
            yield return null;
        }
        Step = "READY";
        Loaded = true;
    }
}

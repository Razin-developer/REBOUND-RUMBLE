using UnityEngine;

/// <summary>Player settings (saved with PlayerPrefs) + the temporary profile.</summary>
public static class GameSettings
{
    public static readonly int[] FpsOptions = { 30, 60, 90, 120, 144, -1 };   // -1 = unlimited
    public static readonly string[] DensityNames = { "LOW", "MEDIUM", "HIGH" };
    public static readonly Color[] AvatarColors =
    {
        new Color(1f, 0.82f, 0.25f), new Color(1f, 0.45f, 0.1f), new Color(0.95f, 0.3f, 0.3f),
        new Color(0.4f, 0.85f, 0.45f), new Color(0.4f, 0.7f, 1f), new Color(0.75f, 0.5f, 1f)
    };

    // sound
    public static float Master = 0.8f, SfxVol = 0.9f, MusicVol = 0.45f;
    // performance / visuals
    public static int FpsIndex = 1, Quality = -1, ParticleDensity = 2;
    public static bool VSync = false, Fullscreen = true, ShowFps = false, ScreenShake = true, DamageNumbers = true;
    // temporary profile (not saved yet)
    public static string ProfileName = "PLAYER";
    public static int AvatarIndex = 0;
    public static int Costume = 0;
    public static int Variant = 0;             // suit colour: 0 classic, 1 ice, 2 jade, 3 violet
    public static int Suit { get { return Suits.Make(Costume, Variant); } }
    public static readonly string[] CostumeNames = { "RED JACKET", "BLACK & GOLD", "HOODIE" };

    public static Color Avatar { get { return AvatarColors[Mathf.Clamp(AvatarIndex, 0, AvatarColors.Length - 1)]; } }
    public static float ParticleScale { get { return ParticleDensity == 0 ? 0.35f : (ParticleDensity == 1 ? 0.7f : 1f); } }

    static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { loaded = false; }

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        Master = PlayerPrefs.GetFloat("rr_master", Master);
        SfxVol = PlayerPrefs.GetFloat("rr_sfx", SfxVol);
        MusicVol = PlayerPrefs.GetFloat("rr_music", MusicVol);
        FpsIndex = PlayerPrefs.GetInt("rr_fps", FpsIndex);
        Quality = PlayerPrefs.GetInt("rr_quality", QualitySettings.GetQualityLevel());
        ParticleDensity = PlayerPrefs.GetInt("rr_density", ParticleDensity);
        VSync = PlayerPrefs.GetInt("rr_vsync", VSync ? 1 : 0) == 1;
        Fullscreen = PlayerPrefs.GetInt("rr_full", Screen.fullScreen ? 1 : 0) == 1;
        ShowFps = PlayerPrefs.GetInt("rr_showfps", 0) == 1;
        ScreenShake = PlayerPrefs.GetInt("rr_shake", 1) == 1;
        DamageNumbers = PlayerPrefs.GetInt("rr_dmgnum", 1) == 1;
        Costume = Mathf.Clamp(PlayerPrefs.GetInt("rr_costume", 0), 0, CostumeNames.Length - 1);
        Variant = Mathf.Clamp(PlayerPrefs.GetInt("rr_variant", 0), 0, Suits.Variants - 1);
        Apply();
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("rr_master", Master);
        PlayerPrefs.SetFloat("rr_sfx", SfxVol);
        PlayerPrefs.SetFloat("rr_music", MusicVol);
        PlayerPrefs.SetInt("rr_fps", FpsIndex);
        PlayerPrefs.SetInt("rr_quality", Quality);
        PlayerPrefs.SetInt("rr_density", ParticleDensity);
        PlayerPrefs.SetInt("rr_vsync", VSync ? 1 : 0);
        PlayerPrefs.SetInt("rr_full", Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt("rr_showfps", ShowFps ? 1 : 0);
        PlayerPrefs.SetInt("rr_shake", ScreenShake ? 1 : 0);
        PlayerPrefs.SetInt("rr_dmgnum", DamageNumbers ? 1 : 0);
        PlayerPrefs.SetInt("rr_costume", Costume);
        PlayerPrefs.SetInt("rr_variant", Variant);
        PlayerPrefs.Save();
    }

    public static void Apply()
    {
        AudioListener.volume = Mathf.Clamp01(Master);
        MusicPlayer.Refresh();
        FpsIndex = Mathf.Clamp(FpsIndex, 0, FpsOptions.Length - 1);
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = VSync ? -1 : FpsOptions[FpsIndex];
        if (Quality >= 0 && Quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(Quality, true);
        QualitySettings.vSyncCount = VSync ? 1 : 0; // SetQualityLevel can reset it
        if (Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;
    }

    public static string FpsLabel { get { int f = FpsOptions[Mathf.Clamp(FpsIndex, 0, FpsOptions.Length - 1)]; return f < 0 ? "UNLIMITED" : f + " FPS"; } }
    public static string QualityLabel { get { var n = QualitySettings.names; return (Quality >= 0 && Quality < n.Length) ? n[Quality].ToUpper() : "DEFAULT"; } }
}

using UnityEngine;

/// <summary>Looping background music. Keeps playing while the game is paused.</summary>
public static class MusicPlayer
{
    static AudioSource src;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { src = null; ducked = false; }

    public static void Start()
    {
        if (src == null)
        {
            var go = new GameObject("Music");
            Object.DontDestroyOnLoad(go);
            src = go.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.ignoreListenerPause = true;
            src.clip = GameAssets.Clip("music_loop");
        }
        Refresh();
        if (src.clip != null && !src.isPlaying) src.Play();
    }

    /// <summary>Switch the looping track (sea battles use their own music).</summary>
    public static void Use(string clipName)
    {
        Start();
        var c = GameAssets.Clip(clipName);
        if (c == null || src == null || src.clip == c) return;
        src.clip = c;
        src.Play();
    }

    static bool ducked;
    public static void Duck(bool on) { ducked = on; Refresh(); }

    public static void Refresh()
    {
        if (src != null) src.volume = ducked ? 0f : Mathf.Clamp01(GameSettings.MusicVol) * 0.6f;
    }
}

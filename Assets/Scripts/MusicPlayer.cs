using UnityEngine;

/// <summary>Looping background music. Keeps playing while the game is paused.</summary>
public static class MusicPlayer
{
    static AudioSource src;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { src = null; }

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

    public static void Refresh()
    {
        if (src != null) src.volume = Mathf.Clamp01(GameSettings.MusicVol) * 0.6f;
    }
}

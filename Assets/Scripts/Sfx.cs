using UnityEngine;

/// <summary>Sound-effect player with a pool of AudioSources, plus a UI source that keeps working while paused.</summary>
public static class Sfx
{
    const int PoolSize = 10;
    static AudioSource[] pool;
    static AudioSource ui;
    static int next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pool = null; ui = null; next = 0; }

    static void Ensure()
    {
        if (pool != null && pool[0] != null && ui != null) return;
        var go = new GameObject("Sfx");
        Object.DontDestroyOnLoad(go);
        pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            pool[i] = go.AddComponent<AudioSource>();
            pool[i].playOnAwake = false;
            pool[i].spatialBlend = 0f;
        }
        ui = go.AddComponent<AudioSource>();
        ui.playOnAwake = false;
        ui.spatialBlend = 0f;
        ui.ignoreListenerPause = true;
    }

    public static void Play(string name, float volume = 1f, float pitch = 1f)
    {
        AudioClip clip = GameAssets.Clip(name);
        if (clip == null) return;
        Ensure();
        AudioSource s = pool[next];
        next = (next + 1) % PoolSize;
        s.pitch = pitch * Random.Range(0.96f, 1.04f);
        s.volume = volume * GameSettings.SfxVol;
        s.clip = clip;
        s.Play();
    }

    /// <summary>Menu / interface sounds (not affected by pausing).</summary>
    public static void PlayUI(string name, float volume = 0.6f, float pitch = 1f)
    {
        AudioClip clip = GameAssets.Clip(name);
        if (clip == null) return;
        Ensure();
        ui.pitch = pitch;
        ui.PlayOneShot(clip, volume * GameSettings.SfxVol);
    }
}

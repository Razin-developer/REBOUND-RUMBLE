using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Original, code-synthesised score for the story (drone, heartbeat, wind, warm pad, plucks, booms). No audio files, no licences.</summary>
public class CineAudio : MonoBehaviour
{
    const int SR = 44100;
    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

    AudioSource drone, heart, wind, pad;
    AudioSource[] one = new AudioSource[4];
    int nextOne;
    float mDrone, mHeart, mWind, mPad;       // target volumes
    float cDrone, cHeart, cWind, cPad;       // current volumes
    public float dronePitch = 1f, heartPitch = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cache.Clear(); }

    public static CineAudio Create(Transform parent)
    {
        var a = parent.gameObject.AddComponent<CineAudio>();
        a.drone = a.Loop("Drone", Clip("drone"));
        a.heart = a.Loop("Heart", Clip("heart"));
        a.wind = a.Loop("Wind", Clip("wind"));
        a.pad = a.Loop("Pad", Clip("pad"));
        for (int i = 0; i < a.one.Length; i++) a.one[i] = a.Make("One" + i, null, false);
        return a;
    }

    AudioSource Make(string n, AudioClip c, bool loop)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = c; s.loop = loop; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
        s.ignoreListenerPause = true;
        if (loop && c != null) s.Play();
        return s;
    }

    AudioSource Loop(string n, AudioClip c) { return Make(n, c, true); }

    public void Mix(float d, float h, float w, float p) { mDrone = d; mHeart = h; mWind = w; mPad = p; }
    public void Silence() { mDrone = mHeart = mWind = mPad = 0f; cDrone = cHeart = cWind = cPad = 0f; }

    static float Master { get { return Mathf.Clamp01(Mathf.Max(0.35f, GameSettings.MusicVol * 2f)); } }

    void Update()
    {
        float dt = Time.unscaledDeltaTime * 1.6f;
        cDrone = Mathf.MoveTowards(cDrone, mDrone, dt); cHeart = Mathf.MoveTowards(cHeart, mHeart, dt);
        cWind = Mathf.MoveTowards(cWind, mWind, dt); cPad = Mathf.MoveTowards(cPad, mPad, dt);
        float m = Master;
        drone.volume = cDrone * m; heart.volume = cHeart * m; wind.volume = cWind * m; pad.volume = cPad * m;
        drone.pitch = dronePitch; heart.pitch = heartPitch;
    }

    void Shot(string clip, float vol, float pitch)
    {
        var s = one[nextOne]; nextOne = (nextOne + 1) % one.Length;
        s.pitch = pitch;
        s.PlayOneShot(Clip(clip), vol * Mathf.Max(0.4f, GameSettings.SfxVol) * 1.0f);
    }

    public void Boom(float pitch = 1f, float vol = 0.9f) { Shot("boom", vol, pitch); }
    public void Roar(float vol = 0.6f) { Shot("roar", vol, UnityEngine.Random.Range(0.8f, 1.1f)); }
    public void Swell(float vol = 0.8f) { Shot("swell", vol, 1f); }
    public void Pluck(float freq, float vol = 0.5f)
    {
        string k = "pluck" + Mathf.RoundToInt(freq);
        if (!cache.ContainsKey(k)) cache[k] = Build(k, 1.3f, t => (Mathf.Sin(6.2832f * freq * t) * Mathf.Exp(-t * 3.2f) + 0.4f * Mathf.Sin(12.566f * freq * t) * Mathf.Exp(-t * 5f) + 0.15f * Mathf.Sin(18.85f * freq * t) * Mathf.Exp(-t * 8f)) * Mathf.Min(1f, t * 400f), 0.6f);
        var s = one[nextOne]; nextOne = (nextOne + 1) % one.Length;
        s.pitch = 1f;
        s.PlayOneShot(cache[k], vol * Master);
    }

    // ------------------------------------------------------------------ synthesis
    static AudioClip Build(string name, float secs, Func<float, float> f, float peakTo)
    {
        int n = Mathf.RoundToInt(secs * SR);
        var d = new float[n];
        float peak = 0.0001f;
        for (int i = 0; i < n; i++) { d[i] = f(i / (float)SR); peak = Mathf.Max(peak, Mathf.Abs(d[i])); }
        for (int i = 0; i < n; i++) d[i] = d[i] / peak * peakTo;
        var c = AudioClip.Create(name, n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    static float Tau(float f, float t) { return 6.2832f * f * t; }

    static AudioClip Clip(string name)
    {
        AudioClip c;
        if (cache.TryGetValue(name, out c) && c != null) return c;
        switch (name)
        {
            case "drone":
                c = Build(name, 4f, t => (0.55f * Mathf.Sin(Tau(55f, t)) + 0.32f * Mathf.Sin(Tau(82.5f, t)) + 0.22f * Mathf.Sin(Tau(110f, t)) + 0.1f * Mathf.Sin(Tau(55.25f, t))) * (0.8f + 0.2f * Mathf.Sin(Tau(0.25f, t))), 0.7f);
                break;
            case "heart":
                c = Build(name, 1f, t =>
                {
                    float t2 = t - 0.3f;
                    float a = Mathf.Sin(Tau(48f, t)) * Mathf.Exp(-t * 14f);
                    float b = t2 > 0f ? Mathf.Sin(Tau(42f, t2)) * Mathf.Exp(-t2 * 16f) * 0.7f : 0f;
                    return a + b;
                }, 0.9f);
                break;
            case "wind":
                c = WindClip();
                break;
            case "pad":
                c = Build(name, 8f, t => (0.5f * Mathf.Sin(Tau(110f, t)) + 0.35f * Mathf.Sin(Tau(220f, t)) * (0.75f + 0.25f * Mathf.Sin(Tau(0.125f, t))) + 0.3f * Mathf.Sin(Tau(262f, t)) * (0.75f + 0.25f * Mathf.Sin(Tau(0.25f, t + 1f))) + 0.28f * Mathf.Sin(Tau(330f, t)) * (0.75f + 0.25f * Mathf.Sin(Tau(0.125f, t + 3f))) + 0.16f * Mathf.Sin(Tau(494f, t)) * (0.7f + 0.3f * Mathf.Sin(Tau(0.375f, t)))), 0.6f);
                break;
            case "boom":
                {
                    float ph = 0f; var rng = new System.Random(3);
                    c = Build(name, 1.8f, t =>
                    {
                        float fr = 34f + 70f * Mathf.Exp(-t * 2.2f);
                        ph += 6.2832f * fr / SR;
                        float noise = t < 0.18f ? ((float)rng.NextDouble() * 2f - 1f) * (1f - t / 0.18f) * 0.5f : 0f;
                        return Mathf.Sin(ph) * Mathf.Exp(-t * 2.0f) + noise + 0.12f * (Mathf.Sin(Tau(1400f, t)) + 0.7f * Mathf.Sin(Tau(2130f, t))) * Mathf.Exp(-t * 5f);
                    }, 0.95f);
                    break;
                }
            case "roar":
                {
                    var rng = new System.Random(11); float lp = 0f, ph = 0f;
                    c = Build(name, 2.2f, t =>
                    {
                        lp += 0.12f * (((float)rng.NextDouble() * 2f - 1f) - lp);
                        ph += 6.2832f * (70f + 25f * Mathf.Sin(Tau(3f, t))) / SR;
                        float env = Mathf.Clamp01(t / 0.35f) * Mathf.Exp(-Mathf.Max(0f, t - 0.5f) * 1.6f);
                        float saw = (ph / 6.2832f % 1f) * 2f - 1f;
                        return (lp * 1.4f + saw * 0.5f) * env;
                    }, 0.8f);
                    break;
                }
            case "swell":
                c = Build(name, 7f, t =>
                {
                    float env = Mathf.Clamp01(t / 3.5f); env = env * env * Mathf.Clamp01((7f - t) / 3f);
                    return (Mathf.Sin(Tau(220f, t)) + 0.8f * Mathf.Sin(Tau(277f, t)) + 0.7f * Mathf.Sin(Tau(330f, t)) + 0.6f * Mathf.Sin(Tau(440f, t)) + 0.4f * Mathf.Sin(Tau(55f, t))) * env;
                }, 0.85f);
                break;
            default:
                c = Build(name, 0.1f, t => 0f, 0f);
                break;
        }
        cache[name] = c;
        return c;
    }

    static AudioClip WindClip()
    {
        int n = SR * 3;
        var d = new float[n];
        var rng = new System.Random(5);
        float y1 = 0f, y2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float nz = (float)rng.NextDouble() * 2f - 1f;
            y1 += 0.02f * (nz - y1); y2 += 0.08f * (nz - y2);
            float t = i / (float)SR;
            d[i] = (y1 * 5f + y2 * 1.3f) * (0.6f + 0.4f * Mathf.Sin(Tau(0.33f, t)));
        }
        int m = SR / 5;
        for (int i = 0; i < m; i++) { float a = i / (float)m; d[n - m + i] = d[n - m + i] * (1f - a) + d[i] * a; }
        float peak = 0.0001f;
        for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] = d[i] / peak * 0.6f;
        var c = AudioClip.Create("wind", n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }
}

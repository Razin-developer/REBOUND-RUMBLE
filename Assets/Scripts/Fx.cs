using System.Collections.Generic;
using UnityEngine;

/// <summary>Extra cinematic effects: crescent slashes, rising soul wisps, ring sparks, dust.</summary>
public static class Fx
{
    static Sprite crescent;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { crescent = null; decals.Clear(); }
    static readonly Queue<GameObject> decals = new Queue<GameObject>();

    static Sprite Crescent
    {
        get
        {
            if (crescent != null) return crescent;
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = Mathf.Sqrt((x - 36f) * (x - 36f) + (y - 32f) * (y - 32f));
                    float b = Mathf.Sqrt((x - 24f) * (x - 24f) + (y - 32f) * (y - 32f));
                    t.SetPixel(x, y, (a < 28f && b > 25f) ? Color.white : Color.clear);
                }
            t.Apply();
            crescent = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 32f);
            return crescent;
        }
    }

    /// <summary>A bright crescent swoosh pointing along dir (throws, impacts).</summary>
    public static void Slash(Vector2 pos, Vector2 dir, Color color, float size = 0.8f, float life = 0.2f)
    {
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        var go = new GameObject("Slash");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Crescent; sr.color = color;
        sr.sortingLayerName = GameState.TopLayer; sr.sortingOrder = 601;
        go.AddComponent<FxAnim>().Init(life, size * 0.7f, size * 1.25f, color, Vector2.zero, 0f);
    }

    /// <summary>Soul wisps drifting upward (Soul Eater death).</summary>
    public static void Wisps(Vector2 pos, Color color, int count = 8)
    {
        count = Mathf.Max(2, Mathf.RoundToInt(count * GameSettings.ParticleScale));
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Wisp");
            go.transform.position = pos + Random.insideUnitCircle * 0.3f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GameAssets.Glow; sr.color = color;
            sr.sortingLayerName = GameState.TopLayer; sr.sortingOrder = 600;
            float s = Random.Range(0.25f, 0.55f);
            go.AddComponent<FxAnim>().Init(Random.Range(0.8f, 1.4f), s, s * 0.3f, color, new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(0.8f, 1.8f)), Random.Range(0f, 6f));
        }
    }

    /// <summary>Blood spurt + ground splatter. `splat` = big death splash. `soul` = Soul Eater (pink-violet) blood.</summary>
    public static void Blood(Vector2 pos, Vector2 dir, int count, bool splat, bool soul = false)
    {
        Color c = soul ? new Color(0.85f, 0.15f, 0.5f) : new Color(0.72f, 0.05f, 0.08f);
        Vfx.Burst(pos, c, count, 4.5f, 0.12f, 0.55f);
        if (splat) Vfx.Burst(pos, new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f), count / 2 + 1, 2.5f, 0.09f, 0.4f);
        Splat(pos + (dir.sqrMagnitude > 0.001f ? dir.normalized * 0.2f : Vector2.zero), c, splat ? 7 : 3, splat ? 0.7f : 0.35f);
    }

    /// <summary>Little blood squares left on the ground; they fade after several seconds.</summary>
    public static void Splat(Vector2 pos, Color c, int n, float radius)
    {
        n = Mathf.Max(1, Mathf.RoundToInt(n * GameSettings.ParticleScale));
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Blood");
            float s = Random.Range(0.06f, 0.2f) * (radius > 0.5f ? 1.4f : 1f);
            go.transform.position = pos + Random.insideUnitCircle * radius - new Vector2(s * 0.5f, 0f);
            go.transform.localScale = new Vector3(s, s * Random.Range(0.6f, 1f), 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GameAssets.WhiteLeftPivot;
            sr.color = Color.Lerp(c, Color.black, Random.Range(0f, 0.35f));
            sr.sortingLayerName = GameState.ActorLayer; sr.sortingOrder = 1;
            go.AddComponent<DecalFx>().life = Random.Range(7f, 11f);
            decals.Enqueue(go);
            while (decals.Count > 90) { var old = decals.Dequeue(); if (old != null) Object.Destroy(old); }
        }
    }

    /// <summary>A column of light (player entrance, rune teleport).</summary>
    public static void Beam(Vector2 pos, Color color)
    {
        var go = new GameObject("Beam");
        go.transform.position = pos + new Vector2(0f, 3.2f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GameAssets.Glow; sr.color = color;
        sr.sortingLayerName = GameState.TopLayer; sr.sortingOrder = 590;
        var a = go.AddComponent<FxAnim>();
        a.stretch = new Vector2(0.6f, 3.2f);
        a.Init(0.7f, 1.6f, 0.2f, color, Vector2.zero, 0f);
    }

    public static void Dust(Vector2 pos, int count = 3)
    {
        Vfx.Burst(pos, new Color(0.78f, 0.7f, 0.55f, 0.8f), count, 0.9f, 0.1f, 0.3f);
    }

    /// <summary>Persistent spark emitter for a flying ring (toggle with emission.enabled).</summary>
    public static ParticleSystem AttachSparks(Transform parent)
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.4f, 0.08f));
        main.maxParticles = 80;
        var em = ps.emission; em.rateOverTime = 50f; em.enabled = false;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.12f;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = Vfx.ParticleMaterial; r.sortingLayerName = GameState.TopLayer; r.sortingOrder = 499;
        ps.Play();
        return ps;
    }
}

/// <summary>Tiny tween: scale from→to, fade out, drift, optional sway. Destroys itself.</summary>
public class FxAnim : MonoBehaviour
{
    public Vector2 stretch = Vector2.one;
    float life, age, s0, s1, sway; Color color; Vector2 vel; SpriteRenderer sr;
    public void Init(float life, float s0, float s1, Color c, Vector2 vel, float sway)
    { this.life = life; this.s0 = s0; this.s1 = s1; color = c; this.vel = vel; this.sway = sway; sr = GetComponent<SpriteRenderer>(); Apply(0f); }
    void Update()
    {
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        float k = age / life;
        transform.position += (Vector3)(vel * Time.deltaTime) + new Vector3(sway > 0f ? Mathf.Sin(age * 6f + sway) * 0.4f * Time.deltaTime : 0f, 0f, 0f);
        Apply(k);
    }
    void Apply(float k)
    {
        float s = Mathf.Lerp(s0, s1, k);
        transform.localScale = new Vector3(s * stretch.x, s * stretch.y, 1f);
        var c = color; c.a = color.a * (1f - k * k);
        sr.color = c;
    }
}

public class DecalFx : MonoBehaviour
{
    public float life = 8f; float age; SpriteRenderer sr;
    void Start() { sr = GetComponent<SpriteRenderer>(); }
    void Update()
    {
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        var c = sr.color; c.a = Mathf.Clamp01((life - age) / 2f); sr.color = c;
    }
}

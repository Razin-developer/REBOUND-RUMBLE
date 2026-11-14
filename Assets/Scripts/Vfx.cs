using UnityEngine;

/// <summary>Code-built particle effects (square pixel sparks that match the pixel-art look).</summary>
public static class Vfx
{
    static Material mat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { mat = null; }

    public static Material ParticleMaterial
    {
        get
        {
            if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
            return mat;
        }
    }

    public static void Burst(Vector2 pos, Color color, int count = 12, float speed = 4f, float size = 0.14f, float life = 0.4f)
    {
        count = Mathf.Max(1, Mathf.RoundToInt(count * GameSettings.ParticleScale));
        var go = new GameObject("VFX Burst");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = life;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startColor = new ParticleSystem.MinMaxGradient(color);
        main.gravityModifier = 0f;
        main.maxParticles = Mathf.Max(count * 2, 16);

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.05f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = ParticleMaterial;
        r.sortingLayerName = GameState.TopLayer;
        r.sortingOrder = 600;

        ps.Play();
        Object.Destroy(go, life + 0.6f);
    }

    static Sprite shockSprite;
    static Sprite ShockSprite()
    {
        if (shockSprite != null) return shockSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 28f) / 3f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        shockSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 32f);
        return shockSprite;
    }

    /// <summary>Expanding energy ring (the golden shockwave of an impact).</summary>
    public static void Shockwave(Vector2 pos, Color color, float radius = 1f, float duration = 0.35f)
    {
        var go = new GameObject("Shockwave");
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = ShockSprite();
        sr.color = color;
        sr.sortingLayerName = GameState.TopLayer;
        sr.sortingOrder = 590;
        go.AddComponent<RingPulse>().Init(sr, radius, duration);
    }
}

public class RingPulse : MonoBehaviour
{
    SpriteRenderer sr;
    float radius, duration, t;
    Color baseColor;

    public void Init(SpriteRenderer renderer, float r, float d)
    {
        sr = renderer; radius = r; duration = d; baseColor = renderer.color;
        transform.localScale = Vector3.one * (r * 0.15f);
    }

    void Update()
    {
        t += Time.deltaTime;
        float u = Mathf.Clamp01(t / duration);
        float eased = 1f - (1f - u) * (1f - u);
        transform.localScale = Vector3.one * Mathf.Lerp(radius * 0.15f, radius, eased);
        sr.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * (1f - u));
        if (u >= 1f) Destroy(gameObject);
    }
}

using UnityEngine;

/// <summary>
/// The sea of the battle arena: a smooth shaded basin (deep in the middle, lighter and foamier toward the edge), slowly drifting wave
/// streaks, and a soft pulsing barrier at the rim. Everything is generated at runtime with bilinear filtering - no pixel art.
/// </summary>
public class NavalWater : MonoBehaviour
{
    SpriteRenderer basin, waveA, waveB;
    LineRenderer rimA, rimB;
    Vector2 center; float radius;
    static Sprite basinSprite, waveSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { basinSprite = null; waveSprite = null; }

    public static NavalWater Build(Transform root, Vector2 center, float radius, int baseOrder)
    {
        var go = new GameObject("NavalWater");
        go.transform.SetParent(root, false);
        var w = go.AddComponent<NavalWater>();
        w.center = center; w.radius = radius;
        int layer = SortingLayer.NameToID(GameState.ActorLayer);

        w.basin = Make(go.transform, "Basin", BasinSprite, center, radius * 1.03f, layer, baseOrder - 130, Color.white);
        w.waveA = MakeTiled(go.transform, "WavesA", center, radius * 2f, layer, baseOrder - 128, new Color(0.75f, 0.92f, 1f, 0.18f));
        w.waveB = MakeTiled(go.transform, "WavesB", center, radius * 2f, layer, baseOrder - 127, new Color(0.55f, 0.82f, 0.95f, 0.12f));
        w.waveB.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
        w.rimA = MakeRim(go.transform, "RimA", center, radius, 0.9f, layer, baseOrder - 100, new Color(0.85f, 0.96f, 1f, 0.22f));
        w.rimB = MakeRim(go.transform, "RimB", center, radius - 0.6f, 0.35f, layer, baseOrder - 99, new Color(1f, 1f, 1f, 0.35f));
        return w;
    }

    static SpriteRenderer Make(Transform parent, string nm, Sprite s, Vector2 c, float r, int layer, int order, Color col)
    {
        var g = new GameObject(nm);
        g.transform.SetParent(parent, false);
        g.transform.position = new Vector3(c.x, c.y, 0.05f);
        g.transform.localScale = new Vector3(r, r, 1f);
        var sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.color = col; sr.sortingLayerID = layer; sr.sortingOrder = order;
        return sr;
    }

    static SpriteRenderer MakeTiled(Transform parent, string nm, Vector2 c, float size, int layer, int order, Color col)
    {
        var g = new GameObject(nm);
        g.transform.SetParent(parent, false);
        g.transform.position = new Vector3(c.x, c.y, 0.04f);
        var sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = WaveSprite;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = new Vector2(size, size);
        sr.color = col; sr.sortingLayerID = layer; sr.sortingOrder = order;
        return sr;
    }

    static LineRenderer MakeRim(Transform parent, string nm, Vector2 c, float r, float width, int layer, int order, Color col)
    {
        var g = new GameObject(nm);
        g.transform.SetParent(parent, false);
        var lr = g.AddComponent<LineRenderer>();
        int n = 220;
        lr.positionCount = n; lr.loop = true; lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.material = Vfx.ParticleMaterial;
        lr.sortingLayerID = layer; lr.sortingOrder = order;
        lr.startColor = lr.endColor = col;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            lr.SetPosition(i, new Vector3(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, 0f));
        }
        return lr;
    }

    /// <summary>Round basin: deep centre -> turquoise shallows -> pale foam band at the rim, soft alpha edge.</summary>
    static Sprite BasinSprite
    {
        get
        {
            if (basinSprite != null) return basinSprite;
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var cols = new Color[n * n];
            Color deep = new Color(0.07f, 0.24f, 0.40f), mid = new Color(0.11f, 0.37f, 0.52f), shallow = new Color(0.20f, 0.56f, 0.64f), foam = new Color(0.78f, 0.92f, 0.92f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - n / 2f + 0.5f) / (n / 2f), dy = (y - n / 2f + 0.5f) / (n / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c;
                    if (d < 0.7f) c = Color.Lerp(deep, mid, d / 0.7f);
                    else if (d < 0.93f) c = Color.Lerp(mid, shallow, (d - 0.7f) / 0.23f);
                    else c = Color.Lerp(shallow, foam, Mathf.Clamp01((d - 0.93f) / 0.05f));
                    // faint swirls so the water is not a flat gradient
                    float sw = Mathf.PerlinNoise(x * 0.035f, y * 0.035f) - 0.5f;
                    c = Color.Lerp(c, sw > 0f ? Color.white : Color.black, Mathf.Abs(sw) * 0.10f);
                    c.a = Mathf.Clamp01((1f - d) / 0.03f) * 0.97f;
                    cols[y * n + x] = c;
                }
            tex.SetPixels(cols); tex.Apply();
            basinSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 2f);
            return basinSprite;
        }
    }

    /// <summary>Tileable soft wave streaks (white on transparent), 8 world units per tile.</summary>
    static Sprite WaveSprite
    {
        get
        {
            if (waveSprite != null) return waveSprite;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var cols = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    // periodic noise (wraps) by sampling on a torus
                    float a = Mathf.Sin((u * 3f + Mathf.Sin(v * 6.283f * 2f) * 0.18f) * 6.283f) * 0.5f + 0.5f;
                    float b = Mathf.Sin((v * 4f + Mathf.Sin(u * 6.283f * 3f) * 0.15f) * 6.283f) * 0.5f + 0.5f;
                    float streak = Mathf.Pow(a * b, 6f);
                    cols[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(streak * 2.2f));
                }
            tex.SetPixels(cols); tex.Apply();
            waveSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 8f, 0, SpriteMeshType.FullRect);
            return waveSprite;
        }
    }

    void Update()
    {
        float t = Time.time;
        if (waveA != null) waveA.transform.position = new Vector3(center.x + Mathf.Repeat(t * 0.35f, 8f) - 4f, center.y + Mathf.Sin(t * 0.2f) * 1.2f, 0.04f);
        if (waveB != null) waveB.transform.position = new Vector3(center.x - Mathf.Repeat(t * 0.22f, 8f) + 4f, center.y + Mathf.Repeat(t * 0.12f, 8f) - 4f, 0.04f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 1.4f);
        if (rimA != null) rimA.startColor = rimA.endColor = new Color(0.85f, 0.96f, 1f, 0.16f + 0.14f * pulse);
        if (rimB != null) rimB.startColor = rimB.endColor = new Color(1f, 1f, 1f, 0.25f + 0.2f * (1f - pulse));
    }
}

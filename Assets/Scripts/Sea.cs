using System.Collections.Generic;
using UnityEngine;

/// <summary>Deep blue sea behind and around the map, with small 2D waves of random size and power rolling by.</summary>
public class Sea : MonoBehaviour
{
    const int MaxWaves = 34;
    static readonly Color Deep = new Color(0.06f, 0.22f, 0.4f);

    class Wave { public Transform t; public SpriteRenderer sr; public Vector2 start, dir; public float age, life, speed, amp, freq, phase, width, power; }
    readonly List<Wave> waves = new List<Wave>();
    Sprite crest;
    Camera cam;
    float nextSpawn;
    int layerId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { new GameObject("Sea").AddComponent<Sea>(); }

    void Start()
    {
        cam = Camera.main;
        if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Deep; }
        layerId = SortingLayer.layers.Length > 0 ? SortingLayer.layers[0].id : 0;

        const int w = 48, h = 8;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var clear = new Color32[w * h];
        tex.SetPixels32(clear);
        for (int x = 0; x < w; x++)
        {
            float edge = Mathf.Clamp01(Mathf.Min(x, w - 1 - x) / 8f);
            int y = 3 + Mathf.RoundToInt(2f * Mathf.Sin(x / (float)w * Mathf.PI * 4f));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, edge));
            tex.SetPixel(x, y + 1, new Color(1f, 1f, 1f, edge * 0.6f));
        }
        tex.Apply();
        crest = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    Rect View()
    {
        float hgt = cam.orthographicSize * 2f, wid = hgt * cam.aspect;
        Vector3 c = cam.transform.position;
        return new Rect(c.x - wid / 2f - 1f, c.y - hgt / 2f - 1f, wid + 2f, hgt + 2f);
    }

    static bool IsSea(Vector2 p)
    {
        if (Arena.Instance != null && Arena.Instance.Bounds.Contains(p)) return false;
        return !Nav.IsOverMap(p);
    }

    void Spawn()
    {
        Rect v = View();
        for (int i = 0; i < 8; i++)
        {
            Vector2 p = new Vector2(Random.Range(v.xMin, v.xMax), Random.Range(v.yMin, v.yMax));
            if (!IsSea(p)) continue;
            var wv = new Wave();
            wv.power = Random.value;                                   // 0 = ripple, 1 = big roller
            wv.width = Mathf.Lerp(0.5f, 1.6f, wv.power) * Random.Range(0.8f, 1.2f);
            wv.amp = Mathf.Lerp(0.03f, 0.22f, wv.power);
            wv.speed = Mathf.Lerp(0.15f, 0.8f, wv.power) * Random.Range(0.7f, 1.2f);
            wv.freq = Random.Range(1.5f, 3.5f);
            wv.phase = Random.value * 6.28f;
            wv.life = Random.Range(2.5f, 5f);
            wv.start = p;
            float ang = Random.Range(-35f, 35f) + (Random.value < 0.5f ? 0f : 180f);
            wv.dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
            var go = new GameObject("Wave");
            go.transform.SetParent(transform, false);
            wv.t = go.transform;
            wv.sr = go.AddComponent<SpriteRenderer>();
            wv.sr.sprite = crest;
            wv.sr.sortingLayerID = layerId;
            wv.sr.sortingOrder = -50;
            wv.sr.color = new Color(0.75f, 0.9f, 1f, 0f);
            waves.Add(wv);
            return;
        }
    }

    void Update()
    {
        if (cam == null || crest == null) return;
        float dt = Time.unscaledDeltaTime;   // keep rolling while the menu / pause is open
        nextSpawn -= dt;
        if (nextSpawn <= 0f && waves.Count < MaxWaves) { Spawn(); nextSpawn = Random.Range(0.05f, 0.18f); }

        Rect v = View();
        for (int i = waves.Count - 1; i >= 0; i--)
        {
            var w = waves[i];
            w.age += dt;
            float k = w.age / w.life;
            Vector2 p = w.start + w.dir * (w.speed * w.age);
            p.y += Mathf.Sin(w.age * w.freq + w.phase) * w.amp;
            if (k >= 1f || !v.Contains(p) || !IsSea(p)) { Destroy(w.t.gameObject); waves.RemoveAt(i); continue; }
            w.t.position = p;
            float swell = 1f + 0.18f * Mathf.Sin(w.age * w.freq * 0.7f + w.phase);
            w.t.localScale = new Vector3(w.width * swell, 0.6f + w.power * 0.5f, 1f);
            float a = Mathf.Sin(k * Mathf.PI) * Mathf.Lerp(0.25f, 0.7f, w.power);
            w.sr.color = new Color(0.75f, 0.9f, 1f, a);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rune Pillars (the "PF Props - Rune Pillar" props already in the map): walk near one to ATTUNE it,
/// then press E (or click the HUD button) to teleport to it from anywhere. 15 second cooldown.
/// </summary>
public class RunePillars : MonoBehaviour
{
    public const float Cooldown = 15f;
    public static int Total, Attuned;
    static float readyAt;
    static RunePillars inst;

    public static float CooldownLeft { get { return Mathf.Max(0f, readyAt - Time.time); } }
    public static bool CanUse { get { return GameState.Active && Attuned > 0 && CooldownLeft <= 0f; } }

    class Pillar
    {
        public Transform t; public Vector2 basePos; public Vector2 top; public bool attuned;
        public SpriteRenderer glow; public SpriteRenderer[] runes; public string sortLayer;
    }
    readonly List<Pillar> pillars = new List<Pillar>();
    Transform player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Total = Attuned = 0; readyAt = 0f; inst = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { new GameObject("RunePillars").AddComponent<RunePillars>(); }

    void Start()
    {
        inst = this;
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            string n = t.name;
            if (n.IndexOf("Rune Pillar", System.StringComparison.OrdinalIgnoreCase) < 0 || n.IndexOf("Broken", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (t.parent != null && t.parent.name.IndexOf("Rune Pillar", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            var srs = t.GetComponentsInChildren<SpriteRenderer>();
            if (srs.Length == 0) continue;
            Bounds b = srs[0].bounds;
            foreach (var s in srs) b.Encapsulate(s.bounds);
            var pl = new Pillar { t = t, basePos = new Vector2(b.center.x, b.min.y + 0.2f), top = new Vector2(b.center.x, b.max.y), sortLayer = srs[0].sortingLayerName };
            var g = new GameObject("PillarGlow");
            g.transform.position = new Vector3(b.center.x, b.center.y, 0f);
            g.transform.localScale = Vector3.one * 2.4f;
            pl.glow = g.AddComponent<SpriteRenderer>();
            pl.glow.sprite = GameAssets.Glow;
            pl.glow.sortingLayerName = srs[0].sortingLayerName; pl.glow.sortingOrder = srs[0].sortingOrder + 1;
            pl.runes = new SpriteRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                var r = new GameObject("Rune").AddComponent<SpriteRenderer>();
                r.transform.SetParent(g.transform, false);
                r.sprite = GameAssets.Glow; r.transform.localScale = Vector3.one * 0.18f;
                r.sortingLayerName = srs[0].sortingLayerName; r.sortingOrder = srs[0].sortingOrder + 2;
                pl.runes[i] = r;
            }
            pillars.Add(pl);
        }
        Total = pillars.Count;
        GameState.OnRunStart += ResetRun;
        GameState.OnReturnToMenu += ResetRun;
    }

    void OnDestroy() { GameState.OnRunStart -= ResetRun; GameState.OnReturnToMenu -= ResetRun; }

    void ResetRun() { foreach (var p in pillars) p.attuned = false; Attuned = 0; readyAt = 0f; }

    void Update()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
        foreach (var p in pillars)
        {
            if (p.glow == null) continue;
            p.glow.color = p.attuned ? new Color(1f, 0.8f, 0.3f, 0.35f + 0.25f * pulse) : new Color(0.4f, 0.85f, 1f, 0.12f + 0.12f * pulse);
            for (int i = 0; i < p.runes.Length; i++)
            {
                float a = Time.time * 1.6f + i * 2.094f;
                p.runes[i].enabled = p.attuned;
                p.runes[i].transform.localPosition = new Vector3(Mathf.Cos(a) * 0.55f, 0.15f + Mathf.Sin(a * 2f) * 0.25f, 0f);
                p.runes[i].color = new Color(1f, 0.9f, 0.5f, 0.9f);
            }
        }
        if (!GameState.Active || player == null) return;

        Vector2 pp = (Vector2)player.position + new Vector2(0f, 0.4f);
        foreach (var p in pillars)
        {
            if (p.attuned) continue;
            if (Vector2.Distance(pp, p.basePos + Vector2.up * 0.6f) < 2.3f) Attune(p);
        }
        if (InputHub.Pressed(KeyCode.E)) Teleport();
    }

    void Attune(Pillar p)
    {
        p.attuned = true; Attuned++;
        Vector2 c = p.basePos + Vector2.up * 0.8f;
        Vfx.Shockwave(c, new Color(0.5f, 0.9f, 1f), 2.2f, 0.6f);
        Vfx.Burst(c, new Color(1f, 0.85f, 0.4f), 26, 4f, 0.14f, 0.7f);
        Sfx.Play("chest_open", 0.8f, 1.4f);
        FloatingText.Add(p.top + Vector2.up * 0.6f, "RUNE PILLAR ATTUNED", new Color(0.6f, 0.95f, 1f), 16, 2.2f);
        if (Attuned == 1) FloatingText.Add(p.top + Vector2.up * 1.2f, "PRESS E TO TELEPORT HERE", new Color(1f, 0.85f, 0.35f), 12, 2.6f);
    }

    public static bool Teleport() { return inst != null && inst.DoTeleport(); }

    bool DoTeleport()
    {
        if (!CanUse || player == null) return false;
        // pick the attuned pillar that is safest (farthest from the nearest enemy), but not the one we are already standing at
        Pillar best = null; float bestScore = -1f;
        Vector2 pp = player.position;
        foreach (var p in pillars)
        {
            if (!p.attuned) continue;
            if (Vector2.Distance(pp, p.basePos) < 2.5f && Attuned > 1) continue;
            float minE = 99f;
            foreach (var e in Enemy.All) if (e != null) minE = Mathf.Min(minE, Vector2.Distance(e.transform.position, p.basePos));
            if (minE > bestScore) { bestScore = minE; best = p; }
        }
        if (best == null) return false;
        if (Vector2.Distance(pp, best.basePos) < 2.5f) { FloatingText.Add(pp + Vector2.up, "ALREADY HERE", Color.white, 12, 1f); return false; }

        Vector2 dest;
        if (!Nav.TryFreeCellNear(best.basePos, 0.9f, 3f, out dest)) dest = best.basePos + Vector2.down * 1.2f;

        Color c = new Color(0.55f, 0.92f, 1f);
        Fx.Beam(pp, c); Vfx.Shockwave(pp + Vector2.up * 0.4f, c, 2f, 0.45f); Vfx.Burst(pp + Vector2.up * 0.4f, c, 30, 5f, 0.15f, 0.6f);

        var rb = player.GetComponent<Rigidbody2D>();
        player.position = dest;
        if (rb != null) { rb.position = dest; rb.linearVelocity = Vector2.zero; }
        string layerName = best.sortLayer;
        int phys = LayerMask.NameToLayer(layerName);
        if (phys >= 0) player.gameObject.layer = phys;
        foreach (var sr in player.GetComponentsInChildren<SpriteRenderer>()) sr.sortingLayerName = layerName;

        Fx.Beam(dest, new Color(1f, 0.85f, 0.4f));
        Vfx.Shockwave(dest + Vector2.up * 0.4f, new Color(1f, 0.85f, 0.4f), 3.4f, 0.55f);
        Vfx.Burst(dest + Vector2.up * 0.4f, new Color(1f, 0.85f, 0.4f), 36, 6f, 0.16f, 0.7f);
        Sfx.Play("portal", 0.8f, 1.3f);
        CameraRig.Shake(0.25f, 0.25f);
        foreach (var e in new List<Enemy>(Enemy.All))
            if (e != null && Vector2.Distance(e.transform.position, dest) < 3.2f) e.Hit(1f, (Vector2)e.transform.position - dest);
        var ph = player.GetComponent<PlayerHealth>();
        if (ph != null) ph.Invuln(1.0f);
        readyAt = Time.time + Cooldown;
        return true;
    }
}

/// <summary>A golden arrow that circles the player and always points at the exit portal.</summary>
public class PortalArrow : MonoBehaviour
{
    public static float Distance = -1f;
    SpriteRenderer sr;
    Transform player;
    static Sprite arrowSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Distance = -1f; arrowSprite = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { new GameObject("PortalArrow").AddComponent<PortalArrow>(); }

    static Sprite Arrow()
    {
        if (arrowSprite != null) return arrowSprite;
        const int n = 16;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float hw = (n - 1 - x) * 0.5f;                       // triangle pointing +x
                float dy = Mathf.Abs(y - (n - 1) / 2f);
                bool fill = x >= 2 && dy <= hw;
                bool edge = fill && (dy > hw - 1.2f || x <= 3);
                t.SetPixel(x, y, !fill ? Color.clear : (edge ? new Color(0.35f, 0.2f, 0.05f) : new Color(1f, 0.85f, 0.3f)));
            }
        t.Apply();
        arrowSprite = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 16f);
        return arrowSprite;
    }

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = Arrow();
        sr.sortingLayerName = GameState.TopLayer; sr.sortingOrder = 700;
    }

    void LateUpdate()
    {
        Portal portal = Portal.Current;
        if (sr == null || player == null || portal == null || GameState.Mode != GameMode.Playing || GameState.Over) { if (sr != null) sr.enabled = false; Distance = -1f; return; }
        Vector2 from = (Vector2)player.position + new Vector2(0f, 0.55f);
        Vector2 to = (Vector2)portal.transform.position;
        Vector2 d = to - from;
        Distance = d.magnitude;
        sr.enabled = true;
        Vector2 dir = d.normalized;
        float bob = Mathf.Sin(Time.time * 6f) * 0.08f;
        transform.position = from + dir * (1.7f + bob);
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        float a = Mathf.Clamp01((Distance - 3f) / 3f) * 0.9f;
        sr.color = new Color(1f, 0.9f, 0.5f, a);
    }
}

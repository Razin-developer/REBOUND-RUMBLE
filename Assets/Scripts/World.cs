using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ChestKind { Heal, Treasure }

/// <summary>Places the entrance chest, the exit portal and the treasure chest beside it.</summary>
public class World : MonoBehaviour
{
    static Transform FindAltar(Vector2 from)
    {
        Transform best = null; float bd = -1f;
        foreach (var t in Object.FindObjectsByType<Transform>())
        {
            if (t.name.IndexOf("Altar 01", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (t.parent != null && t.parent.name.IndexOf("Altar 01", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (t.GetComponentInChildren<SpriteRenderer>() == null) continue;
            float d = Vector2.Distance(t.position, from);
            if (d > bd) { bd = d; best = t; }
        }
        return best;
    }

    public static float PortalProgress;
    public static bool NearPortal;

    static World instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { PortalProgress = 0f; NearPortal = false; instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { new GameObject("World").AddComponent<World>(); }

    IEnumerator Start()
    {
        instance = this;
        yield return null;
        yield return Build();
    }

    /// <summary>Drop the old chests / portal, rescan navigation, and place everything again.</summary>
    public static void Rebuild()
    {
        if (instance == null) return;
        instance.StopAllCoroutines();
        instance.StartCoroutine(instance.RebuildRoutine());
    }

    IEnumerator RebuildRoutine()
    {
        foreach (var c in new List<Chest>(Chest.All)) if (c != null) Destroy(c.gameObject);
        Chest.All.Clear();
        Portal.Clear();
        yield return null;                 // let the new map's colliders register
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) { Physics2D.SyncTransforms(); Nav.Reset(); Nav.Init(p.transform.position); }
        yield return Build();
        RunePillars.Rescan();
    }

    IEnumerator Build()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p == null) yield break;
        Vector2 start = p.transform.position;
        Nav.EnsureInit(start);

        Vector2 pos;
        // exit portal: as far from the entrance as possible ("outside")
        // exit portal: the roomiest open area in the outer half of the map (fixed spot, same every run)
        bool havePortal = Nav.TryRoomyCell(Nav.InitialStart, 0.45f, out pos);
        if (!havePortal) havePortal = Nav.TryFarthestFreeCell(Nav.InitialStart, out pos);
        Vector2 portalPos = pos;
        // the exit is the map's own "PF Props - Altar 01" (the farthest one from the entrance); a scripted portal is only the fallback
        Transform altar = FindAltar(Nav.InitialStart);
        if (altar != null) { portalPos = Portal.Attach(altar).Anchor; havePortal = true; }
        else if (havePortal) Portal.Create(portalPos);

        // chests scattered at random spots across the map (never at the spawn point)
        var placed = new List<Vector2> { start };
        if (havePortal) placed.Add(portalPos);
        int made = 0;
        for (int tries = 0; tries < 120 && made < 5 && !Coop.IsGuest; tries++)
        {
            if (!Nav.TryFreeCellNear(start, 10f, 80f, out pos)) continue;
            bool far = true;
            foreach (var q in placed) if (Vector2.Distance(q, pos) < 7f) { far = false; break; }
            if (!far) continue;
            placed.Add(pos);
            Chest.Create(pos, made % 3 == 2 ? ChestKind.Treasure : ChestKind.Heal);
            made++;
        }

        // treasure chest: right beside the portal
        if (havePortal && !Coop.IsGuest)
        {
            var st = Random.state; Random.InitState(7);   // same treasure spot beside the portal every run
            if (Nav.TryFreeCellNear(portalPos, 1.8f, 3.4f, out pos)) Chest.Create(pos, ChestKind.Treasure);
            Random.state = st;
        }
        Shrine.Scan();
    }
}

/// <summary>Walk into it (no button needed) and stay a moment to leave the run and bank your score.</summary>
public class Portal : MonoBehaviour
{
    public bool IsAltar { get { return existing; } }
    public Vector2 Anchor { get; private set; }     // where the exit is (centre of the altar's base / the scripted portal)
    bool existing;                                  // true when this is the map's own altar
    float reach = 1.3f * GS.S;

    public static Portal Attach(Transform t)
    {
        var p = t.gameObject.AddComponent<Portal>();
        p.existing = true;
        var srs = t.GetComponentsInChildren<SpriteRenderer>();
        Bounds b = srs.Length > 0 ? srs[0].bounds : new Bounds(t.position, Vector3.one);
        foreach (var r in srs) b.Encapsulate(r.bounds);
        p.Anchor = new Vector2(b.center.x, b.min.y + 0.55f);
        p.reach = 2.0f;                              // the altar is solid: be generous
        Current = p;
        return p;
    }

    SpriteRenderer sr;
    Sprite[] frames;
    Transform player;
    float animT, nextSpark, progress;
    int frame;

    public static Portal Current;
    GameObject glowGo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPortal() { Current = null; }

    /// <summary>Remove the current exit (map change). The altar prop itself stays; only the portal behaviour and its glow go.</summary>
    public static void Clear()
    {
        if (Current == null) { Current = null; return; }
        if (Current.existing) Destroy(Current); else Destroy(Current.gameObject);
        Current = null;
        World.PortalProgress = 0f; World.NearPortal = false;
    }

    void OnDestroy() { if (glowGo != null) Destroy(glowGo); }

    public static Portal Create(Vector2 pos)
    {
        var go = new GameObject("Exit Portal");
        go.transform.position = pos;
        var portal = go.AddComponent<Portal>();
        portal.Anchor = pos;
        Current = portal;
        return portal;
    }

    void Start()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        var g = new GameObject("Glow");
        glowGo = g;
        var gsr = g.AddComponent<SpriteRenderer>();
        gsr.sprite = GameAssets.Glow;
        gsr.color = new Color(1f, 0.55f, 0.1f, 0.35f);
        if (existing)
        {
            var asr = GetComponentInChildren<SpriteRenderer>();
            g.transform.position = new Vector3(Anchor.x, Anchor.y + 0.6f, 0f);
            g.transform.localScale = Vector3.one * 4.2f;
            g.transform.SetParent(transform, true);
            gsr.sortingLayerName = asr != null ? asr.sortingLayerName : GameState.ActorLayer;
            gsr.sortingOrder = asr != null ? asr.sortingOrder - 1 : 0;
            return;
        }
        transform.localScale = Vector3.one * GS.S;
        frames = GameAssets.Frames("portal", 4);
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.sortingLayerName = GameState.ActorLayer;
        sr.sortingOrder = 1;
        g.transform.SetParent(transform, false);
        g.transform.localScale = Vector3.one * 4.2f;
        gsr.sortingLayerName = GameState.ActorLayer;
        gsr.sortingOrder = 0;
    }

    void Update()
    {
        animT += Time.deltaTime;
        if (!existing && animT >= 0.12f) { animT = 0f; frame = (frame + 1) % frames.Length; sr.sprite = frames[frame]; }
        if (Time.time >= nextSpark && Time.timeScale > 0f)
        {
            nextSpark = Time.time + 0.5f;
            Vfx.Burst(Anchor + new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.2f, 1.2f)), new Color(1f, 0.8f, 0.3f), 3, 1.2f, 0.1f, 0.6f);
        }

        if (Coop.IsGuest || !GameState.Active || player == null) { progress = 0f; World.PortalProgress = 0f; World.NearPortal = false; return; }

        Vector2 pp = (Vector2)player.position + new Vector2(0f, 0.4f);
        bool near = Vector2.Distance(pp, Anchor + new Vector2(0f, 0.2f)) < reach || Coop.AvatarNear(Anchor, reach, -1);
        World.NearPortal = near;
        progress = near ? progress + Time.deltaTime / 1.2f : Mathf.Max(0f, progress - Time.deltaTime * 2f);
        World.PortalProgress = progress;
        if (progress >= 1f) Use();
    }

    void Use()
    {
        progress = 0f; World.PortalProgress = 0f; World.NearPortal = false;
        Sfx.Play("portal", 0.9f, 1f);
        Vfx.Shockwave(Anchor, new Color(1f, 0.7f, 0.2f), 2.5f, 0.6f);
        Vfx.Burst(Anchor, new Color(1f, 0.8f, 0.3f), 40, 6f, 0.2f, 0.8f);
        if (Tutorial.Active) { Tutorial.PortalUsed(); return; }   // practice level: just finish the lesson
        GameState.ReturnToMenu(true);
    }
}

/// <summary>Walk close (no touching needed) to open it. Heal chest refills over time; treasure chest is one per run.</summary>
public class Chest : MonoBehaviour
{
    public static readonly List<Chest> All = new List<Chest>();

    public static int Opens;        // counter (used by the tutorial)
    ChestKind kind;
    Sprite[] frames;
    SpriteRenderer sr, glow;
    Transform player;
    bool ready = true;
    float reopenAt;

    public static Chest Create(Vector2 pos, ChestKind kind)
    {
        var go = new GameObject(kind == ChestKind.Heal ? "Entrance Chest" : "Treasure Chest");
        go.transform.position = pos;
        var c = go.AddComponent<Chest>();
        c.kind = kind;
        return c;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); }

    void Start()
    {
        transform.localScale = Vector3.one * GS.S;
        frames = GameAssets.Frames("chest", 2);
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.sortingLayerName = GameState.ActorLayer;
        sr.sortingOrder = 1;
        var g = new GameObject("Glow");
        g.transform.SetParent(transform, false);
        g.transform.localScale = Vector3.one * 1.8f;
        glow = g.AddComponent<SpriteRenderer>();
        glow.sprite = GameAssets.Glow;
        glow.color = kind == ChestKind.Heal ? new Color(1f, 0.3f, 0.35f, 0.35f) : new Color(1f, 0.85f, 0.25f, 0.4f);
        glow.sortingLayerName = GameState.ActorLayer;
        glow.sortingOrder = 0;
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        All.Add(this);
        GameState.OnRunStart += ResetChest;
    }

    void OnDestroy() { GameState.OnRunStart -= ResetChest; All.Remove(this); }

    public bool Ready { get { return ready; } }
    public ChestKind Kind { get { return kind; } }
    /// <summary>Co-op guest: show the chest as open/closed like the host's.</summary>
    public void SetReady(bool r) { if (ready == r) return; ready = r; if (sr != null && frames != null) sr.sprite = frames[r ? 0 : 1]; if (glow != null) glow.enabled = r; }
    void ResetChest() { ready = true; if (sr != null) sr.sprite = frames[0]; if (glow != null) glow.enabled = true; }

    void Update()
    {
        if (glow != null && ready) glow.transform.localScale = Vector3.one * (1.8f + Mathf.Sin(Time.time * 4f) * 0.2f);
        if (Coop.IsGuest || !GameState.Active || player == null) return;

        if (!ready)
        {
            if (kind == ChestKind.Heal && Time.time >= reopenAt)
            {
                ready = true; sr.sprite = frames[0]; glow.enabled = true;
                Vfx.Burst(transform.position, new Color(1f, 0.4f, 0.4f), 8, 2f, 0.12f, 0.5f);
            }
            return;
        }

        Vector2 pp = (Vector2)player.position + new Vector2(0f, 0.4f);
        if (Vector2.Distance(pp, transform.position) < 1.0f * GS.S || Coop.AvatarNear(transform.position, 1.0f * GS.S, -1)) Open();
    }

    void Open()
    {
        Opens++;
        ready = false;
        sr.sprite = frames[1];
        glow.enabled = false;
        Sfx.Play("chest_open", 0.8f, 1f);
        Vfx.Burst(transform.position, new Color(1f, 0.85f, 0.3f), 22, 4.5f, 0.17f, 0.6f);
        Vfx.Shockwave(transform.position, new Color(1f, 0.85f, 0.3f), 1.4f, 0.4f);
        Vector2 above = (Vector2)transform.position + new Vector2(0f, 0.9f);
        if (kind == ChestKind.Heal)
        {
            if (GameState.PlayerHp < GameState.PlayerMaxHp) { GameState.Heal(1); FloatingText.Add(above, "+1 HEART", new Color(1f, 0.4f, 0.45f), 16, 1.2f); }
            else { GameState.Score += 50; FloatingText.Add(above, "+50", new Color(1f, 0.85f, 0.25f), 16, 1.2f); }
            reopenAt = Time.time + 30f;
        }
        else
        {
            GameState.Score += 250;
            FloatingText.Add(above, "+250 TREASURE", new Color(1f, 0.85f, 0.25f), 16, 1.5f);
        }
    }
}

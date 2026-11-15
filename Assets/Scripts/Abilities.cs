using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Two movement abilities, both on cooldown like the guard:
///   SHIFT  ring dash - the ten rings burst the player forward in a short, untouchable rush.
///   CTRL   ring flight - the rings lift the player over walls, rocks and every other obstacle for a few seconds
///          (the map edge still stops them). Enemies cannot touch a flying player.
/// </summary>
public class Abilities : MonoBehaviour
{
    public const float DashTime = 0.22f, DashSpeed = 17f, DashCooldown = 3.5f;
    public const float FlyTime = 4f, FlyCooldown = 10f, FlySpeedMul = 1.3f, LandGrace = 2.5f;

    public static bool Dashing { get; private set; }
    public static bool Flying { get; private set; }
    public static bool Landing { get; private set; }                // flight over, waiting for clear ground
    public static Vector2 DashDir { get; private set; }
    public static float FlyLeft;

    static float dashReadyAt, flyReadyAt;
    public static float DashCooldownLeft { get { return Mathf.Max(0f, dashReadyAt - Time.time); } }
    public static float FlyCooldownLeft { get { return Mathf.Max(0f, flyReadyAt - Time.time); } }
    public static bool DashReady { get { return CanAct && !Dashing && DashCooldownLeft <= 0f; } }
    public static bool FlyReady { get { return CanAct && !Flying && !Landing && FlyCooldownLeft <= 0f; } }
    static bool CanAct { get { return GameState.Active && !GameState.Over && inst != null && inst.player != null; } }

    static Abilities inst;
    Transform player;
    Rigidbody2D rb;
    SpriteRenderer aura, shadow;
    readonly List<Collider2D> cols = new List<Collider2D>();
    readonly List<SpriteRenderer> srs = new List<SpriteRenderer>();
    readonly List<int> srLayers = new List<int>();
    float dashLeft, landT, trailT;
    Vector2 facing = Vector2.down, lastSafe;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Dashing = Flying = Landing = false; FlyLeft = 0f; dashReadyAt = flyReadyAt = 0f; inst = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { if (inst == null) new GameObject("Abilities").AddComponent<Abilities>(); }

    void Awake()
    {
        inst = this;
        GameState.OnRunStart += Reset;
        GameState.OnReturnToMenu += Reset;
    }

    void OnDestroy() { GameState.OnRunStart -= Reset; GameState.OnReturnToMenu -= Reset; if (inst == this) inst = null; }

    void Reset()
    {
        if (Flying || Landing) EndFlight(false);
        Dashing = false; dashLeft = 0f; dashReadyAt = flyReadyAt = 0f;
    }

    void Update()
    {
        if (player == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) { player = p.transform; rb = p.GetComponent<Rigidbody2D>(); }
            return;
        }
        float dt = Time.deltaTime;
        if (!GameState.Active || GameState.Over) { if (Flying || Landing) EndFlight(false); Dashing = false; HideFx(); return; }

        Vector2 mv = InputHub.Move();
        if (mv.sqrMagnitude > 0.01f) facing = mv.normalized;

        // ---- dash
        if (!Dashing && DashReady && (InputHub.Pressed(KeyCode.LeftShift) || InputHub.Pressed(KeyCode.RightShift)))
        {
            Dashing = true; dashLeft = DashTime;
            DashDir = mv.sqrMagnitude > 0.01f ? mv.normalized : facing;
            dashReadyAt = Time.time + DashCooldown;
            Vector3 at = player.position + Vector3.up * 0.4f * GS.S;
            Vfx.Burst(at, new Color(1f, 0.85f, 0.3f), 14, 5f * GS.S, 0.14f, 0.35f);
            Sfx.Play("ring_throw", 0.7f, 1.3f);
            CameraRig.Shake(0.08f, 0.1f);
        }
        if (Dashing)
        {
            dashLeft -= dt;
            trailT -= dt;
            if (trailT <= 0f)
            {
                trailT = 0.025f;
                Vfx.Burst(player.position + Vector3.up * 0.35f * GS.S, new Color(1f, 0.82f, 0.25f, 0.9f), 2, 1.2f * GS.S, 0.2f, 0.3f);
                Vfx.Burst(player.position + Vector3.up * 0.35f * GS.S, new Color(1f, 1f, 1f, 0.7f), 1, 0.6f * GS.S, 0.12f, 0.2f);
            }
            if (dashLeft <= 0f) Dashing = false;
        }

        // ---- flight
        if (FlyReady && (InputHub.Pressed(KeyCode.LeftControl) || InputHub.Pressed(KeyCode.RightControl))) StartFlight();
        if (Flying)
        {
            FlyLeft -= dt;
            if (FlyLeft <= 0f) { Flying = false; Landing = true; landT = 0f; }
        }
        if (Landing)
        {
            landT += dt;
            Vector2 pos = player.position;
            if (!Blocked(pos)) EndFlight(true);
            else if (landT >= LandGrace) { if (rb != null) rb.position = lastSafe; player.position = lastSafe; EndFlight(true); }
        }
        if (Flying || Landing)
        {
            trailT -= dt;
            if (trailT <= 0f)
            {
                trailT = 0.06f;
                Vfx.Burst(player.position, new Color(0.7f, 0.92f, 1f, 0.8f), 1, 0.8f * GS.S, 0.14f, 0.45f);
            }
        }
        else
        {
            Vector2 pos = player.position;
            if (!Blocked(pos)) lastSafe = pos;
        }
        UpdateFx();
    }

    // ------------------------------------------------------------------ flight start / end
    void StartFlight()
    {
        Flying = true; Landing = false; FlyLeft = FlyTime;
        flyReadyAt = Time.time + FlyTime + FlyCooldown;
        cols.Clear(); srs.Clear(); srLayers.Clear();
        player.GetComponentsInChildren(true, cols);
        foreach (var c in cols) if (c != null && !c.isTrigger) c.excludeLayers = ~0;       // ghost through everything solid
        player.GetComponentsInChildren(true, srs);
        int top = SortingLayer.NameToID(GameState.TopLayer);
        foreach (var s in srs) { srLayers.Add(s.sortingLayerID); s.sortingLayerID = top; }   // drawn above walls and trees
        Vfx.Burst(player.position, new Color(0.7f, 0.92f, 1f), 18, 4f * GS.S, 0.16f, 0.5f);
        Vfx.Shockwave(player.position, new Color(0.7f, 0.92f, 1f), 2.2f * GS.S, 0.4f);
        Sfx.Play("ring_throw", 0.8f, 0.8f);
    }

    void EndFlight(bool fx)
    {
        Flying = false; Landing = false;
        foreach (var c in cols) if (c != null) c.excludeLayers = 0;
        for (int i = 0; i < srs.Count && i < srLayers.Count; i++) if (srs[i] != null) srs[i].sortingLayerID = srLayers[i];
        // land on the level that actually has floor under us (flying up from the ground floor must not leave us inside it)
        if (player != null)
        {
            string fl = Nav.FloorAt(player.position);
            int phys = LayerMask.NameToLayer(fl);
            if (phys >= 0) player.gameObject.layer = phys;
            foreach (var sr in player.GetComponentsInChildren<SpriteRenderer>(true)) sr.sortingLayerName = fl;
        }
        cols.Clear(); srs.Clear(); srLayers.Clear();
        if (fx && player != null)
        {
            Vfx.Burst(player.position, new Color(0.85f, 0.95f, 1f), 12, 3f * GS.S, 0.14f, 0.4f);
            Sfx.Play("ring_clang", 0.5f, 0.7f);
        }
        HideFx();
    }

    /// <summary>Is `p` inside something solid (a wall, rock, tree trunk ...)? Creatures, rings and the player do not count.</summary>
    bool Blocked(Vector2 p)
    {
        float r = 0.3f * GS.S;
        var hits = Physics2D.OverlapCircleAll(p, r);
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h == null || h.isTrigger) continue;
            if (h.transform == player || h.transform.IsChildOf(player)) continue;
            if (h.GetComponentInParent<Enemy>() != null || h.GetComponentInParent<TenRing>() != null) continue;
            if (h.attachedRigidbody != null && h.attachedRigidbody.bodyType != RigidbodyType2D.Static) continue;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ visuals: shadow on the ground + glow while airborne
    void UpdateFx()
    {
        bool air = (Flying || Landing) && player != null;
        if (!air) { HideFx(); return; }
        if (aura == null)
        {
            aura = new GameObject("FlightGlow").AddComponent<SpriteRenderer>();
            aura.sprite = GameAssets.Glow;
            shadow = new GameObject("FlightShadow").AddComponent<SpriteRenderer>();
            shadow.sprite = GameAssets.Glow;
            shadow.color = new Color(0f, 0f, 0f, 0.35f);
        }
        aura.enabled = shadow.enabled = true;
        float t = Time.time;
        float blink = Landing ? (0.6f + 0.4f * Mathf.Sin(t * 20f)) : (FlyLeft < 1f ? 0.55f + 0.45f * Mathf.Sin(t * 22f) : 1f);
        aura.transform.position = player.position + Vector3.up * 0.4f * GS.S;
        aura.transform.localScale = Vector3.one * 2.4f * GS.S * (1f + 0.06f * Mathf.Sin(t * 8f));
        aura.color = new Color(0.65f, 0.9f, 1f, 0.38f * blink);
        shadow.transform.position = player.position + Vector3.down * 0.55f * GS.S;
        shadow.transform.localScale = new Vector3(1.3f, 0.5f, 1f) * GS.S;
        int top = SortingLayer.NameToID(GameState.TopLayer);
        aura.sortingLayerID = top; aura.sortingOrder = 1;
        shadow.sortingLayerID = SortingLayer.NameToID(GameState.ActorLayer); shadow.sortingOrder = -5;
    }

    void HideFx() { if (aura != null) { aura.enabled = false; shadow.enabled = false; } }
}

using System.Collections.Generic;
using UnityEngine;

public enum ShipKind { Player, Sloop, Gunboat, Frigate, Mortar, Galleon }

/// <summary>Static stats of a ship type. Sizes are world units (sprites are 16 px / unit).</summary>
public class ShipSpec
{
    public ShipKind kind;
    public string name, art;
    public float halfLen, radius;       // collision capsule: total half length, and its thickness
    public float maxSpeed, accel, turn; // units/s, units/s^2, degrees/s
    public float hp, ram;               // hull points, damage dealt to another ship by ramming
    public int score;

    static readonly Dictionary<ShipKind, ShipSpec> all = new Dictionary<ShipKind, ShipSpec>();

    public static ShipSpec Get(ShipKind k)
    {
        ShipSpec s;
        if (all.TryGetValue(k, out s)) return s;
        s = new ShipSpec { kind = k };
        switch (k)
        {
            case ShipKind.Player:  s.name = "RING SHIP"; s.art = "player";  s.halfLen = 2.9f; s.radius = 1.0f; s.maxSpeed = 8.0f; s.accel = 3.4f; s.turn = 62f; s.hp = 140f; s.ram = 10f; s.score = 0; break;
            case ShipKind.Sloop:   s.name = "RAIDER";    s.art = "sloop";   s.halfLen = 1.4f; s.radius = 0.5f; s.maxSpeed = 6.4f; s.accel = 6.0f; s.turn = 105f; s.hp = 22f;  s.ram = 8f;  s.score = 40; break;
            case ShipKind.Gunboat: s.name = "GUNBOAT";   s.art = "gunboat"; s.halfLen = 1.8f; s.radius = 0.7f; s.maxSpeed = 4.4f; s.accel = 3.6f; s.turn = 78f; s.hp = 40f;  s.ram = 5f;  s.score = 70; break;
            case ShipKind.Frigate: s.name = "FRIGATE";   s.art = "frigate"; s.halfLen = 2.5f; s.radius = 0.9f; s.maxSpeed = 3.6f; s.accel = 2.6f; s.turn = 55f;  s.hp = 90f;  s.ram = 9f;  s.score = 130; break;
            case ShipKind.Mortar:  s.name = "MORTAR BARGE"; s.art = "mortar"; s.halfLen = 1.6f; s.radius = 1.0f; s.maxSpeed = 2.6f; s.accel = 2.2f; s.turn = 48f; s.hp = 60f; s.ram = 3f;  s.score = 100; break;
            default:               s.name = "DREAD GALLEON"; s.art = "galleon"; s.halfLen = 4.6f; s.radius = 1.6f; s.maxSpeed = 3.2f; s.accel = 1.8f; s.turn = 34f; s.hp = 950f; s.ram = 18f; s.score = 1500; break;
        }
        all[k] = s;
        return s;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); }
}

/// <summary>Base ship: arcade movement, capsule hull, damage / sinking, wake, smoke and the sprite layers.</summary>
public class NavalShip : MonoBehaviour
{
    public ShipSpec spec;
    public int team;                       // 0 = the player's side, 1 = pirates
    public Vector2 pos;
    public float angle, speed;             // heading in degrees (0 = east); speed = forward speed along the keel
    public float slip, angVel, throttle, rudder, heel;   // sideways drift, turn rate (deg/s), helm state, visual lean
    public float hp, maxHp;
    public bool dead;
    public Vector2 push;                   // knock-back velocity
    public float nextRamAt;
    public Vector2 Vel { get; private set; }

    protected int baseOrder, rigOffset = 4;
    protected SpriteRenderer hullSr, rigSr;
    protected Transform visual, rigT;
    protected float rigWarn;               // > 0: the rigging pulses red (attack telegraph)
    float flashT, bobSeed, sinkT, smokeT, lastRockHit;
    TrailRenderer wakeA, wakeB;
    Transform hpRoot; SpriteRenderer hpBg, hpFg;

    public Vector2 Fwd { get { float r = angle * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(r), Mathf.Sin(r)); } }
    public Vector2 Left { get { Vector2 f = Fwd; return new Vector2(-f.y, f.x); } }
    public Vector2 Local(float x, float y) { return pos + Fwd * x + Left * y; }
    public Vector2 SegA { get { return pos - Fwd * Mathf.Max(0f, spec.halfLen - spec.radius); } }
    public Vector2 SegB { get { return pos + Fwd * Mathf.Max(0f, spec.halfLen - spec.radius); } }

    public static Vector2 ClosestOnSeg(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a; float l = ab.sqrMagnitude;
        if (l < 1e-6f) return a;
        return a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / l);
    }

    /// <summary>Distance from a point to the hull (negative = inside).</summary>
    public float DistTo(Vector2 p) { return Vector2.Distance(p, ClosestOnSeg(SegA, SegB, p)) - spec.radius; }

    // ------------------------------------------------------------------ setup
    public void Init(ShipSpec s, int teamId, Vector2 p, float ang, int order, int rigOff)
    {
        spec = s; team = teamId; pos = p; angle = ang; baseOrder = order; rigOffset = rigOff;
        maxHp = hp = s.hp * (teamId == 1 ? Naval.HpMul : 1f);
        bobSeed = Random.value * 10f;
        name = "Ship " + s.name;
        int layer = SortingLayer.NameToID(GameState.ActorLayer);
        visual = new GameObject("Visual").transform; visual.SetParent(transform, false);
        hullSr = AddSr("Hull", "ship_" + s.art + "_hull", layer, order);
        rigSr = AddSr("Rig", "ship_" + s.art + "_rig", layer, order + rigOff);
        rigT = rigSr.transform;
        wakeA = MakeWake(new Vector2(-s.halfLen * 0.72f, s.radius * 0.5f), order);
        wakeB = MakeWake(new Vector2(-s.halfLen * 0.72f, -s.radius * 0.5f), order);
        if (team == 1 && s.kind != ShipKind.Galleon) MakeBar();
        OnInit();
        ApplyTransform();
        wakeA.Clear(); wakeB.Clear();
    }

    protected virtual void OnInit() { }

    SpriteRenderer AddSr(string n, string art, int layer, int order)
    {
        var go = new GameObject(n);
        go.transform.SetParent(visual, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = NavalArt.Get(art);
        sr.sortingLayerID = layer;
        sr.sortingOrder = order;
        return sr;
    }

    TrailRenderer MakeWake(Vector2 local, int order)
    {
        var go = new GameObject("Wake");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 1.0f;
        tr.minVertexDistance = 0.14f;
        tr.widthMultiplier = 0.26f + spec.radius * 0.22f;
        tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 2.3f));
        tr.material = Vfx.ParticleMaterial;
        tr.sortingLayerName = GameState.ActorLayer;
        tr.sortingOrder = order - 70;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.92f, 0.98f, 1f), 0f), new GradientColorKey(new Color(0.55f, 0.8f, 0.95f), 1f) },
            new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;
        tr.emitting = false;
        return tr;
    }

    void MakeBar()
    {
        hpRoot = new GameObject("HP").transform;
        hpRoot.SetParent(Naval.Root, false);
        int top = SortingLayer.NameToID(GameState.TopLayer);
        var bg = new GameObject("bg"); bg.transform.SetParent(hpRoot, false);
        hpBg = bg.AddComponent<SpriteRenderer>(); hpBg.sprite = GameAssets.WhiteLeftPivot; hpBg.color = new Color(0f, 0f, 0f, 0.7f);
        hpBg.sortingLayerID = top; hpBg.sortingOrder = 450;
        var fg = new GameObject("fg"); fg.transform.SetParent(hpRoot, false);
        hpFg = fg.AddComponent<SpriteRenderer>(); hpFg.sprite = GameAssets.WhiteLeftPivot;
        hpFg.sortingLayerID = top; hpFg.sortingOrder = 451;
        hpRoot.gameObject.SetActive(false);
    }

    void OnDestroy() { if (hpRoot != null) Destroy(hpRoot.gameObject); }

    // ------------------------------------------------------------------ movement
    protected virtual float SpeedMul { get { return 1f; } }

    /// <summary>Turn toward `want` and accelerate; `throttle` 0..1.</summary>
    protected void Steer(Vector2 want, float throttleCmd, float dt)
    {
        float maxS = spec.maxSpeed * SpeedMul;
        float rate = 0f;
        float align = 1f;
        if (want.sqrMagnitude > 0.01f && throttleCmd > 0.01f)
        {
            float target = Mathf.Atan2(want.y, want.x) * Mathf.Rad2Deg;
            float diff = Mathf.DeltaAngle(angle, target);
            float flow = 0.4f + 0.6f * Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(0.1f, maxS * 0.6f));
            rate = Mathf.Clamp(diff * 3.2f, -spec.turn * flow, spec.turn * flow);
            align = Mathf.Clamp(Mathf.Cos(diff * Mathf.Deg2Rad) * 0.5f + 0.62f, 0.25f, 1f);
        }
        angVel = Mathf.MoveTowards(angVel, rate, spec.turn * 3f * dt);
        angle += angVel * dt;
        float tgt = (want.sqrMagnitude > 0.01f && throttleCmd > 0.01f) ? maxS * throttleCmd * align : 0f;
        float a = tgt > speed ? spec.accel : spec.accel * 0.6f;
        speed = Mathf.MoveTowards(speed, tgt, a * dt);
        KeelPhysics(dt);
    }

    /// <summary>Player helm: throttle (-0.35..1) sets the engine order, rudder (-1..1) turns the ship in proportion to water flow.</summary>
    protected void Helm(float throttleOrder, float rudderCmd, float dt, float boost)
    {
        float maxS = spec.maxSpeed * SpeedMul;
        float tgt = throttleOrder >= 0f ? maxS * throttleOrder : spec.maxSpeed * 0.4f * throttleOrder;
        float a = tgt > speed ? spec.accel * (1f + (boost - 1f) * 2f) : spec.accel * 0.7f;
        if (tgt < speed && tgt >= 0f) a = spec.accel * 0.45f;        // coasting down is slow: the ship has momentum
        speed = Mathf.MoveTowards(speed, tgt, a * dt);
        float flow = Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(0.1f, spec.maxSpeed * 0.55f));
        float dirSign = speed >= -0.05f ? 1f : -1f;
        float wantRate = rudderCmd * spec.turn * Mathf.Lerp(0.14f, 1f, flow) * dirSign;
        angVel = Mathf.MoveTowards(angVel, wantRate, spec.turn * 1.7f * dt);
        angle += angVel * dt;
        // turning bleeds speed (hard rudder = drag)
        speed -= speed * Mathf.Abs(angVel) / Mathf.Max(1f, spec.turn) * 0.32f * dt;
        KeelPhysics(dt);
    }

    /// <summary>Centrifugal drift: the hull skids outwards in a turn and the keel slowly bites it back.</summary>
    void KeelPhysics(float dt)
    {
        slip += -angVel * Mathf.Deg2Rad * speed * 0.22f * dt;
        slip *= Mathf.Exp(-2.1f * dt);
        slip = Mathf.Clamp(slip, -spec.maxSpeed * 0.5f, spec.maxSpeed * 0.5f);
        heel = Mathf.Lerp(heel, Mathf.Clamp(-angVel / Mathf.Max(1f, spec.turn) * Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(0.1f, spec.maxSpeed)) * 7f, -7f, 7f), 1f - Mathf.Exp(-4f * dt));
    }

    public virtual void Think(float dt) { }

    /// <summary>Integrates movement and animates. Called once per frame by Naval after Think().</summary>
    public void Tick(float dt)
    {
        if (dead) { SinkStep(dt); return; }
        Vector2 old = pos;
        pos += Fwd * speed * dt + Left * slip * dt + push * dt;
        push = Vector2.Lerp(push, Vector2.zero, Mathf.Clamp01(4f * dt));
        Naval.Contain(this);
        Vel = dt > 0f ? (pos - old) / dt : Vector2.zero;

        // hit flash + telegraph pulse
        flashT -= dt;
        if (rigWarn > 0f) rigWarn -= dt;
        float hfrac = hp / Mathf.Max(1f, maxHp);
        float soot = hfrac < 0.75f ? (0.75f - hfrac) / 0.75f * 0.42f : 0f;
        Color c = Color.Lerp(Color.white, new Color(0.62f, 0.58f, 0.56f), soot);
        if (flashT > 0f) c = Color.Lerp(Color.white, new Color(1f, 0.4f, 0.35f), Mathf.Clamp01(flashT / 0.12f));
        if (rigWarn > 0f) c = Color.Lerp(c, new Color(1f, 0.3f, 0.2f), 0.5f + 0.5f * Mathf.Sin(Time.time * 30f));
        hullSr.color = c; rigSr.color = c;

        // damaged ships smoke and burn
        float frac = hp / Mathf.Max(1f, maxHp);
        if (frac < 0.45f)
        {
            smokeT -= dt;
            if (smokeT <= 0f)
            {
                smokeT = Mathf.Lerp(0.1f, 0.35f, frac / 0.45f);
                Vector2 at = Local(Random.Range(-spec.halfLen * 0.6f, spec.halfLen * 0.6f), Random.Range(-spec.radius * 0.5f, spec.radius * 0.5f));
                Vfx.Burst(at, new Color(0.22f, 0.22f, 0.24f, 0.8f), 3, 1.1f, 0.3f, 0.9f);
                if (frac < 0.22f) Vfx.Burst(at, new Color(1f, 0.55f, 0.15f), 2, 1.6f, 0.16f, 0.4f);
                // leaking: foam boiling out along the waterline
                Vec2Foam(frac);
            }
        }

        bool wake = Mathf.Abs(speed) > 0.9f;
        wakeA.emitting = wake; wakeB.emitting = wake;
        ApplyTransform();
        UpdateBar();
    }

    void Vec2Foam(float frac)
    {
        float side = Random.value < 0.5f ? -1f : 1f;
        Vector2 at = Local(Random.Range(-spec.halfLen * 0.7f, spec.halfLen * 0.7f), side * spec.radius * 0.95f);
        Vfx.Burst(at, new Color(0.88f, 0.96f, 1f, 0.7f), 2, 0.9f, 0.14f, 0.55f);
    }

    void ApplyTransform()
    {
        transform.position = new Vector3(pos.x, pos.y, 0f);
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        float t = Time.time + bobSeed;
        visual.localPosition = new Vector3(0f, Mathf.Sin(t * 1.7f) * 0.03f, 0f);
        visual.localScale = new Vector3(1f, 1f + Mathf.Sin(t * 1.7f + 1f) * 0.02f, 1f);
        rigT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.1f) * 1.3f + heel * 0.5f);
        visual.localPosition += new Vector3(0f, heel * 0.012f, 0f);
    }

    void UpdateBar()
    {
        if (hpRoot == null) return;
        bool show = hp < maxHp - 0.01f && !dead;
        if (hpRoot.gameObject.activeSelf != show) hpRoot.gameObject.SetActive(show);
        if (!show) return;
        float w = Mathf.Max(1.0f, spec.halfLen * 0.9f);
        hpRoot.position = new Vector3(pos.x - w / 2f, pos.y + spec.radius + 0.85f, 0f);
        hpBg.transform.localPosition = new Vector3(-0.04f, 0f, 0f);
        hpBg.transform.localScale = new Vector3(w + 0.08f, 0.2f, 1f);
        float k = Mathf.Clamp01(hp / maxHp);
        hpFg.transform.localScale = new Vector3(w * k, 0.13f, 1f);
        hpFg.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.35f, 0.9f, 0.3f), k);
    }

    // ------------------------------------------------------------------ damage
    public void AddPush(Vector2 v) { push += v; }
    public void ScrapeRock(Vector2 at)
    {
        if (Time.time - lastRockHit < 0.7f) return;
        lastRockHit = Time.time;
        Vfx.Burst(at, new Color(0.75f, 0.75f, 0.8f), 7, 3f, 0.12f, 0.3f);
        Sfx.Play("naval_crack", 0.4f, 0.8f);
        if (Mathf.Abs(speed) > 2.5f) Damage(2.5f, at);
    }

    public void Damage(float amt, Vector2 from)
    {
        if (dead || amt <= 0f) return;
        if (team == 0 && Boosts.Aegis)
        {
            Vfx.Burst(from, new Color(0.6f, 0.8f, 1f), 6, 3f, 0.1f, 0.3f);
            return;
        }
        hp -= amt;
        flashT = 0.12f;
        if (GameSettings.DamageNumbers)
            FloatingText.Add(pos + Random.insideUnitCircle * 0.5f + Vector2.up * 0.4f, Mathf.CeilToInt(amt).ToString(),
                team == 0 ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.93f, 0.55f), team == 0 ? 16 : 13, 0.7f);
        AddScar(from, amt);
        OnHit(amt, from);
        if (hp <= 0f) { hp = 0f; Die(); }
    }

    // ---- battle damage: charred holes that stay on the hull, flying splinters, hull darkening and leaks
    readonly List<SpriteRenderer> scars = new List<SpriteRenderer>();

    void AddScar(Vector2 world, float amt)
    {
        Vfx.Burst(world, new Color(0.62f, 0.44f, 0.25f), 4 + Mathf.Min(8, (int)(amt * 0.4f)), 3.4f, 0.1f, 0.45f);
        Vfx.Burst(world, new Color(1f, 0.75f, 0.3f), 2, 2.2f, 0.08f, 0.2f);
        if (visual == null || hullSr == null || amt < 1f) return;
        Vector3 lp3 = transform.InverseTransformPoint(new Vector3(world.x, world.y, 0f));
        float lx = Mathf.Clamp(lp3.x, -spec.halfLen * 0.8f, spec.halfLen * 0.8f);
        float ly = Mathf.Clamp(lp3.y, -spec.radius * 0.7f, spec.radius * 0.7f);
        var go = new GameObject("Scar");
        go.transform.SetParent(visual, false);
        go.transform.localPosition = new Vector3(lx, ly, -0.002f);
        float sz = Mathf.Clamp(0.14f + amt * 0.012f, 0.14f, 0.5f) * Mathf.Clamp(spec.radius, 0.6f, 1.4f);
        go.transform.localScale = new Vector3(sz * Random.Range(0.8f, 1.3f), sz * Random.Range(0.8f, 1.3f), 1f);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = NavalArt.DiscSprite;
        sr.color = new Color(0.04f, 0.03f, 0.03f, 0.62f);
        sr.sortingLayerID = hullSr.sortingLayerID;
        sr.sortingOrder = hullSr.sortingOrder + 1;
        scars.Add(sr);
        if (scars.Count > 16) { if (scars[0] != null) Destroy(scars[0].gameObject); scars.RemoveAt(0); }
    }

    protected virtual void OnHit(float amt, Vector2 from) { }

    protected virtual void Die()
    {
        dead = true; sinkT = 0f;
        wakeA.emitting = false; wakeB.emitting = false;
        bool big = spec.radius > 0.85f;
        Vfx.Burst(pos, new Color(1f, 0.7f, 0.2f), big ? 36 : 20, 6f, 0.22f, 0.8f);
        Vfx.Burst(pos, new Color(0.45f, 0.3f, 0.2f), big ? 30 : 14, 5f, 0.18f, 1.0f);
        Vfx.Shockwave(pos, new Color(1f, 0.6f, 0.2f), spec.radius * 3f + 1f, 0.5f);
        Sfx.Play("naval_boom", big ? 1f : 0.7f, big ? 0.8f : 1.1f);
        Naval.Shake(big ? 0.7f : 0.3f, big ? 0.7f : 0.35f);
        if (hpRoot != null) hpRoot.gameObject.SetActive(false);
        Naval.ShipSunk(this);
    }

    void SinkStep(float dt)
    {
        sinkT += dt;
        float k = Mathf.Clamp01(sinkT / (spec.radius > 0.85f ? 2.4f : 1.5f));
        speed *= 0.97f;
        pos += Fwd * speed * dt;
        angle += 14f * dt;
        ApplyTransform();
        visual.localScale = new Vector3(1f - 0.22f * k, 1f - 0.22f * k, 1f);
        Color c = new Color(1f - 0.5f * k, 1f - 0.5f * k, 1f - 0.4f * k, 1f - k);
        hullSr.color = c; rigSr.color = c;
        if (Random.value < 0.35f) Vfx.Burst(Local(Random.Range(-spec.halfLen, spec.halfLen) * 0.7f, Random.Range(-1f, 1f) * spec.radius * 0.6f), new Color(0.9f, 0.97f, 1f, 0.7f), 2, 0.8f, 0.14f, 0.6f);
        if (k >= 1f) Destroy(gameObject);
    }
}

// ====================================================================================================================
//  the player's ship + its crew
// ====================================================================================================================
public class Crew
{
    public Transform root;
    public SpriteRenderer body, gun;
    public Vector2 local;
    public float cd, recoil, aim, nextChat;
    public int index;
}

public class PlayerShip : NavalShip
{
    public static readonly Vector2[] Stations =
    {
        new Vector2(-0.25f, 0.875f), new Vector2(-0.25f, -0.875f), new Vector2(2.125f, 0f),
        new Vector2(0.56f, 0.875f), new Vector2(0.56f, -0.875f), new Vector2(-2.125f, 0f)
    };
    static readonly string[] Shouts = { "FIRE!", "AYE AYE!", "FOR THE RINGS!", "READY!", "BROADSIDE!", "SHE'S SINKING!" };

    public readonly List<Crew> crew = new List<Crew>();
    public float fireRate = 1f, dmgMul = 1f, regenPerSec = 0f, speedUpgrade = 1f, ramMul = 1f, ramTaken = 1f;
    public int pierceBonus;
    public float dashLeft;
    public bool Dashing { get { return dashLeft > 0f; } }
    public Vector2 CaptainPos { get { return Local(-0.75f, 0f); } }

    protected override float SpeedMul { get { return (Dashing ? 2.05f : 1f) * Boosts.Move * speedUpgrade; } }

    public override void Think(float dt)
    {
        if (dead) return;
        Vector2 mv = Naval.AcceptInput ? InputHub.Move() : Vector2.zero;
        // W/S (stick up/down) move the engine telegraph, A/D (stick left/right) the rudder
        float tUp = Mathf.Abs(mv.y) > 0.2f ? mv.y : 0f;
        throttle = Mathf.Clamp(throttle + tUp * dt * 0.75f, -0.35f, 1f);
        if (Naval.AcceptInput && InputHub.Held(KeyCode.X)) throttle = Mathf.MoveTowards(throttle, 0f, 2.2f * dt);   // X = all stop
        float rudTarget = Mathf.Abs(mv.x) > 0.15f ? -Mathf.Clamp(mv.x, -1f, 1f) : 0f;
        rudder = Mathf.MoveTowards(rudder, rudTarget, (rudTarget == 0f ? 3.2f : 2.4f) * dt);
        float order = throttle;
        float boost = 1f;
        if (Dashing) { dashLeft -= dt; order = 1f; boost = 2f; }
        Helm(order, rudder, dt, boost);
        if (regenPerSec > 0f && hp < maxHp) hp = Mathf.Min(maxHp, hp + regenPerSec * dt);
        UpdateCrew(dt);
    }

    public void AddCrew()
    {
        if (crew.Count >= Stations.Length) return;
        var c = new Crew { index = crew.Count };
        c.local = Stations[c.index];
        int layer = SortingLayer.NameToID(GameState.ActorLayer);
        var go = new GameObject("Crew " + (c.index + 1));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(c.local.x, c.local.y, 0f);
        c.root = go.transform;
        var b = new GameObject("Body"); b.transform.SetParent(c.root, false);
        c.body = b.AddComponent<SpriteRenderer>();
        c.body.sprite = NavalArt.Get("crew_" + (char)('a' + (c.index % 4)));
        c.body.sortingLayerID = layer; c.body.sortingOrder = baseOrder + 10;
        var g = new GameObject("Gun"); g.transform.SetParent(c.root, false);
        g.transform.localPosition = new Vector3(0.25f, 0f, 0f);
        c.gun = g.AddComponent<SpriteRenderer>();
        c.gun.sprite = NavalArt.Get("swivel_gun", 6f / 22f);
        c.gun.sortingLayerID = layer; c.gun.sortingOrder = baseOrder + 11;
        c.aim = angle + (c.local.y > 0.1f ? 90f : (c.local.y < -0.1f ? -90f : (c.local.x > 0f ? 0f : 180f)));
        c.cd = Random.Range(0.3f, 1.0f);
        c.nextChat = Time.time + Random.Range(4f, 9f);
        crew.Add(c);
        c.root.rotation = Quaternion.Euler(0f, 0f, c.aim);
        Vfx.Burst(Local(c.local.x, c.local.y), new Color(1f, 0.9f, 0.5f), 8, 2.5f, 0.12f, 0.4f);
    }

    void UpdateCrew(float dt)
    {
        bool shooting = Naval.AcceptInput && Naval.Phase != NavalPhase.Intro;
        for (int i = 0; i < crew.Count; i++)
        {
            var c = crew[i];
            Vector2 st = Local(c.local.x, c.local.y);
            NavalShip tgt = shooting ? Naval.NearestEnemy(st, 13.5f) : null;
            float desired;
            if (tgt != null)
            {
                Vector2 aimPt = Naval.Lead(st, tgt, 14f);
                desired = Mathf.Atan2(aimPt.y - st.y, aimPt.x - st.x) * Mathf.Rad2Deg;
            }
            else desired = angle + (c.local.y > 0.1f ? 90f : (c.local.y < -0.1f ? -90f : (c.local.x > 0f ? 0f : 180f)));
            c.aim = Mathf.MoveTowards(c.aim, c.aim + Mathf.DeltaAngle(c.aim, desired), 440f * dt);
            c.root.rotation = Quaternion.Euler(0f, 0f, c.aim);
            c.recoil = Mathf.MoveTowards(c.recoil, 0f, 1.5f * dt);
            c.gun.transform.localPosition = new Vector3(0.25f - c.recoil, 0f, 0f);
            c.cd -= dt;
            if (tgt != null && c.cd <= 0f && Mathf.Abs(Mathf.DeltaAngle(c.aim, desired)) < 9f)
            {
                float a = c.aim * Mathf.Deg2Rad + Random.Range(-2.5f, 2.5f) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 muzzle = st + dir * 0.9f;
                Naval.FireBall(muzzle, dir, 14f, 4.5f * dmgMul * Boosts.Damage, 0, 1.7f);
                c.cd = (1.15f / fireRate) * Boosts.FireDelay * Random.Range(0.9f, 1.15f);
                c.recoil = 0.22f;
                Vfx.Burst(muzzle, new Color(0.85f, 0.85f, 0.88f, 0.9f), 4, 1.4f, 0.22f, 0.5f);
                Vfx.Burst(muzzle, new Color(1f, 0.8f, 0.35f), 3, 3.2f, 0.12f, 0.14f);
                Sfx.Play("naval_cannon", 0.26f, Random.Range(1.15f, 1.55f));
                if (Time.time >= c.nextChat)
                {
                    c.nextChat = Time.time + Random.Range(7f, 14f);
                    FloatingText.Add(st + Vector2.up * 0.7f, Shouts[Random.Range(0, 4)], new Color(1f, 1f, 1f), 11, 0.9f);
                }
            }
        }
    }

    protected override void OnHit(float amt, Vector2 from)
    {
        Naval.Shake(Mathf.Clamp(amt * 0.03f, 0.12f, 0.5f), 0.2f);
        Sfx.Play("hurt", 0.55f, 0.85f);
        Sfx.Play("naval_crack", 0.5f, Random.Range(0.8f, 1.1f));
        Vfx.Burst(from, new Color(0.55f, 0.35f, 0.2f), 8, 3.5f, 0.14f, 0.5f);
        if (hp / maxHp < 0.3f && Random.value < 0.4f && crew.Count > 0)
            FloatingText.Add(pos + Vector2.up * 1.2f, Shouts[5], new Color(1f, 0.6f, 0.4f), 12, 1.0f);
    }
}

// ====================================================================================================================
//  pirates
// ====================================================================================================================
public class EnemyShip : NavalShip
{
    struct Pending { public float at, rel, speed, dmg; public Vector2 local; }

    readonly List<Pending> queue = new List<Pending>();
    float fireAt, altAt, mortarAt, summonAt, ringAt, orbit, flipAt, ringSpin, tubePulse;
    int bossPhase = -1;
    Transform tub;

    public bool IsBoss { get { return spec.kind == ShipKind.Galleon; } }

    protected override void OnInit()
    {
        orbit = Random.value < 0.5f ? 1f : -1f;
        flipAt = Time.time + Random.Range(14f, 26f);
        fireAt = Time.time + Random.Range(1.4f, 2.6f);
        altAt = Time.time + Random.Range(2.5f, 4.5f);
        mortarAt = Time.time + Random.Range(2.5f, 4f);
        summonAt = Time.time + 14f;
        ringAt = Time.time + 8f;
        if (spec.kind == ShipKind.Mortar)
        {
            var go = new GameObject("Tub");
            go.transform.SetParent(visual, false);
            go.transform.localPosition = new Vector3(0.1f, 0f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = NavalArt.Get("mortar_tub");
            sr.sortingLayerID = SortingLayer.NameToID(GameState.ActorLayer);
            sr.sortingOrder = baseOrder + 2;
            tub = go.transform;
        }
    }

    static Vector2 Dir(float deg) { float r = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(r), Mathf.Sin(r)); }

    public override void Think(float dt)
    {
        if (tub != null)
        {
            tubePulse = Mathf.MoveTowards(tubePulse, 0f, 2.5f * dt);
            float s = 1f + tubePulse * 0.35f;
            tub.localScale = new Vector3(s, s, 1f);
        }
        var P = Naval.Player;
        if (dead) return;
        if (P == null || P.dead || !Naval.EnemiesActive) { Steer(Vector2.zero, 0f, dt); return; }

        Vector2 to = P.pos - pos;
        float d = to.magnitude;
        Vector2 dir = d > 0.001f ? to / d : Fwd;
        if (Time.time >= flipAt) { orbit = -orbit; flipAt = Time.time + Random.Range(14f, 26f); }
        Vector2 tang = new Vector2(-dir.y, dir.x) * orbit;
        float toA = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 want;
        float dmgMul = Naval.EnemyDmg;

        switch (spec.kind)
        {
            case ShipKind.Sloop:
            {
                Vector2 aim = Naval.Lead(pos, P, spec.maxSpeed * 0.9f);
                want = (aim - pos).normalized;
                break;
            }
            case ShipKind.Gunboat:
            {
                float k = Mathf.Clamp((d - 7.5f) / 3f, -1f, 1f);
                want = dir * k + tang * 0.8f;
                if (Time.time >= fireAt && d < 12.5f && Vector2.Angle(Fwd, dir) < 32f)
                {
                    Vector2 aim = Naval.Lead(pos, P, 9.5f);
                    Vector2 shot = (aim - pos).normalized;
                    Vector2 muzzle = Local(spec.halfLen * 0.85f, 0f);
                    Naval.FireBall(muzzle, Naval.Rot(shot, Random.Range(-5f, 5f)), 9.5f, 6f * dmgMul, 1, 2.4f);
                    Vfx.Burst(muzzle, new Color(0.85f, 0.85f, 0.88f, 0.9f), 4, 1.4f, 0.22f, 0.5f);
                    Sfx.Play("naval_cannon", 0.4f, Random.Range(0.95f, 1.2f));
                    fireAt = Time.time + Random.Range(2.0f, 2.9f);
                }
                break;
            }
            case ShipKind.Frigate:
            {
                float off = d > 10.5f ? 40f : (d < 6.5f ? 125f : 90f);
                float hd = toA - off * orbit;
                want = Dir(hd);
                Vector2 side = orbit > 0f ? Left : -Left;
                if (Time.time >= fireAt && d < 13f && Vector2.Angle(side, dir) < 22f)
                {
                    float corr = Mathf.Clamp(Vector2.SignedAngle(side, dir), -12f, 12f);
                    float rel = (orbit > 0f ? 90f : -90f) + corr;
                    rigWarn = 0.45f;
                    for (int i = 0; i < 3; i++)
                        queue.Add(new Pending { at = Time.time + 0.45f + i * 0.16f, rel = rel + Random.Range(-4f, 4f), speed = 8.8f, dmg = 5f * dmgMul, local = new Vector2(-1.4f + i * 1.4f, (orbit > 0f ? 0.8f : -0.8f)) });
                    fireAt = Time.time + Random.Range(3.4f, 4.4f);
                }
                break;
            }
            case ShipKind.Mortar:
            {
                float k = Mathf.Clamp((d - 11f) / 3f, -1f, 1f);
                want = dir * k + tang * 0.6f;
                if (Time.time >= mortarAt && d < 18f)
                {
                    Vector2 tgt = P.pos + P.Vel * 1.5f;
                    Naval.LobShell(Local(0.1f, 0f), tgt, 1.6f, 16f * dmgMul, 2.1f);
                    if (hp < maxHp * 0.5f) Naval.LobShell(Local(0.1f, 0f), tgt + Random.insideUnitCircle * 2.4f, 1.9f, 14f * dmgMul, 1.9f);
                    tubePulse = 1f;
                    Sfx.Play("naval_whoosh", 0.5f, 0.8f);
                    Vfx.Burst(Local(0.1f, 0f), new Color(0.5f, 0.5f, 0.55f), 8, 2f, 0.3f, 0.7f);
                    mortarAt = Time.time + Random.Range(4f, 5.2f);
                }
                break;
            }
            default:
                want = BossThink(P, d, dir, toA, dmgMul);
                break;
        }

        want += Naval.Avoid(this) * 1.6f;
        Steer(want, 1f, dt);
        ProcessQueue();
    }

    Vector2 BossThink(PlayerShip P, float d, Vector2 dir, float toA, float dmgMul)
    {
        float frac = hp / Mathf.Max(1f, maxHp);
        int ph = frac > 0.66f ? 0 : (frac > 0.33f ? 1 : 2);
        if (ph != bossPhase)
        {
            if (bossPhase >= 0)
            {
                Naval.Announce(ph == 1 ? "THE GALLEON CALLS FOR HELP" : "THE GALLEON IS ENRAGED", new Color(1f, 0.45f, 0.3f));
                Sfx.Play("naval_horn", 1f, 0.7f);
                Naval.Shake(0.5f, 0.6f);
                summonAt = Time.time + 1.5f;
            }
            bossPhase = ph;
        }
        float spdMul = ph == 2 ? 1.25f : 1f;
        float off = d > 14f ? 35f : (d < 7f ? 130f : 90f);
        Vector2 want = Dir(toA - off * orbit);
        Vector2 side = orbit > 0f ? Left : -Left;

        // broadside
        float salvoCd = ph == 2 ? 3.0f : 4.4f;
        if (Time.time >= fireAt && d < 15f && Vector2.Angle(side, dir) < 28f)
        {
            float corr = Mathf.Clamp(Vector2.SignedAngle(side, dir), -14f, 14f);
            float rel = (orbit > 0f ? 90f : -90f) + corr;
            rigWarn = 0.6f;
            FloatingText.Add(pos + Vector2.up * 2.2f, "BROADSIDE!", new Color(1f, 0.4f, 0.3f), 14, 0.9f);
            int n = ph == 0 ? 6 : 7;
            for (int i = 0; i < n; i++)
                queue.Add(new Pending { at = Time.time + 0.6f + i * 0.11f, rel = rel + Random.Range(-5f, 5f), speed = 9f, dmg = 6f * dmgMul, local = new Vector2(Mathf.Lerp(-3.6f, 3.6f, i / (float)(n - 1)), (orbit > 0f ? 1.3f : -1.3f)) });
            fireAt = Time.time + salvoCd * Random.Range(0.9f, 1.15f);
        }
        // bow chasers
        if (Time.time >= altAt && d < 17f && Vector2.Angle(Fwd, dir) < 30f)
        {
            for (int i = -1; i <= 1; i++)
                queue.Add(new Pending { at = Time.time + 0.2f, rel = Vector2.SignedAngle(Fwd, dir) + i * 8f, speed = 10f, dmg = 6f * dmgMul, local = new Vector2(spec.halfLen * 0.9f, 0f) });
            altAt = Time.time + 3.2f;
        }
        // mortar barrage (phase 2+)
        if (ph >= 1 && Time.time >= mortarAt && d < 20f)
        {
            Vector2 tgt = P.pos + P.Vel * 1.6f;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Naval.LobShell(Local(0f, 0f), tgt, 1.8f, 18f * dmgMul, 2.2f);
            Naval.LobShell(Local(0f, 0f), tgt + perp * 2.8f, 2.0f, 16f * dmgMul, 2.0f);
            Naval.LobShell(Local(0f, 0f), tgt - perp * 2.8f, 2.2f, 16f * dmgMul, 2.0f);
            Sfx.Play("naval_whoosh", 0.7f, 0.7f);
            mortarAt = Time.time + 6.5f;
        }
        // reinforcements
        if (ph >= 1 && Time.time >= summonAt)
        {
            summonAt = Time.time + 22f;
            if (Naval.Enemies.Count < 7) { Naval.SpawnEnemy(ShipKind.Sloop, -1f); Naval.SpawnEnemy(ShipKind.Sloop, -1f); Naval.Announce("RAIDERS INCOMING", new Color(1f, 0.7f, 0.4f)); }
        }
        // ring of shot (enraged)
        if (ph == 2 && Time.time >= ringAt)
        {
            ringAt = Time.time + 6f;
            ringSpin += 11f;
            rigWarn = 0.5f;
            for (int i = 0; i < 14; i++)
                Naval.FireBall(pos, Dir(ringSpin + i * (360f / 14f)), 6.5f, 4f * dmgMul, 1, 3.2f);
            Sfx.Play("naval_cannon", 0.8f, 0.6f);
            Vfx.Shockwave(pos, new Color(1f, 0.45f, 0.2f), 3.5f, 0.5f);
        }
        speedBoost = spdMul;
        return want;
    }

    float speedBoost = 1f;
    protected override float SpeedMul { get { return speedBoost; } }

    void ProcessQueue()
    {
        for (int i = queue.Count - 1; i >= 0; i--)
        {
            var q = queue[i];
            if (Time.time < q.at) continue;
            queue.RemoveAt(i);
            Vector2 origin = Local(q.local.x, q.local.y);
            Vector2 shot = Naval.Rot(Fwd, q.rel);
            Naval.FireBall(origin, shot, q.speed, q.dmg, 1, 2.6f);
            Vfx.Burst(origin + shot * 0.3f, new Color(0.85f, 0.85f, 0.88f, 0.9f), 4, 1.4f, 0.22f, 0.5f);
            Vfx.Burst(origin + shot * 0.3f, new Color(1f, 0.75f, 0.3f), 3, 3.2f, 0.12f, 0.14f);
            Sfx.Play("naval_cannon", IsBoss ? 0.5f : 0.4f, Random.Range(0.85f, 1.1f));
        }
    }

    protected override void OnHit(float amt, Vector2 from)
    {
        Vfx.Burst(from, new Color(0.55f, 0.35f, 0.2f), 5, 3f, 0.12f, 0.4f);
    }
}

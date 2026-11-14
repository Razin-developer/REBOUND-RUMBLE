using System.Collections.Generic;
using UnityEngine;

public enum EnemyKind { Slime, Bat, Golem, Ant, Beetle, Wasp, Boss }

/// <summary>Chasing enemy. Rings pass through (and hurt) it; touching the player hurts the player.</summary>
public class Enemy : MonoBehaviour
{
    public static readonly List<Enemy> All = new List<Enemy>();

    public EnemyKind kind;
    public float Radius { get; private set; }
    public bool passive;
    string spawnLayer = GameState.ActorLayer;
    public static Enemy Boss;          // tutorial dummy: stands still and never hurts
    public int Id;                     // co-op: identifies this enemy in snapshots
    static int nextId;
    public Vector2 Pos { get { return rb != null ? rb.position : (Vector2)transform.position; } }
    public int Flags { get { return ((dashState == 1 || bossState == 1) ? 1 : 0) | ((kind == EnemyKind.Boss && Enraged) ? 2 : 0); } }

    float speed, hp, maxHp, bodyScale, seed, nextPath, flashUntil, stuckTime, sideUntil;
    int damage, score;
    Color burstColor;
    Vector2 desired, knock, sideStep, lastPos, dashDir;
    Rigidbody2D rb;
    SpriteRenderer sr;
    Sprite[] frames;
    float animTimer, frameTime;
    int frame;
    Transform playerT;
    Transform barRoot, barFill;
    SpriteRenderer[] barRenderers;
    float barWidth = 0.8f;
    int dashState;               // wasp: 0 chase, 1 wind-up, 2 dash
    float dashUntil, nextDash;
    static PhysicsMaterial2D slick;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); slick = null; Boss = null; nextId = 0; }

    public static Enemy Spawn(EnemyKind kind, Vector2 pos, float hpScale = 1f, float speedScale = 1f, string layerName = null)
    {
        var go = new GameObject("Enemy " + kind);
        go.transform.position = pos;
        int pl = layerName != null ? LayerMask.NameToLayer(layerName) : -1;
        go.layer = pl >= 0 ? pl : 20; // same physics layer as the player / map walls
        var e = go.AddComponent<Enemy>();
        e.spawnLayer = pl >= 0 ? layerName : GameState.ActorLayer;
        e.Setup(kind, hpScale, speedScale);
        return e;
    }

    void Setup(EnemyKind k, float hpScale, float speedScale)
    {
        kind = k;
        Id = ++nextId;
        seed = Random.value * 10f;
        string sheet;
        float baseHp;
        switch (k)
        {
            case EnemyKind.Bat:
                sheet = "enemy_bat"; speed = 3.4f; baseHp = 1; damage = 1; score = 15; bodyScale = 1f; Radius = 0.28f;
                burstColor = new Color(0.8f, 0.35f, 0.95f); frameTime = 0.1f; break;
            case EnemyKind.Golem:
                sheet = "enemy_golem"; speed = 1.3f; baseHp = 6; damage = 2; score = 40; bodyScale = 1.5f; Radius = 0.5f;
                burstColor = new Color(0.9f, 0.6f, 0.35f); frameTime = 0.35f; break;
            case EnemyKind.Ant:
                sheet = "enemy_ant"; speed = 3.0f; baseHp = 1; damage = 1; score = 5; bodyScale = 0.8f; Radius = 0.2f;
                burstColor = new Color(0.8f, 0.3f, 0.2f); frameTime = 0.08f; break;
            case EnemyKind.Beetle:
                sheet = "enemy_beetle"; speed = 1.7f; baseHp = 3; damage = 1; score = 25; bodyScale = 1.1f; Radius = 0.4f;
                burstColor = new Color(0.3f, 0.7f, 0.8f); frameTime = 0.2f; break;
            case EnemyKind.Wasp:
                sheet = "enemy_wasp"; speed = 2.4f; baseHp = 1; damage = 1; score = 20; bodyScale = 1f; Radius = 0.28f;
                burstColor = new Color(1f, 0.85f, 0.2f); frameTime = 0.06f; nextDash = Time.time + Random.Range(1.5f, 3f); break;
            case EnemyKind.Boss:
                sheet = "boss"; speed = 1.35f; baseHp = 70; damage = 2; score = 500; bodyScale = 2.1f; Radius = 0.85f;
                burstColor = new Color(0.7f, 0.45f, 1f); frameTime = 0.5f; Boss = this; break;
            default:
                sheet = "enemy_slime"; speed = 2.0f; baseHp = 2; damage = 1; score = 10; bodyScale = 1f; Radius = 0.35f;
                burstColor = new Color(0.4f, 0.9f, 0.35f); frameTime = 0.3f; break;
        }
        hp = maxHp = Mathf.Max(1, Mathf.CeilToInt(baseHp * hpScale));
        speed *= speedScale * GS.S;
        bodyScale *= GS.S; Radius *= GS.S;
        transform.localScale = Vector3.one * bodyScale;
        frameTime *= 0.5f;
        frames = kind == EnemyKind.Boss ? BossArt.Frames() : GameAssets.Frames(sheet, 4);

        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.sortingLayerName = spawnLayer;
        sr.sortingOrder = kind == EnemyKind.Boss ? 3 : 2;

        if (slick == null) slick = new PhysicsMaterial2D("EnemySlick") { friction = 0f, bounciness = 0f };
        rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.mass = kind == EnemyKind.Boss ? 30f : kind == EnemyKind.Golem ? 4f : (kind == EnemyKind.Ant ? 0.4f : 1f);
        var col = gameObject.AddComponent<CircleCollider2D>();
        col.radius = Radius / bodyScale;
        col.sharedMaterial = slick;

        // rings fly through enemies (they damage them in TenRing.FixedUpdate)
        foreach (var r in TenRing.All) if (r != null) r.IgnoreCollider(col);

        var p = GameObject.FindWithTag("Player");
        if (p != null) playerT = p.transform;
        lastPos = rb.position;
        All.Add(this);
    }

    void OnDestroy() { All.Remove(this); if (Boss == this) Boss = null; }

    void Update()
    {
        animTimer += Time.deltaTime;
        bool moving = rb.linearVelocity.sqrMagnitude > 0.2f || kind == EnemyKind.Bat;
        if (!moving) { frame = 0; sr.sprite = frames[0]; animTimer = 0f; }
        else if (animTimer >= frameTime) { animTimer = 0f; frame = (frame + 1) % frames.Length; sr.sprite = frames[frame]; }

        if (Mathf.Abs(rb.linearVelocity.x) > 0.1f) sr.flipX = rb.linearVelocity.x < 0f;

        if (Time.time < flashUntil) sr.color = new Color(1f, 0.35f, 0.35f);
        else if (dashState == 1) sr.color = new Color(1f, 1f, 0.35f);   // wasp telegraphs its dash
        else if (kind == EnemyKind.Boss && bossState == 1) sr.color = Color.Lerp(Color.white, new Color(1f, 0.35f, 0.3f), 0.5f + 0.5f * Mathf.Sin(Time.time * 24f));
        else if (kind == EnemyKind.Boss && Enraged) sr.color = new Color(1f, 0.7f, 0.7f);
        else sr.color = Difficulty.Frenzy ? new Color(1f, 0.55f, 0.55f) : Color.white;
    }

    void LateUpdate()
    {
        // the Cainos stair triggers re-sort every child sprite; keep the health bar on top
        if (barRenderers != null)
            foreach (var b in barRenderers) if (b != null) b.sortingLayerName = GameState.TopLayer;
    }

    void FixedUpdate()
    {
        if (playerT == null || !GameState.Active) { rb.linearVelocity = Vector2.zero; return; }

        if (passive) { knock = Vector2.Lerp(knock, Vector2.zero, 8f * Time.fixedDeltaTime); rb.linearVelocity = knock; return; }
        if (kind == EnemyKind.Boss && BossTick()) return;

        Vector2 pos = rb.position;
        Transform pt = Coop.Target(playerT, pos);   // co-op: chase whichever hero is closer
        Vector2 target = pt.position;

        // ---- wasp: wind up, then dash straight at where you were ----
        if (kind == EnemyKind.Wasp)
        {
            if (dashState == 0 && Time.time >= nextDash && (target - pos).sqrMagnitude < 36f)
            {
                dashState = 1; dashUntil = Time.time + 0.45f;
            }
            else if (dashState == 1)
            {
                rb.linearVelocity = Vector2.zero;
                if (Time.time >= dashUntil)
                {
                    dashState = 2; dashUntil = Time.time + 0.5f;
                    dashDir = (target - pos).normalized;
                }
                return;
            }
            else if (dashState == 2)
            {
                rb.linearVelocity = dashDir * 8f * GS.S;
                if (Time.time >= dashUntil) { dashState = 0; nextDash = Time.time + Random.Range(2f, 3.5f); }
                return;
            }
        }

        if (Time.time >= nextPath)
        {
            nextPath = Time.time + 0.15f;
            int pl = pt.gameObject.layer, el = gameObject.layer;
            Vector2 sd;
            if (pl != el && pl >= 20 && el >= 20 && Nav.StairDirection(pos, el, pl > el, Radius * 0.8f, out sd)) desired = sd;   // other level: walk to the stairs first
            else desired = Nav.Direction(pos, target, Radius * 0.8f);
        }

        // stuck on a corner / prop? sidestep for a moment
        stuckTime += ((pos - lastPos).magnitude < speed * Time.fixedDeltaTime * 0.2f) ? Time.fixedDeltaTime : -stuckTime;
        lastPos = pos;
        if (stuckTime > 0.4f)
        {
            stuckTime = 0f;
            sideStep = new Vector2(-desired.y, desired.x) * (Random.value < 0.5f ? -1f : 1f);
            sideUntil = Time.time + 0.5f;
        }

        Vector2 dir = desired;
        if (Time.time < sideUntil) dir = (dir * 0.3f + sideStep).normalized;

        // personal space: a crowd spreads out around obstacles instead of stacking on one spot
        Vector2 sep = Vector2.zero;
        for (int i = 0; i < All.Count; i++)
        {
            Enemy o = All[i];
            if (o == null || o == this) continue;
            Vector2 away = rb.position - (Vector2)o.transform.position;
            float m = away.magnitude, rr = Radius + o.Radius + 0.15f;
            if (m < rr && m > 0.001f) sep += away / m * ((rr - m) / rr);
        }
        if (sep.sqrMagnitude > 0f) dir = (dir + sep * 0.9f).normalized;

        float spd = speed * Difficulty.LiveSpeed;
        if (kind == EnemyKind.Slime) spd *= 0.35f + 1.0f * Mathf.Abs(Mathf.Sin(Time.time * 3f + seed)); // hopping
        if (kind == EnemyKind.Bat) dir = (dir + new Vector2(-dir.y, dir.x) * Mathf.Sin(Time.time * 6f + seed) * 0.6f).normalized;
        if (kind == EnemyKind.Ant) dir = (dir + new Vector2(-dir.y, dir.x) * Mathf.Sin(Time.time * 9f + seed) * 0.25f).normalized;

        knock = Vector2.Lerp(knock, Vector2.zero, 8f * Time.fixedDeltaTime);
        rb.linearVelocity = dir * spd + knock;
    }

    void OnCollisionStay2D(Collision2D c)
    {
        if (passive) return;
        var ph = c.collider.GetComponentInParent<PlayerHealth>();
        if (ph != null) ph.Damage(Mathf.Max(1, Mathf.RoundToInt(damage * Difficulty.DamageMul)), ((Vector2)ph.transform.position - rb.position));
        var rbody = c.collider.GetComponentInParent<RemoteBody>();
        if (rbody != null) rbody.Damage(Mathf.Max(1, Mathf.RoundToInt(damage * Difficulty.DamageMul)), ((Vector2)rbody.transform.position - rb.position));
    }

    public void Hit(float dmg, Vector2 dir)
    {
        if (hp <= 0f) return;
        hp -= dmg;
        flashUntil = Time.time + 0.12f;
        knock = dir.normalized * (kind == EnemyKind.Boss ? 0.6f : kind == EnemyKind.Golem ? 3f : (kind == EnemyKind.Beetle ? 4f : 6f));
        if (GameSettings.DamageNumbers)
            FloatingText.Add((Vector2)transform.position + new Vector2(0f, 0.5f), Mathf.RoundToInt(dmg).ToString(), new Color(1f, 0.9f, 0.4f), 16, 0.6f);
        CameraRig.Shake(0.05f, 0.08f);
        Fx.Slash((Vector2)transform.position + Vector2.up * 0.4f, dir, new Color(1f, 0.55f, 0.15f, 0.95f), 0.6f, 0.16f);
        Fx.Blood((Vector2)transform.position + Vector2.up * 0.3f, dir, 6, false, kind == EnemyKind.Bat);
        UpdateBar();
        if (hp <= 0f) Die();
    }

    // ------------------------------------------------------------------ boss behaviour: slam + summon + enrage
    int bossState;                 // 0 chase, 1 slam wind-up
    float bossStateUntil, nextSlam = -1f, nextSummon = -1f;
    public bool Enraged { get { return hp < maxHp * 0.5f; } }
    public float HpFrac { get { return Mathf.Clamp01(hp / Mathf.Max(1f, maxHp)); } }
    public const float SlamRadius = 3.4f;

    bool BossTick()
    {
        if (nextSlam < 0f) { nextSlam = Time.time + 4f; nextSummon = Time.time + 6f; }
        if (bossState == 1)
        {
            rb.linearVelocity = Vector2.zero;
            if (Time.time >= bossStateUntil)
            {
                bossState = 0;
                float R = SlamRadius * GS.S;
                Vector2 c = rb.position;
                Vfx.Shockwave(c, new Color(1f, 0.5f, 0.2f), R * 1.2f, 0.35f);
                Vfx.Burst(c, new Color(1f, 0.6f, 0.2f), 40, 7f, 0.22f, 0.6f);
                Sfx.Play("enemy_die", 1f, 0.45f);
                CameraRig.Shake(0.6f, 0.35f);
                if (playerT != null && ((Vector2)playerT.position - c).sqrMagnitude < R * R && playerT.gameObject.layer == gameObject.layer)
                {
                    var ph = playerT.GetComponent<PlayerHealth>();
                    if (ph != null) ph.Damage(Mathf.Max(1, Mathf.RoundToInt(2f * Difficulty.DamageMul)), (Vector2)playerT.position - c);
                }
                Coop.SlamAvatar(c, R, gameObject.layer);
                nextSlam = Time.time + (Enraged ? 4.5f : 7f);
            }
            return true;
        }
        if (Time.time >= nextSlam && playerT != null && ((Vector2)playerT.position - rb.position).sqrMagnitude < 14f * 14f)
        {
            bossState = 1; bossStateUntil = Time.time + 0.9f;
            Vfx.Shockwave(rb.position, new Color(1f, 0.25f, 0.2f), SlamRadius * GS.S * 1.1f, 0.9f);
            Sfx.Play("hurt", 0.7f, 0.5f);
            FloatingText.Add(rb.position + Vector2.up * 2.2f, "!", new Color(1f, 0.3f, 0.25f), 26, 0.9f);
            return true;
        }
        if (Time.time >= nextSummon)
        {
            nextSummon = Time.time + (Enraged ? 7f : 11f);
            int n = Mathf.Min(Enraged ? 4 : 3, 20 - All.Count);
            for (int i = 0; i < n; i++)
            {
                Vector2 p = rb.position + Random.insideUnitCircle.normalized * (1.6f * GS.S + 0.5f);
                Spawn(Random.value < 0.5f ? EnemyKind.Ant : EnemyKind.Slime, p, 1f, 1.1f, spawnLayer);
            }
            Vfx.Shockwave(rb.position, burstColor, 2.2f, 0.4f);
        }
        if (Enraged) speed = Mathf.Max(speed, 1.35f * 1.5f * GS.S * 1.0f);
        return false;
    }

    void Die()
    {
        Vfx.Burst(transform.position, burstColor, 26, 5.5f, 0.22f, 0.55f);
        Vfx.Burst(transform.position, Color.white, 8, 3f, 0.14f, 0.3f);
        Sfx.Play("enemy_die", 0.8f, kind == EnemyKind.Golem || kind == EnemyKind.Boss ? 0.6f : 1f);
        if (kind == EnemyKind.Boss) { for (int i = 0; i < 4; i++) Vfx.Shockwave(transform.position, Boosts.Tint((BoostKind)(i % 5)), 2f + i * 0.9f, 0.5f + i * 0.15f); Vfx.Burst(transform.position, Color.white, 60, 8f, 0.25f, 0.9f); BossDirector.OnBossDied(transform.position); }
        if (kind == EnemyKind.Bat) Fx.Wisps(transform.position, new Color(0.95f, 0.3f, 0.5f, 0.9f), 9);
        else Fx.Dust(transform.position, 8);
        if (GameSettings.DamageNumbers)
            FloatingText.Add((Vector2)transform.position + new Vector2(0f, 0.9f), "+" + score, new Color(1f, 0.85f, 0.25f), 16, 0.9f);
        CameraRig.Shake(kind == EnemyKind.Golem ? 0.25f : 0.12f, 0.15f);
        GameState.AddScore(score);
        Destroy(gameObject);
    }

    void UpdateBar()
    {
        if (barRoot == null)
        {
            barRoot = new GameObject("HP").transform;
            barRoot.SetParent(transform, false);
            barRoot.localPosition = new Vector3(0f, 0.62f, 0f);
            barRoot.localScale = Vector3.one * GS.S / bodyScale;
            MakeBarPart("bg", Color.black, 0.9f, 0.14f, 510);
            barFill = MakeBarPart("fill", new Color(0.3f, 1f, 0.35f), barWidth, 0.08f, 511);
            barRenderers = barRoot.GetComponentsInChildren<SpriteRenderer>();
        }
        float f = Mathf.Clamp01(hp / maxHp);
        barFill.localScale = new Vector3(barWidth * f, 0.08f, 1f);
        var fsr = barFill.GetComponent<SpriteRenderer>();
        fsr.color = f > 0.5f ? new Color(0.3f, 1f, 0.35f) : (f > 0.25f ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.3f, 0.25f));
    }

    Transform MakeBarPart(string n, Color c, float w, float h, int order)
    {
        var g = new GameObject(n);
        g.transform.SetParent(barRoot, false);
        g.transform.localPosition = new Vector3(-w / 2f, 0f, 0f);
        g.transform.localScale = new Vector3(w, h, 1f);
        var s = g.AddComponent<SpriteRenderer>();
        s.sprite = GameAssets.WhiteLeftPivot;
        s.color = c;
        s.sortingLayerName = GameState.TopLayer;
        s.sortingOrder = order;
        return g.transform;
    }
}

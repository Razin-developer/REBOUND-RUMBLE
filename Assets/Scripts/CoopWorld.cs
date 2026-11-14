using System.Collections.Generic;
using UnityEngine;

/// <summary>Hero suits: 3 costumes x 4 colour variants = 12 suits (variant = a tint over the costume art).</summary>
public static class Suits
{
    public const int Costumes = 3, Variants = 4, Count = 12;
    public static readonly string[] VariantNames = { "CLASSIC", "ICE", "JADE", "VIOLET" };
    public static readonly Color[] VariantTint =
    {
        Color.white, new Color(0.62f, 0.85f, 1f), new Color(0.62f, 1f, 0.72f), new Color(0.88f, 0.66f, 1f)
    };
    public static int Make(int costume, int variant) { return Mathf.Clamp(costume, 0, Costumes - 1) + Costumes * Mathf.Clamp(variant, 0, Variants - 1); }
    public static int CostumeOf(int suit) { return Mathf.Clamp(suit, 0, Count - 1) % Costumes; }
    public static int VariantOf(int suit) { return Mathf.Clamp(suit, 0, Count - 1) / Costumes; }
    public static Color Tint(int suit) { return VariantTint[VariantOf(suit)]; }
}

/// <summary>Procedural art for co-op: the blue ring (the guest's rings) is the gold ring recoloured.</summary>
public static class CoopArt
{
    public static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
    public static readonly Color Blue = new Color(0.35f, 0.7f, 1f);
    static Sprite blueRing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { blueRing = null; }

    public static Sprite GoldRingSprite { get { return GameAssets.Single("ring"); } }

    public static Sprite BlueRingSprite
    {
        get
        {
            if (blueRing != null) return blueRing;
            Sprite g = GameAssets.Single("ring");
            if (g == null) return null;
            Texture2D src = g.texture;
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color[] px = t.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                if (c.a < 0.01f) continue;
                float h, s, v;
                Color.RGBToHSV(c, out h, out s, out v);
                h = (h + 0.46f) % 1f;
                Color n = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.1f), v);
                n.a = c.a;
                px[i] = n;
            }
            t.SetPixels(px); t.Apply();
            t.filterMode = FilterMode.Point;
            Rect rc = g.rect;
            blueRing = Sprite.Create(t, rc, new Vector2(g.pivot.x / rc.width, g.pivot.y / rc.height), g.pixelsPerUnit);
            return blueRing;
        }
    }
}

/// <summary>Host side: the guest's body in the host's world. Enemies hurt it; hits come out of the shared hearts.</summary>
public class RemoteBody : MonoBehaviour
{
    public bool Guard;
    float invulnUntil;

    public void Damage(int amount, Vector2 away)
    {
        if (!GameState.Active || Time.time < invulnUntil) return;
        if (Boosts.Aegis) { invulnUntil = Time.time + 0.2f; return; }
        if (Guard) { invulnUntil = Time.time + 0.25f; Guard_Blocked(); return; }
        invulnUntil = Time.time + 1.0f;
        GameState.PlayerHp = Mathf.Max(0, GameState.PlayerHp - amount);
        Coop.SendHurt(away);
        Vector3 c = transform.position + Vector3.up * 0.4f;
        Vfx.Burst(c, new Color(0.35f, 0.65f, 1f), 14, 4.5f, 0.18f, 0.45f);
        Fx.Blood(c, away, 10, false);
        Sfx.Play("hurt", 0.7f, 1.15f);
        if (GameState.PlayerHp <= 0) GameState.GameOver();
    }

    void Guard_Blocked() { global::Guard.Blocked(transform.position + Vector3.up * 0.4f); }
}

/// <summary>The other player's hero. Host: a real body (kinematic) driven by the guest's messages. Guest: only a picture of the host.</summary>
public class RemoteAvatar : MonoBehaviour
{
    const int CellW = 40, CellH = 44;
    public string user = "";
    public int suit;
    public bool physical;
    public bool WantThrow, WantVolley;
    public RemoteBody Body;
    public int Layer;

    Sprite[][] sets = new Sprite[3][];
    SpriteRenderer sr;
    Rigidbody2D rb;
    Vector2 target, lastPos, vel, throwDir = Vector2.down;
    int view; bool flip;
    float animT, dustT, throwUntil;

    public Vector2 Pos { get { return physical && rb != null ? rb.position : (Vector2)transform.position; } }

    public static RemoteAvatar Create(string user, int suit, bool physical, Vector2 pos)
    {
        var go = new GameObject("Partner " + user);
        go.transform.position = pos;
        var a = go.AddComponent<RemoteAvatar>();
        a.user = user; a.suit = suit; a.physical = physical; a.target = pos; a.lastPos = pos;
        a.Build();
        return a;
    }

    void Build()
    {
        var v = new GameObject("Sprite");
        v.transform.SetParent(transform, false);
        v.transform.localScale = new Vector3(1.25f, GS.S, 1f);
        sr = v.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = GameState.ActorLayer;
        sr.sortingOrder = 2;
        sr.color = Suits.Tint(suit);

        string suf = Suits.CostumeOf(suit) == 0 ? "" : Suits.CostumeOf(suit).ToString();
        string[] names = { "player_s", "player_n", "player_e" };
        for (int i = 0; i < 3; i++)
        {
            Texture2D tex = GameAssets.Texture(names[i] + suf);
            sets[i] = new Sprite[6];
            if (tex == null) continue;
            tex.filterMode = FilterMode.Point;
            for (int f = 0; f < 6; f++) sets[i][f] = Sprite.Create(tex, new Rect(f * CellW, 0, CellW, CellH), new Vector2(0.5f, 0.08f), 32f);
        }
        if (sets[0][0] != null) sr.sprite = sets[0][0];

        if (physical)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            var col = gameObject.AddComponent<CircleCollider2D>();
            col.radius = 0.3f * GS.S * 1.3f;
            col.offset = new Vector2(0f, 0.2f);
            Body = gameObject.AddComponent<RemoteBody>();
            gameObject.layer = 20;
            var p = GameObject.FindWithTag("Player");
            if (p != null) foreach (var c in p.GetComponentsInChildren<Collider2D>()) Physics2D.IgnoreCollision(col, c);   // the two heroes walk through each other
        }
    }

    public void SetTarget(Vector2 p, int lay)
    {
        target = p;
        Layer = Mathf.Clamp(lay, 0, 2);
        if (physical) gameObject.layer = 20 + Layer;
    }

    public void Throw(Vector2 dir)
    {
        throwUntil = Time.time + 0.3f;
        if (dir.sqrMagnitude > 0.01f) throwDir = dir.normalized;
        Vector2 hand = Pos + new Vector2(0f, 0.5f) + throwDir * 0.55f;
        Fx.Slash(hand, throwDir, new Color(0.4f, 0.7f, 1f, 0.95f), 0.9f, 0.22f);
        Vfx.Burst(hand, new Color(0.4f, 0.7f, 1f), 8, 3.5f, 0.1f, 0.25f);
    }

    void FixedUpdate()
    {
        if (!physical || rb == null) return;
        Vector2 np = (target - rb.position).sqrMagnitude > 25f ? target : Vector2.Lerp(rb.position, target, 0.4f);
        rb.MovePosition(np);
    }

    void LateUpdate()
    {
        if (sr == null) return;
        if (!physical)
        {
            Vector2 p = transform.position;
            p = (target - p).sqrMagnitude > 25f ? target : Vector2.Lerp(p, target, 1f - Mathf.Exp(-16f * Time.deltaTime));
            transform.position = new Vector3(p.x, p.y, 0f);
        }
        Vector2 pos = Pos;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        vel = Vector2.Lerp(vel, (pos - lastPos) / dt, 0.3f);
        lastPos = pos;

        sr.enabled = GameState.Mode == GameMode.Playing;
        sr.sortingLayerName = "Layer " + (Layer + 1);
        sr.sortingOrder = 2;
        Color tint = Suits.Tint(suit);
        sr.color = GameState.Over ? new Color(0.4f, 0.4f, 0.45f) : tint;
        if (sets[0][0] == null) return;

        bool moving = vel.sqrMagnitude > 0.4f;
        bool throwing = Time.time < throwUntil;
        Vector2 face = throwing ? throwDir : vel;
        if (face.sqrMagnitude > 0.01f)
        {
            if (Mathf.Abs(face.y) > Mathf.Abs(face.x) * 1.1f) view = face.y > 0 ? 1 : 0;
            else { view = 2; flip = face.x < 0; }
        }
        int frame = 0;
        if (throwing) frame = (throwUntil - Time.time) > 0.18f ? 4 : 5;
        else if (moving) { animT += Time.deltaTime; frame = Mathf.FloorToInt(animT * 10f) % 4; }
        else animT = 0f;
        if (sets[view][frame] != null) sr.sprite = sets[view][frame];
        sr.flipX = view == 2 && flip;

        if (moving && !throwing)
        {
            dustT -= Time.deltaTime;
            if (dustT <= 0f) { dustT = 0.2f; Fx.Dust(pos + new Vector2(0f, 0.05f), 2); }
        }
    }
}

/// <summary>Host side blue ring thrown by the guest. Hunts the nearest enemy, ricochets off walls, boomerangs home.</summary>
public class CoopRing : MonoBehaviour
{
    public static readonly List<CoopRing> All = new List<CoopRing>();
    public static int Count { get { return All.Count; } }

    RemoteAvatar owner;
    Vector2 vel;
    float born, noTarget;
    int hits, bounces;
    bool returning;
    Enemy target;
    float curveSign = 1f, seed;
    readonly Dictionary<Enemy, float> lastHit = new Dictionary<Enemy, float>();
    SpriteRenderer vsr, gsr;
    Transform visual, glow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); }

    public static void ClearAll()
    {
        for (int i = All.Count - 1; i >= 0; i--) if (All[i] != null) Destroy(All[i].gameObject);
        All.Clear();
    }

    public static CoopRing Spawn(RemoteAvatar owner, Vector2 from, Vector2 dir)
    {
        var go = new GameObject("Blue Ring");
        go.transform.position = from;
        var r = go.AddComponent<CoopRing>();
        r.owner = owner;
        r.born = Time.time;
        r.seed = Random.value * 10f;
        r.curveSign = Random.value < 0.5f ? -1f : 1f;
        float speed = 7.5f * GS.S * Boosts.RingSpeed;
        r.vel = (dir.sqrMagnitude < 0.0001f ? Vector2.right : dir.normalized) * speed;
        r.Build();
        All.Add(r);
        Vfx.Shockwave(from, CoopArt.Blue, 0.6f, 0.22f);
        return r;
    }

    void Build()
    {
        var v = new GameObject("Visual"); v.transform.SetParent(transform, false);
        visual = v.transform;
        vsr = v.AddComponent<SpriteRenderer>();
        vsr.sprite = CoopArt.BlueRingSprite;
        vsr.sortingLayerName = GameState.TopLayer; vsr.sortingOrder = 502;
        var g = new GameObject("Glow"); g.transform.SetParent(transform, false);
        glow = g.transform;
        gsr = g.AddComponent<SpriteRenderer>();
        gsr.sprite = GameAssets.Glow;
        gsr.color = new Color(0.3f, 0.6f, 1f, 0.5f);
        gsr.sortingLayerName = GameState.TopLayer; gsr.sortingOrder = 501;

        var trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.3f;
        trail.widthMultiplier = 0.28f * GS.S;
        trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        trail.material = Vfx.ParticleMaterial;
        trail.sortingLayerName = GameState.TopLayer;
        trail.sortingOrder = 500;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.8f, 0.95f, 1f), 0f), new GradientColorKey(CoopArt.Blue, 0.35f), new GradientColorKey(new Color(0.1f, 0.2f, 0.7f), 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = grad;
    }

    void OnDestroy() { All.Remove(this); }

    static Vector2 Rot(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    Enemy PickTarget(Vector2 from)
    {
        Enemy best = null; float bs = float.MaxValue;
        foreach (var e in Enemy.All)
        {
            if (e == null) continue;
            float last;
            if (lastHit.TryGetValue(e, out last) && Time.time - last < 0.6f) continue;
            float score = ((Vector2)e.transform.position - from).sqrMagnitude;
            if (score < bs) { bs = score; best = e; }
        }
        return best;
    }

    void Update()
    {
        if (owner == null || !GameState.Active) { if (owner == null) Destroy(gameObject); return; }
        float dt = Time.deltaTime;
        Vector2 p = transform.position;
        float speed = 7.5f * GS.S * Boosts.RingSpeed;

        // hurt enemies on contact
        for (int i = Enemy.All.Count - 1; i >= 0; i--)
        {
            Enemy e = Enemy.All[i];
            if (e == null) continue;
            float reach = e.Radius + 0.22f * GS.S;
            if (((Vector2)e.transform.position - p).sqrMagnitude > reach * reach) continue;
            float last;
            if (lastHit.TryGetValue(e, out last) && Time.time - last < 0.4f) continue;
            lastHit[e] = Time.time;
            hits++;
            Vfx.Burst(p, CoopArt.Blue, 10, 4f, 0.15f, 0.3f);
            Vfx.Shockwave(p, new Color(0.3f, 0.5f, 1f), 0.9f, 0.28f);
            Fx.Slash(p, vel, CoopArt.Blue, 0.7f, 0.18f);
            Sfx.Play("ring_clang", 0.7f, 1.1f + Random.value * 0.4f);
            e.Hit(2f * Boosts.Damage, vel);
            if (e == target) target = null;
            if (!returning) vel = Rot(vel, Random.Range(-40f, 40f));
        }

        if (!returning)
        {
            Vector2 dir = vel.sqrMagnitude < 0.01f ? Random.insideUnitCircle.normalized : vel.normalized;
            if (target == null) target = PickTarget(p);
            if (target != null)
            {
                noTarget = 0f;
                float delta = Vector2.SignedAngle(dir, (Vector2)target.transform.position - p);
                float maxTurn = 420f * dt;
                dir = Rot(dir, Mathf.Clamp(delta, -maxTurn, maxTurn));
            }
            else noTarget += dt;
            vel = dir * speed;

            Vector2 step = vel * dt;
            float len = step.magnitude;
            bool bounced = false;
            if (len > 0.0001f)
            {
                var hit = Physics2D.CircleCast(p, 0.2f * GS.S, dir, len + 0.05f, 1 << owner.gameObject.layer);
                if (hit.collider != null && !hit.collider.isTrigger && hit.collider.GetComponentInParent<Enemy>() == null
                    && hit.collider.GetComponentInParent<RemoteBody>() == null && hit.collider.GetComponentInParent<PlayerHealth>() == null)
                {
                    vel = Rot(Vector2.Reflect(vel, hit.normal), Random.Range(-25f, 25f));
                    bounces++; bounced = true;
                    Vfx.Burst(hit.point, CoopArt.Blue, 5, 3f, 0.12f, 0.25f);
                    Sfx.Play("ring_clang", 0.4f, 0.9f + Random.value * 0.5f);
                }
            }
            if (!bounced)
            {
                Vector2 np = p + step;
                if (!Nav.IsOverMap(np)) { vel = Rot(-vel, Random.Range(-30f, 30f)); bounces++; }
                else p = np;
            }
            if (hits >= 5 + Boosts.ExtraHits || bounces >= 9 || Time.time - born > 4.5f || noTarget > 0.9f) returning = true;
        }
        else
        {
            Vector2 anchor = owner.Pos + new Vector2(0f, 0.45f) * GS.S;
            Vector2 to = anchor - p;
            float d = to.magnitude;
            if (d < 0.6f)
            {
                Sfx.Play("catch", 0.6f, 1.1f);
                Vfx.Burst(anchor, CoopArt.Blue, 6, 2.2f, 0.1f, 0.25f);
                Destroy(gameObject);
                return;
            }
            Vector2 dir2 = Rot(to / d, curveSign * Mathf.Clamp(d * 6f, 0f, 45f));
            vel = dir2 * speed * 1.35f;
            p += vel * dt;
        }
        transform.position = new Vector3(p.x, p.y, 0f);

        float flipS = 0.3f + 0.7f * Mathf.Abs(Mathf.Cos(Time.time * 16f + seed));
        float s = 0.55f * GS.S;
        visual.localScale = new Vector3(s * flipS, s, 1f);
        if (vel.sqrMagnitude > 0.01f) visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg);
        glow.localScale = Vector3.one * GS.S * (1.1f + Mathf.Sin(Time.time * 18f + seed) * 0.15f);
    }
}

/// <summary>Guest side: a picture of one host enemy, moved by snapshots.</summary>
public class EnemyGhost : MonoBehaviour
{
    public int kind;
    public Vector2 Target;
    public float Hp01 = 1f;
    public int Flags;
    Sprite[] frames;
    SpriteRenderer sr;
    float frameTime, animT; int frame;
    Vector2 last;
    Color burst = Color.white;

    static readonly string[] Sheets = { "enemy_slime", "enemy_bat", "enemy_golem", "enemy_ant", "enemy_beetle", "enemy_wasp", "" };
    static readonly float[] Scales = { 1f, 1f, 1.5f, 0.8f, 1.1f, 1f, 2.1f };
    static readonly float[] FrameT = { 0.15f, 0.05f, 0.175f, 0.04f, 0.1f, 0.03f, 0.25f };
    static readonly Color[] Bursts =
    {
        new Color(0.4f, 0.9f, 0.35f), new Color(0.8f, 0.35f, 0.95f), new Color(0.9f, 0.6f, 0.35f), new Color(0.8f, 0.3f, 0.2f),
        new Color(0.3f, 0.7f, 0.8f), new Color(1f, 0.85f, 0.2f), new Color(0.7f, 0.45f, 1f)
    };

    // enum order used on the wire: EnemyKind { Slime, Bat, Golem, Ant, Beetle, Wasp, Boss }
    public static EnemyGhost Create(int kind, Vector2 pos)
    {
        kind = Mathf.Clamp(kind, 0, 6);
        var go = new GameObject("Ghost " + kind);
        go.transform.position = pos;
        var g = go.AddComponent<EnemyGhost>();
        g.kind = kind; g.Target = pos; g.last = pos;
        g.burst = Bursts[kind];
        g.frameTime = FrameT[kind];
        go.transform.localScale = Vector3.one * Scales[kind] * GS.S;
        g.frames = kind == 6 ? BossArt.Frames() : GameAssets.Frames(Sheets[kind], 4);
        g.sr = go.AddComponent<SpriteRenderer>();
        g.sr.sprite = g.frames[0];
        g.sr.sortingLayerName = GameState.ActorLayer;
        g.sr.sortingOrder = kind == 6 ? 3 : 2;
        return g;
    }

    public void SetLayer(int lay) { if (sr != null) sr.sortingLayerName = "Layer " + (Mathf.Clamp(lay, 0, 2) + 1); }

    public void Die()
    {
        Vfx.Burst(transform.position, burst, 20, 5f, 0.2f, 0.5f);
        Sfx.Play("enemy_die", 0.5f, kind == 2 || kind == 6 ? 0.6f : 1f);
        Destroy(gameObject);
    }

    void LateUpdate()
    {
        Vector2 p = transform.position;
        p = (Target - p).sqrMagnitude > 36f ? Target : Vector2.Lerp(p, Target, 1f - Mathf.Exp(-18f * Time.deltaTime));
        transform.position = new Vector3(p.x, p.y, 0f);
        Vector2 d = p - last; last = p;
        bool moving = d.sqrMagnitude > 0.00002f || kind == 1;
        animT += Time.deltaTime;
        if (!moving) { frame = 0; sr.sprite = frames[0]; animT = 0f; }
        else if (animT >= frameTime) { animT = 0f; frame = (frame + 1) % frames.Length; sr.sprite = frames[frame]; }
        if (Mathf.Abs(d.x) > 0.0005f) sr.flipX = d.x < 0f;
        if ((Flags & 1) != 0) sr.color = Color.Lerp(Color.white, new Color(1f, 0.4f, 0.3f), 0.5f + 0.5f * Mathf.Sin(Time.time * 24f));
        else if ((Flags & 2) != 0) sr.color = new Color(1f, 0.7f, 0.7f);
        else sr.color = Difficulty.Frenzy ? new Color(1f, 0.55f, 0.55f) : Color.white;
    }
}

/// <summary>Guest side: flying rings (gold = host, blue = guest) drawn from snapshots.</summary>
public class RingGhost : MonoBehaviour
{
    SpriteRenderer vsr, gsr;
    Transform visual, glow;
    Vector2 prev;
    float seed;
    bool blue;

    public static RingGhost Create()
    {
        var go = new GameObject("Ring Ghost");
        var r = go.AddComponent<RingGhost>();
        var v = new GameObject("Visual"); v.transform.SetParent(go.transform, false);
        r.visual = v.transform;
        r.vsr = v.AddComponent<SpriteRenderer>();
        r.vsr.sortingLayerName = GameState.TopLayer; r.vsr.sortingOrder = 502;
        var g = new GameObject("Glow"); g.transform.SetParent(go.transform, false);
        r.glow = g.transform;
        r.gsr = g.AddComponent<SpriteRenderer>();
        r.gsr.sprite = GameAssets.Glow;
        r.gsr.sortingLayerName = GameState.TopLayer; r.gsr.sortingOrder = 501;
        r.seed = Random.value * 10f;
        r.SetBlue(false, true);
        return r;
    }

    void SetBlue(bool b, bool force = false)
    {
        if (!force && b == blue) return;
        blue = b;
        vsr.sprite = b ? CoopArt.BlueRingSprite : CoopArt.GoldRingSprite;
        gsr.color = b ? new Color(0.3f, 0.6f, 1f, 0.5f) : new Color(1f, 0.55f, 0.1f, 0.5f);
    }

    public void Place(Vector2 p, bool isBlue)
    {
        SetBlue(isBlue);
        Vector2 cur = transform.position;
        Vector2 d = p - cur;
        transform.position = new Vector3(p.x, p.y, 0f);
        if (d.sqrMagnitude > 0.0004f) visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        float flip = 0.3f + 0.7f * Mathf.Abs(Mathf.Cos(Time.time * 16f + seed));
        float s = 0.55f * GS.S;
        visual.localScale = new Vector3(s * flip, s, 1f);
        glow.localScale = Vector3.one * GS.S * (1.1f + Mathf.Sin(Time.time * 18f + seed) * 0.15f);
    }
}

/// <summary>Guest side: the boost orb the host sees.</summary>
public class PickupGhost : MonoBehaviour
{
    public static PickupGhost Create(Vector2 pos, int kind, int lay)
    {
        var go = new GameObject("Boost Ghost");
        go.transform.position = pos;
        var pg = go.AddComponent<PickupGhost>();
        Color c = Boosts.Tint((BoostKind)Mathf.Clamp(kind, 0, 4));
        string sl = "Layer " + (Mathf.Clamp(lay, 0, 2) + 1);
        var orb = new GameObject("Orb"); orb.transform.SetParent(go.transform, false);
        orb.transform.localScale = Vector3.one * 0.75f * GS.S * 1.4f;
        orb.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        var osr = orb.AddComponent<SpriteRenderer>(); osr.sprite = GameAssets.Glow; osr.color = Color.Lerp(c, Color.white, 0.55f);
        osr.sortingLayerName = sl; osr.sortingOrder = 6;
        var glow = new GameObject("Glow"); glow.transform.SetParent(go.transform, false);
        glow.transform.localScale = Vector3.one * 1.8f;
        var gsr = glow.AddComponent<SpriteRenderer>(); gsr.sprite = GameAssets.Glow; gsr.color = new Color(c.r, c.g, c.b, 0.5f);
        gsr.sortingLayerName = sl; gsr.sortingOrder = 5;
        return pg;
    }
}

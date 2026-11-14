using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One of the rings. While ready it orbits the player's waist (front half in front of the player, back half behind).
/// Thrown, it ricochets off walls and props, slices through enemies, curves toward the nearest fresh enemy,
/// then boomerangs back to be caught. Crosses stairs like the player does.
/// </summary>
public class TenRing : MonoBehaviour
{
    public enum RingState { Orbit, Flying, Returning }

    public const int Total = 10;
    public static readonly List<TenRing> All = new List<TenRing>();
    public static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
    public static readonly Color Amber = new Color(1f, 0.5f, 0.08f);

    public RingState State { get; private set; }
    public int Index { get { return index; } }

    public float speed = 7.5f * GS.S;
    public float damage = 2f;
    public int maxHits = 5;          // enemy hits before it heads home
    public int maxBounces = 9;       // wall bounces before it heads home
    public float maxFlight = 4.5f;   // seconds before it heads home

    Rigidbody2D rb;
    CircleCollider2D col;
    TrailRenderer trail;
    ParticleSystem sparks;
    Transform owner, visual, glow;
    SpriteRenderer rootSr, vsr, gsr, ownerSr;
    int index, hits, bounces, topLayerId;
    float flyStart, lastBounce, curveSign, noTargetTime;
    public Enemy target;   // every thrown ring has a job: hit this enemy
    Vector2 lastGood;
    readonly Dictionary<Enemy, float> lastHit = new Dictionary<Enemy, float>();
    static PhysicsMaterial2D mat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); mat = null; }

    public static int ReadyCount
    {
        get { int n = 0; foreach (var r in All) if (r != null && r.State == RingState.Orbit) n++; return n; }
    }

    public static int OutCount
    {
        get { int n = 0; foreach (var r in All) if (r != null && r.State != RingState.Orbit) n++; return n; }
    }

    public static TenRing FirstReady()
    {
        foreach (var r in All) if (r != null && r.State == RingState.Orbit) return r;
        return null;
    }

    public static TenRing Create(Transform owner, int index)
    {
        var go = new GameObject("Ring " + (index + 1));
        go.transform.position = owner.position;
        var r = go.AddComponent<TenRing>();
        r.Build(owner, index);
        return r;
    }

    void Build(Transform ownerT, int i)
    {
        owner = ownerT;
        ownerSr = owner.GetComponent<SpriteRenderer>();
        index = i;
        topLayerId = SortingLayer.NameToID(GameState.TopLayer);
        if (mat == null) mat = new PhysicsMaterial2D("RingBounce") { bounciness = 1f, friction = 0f };

        // The Cainos stair triggers expect a SpriteRenderer on the root of anything that touches them,
        // so the root carries an invisible one; the real art lives in children.
        rootSr = gameObject.AddComponent<SpriteRenderer>();
        rootSr.sprite = GameAssets.WhiteLeftPivot;
        rootSr.color = new Color(1f, 1f, 1f, 0f);

        var v = new GameObject("Visual");
        v.transform.SetParent(transform, false);
        visual = v.transform;
        vsr = v.AddComponent<SpriteRenderer>();
        vsr.sprite = GameAssets.Single("ring");

        var g = new GameObject("Glow");
        g.transform.SetParent(transform, false);
        glow = g.transform;
        gsr = g.AddComponent<SpriteRenderer>();
        gsr.sprite = GameAssets.Glow;

        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.3f;
        trail.widthMultiplier = 0.28f * GS.S;
        trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        trail.material = Vfx.ParticleMaterial;
        trail.sortingLayerName = GameState.TopLayer;
        trail.sortingOrder = 500;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(Gold, 0.3f), new GradientColorKey(Amber, 0.7f), new GradientColorKey(new Color(0.6f, 0.15f, 0.05f), 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = grad;

        sparks = Fx.AttachSparks(transform);

        rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        col = gameObject.AddComponent<CircleCollider2D>();
        col.radius = 0.2f * GS.S;
        col.sharedMaterial = mat;

        // never collide with the owner, other rings, or existing enemies
        foreach (var c in owner.GetComponentsInChildren<Collider2D>()) Physics2D.IgnoreCollision(col, c);
        foreach (var other in All)
            if (other != null && other.col != null) Physics2D.IgnoreCollision(col, other.col);
        foreach (var e in Enemy.All)
        {
            var ec = e != null ? e.GetComponent<Collider2D>() : null;
            if (ec != null) Physics2D.IgnoreCollision(col, ec);
        }

        All.Add(this);
        GameState.OnRunStart += ForceOrbit;
        GameState.OnReturnToMenu += ForceOrbit;
        SetOrbit();
    }

    void OnDestroy()
    {
        GameState.OnRunStart -= ForceOrbit;
        GameState.OnReturnToMenu -= ForceOrbit;
        All.Remove(this);
    }

    public void IgnoreCollider(Collider2D other)
    {
        if (col != null && other != null) Physics2D.IgnoreCollision(col, other);
    }

    void SetSorting(int layerId, int order)
    {
        rootSr.sortingLayerID = layerId; rootSr.sortingOrder = order;
        vsr.sortingLayerID = layerId; vsr.sortingOrder = order;
        gsr.sortingLayerID = layerId; gsr.sortingOrder = order - 1;
    }

    // ---------------------------------------------------------------- states
    void SetOrbit()
    {
        State = RingState.Orbit;
        rb.simulated = false;
        col.enabled = false;
        trail.emitting = false;
        trail.Clear();
        if (sparks != null) { var se = sparks.emission; se.enabled = false; }
        gsr.color = new Color(1f, 0.6f, 0.15f, 0.3f);
        vsr.color = Color.white;
    }

    public void ForceOrbit() { SetOrbit(); }

    public void Launch(Vector2 dir)
    {
        State = RingState.Flying;
        hits = 0; bounces = 0;
        flyStart = lastBounce = Time.time;
        lastHit.Clear();
        target = PickTarget(transform.position); noTargetTime = 0f;
        if (target != null) dir = (Vector2)target.transform.position - (Vector2)transform.position;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        gameObject.layer = owner.gameObject.layer;   // same physics level as the player (stairs change it later)
        rb.simulated = true;
        col.enabled = true;
        rb.position = transform.position;
        rb.linearVelocity = dir.normalized * speed * Boosts.RingSpeed;
        lastGood = rb.position;
        trail.Clear();
        trail.emitting = true;
        if (sparks != null) { var se = sparks.emission; se.enabled = true; }
        vsr.color = Color.white;
        gsr.color = new Color(1f, 0.55f, 0.1f, 0.5f);
        SetSorting(topLayerId, 502);
        Vfx.Shockwave(transform.position, Gold, 0.6f, 0.22f);
    }

    void StartReturn()
    {
        if (State != RingState.Flying) return;
        State = RingState.Returning;
        col.enabled = false;
        curveSign = Random.value < 0.5f ? -1f : 1f;
    }

    void Catch()
    {
        Vector2 at = Anchor();
        SetOrbit();
        Sfx.Play("catch", 0.7f, 0.9f + Random.value * 0.3f);
        Vfx.Shockwave(at, Gold, 0.7f, 0.25f);
        Vfx.Burst(at, Gold, 6, 2.2f, 0.1f, 0.25f);
    }

    Vector2 Anchor() { return (Vector2)owner.position + new Vector2(0f, 0.45f) * GS.S; }

    // ---------------------------------------------------------------- per frame
    void LateUpdate()
    {
        if (owner == null) return;
        bool show = GameState.Mode == GameMode.Playing && !Coop.IsGuest;
        vsr.enabled = show; gsr.enabled = show;

        if (State == RingState.Orbit)
        {
            // a ring around the waist seen from above-front: back half is behind the player, front half in front
            float a = Time.time * 1.8f + index * (Mathf.PI * 2f / Total);
            float sn = Mathf.Sin(a), cs = Mathf.Cos(a);
            bool back = sn > 0f;
            transform.position = Anchor() + new Vector2(cs * 0.62f, sn * 0.22f) * GS.S;
            float depth = 0.5f - 0.5f * sn;                       // 0 = far back .. 1 = near front
            float s = Mathf.Lerp(0.2f, 0.32f, depth) * GS.S;
            float flip = 0.25f + 0.75f * Mathf.Abs(Mathf.Cos(Time.time * 3f + index));  // spins around its own axis
            visual.localScale = new Vector3(s * flip, s, 1f);
            visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(a) * 18f);
            glow.localScale = Vector3.one * (s * 1.8f);
            vsr.color = back ? new Color(0.7f, 0.7f, 0.7f, 1f) : Color.white;
            int layerId = ownerSr != null ? ownerSr.sortingLayerID : SortingLayer.NameToID(GameState.ActorLayer);
            int ord = ownerSr != null ? ownerSr.sortingOrder : 2;
            SetSorting(layerId, back ? ord - 1 : ord + 1);
        }
        else
        {
            SetSorting(topLayerId, 502); // stairs re-sort us; always draw flying rings on top
        }
    }

    void Update()
    {
        if (State == RingState.Orbit) return;
        // thrown rings tumble like a spinning disc
        float flip = 0.3f + 0.7f * Mathf.Abs(Mathf.Cos(Time.time * 16f + index));
        float s = 0.55f * GS.S;
        visual.localScale = new Vector3(s * flip, s, 1f);
        Vector2 v = rb.linearVelocity;
        if (v.sqrMagnitude > 0.01f) visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
        glow.localScale = Vector3.one * GS.S * (1.1f + Mathf.Sin(Time.time * 18f + index) * 0.15f);
    }

    static Vector2 Rot(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    Enemy Nearest(Vector2 p)
    {
        Enemy best = null;
        float bd = float.MaxValue;
        foreach (var e in Enemy.All)
        {
            if (e == null) continue;
            float last;
            if (lastHit.TryGetValue(e, out last) && Time.time - last < 0.6f) continue; // look for a NEW victim
            float d = ((Vector2)e.transform.position - p).sqrMagnitude;
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    /// <summary>The nearest enemy this ring has not just hit, preferring ones no other flying ring is already chasing.</summary>
    public Enemy PickTarget(Vector2 from)
    {
        Enemy best = null; float bestScore = float.MaxValue;
        foreach (var e in Enemy.All)
        {
            if (e == null) continue;
            float last;
            if (lastHit.TryGetValue(e, out last) && Time.time - last < 0.6f) continue;
            int taken = 0;
            foreach (var r in All) if (r != null && r != this && r.State == RingState.Flying && r.target == e) taken++;
            float score = ((Vector2)e.transform.position - from).sqrMagnitude + taken * 40f;
            if (score < bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    void FixedUpdate()
    {
        if (State == RingState.Orbit) return;
        if (owner == null) { Destroy(gameObject); return; }

        Vector2 p = rb.position;

        // ---- hurt enemies on contact (flying and returning) ----
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
            Vfx.Burst(p, Gold, 10, 4f, 0.15f, 0.3f);
            Vfx.Shockwave(p, Amber, 0.9f, 0.28f);
            Fx.Slash(p, rb.linearVelocity, Gold, 0.7f, 0.18f);
            Sfx.Play("ring_clang", 0.8f, 0.9f + Random.value * 0.4f);
            e.Hit(damage * Boosts.Damage, rb.linearVelocity);
            if (e == target) target = null;   // job done - pick the next nearest below
            if (State == RingState.Flying)
                rb.linearVelocity = Rot(rb.linearVelocity, Random.Range(-40f, 40f)); // ricochet off the victim
        }

        if (State == RingState.Flying)
        {
            float t = Time.time - flyStart;

            // ---- steering: hunt the assigned enemy (always the nearest one nobody else is chasing) ----
            Vector2 v = rb.linearVelocity;
            if (v.sqrMagnitude < 0.01f) v = Random.insideUnitCircle.normalized * speed;
            Vector2 dir = v.normalized;
            if (target == null) target = PickTarget(p);
            if (target != null)
            {
                noTargetTime = 0f;
                float delta = Vector2.SignedAngle(dir, (Vector2)target.transform.position - p);
                float maxTurn = 420f * Time.fixedDeltaTime;
                dir = Rot(dir, Mathf.Clamp(delta, -maxTurn, maxTurn));
            }
            else noTargetTime += Time.fixedDeltaTime;   // nothing to hit: fly on, then come home
            rb.linearVelocity = dir * speed;

            // ---- keep it on the map: bounce off the empty void and the arena edge ----
            if (!Nav.IsOverMap(p))
            {
                rb.position = lastGood;
                rb.linearVelocity = Rot(-rb.linearVelocity, Random.Range(-30f, 30f));
                bounces++;
                lastBounce = Time.time;
            }
            else lastGood = p;

            Arena a = Arena.Instance;
            if (a != null)
            {
                Vector2 c = a.Clamp(rb.position, 0.2f);
                if (c != rb.position)
                {
                    Vector2 vel = rb.linearVelocity;
                    if (!Mathf.Approximately(c.x, rb.position.x)) vel.x = -vel.x;
                    if (!Mathf.Approximately(c.y, rb.position.y)) vel.y = -vel.y;
                    rb.position = c;
                    rb.linearVelocity = vel;
                    bounces++;
                    lastBounce = Time.time;
                }
            }

            // ---- time to go home? ----
            if (hits >= maxHits + Boosts.ExtraHits || bounces >= maxBounces || t > maxFlight || noTargetTime > 0.9f)
                StartReturn();
        }
        else // Returning: boomerang arc back to the owner
        {
            Vector2 to = Anchor() - p;
            float d = to.magnitude;
            if (d < 0.6f) { Catch(); return; }
            Vector2 dir = Rot(to / d, curveSign * Mathf.Clamp(d * 6f, 0f, 45f));
            rb.linearVelocity = dir * speed * 1.35f;
        }
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        if (State != RingState.Flying) return;
        bounces++;
        lastBounce = Time.time;
        // chaotic ricochet: nudge the bounce direction randomly
        rb.linearVelocity = Rot(rb.linearVelocity, Random.Range(-30f, 30f));
        Vector2 point = c.contactCount > 0 ? c.GetContact(0).point : rb.position;
        Vfx.Burst(point, Gold, 6, 3f, 0.12f, 0.25f);
        Vfx.Shockwave(point, Gold, 0.45f, 0.2f);
        Sfx.Play("ring_clang", 0.5f, 0.8f + Random.value * 0.5f);
    }
}

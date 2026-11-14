using System.Collections.Generic;
using UnityEngine;

// ====================================================================================================================
//  everything that flies, floats or explodes in a sea battle
// ====================================================================================================================

/// <summary>A cannonball (pooled).</summary>
public class Ball
{
    static readonly Stack<Ball> pool = new Stack<Ball>();
    public GameObject go; Transform t; SpriteRenderer sr, glow; TrailRenderer trail;
    public Vector2 pos, vel;
    public float life, dmg, radius = 0.2f;
    public int team;

    public static void ResetPool() { pool.Clear(); }

    public static Ball Get(Vector2 p, Vector2 v, float damage, int teamId, float lifetime)
    {
        Ball b = null;
        while (pool.Count > 0 && b == null) { b = pool.Pop(); if (b.go == null) b = null; }
        if (b == null) b = Create();
        b.go.SetActive(true);
        b.pos = p; b.vel = v; b.dmg = damage; b.team = teamId; b.life = lifetime;
        b.t.position = new Vector3(p.x, p.y, 0f);
        b.trail.Clear();
        b.sr.color = teamId == 0 ? new Color(0.85f, 0.92f, 1f) : new Color(1f, 0.6f, 0.5f);
        b.glow.color = teamId == 0 ? new Color(0.5f, 0.85f, 1f, 0.5f) : new Color(1f, 0.4f, 0.2f, 0.6f);
        return b;
    }

    static Ball Create()
    {
        var b = new Ball();
        b.go = new GameObject("Ball");
        b.go.transform.SetParent(Naval.Root, false);
        b.t = b.go.transform;
        int top = SortingLayer.NameToID(GameState.TopLayer);
        var g = new GameObject("Glow"); g.transform.SetParent(b.t, false);
        b.glow = g.AddComponent<SpriteRenderer>(); b.glow.sprite = GameAssets.Glow; b.glow.sortingLayerID = top; b.glow.sortingOrder = 400;
        g.transform.localScale = Vector3.one * 0.9f;
        var s = new GameObject("Sprite"); s.transform.SetParent(b.t, false);
        b.sr = s.AddComponent<SpriteRenderer>(); b.sr.sprite = NavalArt.Get("cannonball"); b.sr.sortingLayerID = top; b.sr.sortingOrder = 401;
        b.trail = b.go.AddComponent<TrailRenderer>();
        b.trail.time = 0.2f; b.trail.widthMultiplier = 0.16f; b.trail.minVertexDistance = 0.1f;
        b.trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        b.trail.material = Vfx.ParticleMaterial;
        b.trail.sortingLayerID = top; b.trail.sortingOrder = 399;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(new Color(0.8f, 0.8f, 0.85f), 0f), new GradientColorKey(new Color(0.45f, 0.45f, 0.5f), 1f) },
                   new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
        b.trail.colorGradient = gr;
        return b;
    }

    public void Kill() { if (go != null) { go.SetActive(false); pool.Push(this); } }

    /// <summary>false = the ball is gone.</summary>
    public bool Tick(float dt)
    {
        pos += vel * dt;
        t.position = new Vector3(pos.x, pos.y, 0f);
        life -= dt;
        if (life <= 0f) { Vfx.Burst(pos, new Color(0.7f, 0.88f, 1f, 0.8f), 5, 1.6f, 0.16f, 0.4f); return false; }
        if ((pos - Naval.Center).sqrMagnitude > (Naval.Radius + 16f) * (Naval.Radius + 16f)) return false;

        for (int i = 0; i < Naval.Rocks.Count; i++)
        {
            var r = Naval.Rocks[i];
            if ((pos - r.pos).sqrMagnitude < (r.r + radius) * (r.r + radius))
            {
                Vfx.Burst(pos, new Color(0.75f, 0.75f, 0.8f), 6, 3f, 0.12f, 0.3f);
                Sfx.Play("naval_crack", 0.3f, 1.2f);
                return false;
            }
        }
        for (int i = Naval.Barrels.Count - 1; i >= 0; i--)
        {
            var bar = Naval.Barrels[i];
            if (!bar.dead && (pos - bar.pos).sqrMagnitude < 0.5f * 0.5f) { bar.Detonate(); return false; }
        }
        if (team == 0)
        {
            for (int i = 0; i < Naval.Enemies.Count; i++)
            {
                var e = Naval.Enemies[i];
                if (e == null || e.dead) continue;
                if (e.DistTo(pos) < radius) { Hit(e); return false; }
            }
        }
        else
        {
            var p = Naval.Player;
            if (p != null && !p.dead && p.DistTo(pos) < radius) { Hit(p); return false; }
        }
        return true;
    }

    void Hit(NavalShip s)
    {
        s.Damage(dmg, pos);
        Vfx.Burst(pos, new Color(1f, 0.75f, 0.3f), 7, 3.5f, 0.14f, 0.3f);
        Vfx.Burst(pos, new Color(0.5f, 0.35f, 0.2f), 4, 2.5f, 0.12f, 0.45f);
        Sfx.Play("naval_crack", 0.4f, Random.Range(0.9f, 1.3f));
        s.AddPush(vel.normalized * 0.4f);
    }
}

/// <summary>A mortar shell: lobbed in an arc, with a warning circle where it will land.</summary>
public class Shell
{
    GameObject go; Transform shellT, shadowT, warnT, ringT;
    SpriteRenderer shellSr, shadowSr, warnSr, ringSr;
    Vector2 from, to;
    float age, dur, dmg, radius;

    public Shell(Vector2 start, Vector2 target, float flight, float damage, float rad)
    {
        from = start; to = target; dur = flight; dmg = damage; radius = rad;
        int top = SortingLayer.NameToID(GameState.TopLayer);
        go = new GameObject("Shell");
        go.transform.SetParent(Naval.Root, false);
        var w = new GameObject("Warn"); w.transform.SetParent(go.transform, false); warnT = w.transform;
        warnSr = w.AddComponent<SpriteRenderer>(); warnSr.sprite = NavalArt.DiscSprite; warnSr.sortingLayerID = top; warnSr.sortingOrder = 380;
        var r = new GameObject("Ring"); r.transform.SetParent(go.transform, false); ringT = r.transform;
        ringSr = r.AddComponent<SpriteRenderer>(); ringSr.sprite = NavalArt.RingSprite; ringSr.sortingLayerID = top; ringSr.sortingOrder = 381;
        var sh = new GameObject("Shadow"); sh.transform.SetParent(go.transform, false); shadowT = sh.transform;
        shadowSr = sh.AddComponent<SpriteRenderer>(); shadowSr.sprite = GameAssets.Glow; shadowSr.color = new Color(0f, 0f, 0f, 0.45f); shadowSr.sortingLayerID = top; shadowSr.sortingOrder = 382;
        var s = new GameObject("Shell"); s.transform.SetParent(go.transform, false); shellT = s.transform;
        shellSr = s.AddComponent<SpriteRenderer>(); shellSr.sprite = NavalArt.Get("mortar_shell"); shellSr.sortingLayerID = top; shellSr.sortingOrder = 410;
        warnT.position = new Vector3(to.x, to.y, 0f);
        ringT.position = warnT.position;
        Sfx.Play("naval_whoosh", 0.35f, 1.2f);
    }

    public bool Tick(float dt)
    {
        if (go == null) return false;     // cancelled by an explosion
        age += dt;
        float k = Mathf.Clamp01(age / dur);
        Vector2 g = Vector2.Lerp(from, to, k);
        float h = Mathf.Sin(k * Mathf.PI) * 3.6f;
        shellT.position = new Vector3(g.x, g.y + h, 0f);
        shellT.Rotate(0f, 0f, 260f * dt);
        shadowT.position = new Vector3(g.x, g.y, 0f);
        shadowT.localScale = Vector3.one * (0.7f - 0.3f * Mathf.Sin(k * Mathf.PI));
        float pulse = 0.5f + 0.5f * Mathf.Sin(age * 14f);
        warnT.localScale = Vector3.one * radius;
        warnSr.color = new Color(1f, 0.25f, 0.15f, 0.12f + 0.18f * k + 0.08f * pulse);
        ringT.localScale = Vector3.one * Mathf.Lerp(radius * 1.6f, radius, k);
        ringSr.color = new Color(1f, 0.5f, 0.3f, 0.5f + 0.4f * pulse);
        if (k >= 1f)
        {
            Naval.Explode(to, radius, dmg, true, false);
            Object.Destroy(go);
            return false;
        }
        return true;
    }

    public void Cancel() { if (go != null) Object.Destroy(go); }
    public bool Alive { get { return go != null; } }
    public Vector2 Target { get { return to; } }
    public float Radius { get { return radius; } }
}

/// <summary>Moves a borrowed ring home and gives it back.</summary>
public class RingReturn
{
    TenRing ring; Vector2 pos; float curve;

    public RingReturn(TenRing r, Vector2 from)
    {
        ring = r; pos = from; curve = Random.value < 0.5f ? -1f : 1f;
    }

    public bool Tick(float dt)
    {
        if (ring == null) return false;
        Vector2 home = Naval.Anchor();
        Vector2 to = home - pos;
        float d = to.magnitude;
        if (d < 0.7f || Naval.Player == null) { Naval.Return(ring); return false; }
        Vector2 dir = Naval.Rot(to / d, curve * Mathf.Clamp(d * 5f, 0f, 40f));
        pos += dir * 24f * dt;
        ring.transform.position = new Vector3(pos.x, pos.y, 0f);
        ring.LoanVel = dir;
        return true;
    }
}

/// <summary>FIRE: one ring thrown at the nearest pirate ship; slices through up to a few ships, then boomerangs home.</summary>
public class RingShot
{
    TenRing ring; Vector2 pos, vel; NavalShip target; float life, noTarget, dmg, speed; int hitsLeft;
    readonly Dictionary<NavalShip, float> lastHit = new Dictionary<NavalShip, float>();

    public RingShot(TenRing r, Vector2 from, Vector2 dir, float damage, int pierce)
    {
        ring = r; pos = from; dmg = damage; hitsLeft = pierce;
        speed = 16f * Boosts.RingSpeed;
        vel = dir.normalized * speed;
    }

    NavalShip Pick()
    {
        NavalShip best = null; float bs = float.MaxValue;
        for (int i = 0; i < Naval.Enemies.Count; i++)
        {
            var e = Naval.Enemies[i];
            if (e == null || e.dead) continue;
            float last;
            if (lastHit.TryGetValue(e, out last) && Time.time - last < 0.7f) continue;
            float d = Vector2.Distance(e.pos, pos);
            if (d > 17f) continue;
            float ang = Vector2.Angle(vel, e.pos - pos);
            float score = d + ang * 0.12f;
            if (score < bs) { bs = score; best = e; }
        }
        return best;
    }

    public bool Tick(float dt)
    {
        if (ring == null) return false;
        life += dt;
        if (target == null || target.dead) { target = Pick(); }
        if (target != null)
        {
            noTarget = 0f;
            float delta = Vector2.SignedAngle(vel, target.pos - pos);
            float max = 560f * dt;
            vel = Naval.Rot(vel, Mathf.Clamp(delta, -max, max)).normalized * speed;
        }
        else noTarget += dt;
        pos += vel * dt;

        // rocks bounce rings
        for (int i = 0; i < Naval.Rocks.Count; i++)
        {
            var r = Naval.Rocks[i];
            Vector2 n = pos - r.pos;
            if (n.sqrMagnitude < (r.r + 0.2f) * (r.r + 0.2f))
            {
                n = n.normalized;
                vel = Vector2.Reflect(vel, n).normalized * speed;
                pos = r.pos + n * (r.r + 0.22f);
                Vfx.Burst(pos, TenRing.Gold, 5, 3f, 0.12f, 0.25f);
                Sfx.Play("ring_clang", 0.4f, 1.1f);
            }
        }
        // barrels pop
        for (int i = Naval.Barrels.Count - 1; i >= 0; i--)
        {
            var b = Naval.Barrels[i];
            if (!b.dead && (pos - b.pos).sqrMagnitude < 0.55f * 0.55f) b.Detonate();
        }
        // knock enemy shots out of the air
        for (int i = 0; i < Naval.Balls.Count; i++)
        {
            var b = Naval.Balls[i];
            if (b.team == 1 && (b.pos - pos).sqrMagnitude < 0.45f * 0.45f) { b.life = 0f; Vfx.Burst(b.pos, TenRing.Gold, 6, 3f, 0.12f, 0.25f); Sfx.Play("ring_clang", 0.35f, 1.5f); }
        }
        // hit ships
        for (int i = 0; i < Naval.Enemies.Count; i++)
        {
            var e = Naval.Enemies[i];
            if (e == null || e.dead) continue;
            if (e.DistTo(pos) > 0.3f) continue;
            float last;
            if (lastHit.TryGetValue(e, out last) && Time.time - last < 0.45f) continue;
            lastHit[e] = Time.time;
            e.Damage(dmg, pos);
            e.AddPush(vel.normalized * 1.2f);
            hitsLeft--;
            Vfx.Burst(pos, TenRing.Gold, 10, 4f, 0.15f, 0.3f);
            Vfx.Shockwave(pos, TenRing.Amber, 0.9f, 0.28f);
            Fx.Slash(pos, vel, TenRing.Gold, 0.8f, 0.18f);
            Sfx.Play("ring_clang", 0.8f, 0.9f + Random.value * 0.4f);
            if (e == target) target = null;
            vel = Naval.Rot(vel, Random.Range(-35f, 35f));
        }

        bool far = (pos - Naval.Anchor()).sqrMagnitude > 19f * 19f;
        if (hitsLeft <= 0 || life > 2.3f || noTarget > 0.6f || far)
        {
            Naval.Returns.Add(new RingReturn(ring, pos));
            return false;
        }
        ring.transform.position = new Vector3(pos.x, pos.y, 0f);
        ring.LoanVel = vel;
        return true;
    }
}

/// <summary>BLAST: three rings fly to the aimed point, spin up into a vortex, and detonate.</summary>
public class BlastBomb
{
    readonly TenRing[] rings; readonly Vector2[] rpos; Vector2 start, target, perp;
    float t; int phase; float radius, dmg;
    GameObject warn; SpriteRenderer warnSr, ringSr;

    public BlastBomb(TenRing[] r, Vector2 from, Vector2 aim, float rad, float damage)
    {
        rings = r; rpos = new Vector2[r.Length]; start = from; target = aim; radius = rad; dmg = damage;
        Vector2 dir = (aim - from); perp = new Vector2(-dir.y, dir.x).normalized;
        warn = new GameObject("BlastWarn");
        warn.transform.SetParent(Naval.Root, false);
        warn.transform.position = new Vector3(aim.x, aim.y, 0f);
        int top = SortingLayer.NameToID(GameState.TopLayer);
        warnSr = warn.AddComponent<SpriteRenderer>(); warnSr.sprite = NavalArt.DiscSprite; warnSr.sortingLayerID = top; warnSr.sortingOrder = 370;
        var rr = new GameObject("Ring"); rr.transform.SetParent(warn.transform, false);
        ringSr = rr.AddComponent<SpriteRenderer>(); ringSr.sprite = NavalArt.RingSprite; ringSr.sortingLayerID = top; ringSr.sortingOrder = 371;
        warn.transform.localScale = Vector3.one * radius;
        Sfx.Play("naval_charge", 0.5f, 1f);
    }

    public bool Tick(float dt)
    {
        t += dt;
        if (phase == 0)
        {
            float k = Mathf.Clamp01(t / 0.5f);
            Vector2 c = Vector2.Lerp(start, target, k);
            for (int i = 0; i < rings.Length; i++)
            {
                float off = (i - (rings.Length - 1) / 2f) * 1.1f * Mathf.Sin(k * Mathf.PI);
                rpos[i] = c + perp * off;
                Put(i, rpos[i], (target - start));
            }
            warnSr.color = new Color(1f, 0.55f, 0.15f, 0.05f + 0.1f * k);
            ringSr.color = new Color(1f, 0.8f, 0.3f, 0.35f);
            if (k >= 1f) { phase = 1; t = 0f; }
        }
        else
        {
            float k = Mathf.Clamp01(t / 0.7f);
            float r = Mathf.Lerp(1.7f, 0.25f, k * k);
            float a0 = t * (9f + 16f * k);
            for (int i = 0; i < rings.Length; i++)
            {
                float a = a0 + i * (Mathf.PI * 2f / rings.Length);
                rpos[i] = target + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                Put(i, rpos[i], new Vector2(-Mathf.Sin(a), Mathf.Cos(a)));
            }
            // the vortex drags pirate ships toward its eye and swallows their shots
            for (int i = 0; i < Naval.Enemies.Count; i++)
            {
                var e = Naval.Enemies[i];
                if (e == null || e.dead) continue;
                Vector2 to = target - e.pos;
                if (to.magnitude < radius * 1.5f) e.AddPush(to.normalized * 2.4f * dt * 4f);
            }
            warnSr.color = new Color(1f, 0.5f + 0.4f * k, 0.1f + 0.4f * k, 0.08f + 0.22f * k);
            ringSr.color = new Color(1f, 0.85f, 0.4f, 0.4f + 0.5f * k);
            ringSr.transform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(t * 40f));
            if (Random.value < 0.5f) Vfx.Burst(target + Random.insideUnitCircle * radius * 0.6f, new Color(1f, 0.75f, 0.3f), 2, 2.5f, 0.12f, 0.3f);
            if (k >= 1f) { Detonate(); return false; }
        }
        return true;
    }

    void Put(int i, Vector2 p, Vector2 v)
    {
        if (rings[i] == null) return;
        rings[i].transform.position = new Vector3(p.x, p.y, 0f);
        rings[i].LoanVel = v;
    }

    void Detonate()
    {
        Naval.Explode(target, radius, dmg, false, true);
        Vfx.Shockwave(target, new Color(1f, 0.85f, 0.4f), radius * 1.3f, 0.5f);
        Vfx.Shockwave(target, new Color(1f, 0.45f, 0.15f), radius * 0.8f, 0.4f);
        Vfx.Burst(target, new Color(1f, 0.85f, 0.4f), 36, 8f, 0.2f, 0.7f);
        Naval.Shake(0.45f, 0.4f);
        for (int i = 0; i < rings.Length; i++)
            if (rings[i] != null) Naval.Returns.Add(new RingReturn(rings[i], rpos[i]));
        Object.Destroy(warn);
    }

    public void Cancel() { if (warn != null) Object.Destroy(warn); }
}

/// <summary>TRAVEL: two rings spin behind the stern like propellers while the ship surges forward.</summary>
public class DashRings
{
    readonly TenRing[] rings; readonly PlayerShip ship; float spin, puff;
    readonly Vector2[] rpos;

    public DashRings(TenRing[] r, PlayerShip s)
    {
        rings = r; ship = s; rpos = new Vector2[r.Length];
        s.dashLeft = 1.7f;
        Sfx.Play("naval_whoosh", 0.7f, 1.4f);
        Sfx.Play("naval_horn", 0.35f, 1.6f);
    }

    public bool Tick(float dt)
    {
        if (ship == null || ship.dead || !ship.Dashing)
        {
            for (int i = 0; i < rings.Length; i++) if (rings[i] != null) Naval.Returns.Add(new RingReturn(rings[i], rpos[i] == Vector2.zero ? Naval.Anchor() : rpos[i]));
            return false;
        }
        spin += dt * 22f;
        puff -= dt;
        Vector2 c = ship.Local(-ship.spec.halfLen * 0.95f, 0f);
        for (int i = 0; i < rings.Length; i++)
        {
            float a = spin + i * Mathf.PI;
            rpos[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.55f;
            if (rings[i] == null) continue;
            rings[i].transform.position = new Vector3(rpos[i].x, rpos[i].y, 0f);
            rings[i].LoanVel = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
        }
        if (puff <= 0f)
        {
            puff = 0.05f;
            Vfx.Burst(c, new Color(0.55f, 0.9f, 1f), 4, 3.2f, 0.16f, 0.45f);
        }
        return true;
    }
}

/// <summary>Floating supply crate: 0 = repairs, 1 = a boost, 2 = doubloons.</summary>
public class Crate
{
    GameObject go; Transform t; SpriteRenderer sr, glow;
    public Vector2 pos; public int kind; float age, life = 45f, seed;

    public Crate(Vector2 p, int k)
    {
        pos = p; kind = k; seed = Random.value * 6f;
        go = new GameObject("Crate");
        go.transform.SetParent(Naval.Root, false);
        t = go.transform;
        int layer = SortingLayer.NameToID(GameState.ActorLayer);
        var g = new GameObject("Glow"); g.transform.SetParent(t, false);
        glow = g.AddComponent<SpriteRenderer>(); glow.sprite = GameAssets.Glow; glow.sortingLayerID = layer; glow.sortingOrder = Naval.BaseOrder - 71;
        g.transform.localScale = Vector3.one * 2.2f;
        glow.color = k == 0 ? new Color(0.4f, 1f, 0.5f, 0.55f) : (k == 1 ? new Color(0.5f, 0.85f, 1f, 0.55f) : new Color(1f, 0.85f, 0.3f, 0.6f));
        var c = new GameObject("Sprite"); c.transform.SetParent(t, false);
        sr = c.AddComponent<SpriteRenderer>(); sr.sprite = NavalArt.Get("crate"); sr.sortingLayerID = layer; sr.sortingOrder = Naval.BaseOrder - 70;
        t.position = new Vector3(p.x, p.y, 0f);
        Vfx.Burst(p, new Color(0.8f, 0.9f, 1f), 8, 2f, 0.14f, 0.5f);
    }

    public bool Tick(float dt)
    {
        age += dt;
        float b = Mathf.Sin(Time.time * 2.2f + seed);
        t.position = new Vector3(pos.x, pos.y + b * 0.06f, 0f);
        t.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 1.3f + seed) * 6f);
        glow.color = new Color(glow.color.r, glow.color.g, glow.color.b, 0.35f + 0.2f * Mathf.Sin(Time.time * 4f + seed));
        bool blink = life - age < 6f && Mathf.FloorToInt(Time.time * 6f) % 2 == 0;
        sr.enabled = !blink;
        var p = Naval.Player;
        if (p != null && !p.dead && p.DistTo(pos) < 0.7f) { Collect(p); Object.Destroy(go); return false; }
        if (age >= life) { Object.Destroy(go); return false; }
        return true;
    }

    void Collect(PlayerShip p)
    {
        Sfx.Play("chest_open", 0.8f, 1.3f);
        Vfx.Burst(pos, new Color(1f, 0.95f, 0.6f), 14, 4f, 0.16f, 0.5f);
        Vfx.Shockwave(pos, new Color(1f, 0.9f, 0.5f), 1.4f, 0.35f);
        if (kind == 0)
        {
            float heal = Mathf.Min(35f, p.maxHp - p.hp);
            p.hp += heal;
            FloatingText.Add(pos + Vector2.up * 0.6f, "+" + Mathf.RoundToInt(Mathf.Max(heal, 1f)) + " HULL REPAIRED", new Color(0.5f, 1f, 0.55f), 15, 1.4f);
        }
        else if (kind == 1)
        {
            var k = (BoostKind)Random.Range(0, 5);
            Boosts.Give(k);
            Sfx.Play("portal", 0.5f, 1.3f);
            FloatingText.Add(pos + Vector2.up * 0.6f, Boosts.Name(k), Boosts.Tint(k), 16, 1.6f);
            FloatingText.Add(pos + Vector2.up * 1.3f, Boosts.Blurb(k), new Color(1f, 1f, 1f, 0.9f), 11, 1.6f);
        }
        else
        {
            int gold = 120 + 40 * Naval.Wave;
            GameState.Score += gold; Naval.Loot += gold;
            FloatingText.Add(pos + Vector2.up * 0.6f, "+" + gold + " DOUBLOONS", new Color(1f, 0.85f, 0.3f), 15, 1.4f);
        }
    }

    public void Destroy() { if (go != null) Object.Destroy(go); }
}

/// <summary>Explosive powder keg. Shoot it, ram it or blast it - it hurts every ship nearby (yours too).</summary>
public class Barrel
{
    GameObject go; Transform t; SpriteRenderer sr, glow;
    public Vector2 pos; Vector2 drift; public bool dead; float seed;

    public Barrel(Vector2 p)
    {
        pos = p; seed = Random.value * 6f; drift = Random.insideUnitCircle * 0.12f;
        go = new GameObject("Barrel");
        go.transform.SetParent(Naval.Root, false);
        t = go.transform;
        int layer = SortingLayer.NameToID(GameState.ActorLayer);
        var g = new GameObject("Glow"); g.transform.SetParent(t, false);
        glow = g.AddComponent<SpriteRenderer>(); glow.sprite = GameAssets.Glow; glow.sortingLayerID = layer; glow.sortingOrder = Naval.BaseOrder - 71;
        g.transform.localScale = Vector3.one * 1.8f;
        glow.color = new Color(1f, 0.3f, 0.2f, 0.35f);
        var c = new GameObject("Sprite"); c.transform.SetParent(t, false);
        sr = c.AddComponent<SpriteRenderer>(); sr.sprite = NavalArt.Get("barrel"); sr.sortingLayerID = layer; sr.sortingOrder = Naval.BaseOrder - 69;
        t.position = new Vector3(p.x, p.y, 0f);
    }

    public bool Tick(float dt)
    {
        if (dead) return false;
        pos += drift * dt;
        if ((pos - Naval.Center).magnitude > Naval.Radius - 2f) drift = -drift;
        t.position = new Vector3(pos.x, pos.y + Mathf.Sin(Time.time * 2f + seed) * 0.05f, 0f);
        t.rotation = Quaternion.Euler(0f, 0f, Time.time * 14f + seed * 40f);
        glow.color = new Color(1f, 0.3f, 0.2f, 0.22f + 0.15f * Mathf.Sin(Time.time * 5f + seed));
        // a moving ship rams it
        var p = Naval.Player;
        if (p != null && !p.dead && p.DistTo(pos) < 0.35f && Mathf.Abs(p.speed) > 1.6f) { Detonate(); return false; }
        for (int i = 0; i < Naval.Enemies.Count; i++)
        {
            var e = Naval.Enemies[i];
            if (e != null && !e.dead && e.DistTo(pos) < 0.35f) { Detonate(); return false; }
        }
        return true;
    }

    public void Detonate()
    {
        if (dead) return;
        dead = true;
        Naval.Explode(pos, 2.7f, 30f, true, true);
        if (go != null) Object.Destroy(go);
    }

    public void Destroy() { dead = true; if (go != null) Object.Destroy(go); }
}

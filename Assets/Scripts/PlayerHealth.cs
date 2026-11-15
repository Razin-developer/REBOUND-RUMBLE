using UnityEngine;

/// <summary>
/// Auto-attached to the Player. 5 hearts, brief invulnerability + knockback after a hit.
/// Hidden while in the menu; when a run starts the player fades in at the entrance with a flash.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    float invulnUntil, spawnTime;
    Vector3 startPos;
    SpriteRenderer sr;
    SpriteRenderer[] allSr;
    Rigidbody2D rb;
    bool lastVisible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null && p.GetComponent<PlayerHealth>() == null) p.AddComponent<PlayerHealth>();
    }

    void Awake()
    {
        startPos = transform.position;
        sr = GetComponent<SpriteRenderer>();
        allSr = GetComponentsInChildren<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        GameState.PlayerHp = GameState.PlayerMaxHp;
        GameState.OnRunStart += BeginRun;
    }

    void OnDestroy() { GameState.OnRunStart -= BeginRun; }

    void BeginRun()
    {
        // always start on the ground level (an earlier run may have ended upstairs)
        int l1 = LayerMask.NameToLayer(GameState.ActorLayer);
        if (l1 >= 0) gameObject.layer = l1;
        if (allSr != null) foreach (var s in allSr) if (s != null) s.sortingLayerName = GameState.ActorLayer;
        transform.position = startPos;
        if (rb != null) { rb.position = startPos; rb.linearVelocity = Vector2.zero; }
        spawnTime = Time.time;
        invulnUntil = Time.time + 2.5f;
        Vfx.Shockwave(startPos + Vector3.up * 0.4f, new Color(1f, 0.8f, 0.3f), 2.2f, 0.6f);
        Vfx.Burst(startPos + Vector3.up * 0.4f, new Color(1f, 0.85f, 0.3f), 24, 4f, 0.16f, 0.6f);
        Sfx.Play("spawn", 0.8f, 1f);
    }

    /// <summary>The map changed: this is where every run starts now.</summary>
    public void SetStart(Vector2 p) { startPos = new Vector3(p.x, p.y, transform.position.z); }

    public void Invuln(float seconds) { invulnUntil = Mathf.Max(invulnUntil, Time.time + seconds); }

    /// <summary>Co-op guest: the host decided we were hit. Same knockback and effects, hearts come from the host.</summary>
    public void RemoteHurt(Vector2 away)
    {
        invulnUntil = Time.time + 1.0f;
        var pm = GetComponent<PlayerMovement>();
        if (pm != null) pm.Push(away.normalized * 9f);
        Vfx.Burst(transform.position + Vector3.up * 0.4f, new Color(1f, 0.25f, 0.25f), 16, 4.5f, 0.18f, 0.45f);
        Fx.Blood(transform.position + Vector3.up * 0.4f, away, 12, false);
        Sfx.Play("hurt", 0.9f, 1f);
        CameraRig.Shake(0.3f, 0.25f);
    }

    public void Damage(int amount, Vector2 away)
    {
        if (Coop.IsGuest) return;
        if (!GameState.Active || Time.time < invulnUntil) return;
        if (Abilities.Dashing || Abilities.Flying || Abilities.Landing) return;      // ring dash / flight: untouchable
        if (Boosts.Aegis) { Vfx.Burst(transform.position + Vector3.up * 0.4f, new Color(0.6f, 0.75f, 1f), 6, 3f, 0.1f, 0.3f); invulnUntil = Time.time + 0.2f; return; }
        if (Guard.Active)
        {
            Guard.Blocked(transform.position + Vector3.up * 0.4f);
            var pg = GetComponent<PlayerMovement>();
            if (pg != null) pg.Push(away.normalized * 3f);
            invulnUntil = Time.time + 0.25f;
            return;
        }
        invulnUntil = Time.time + 1.0f;
        GameState.PlayerHp = Mathf.Max(Tutorial.Active ? 1 : 0, GameState.PlayerHp - amount);   // practice level: cannot die

        var pm = GetComponent<PlayerMovement>();
        if (pm != null) pm.Push(away.normalized * 9f);

        Vfx.Burst(transform.position + Vector3.up * 0.4f, new Color(1f, 0.25f, 0.25f), 16, 4.5f, 0.18f, 0.45f);
        Fx.Blood(transform.position + Vector3.up * 0.4f, away, 12, false);
        Sfx.Play("hurt", 0.9f, 1f);
        CameraRig.Shake(0.3f, 0.25f);

        if (GameState.PlayerHp <= 0)
        {
            Vfx.Burst(transform.position + Vector3.up * 0.4f, new Color(1f, 0.85f, 0.25f), 40, 6f, 0.25f, 0.8f);
            GameState.GameOver();
        }
    }

    void Update()
    {
        bool visible = GameState.Mode == GameMode.Playing;
        if (visible != lastVisible)
        {
            foreach (var s in allSr) if (s != null) s.enabled = visible;
            lastVisible = visible;
        }
        if (!visible || sr == null) return;

        float alpha = Mathf.Clamp01((Time.time - spawnTime) / 0.8f);
        foreach (var s in allSr) if (s != null && s != sr) s.color = new Color(1f, 1f, 1f, alpha);

        if (GameState.Over) { sr.enabled = true; sr.color = new Color(0.4f, 0.4f, 0.45f); return; }
        Color tint = Suits.Tint(GameSettings.Suit);
        sr.color = new Color(tint.r, tint.g, tint.b, alpha);
        sr.enabled = !(Time.time < invulnUntil && alpha >= 1f) || Mathf.FloorToInt(Time.time * 14f) % 2 == 0; // blink
    }
}

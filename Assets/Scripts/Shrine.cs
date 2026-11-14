using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "PF Props - Statue 01" becomes a blessing shrine: walk up to the statue and it grants a boost, then recharges.
/// Each statue on a map offers a different blessing (Life, Might, Gale, Ward, Storm).
/// </summary>
public class Shrine : MonoBehaviour
{
    enum Kind { Life, Might, Gale, Ward, Storm }
    public static readonly List<Shrine> All = new List<Shrine>();
    const float Cooldown = 40f, Reach = 2.0f;

    Kind kind;
    Vector2 anchor;
    SpriteRenderer glow;
    SpriteRenderer[] sprites;
    float readyAt;
    float nextHint;
    Transform player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); }

    static string Title(Kind k)
    {
        switch (k) { case Kind.Life: return "STATUE OF LIFE"; case Kind.Might: return "STATUE OF MIGHT"; case Kind.Gale: return "STATUE OF THE GALE"; case Kind.Ward: return "STATUE OF WARDING"; default: return "STATUE OF STORMS"; }
    }
    static string Blurb(Kind k)
    {
        switch (k) { case Kind.Life: return "+2 HEARTS (OR BONUS SCORE WHEN FULL)"; case Kind.Might: return "RINGS HIT TWICE AS HARD"; case Kind.Gale: return "FASTER MOVEMENT AND FASTER RINGS"; case Kind.Ward: return "AEGIS SHIELD AGAINST HITS"; default: return "RINGS FLY FASTER AND BOUNCE FURTHER"; }
    }
    static Color Tint(Kind k)
    {
        switch (k) { case Kind.Life: return new Color(1f, 0.35f, 0.45f); case Kind.Might: return new Color(1f, 0.6f, 0.15f); case Kind.Gale: return new Color(0.4f, 1f, 0.7f); case Kind.Ward: return new Color(0.45f, 0.75f, 1f); default: return new Color(0.8f, 0.5f, 1f); }
    }

    /// <summary>Find every statue in the active map and turn it into a shrine. Safe to call again.</summary>
    public static void Scan()
    {
        All.Clear();
        var found = new List<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>())
        {
            if (t == null || t.name.IndexOf("Statue 01", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (t.parent != null && t.parent.name.IndexOf("Statue 01", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (t.GetComponentInChildren<SpriteRenderer>() == null) continue;
            found.Add(t);
        }
        found.Sort((a, b) => a.position.x != b.position.x ? a.position.x.CompareTo(b.position.x) : a.position.y.CompareTo(b.position.y));
        for (int i = 0; i < found.Count; i++)
        {
            var sh = found[i].gameObject.GetComponent<Shrine>();
            if (sh == null) sh = found[i].gameObject.AddComponent<Shrine>();
            sh.Setup((Kind)(i % 5));
            All.Add(sh);
        }
    }

    void Setup(Kind k)
    {
        kind = k;
        var own = new List<SpriteRenderer>();
        foreach (var r in GetComponentsInChildren<SpriteRenderer>()) if (r != null && r.sprite != GameAssets.Glow) own.Add(r);   // ignore our own glow from an earlier scan
        if (own.Count == 0) return;
        sprites = own.ToArray();
        Bounds b = sprites[0].bounds;
        foreach (var s in sprites) b.Encapsulate(s.bounds);
        anchor = new Vector2(b.center.x, b.min.y + 0.35f);
        if (glow != null) Destroy(glow.gameObject);
        var g = new GameObject("ShrineGlow");
        g.transform.position = new Vector3(b.center.x, b.center.y, 0f);
        g.transform.localScale = Vector3.one * 3.2f;
        g.transform.SetParent(transform, true);
        glow = g.AddComponent<SpriteRenderer>();
        glow.sprite = GameAssets.Glow;
        glow.color = Tint(kind);
        glow.sortingLayerName = sprites[0].sortingLayerName;
        glow.sortingOrder = sprites[0].sortingOrder + 1;
        readyAt = 0f;
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        GameState.OnRunStart -= ResetShrine;
        GameState.OnRunStart += ResetShrine;
    }

    void OnDestroy() { GameState.OnRunStart -= ResetShrine; All.Remove(this); }

    void ResetShrine() { readyAt = 0f; }

    bool Ready { get { return Time.time >= readyAt; } }

    void Update()
    {
        if (glow == null) return;
        bool ready = Ready;
        Color c = Tint(kind);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
        c.a = ready ? 0.22f + 0.18f * pulse : 0.05f;
        glow.color = c;
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) player = p.transform; if (player == null) return; }
        if (!GameState.Active || Tutorial.Active || Coop.IsGuest) return;

        Vector2 pp = (Vector2)player.position + new Vector2(0f, 0.4f);
        float d = Vector2.Distance(pp, anchor);
        if (ready && d < 6f && Time.time >= nextHint && d >= Reach)
        {
            nextHint = Time.time + 3.5f;
            FloatingText.Add(anchor + new Vector2(0f, 2.2f), Title(kind), Tint(kind), 12, 1.6f);
        }
        if (ready && (d < Reach || Coop.AvatarNear(anchor, Reach, -1))) Activate();
    }

    void Activate()
    {
        readyAt = Time.time + Cooldown;
        Color col = Tint(kind);
        Vector2 above = anchor + new Vector2(0f, 2.0f);
        switch (kind)
        {
            case Kind.Life:
                if (GameState.PlayerHp < GameState.PlayerMaxHp) GameState.Heal(2);
                else { GameState.Score += 150; FloatingText.Add(above + Vector2.up * 0.6f, "+150 BLESSED", col, 14, 1.3f); }
                break;
            case Kind.Might: Boosts.Give(BoostKind.Power); break;
            case Kind.Gale: Boosts.Give(BoostKind.Speed); Boosts.Give(BoostKind.Rapid); break;
            case Kind.Ward: Boosts.Give(BoostKind.Aegis); break;
            default: Boosts.Give(BoostKind.Storm); break;
        }
        BoostDirector.Announce(Title(kind), Blurb(kind), col, 2.8f);
        Sfx.Play("chest_open", 0.8f, 1.25f);
        Vfx.Shockwave(anchor, col, 2.6f, 0.6f);
        Vfx.Burst(anchor + Vector2.up * 0.6f, col, 36, 5f, 0.18f, 0.8f);
        FloatingText.Add(above, Title(kind), col, 16, 1.6f);
    }
}

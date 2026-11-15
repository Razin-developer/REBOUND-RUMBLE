using UnityEngine;

/// <summary>
/// The map's own "PF Props - Chest 01" (the one nearest the centre) becomes the KILL CHEST. Reaching 1, 10, 100, 500 kills and then every
/// further 500 (it never ends) stocks it with a reward; the rewards only drop when the player walks up and touches it, and they grow
/// with every tier. While rewards are waiting the HUD shows a banner and a yellow "!" marker (at the screen edge, pointing to the chest).
/// </summary>
public class RewardChest : MonoBehaviour
{
    public static RewardChest Instance { get; private set; }
    public static int Claimed;                          // milestones already collected this run
    public static float BannerUntil;
    public static string BannerTitle = "", BannerSub = "";
    public static string LastRewardLine = "";
    public static float LastRewardUntil;

    public Vector2 Pos { get; private set; }
    public Vector2 MarkerPos { get { return Pos + Vector2.up * (top + 0.9f) * GS.S; } }
    float top = 0.6f;

    Bounds bounds;
    SpriteRenderer glow;
    Transform player;
    int lastPending;

    // ---------------------------------------------------------------- milestones
    /// <summary>Kills needed for milestone i: 1, 10, 100, 500, 1000, 1500, 2000 ... (endless).</summary>
    public static int Milestone(int i)
    {
        switch (i) { case 0: return 1; case 1: return 10; case 2: return 100; case 3: return 500; }
        return 500 + (i - 3) * 500;
    }

    public static int Reached { get { int n = 0; while (Milestone(n) <= GameState.Kills) n++; return n; } }
    public static int Pending { get { return Mathf.Max(0, Reached - Claimed); } }
    public static int NextAt { get { return Milestone(Reached); } }
    public static int PrevAt { get { int r = Reached; return r == 0 ? 0 : Milestone(r - 1); } }

    /// <summary>What milestone t (0-based) pays: score grows with the square of the tier, hearts, and a growing set of boosts.</summary>
    public static int ScoreFor(int t) { return 100 * (t + 1) * (t + 1); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; Claimed = 0; BannerUntil = 0f; LastRewardUntil = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { new GameObject("RewardChest").AddComponent<RewardChest>(); }

    System.Collections.IEnumerator Start()
    {
        Instance = this;
        GameState.OnRunStart += ResetRun;
        GameState.OnReturnToMenu += ResetRun;
        yield return null; yield return null;
        Find();
    }

    void OnDestroy() { GameState.OnRunStart -= ResetRun; GameState.OnReturnToMenu -= ResetRun; if (Instance == this) Instance = null; }

    void ResetRun() { Claimed = 0; lastPending = 0; BannerUntil = 0f; LastRewardUntil = 0f; }

    void Find()
    {
        Vector2 centre = Arena.Instance != null ? (Vector2)Arena.Instance.Bounds.center : Vector2.zero;
        Transform best = null; float bd = float.MaxValue;
        foreach (var t in FindObjectsByType<Transform>())
        {
            if (t.name.IndexOf("Chest 01", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (t.parent != null && t.parent.name.IndexOf("Chest 01", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (t.GetComponentInChildren<SpriteRenderer>() == null) continue;
            float d = Vector2.Distance(t.position, centre);
            if (d < bd) { bd = d; best = t; }
        }
        if (best == null) { Debug.LogWarning("[RewardChest] no 'PF Props - Chest 01' found in the scene"); return; }
        var srs = best.GetComponentsInChildren<SpriteRenderer>();
        bounds = srs[0].bounds;
        foreach (var s in srs) bounds.Encapsulate(s.bounds);
        Pos = bounds.center;
        top = bounds.extents.y;
        var g = new GameObject("RewardGlow");
        g.transform.SetParent(best, true);
        g.transform.position = new Vector3(bounds.center.x, bounds.center.y, 0f);
        g.transform.localScale = Vector3.one * 3f;
        glow = g.AddComponent<SpriteRenderer>();
        glow.sprite = GameAssets.Glow;
        glow.color = new Color(1f, 0.85f, 0.2f, 0f);
        glow.sortingLayerName = srs[0].sortingLayerName;
        glow.sortingOrder = srs[0].sortingOrder - 1;
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
    }

    void Update()
    {
        if (glow == null) return;
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) player = p.transform; }
        bool live = GameState.Active && !GameState.Over && !Tutorial.Active && !Coop.IsGuest;
        int pend = live ? Pending : 0;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
        glow.color = new Color(1f, 0.85f, 0.2f, pend > 0 ? 0.35f + 0.3f * pulse : 0f);
        glow.transform.localScale = Vector3.one * (3f + pulse * 0.6f + Mathf.Min(pend, 4) * 0.3f);

        if (!live) { lastPending = 0; return; }
        if (pend > lastPending)
        {
            BannerTitle = "REWARDS WAITING IN THE CHEST!";
            BannerSub = "KILL MILESTONE  " + Milestone(Reached - 1) + "  -  TOUCH THE CHEST AT THE CENTRE TO CLAIM";
            BannerUntil = Time.time + 5f;
            Sfx.PlayUI("wave", 0.8f, 1.2f);
        }
        lastPending = pend;

        if (pend > 0 && player != null)
        {
            Vector2 pp = (Vector2)player.position + new Vector2(0f, 0.3f * GS.S);
            float reach = 0.7f * GS.S;
            if (bounds.SqrDistance(pp) < reach * reach) Claim();
        }
    }

    void Claim()
    {
        int n = Pending;
        if (n <= 0) return;
        int first = Claimed, score = 0, hearts = 0, boostCount = 0;
        for (int t = first; t < first + n; t++)
        {
            score += ScoreFor(t);
            hearts += t < 2 ? 1 : (t < 4 ? 2 : 99);
            boostCount = Mathf.Max(boostCount, Mathf.Min(5, t + 1));
        }
        Claimed += n;
        GameState.Score += score;
        bool healed = GameState.PlayerHp < GameState.PlayerMaxHp;
        if (healed) GameState.Heal(hearts);
        // boosts: the higher the tier the more of them at once (all five from tier 5)
        int[] order = { 0, 1, 2, 3, 4 };
        for (int i = 4; i > 0; i--) { int j = Random.Range(0, i + 1); int tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
        string boostNames = "";
        for (int i = 0; i < boostCount; i++) { var k = (BoostKind)order[i]; Boosts.Give(k); boostNames += (i > 0 ? ", " : "") + Boosts.Name(k); }

        Vector2 above = Pos + Vector2.up * (top + 0.4f) * GS.S;
        FloatingText.Add(above, "+" + score + " CHEST REWARD", new Color(1f, 0.85f, 0.25f), 20, 2.2f);
        if (healed) FloatingText.Add(above + Vector2.up * 0.6f, hearts >= 99 ? "FULL HEAL" : "+" + hearts + " HEART" + (hearts > 1 ? "S" : ""), new Color(1f, 0.4f, 0.45f), 16, 2f);
        if (boostCount > 0) FloatingText.Add(above + Vector2.up * 1.2f, boostNames.ToUpper(), new Color(0.6f, 0.9f, 1f), 14, 2.4f);
        LastRewardLine = "CHEST CLAIMED   +" + score + "   " + boostNames.ToUpper();
        LastRewardUntil = Time.time + 4f;
        BannerUntil = 0f;

        Sfx.Play("chest_open", 1f, 1f);
        Sfx.Play("portal", 0.5f, 1.4f);
        int burst = Mathf.Min(80, 24 + 8 * (Claimed));
        Vfx.Burst(Pos, new Color(1f, 0.85f, 0.3f), burst, 6f, 0.2f, 0.9f);
        Vfx.Burst(Pos, Color.white, burst / 3, 4f, 0.12f, 0.6f);
        Vfx.Shockwave(Pos, new Color(1f, 0.85f, 0.3f), 2.2f + Mathf.Min(4f, Claimed * 0.4f), 0.6f);
        CameraRig.Shake(0.2f, 0.25f);
        lastPending = 0;
    }
}

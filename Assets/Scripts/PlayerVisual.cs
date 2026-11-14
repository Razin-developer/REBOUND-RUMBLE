using UnityEngine;

/// <summary>
/// Drives the player's Shang-Chi sprite: 3 costumes, 4-direction walk cycle, throw wind-up/release poses,
/// footstep dust, a golden Ten Rings aura, the entrance beam and the death animation.
/// </summary>
public class PlayerVisual : MonoBehaviour
{
    public static PlayerVisual Instance;
    const int CellW = 40, CellH = 44;
    Sprite[][] sets = new Sprite[3][];   // 0 = S (down), 1 = N (up), 2 = E (right; flipped for left)
    int builtCostume = -1;
    SpriteRenderer sr, auraSr;
    Animator anim;
    Rigidbody2D rb;
    int view; bool flip, wasOver;
    float animT, dustT, throwStart = -10f;
    Vector2 throwDir;
    GameObject corpse;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null && p.GetComponent<PlayerVisual>() == null) p.AddComponent<PlayerVisual>();
    }

    public static void Throw(Vector2 dir) { if (Instance != null) Instance.DoThrow(dir); }

    void Awake()
    {
        Instance = this;
        sr = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        if (anim != null) anim.enabled = false;
        BuildSets();
        var a = new GameObject("RingAura");
        a.transform.SetParent(transform, false);
        a.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        a.transform.localScale = new Vector3(2.4f, 1.1f, 1f);
        auraSr = a.AddComponent<SpriteRenderer>();
        auraSr.sprite = GameAssets.Glow;
        auraSr.color = new Color(1f, 0.6f, 0.15f, 0f);
        GameState.OnRunStart += OnRunStart;
        GameState.OnReturnToMenu += ClearCorpse;
    }

    void OnDestroy() { GameState.OnRunStart -= OnRunStart; GameState.OnReturnToMenu -= ClearCorpse; }

    void BuildSets()
    {
        builtCostume = GameSettings.Costume;
        string suf = builtCostume == 0 ? "" : builtCostume.ToString();
        string[] names = { "player_s", "player_n", "player_e" };
        for (int v = 0; v < 3; v++)
        {
            Texture2D tex = GameAssets.Texture(names[v] + suf);
            sets[v] = new Sprite[6];
            if (tex == null) continue;
            tex.filterMode = FilterMode.Point;
            for (int i = 0; i < 6; i++)
                sets[v][i] = Sprite.Create(tex, new Rect(i * CellW, 0, CellW, CellH), new Vector2(0.5f, 0.08f), 32f);
        }
    }

    void ClearCorpse() { if (corpse != null) Destroy(corpse); corpse = null; }

    void OnRunStart()
    {
        ClearCorpse();
        Fx.Beam(transform.position, new Color(1f, 0.85f, 0.4f));   // you descend in a column of golden light
    }

    void DoThrow(Vector2 dir)
    {
        throwStart = Time.time; throwDir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.down;
        Vector2 hand = (Vector2)transform.position + new Vector2(0f, 0.5f) + throwDir * 0.55f;
        Fx.Slash(hand, throwDir, new Color(1f, 0.82f, 0.3f, 0.95f), 0.9f, 0.22f);
        Vfx.Burst(hand, new Color(1f, 0.7f, 0.2f), 8, 3.5f, 0.1f, 0.25f);
    }

    void Die()
    {
        Vector2 p = transform.position;
        Vfx.Shockwave(p + Vector2.up * 0.4f, new Color(0.9f, 0.1f, 0.15f), 2.6f, 0.6f);
        ClearCorpse();
        corpse = new GameObject("Player Corpse");
        corpse.transform.position = p;
        var csr = corpse.AddComponent<SpriteRenderer>();
        csr.sprite = sr.sprite; csr.flipX = sr.flipX;
        csr.sortingLayerID = sr.sortingLayerID; csr.sortingOrder = sr.sortingOrder;
        corpse.AddComponent<CorpseFx>().fallDir = (view == 2 && flip) ? 1f : -1f;
    }

    void LateUpdate()
    {
        if (sr == null) return;
        if (GameState.Over && !wasOver) Die();
        wasOver = GameState.Over;
        if (builtCostume != GameSettings.Costume) BuildSets();
        if (GameState.Over) { sr.enabled = false; return; }
        if (sets[0][0] == null) return;

        Vector2 v = rb != null ? rb.linearVelocity : Vector2.zero;
        bool moving = GameState.Active && v.sqrMagnitude > 0.4f;
        bool throwing = Time.time - throwStart < 0.3f;

        Vector2 faceDir = throwing ? throwDir : v;
        if (faceDir.sqrMagnitude > 0.01f)
        {
            if (Mathf.Abs(faceDir.y) > Mathf.Abs(faceDir.x) * 1.1f) { view = faceDir.y > 0 ? 1 : 0; }
            else { view = 2; flip = faceDir.x < 0; }
        }

        int frame = 0;
        if (throwing) frame = (Time.time - throwStart) < 0.12f ? 4 : 5;
        else if (moving) { animT += Time.deltaTime; frame = Mathf.FloorToInt(animT * 10f) % 4; }
        else animT = 0f;

        if (sets[view][frame] != null) sr.sprite = sets[view][frame];
        sr.flipX = view == 2 && flip;

        if (moving && !throwing)
        {
            dustT -= Time.deltaTime;
            if (dustT <= 0f) { dustT = 0.2f; Fx.Dust((Vector2)transform.position + new Vector2(0f, 0.05f), 2); }
        }

        float out01 = Mathf.Clamp01(TenRing.OutCount / 3f);
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 9f);
        auraSr.color = new Color(1f, 0.6f, 0.15f, GameState.Mode == GameMode.Playing ? 0.12f + 0.35f * out01 * pulse : 0f);
        auraSr.sortingLayerID = sr.sortingLayerID;
        auraSr.sortingOrder = sr.sortingOrder - 2;
    }
}

/// <summary>The fallen Shang-Chi: hops back, topples sideways, dust puffs.</summary>
public class CorpseFx : MonoBehaviour
{
    public float fallDir = -1f;
    float age; bool pooled; Vector3 start;
    void Start() { start = transform.position; }
    void Update()
    {
        age += Time.deltaTime;
        float k = Mathf.Clamp01(age / 0.9f);
        float ease = 1f - (1f - k) * (1f - k) * (1f - k);
        transform.rotation = Quaternion.Euler(0f, 0f, fallDir * 90f * ease);
        transform.position = start + new Vector3(-fallDir * 0.35f * ease, Mathf.Sin(Mathf.Min(k * 1.5f, 1f) * Mathf.PI) * 0.45f, 0f);
        if (!pooled && k >= 0.7f)
        {
            pooled = true;
            Vfx.Burst((Vector2)start + new Vector2(-fallDir * 0.4f, 0.1f), new Color(0.8f, 0.78f, 0.6f, 0.8f), 8, 1.5f, 0.12f, 0.4f);
            Sfx.Play("hit", 0.7f, 0.6f);
        }
    }
}

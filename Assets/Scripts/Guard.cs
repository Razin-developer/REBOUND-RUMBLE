using UnityEngine;

/// <summary>Hold E to raise a guard that blocks all enemy hits. A 5s meter drains while held and refills quickly when released; only running it completely dry locks it briefly.</summary>
public class Guard : MonoBehaviour
{
    public const float MaxTime = 5f, MoveMul = 0.55f;
    const float RegenDelay = 0.3f, Regen = 2.5f, LockRegen = 1.0f, UnlockAt = 1.5f;   // a 1s guard is back in well under a second; running dry = ~1.5s lockout
    public static bool Active;
    public static int Blocks, Depletions;      // counters (used by the tutorial)
    public static float Left = MaxTime;        // seconds of guard remaining
    static bool locked;
    static float idle;
    public static float CooldownTotal { get { return UnlockAt / LockRegen; } }
    public static float CooldownLeft { get { return locked ? Mathf.Max(0f, (UnlockAt - Left) / LockRegen) : 0f; } }
    public static bool Ready { get { return !locked; } }

    static Guard inst;
    Transform player;
    SpriteRenderer bubble, rim;
    float blockFlash;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = false; Left = MaxTime; locked = false; idle = 0f; inst = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { if (inst == null) new GameObject("Guard").AddComponent<Guard>(); }

    void Awake()
    {
        inst = this;
        GameState.OnRunStart += Reset;
        GameState.OnReturnToMenu += Reset;
    }

    void OnDestroy() { GameState.OnRunStart -= Reset; GameState.OnReturnToMenu -= Reset; if (inst == this) inst = null; }

    static void Reset() { Active = false; Left = MaxTime; locked = false; idle = 0f; }

    /// <summary>Called by PlayerHealth when a hit lands while guarding.</summary>
    public static void Blocked(Vector2 at)
    {
        Blocks++;
        if (inst != null) inst.blockFlash = 0.25f;
        Vfx.Burst(at, new Color(0.6f, 0.9f, 1f), 10, 4f * GS.S, 0.12f, 0.35f);
        Sfx.Play("ring_clang", 0.7f, Random.Range(1.1f, 1.5f));
        CameraRig.Shake(0.1f, 0.1f);
    }

    void MakeBubble()
    {
        var g = new GameObject("GuardBubble");
        g.transform.SetParent(transform, false);
        bubble = g.AddComponent<SpriteRenderer>();
        bubble.sprite = GameAssets.Glow;
        var r = new GameObject("GuardRim");
        r.transform.SetParent(g.transform, false);
        rim = r.AddComponent<SpriteRenderer>();
        rim.sprite = GameAssets.Single("ring");
        bubble.enabled = rim.enabled = false;
    }

    void Update()
    {
        if (player == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) player = p.transform;
        }
        bool canUse = GameState.Active && !GameState.Over && player != null;
        float dt = Time.deltaTime;

        bool want = canUse && !locked && InputHub.Held(KeyCode.E) && Left > 0f;
        if (want)
        {
            Active = true;
            idle = 0f;
            Left = Mathf.Max(0f, Left - dt);
            if (Left <= 0f) { Active = false; locked = true; Depletions++; }   // ran completely dry
        }
        else
        {
            Active = false;
            idle += dt;
            if (idle >= RegenDelay && Left < MaxTime)
            {
                Left = Mathf.Min(MaxTime, Left + (locked ? LockRegen : Regen) * dt);
                if (locked && Left >= UnlockAt) locked = false;
            }
        }

        if (bubble == null) MakeBubble();
        if (blockFlash > 0f) blockFlash -= dt;
        bool show = Active && player != null;
        bubble.enabled = rim.enabled = show;
        if (!show) return;
        Vector3 c = player.position + Vector3.up * 0.35f * GS.S;
        bubble.transform.position = c;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);
        float low = Left < 1.2f ? (0.5f + 0.5f * Mathf.Sin(Time.time * 24f)) : 1f;   // flickers when about to drop
        float s = 2.1f * GS.S * (1f + 0.04f * pulse + blockFlash * 0.5f);
        bubble.transform.localScale = Vector3.one * s;
        bubble.color = new Color(0.4f, 0.8f, 1f, (0.35f + 0.1f * pulse + blockFlash * 1.2f) * low);
        rim.transform.localScale = Vector3.one * 0.62f;
        rim.transform.localRotation = Quaternion.Euler(0, 0, Time.time * 90f);
        rim.color = new Color(0.7f, 0.95f, 1f, (0.5f + blockFlash * 2f) * low);
        var psr = player.GetComponent<SpriteRenderer>();
        if (psr != null)
        {
            bubble.sortingLayerID = psr.sortingLayerID; bubble.sortingOrder = psr.sortingOrder + 3;
            rim.sortingLayerID = psr.sortingLayerID; rim.sortingOrder = psr.sortingOrder + 4;
        }
    }
}

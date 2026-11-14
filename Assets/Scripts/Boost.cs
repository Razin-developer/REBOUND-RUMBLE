using UnityEngine;

public enum BoostKind { Speed, Power, Rapid, Storm, Aegis }

/// <summary>Timed power-ups. Other scripts only read the multipliers below.</summary>
public static class Boosts
{
    public const float Duration = 15f, AegisDuration = 8f;
    static readonly float[] until = new float[5];

    public static bool On(BoostKind k) { return Time.time < until[(int)k]; }
    public static float Left(BoostKind k) { return Mathf.Max(0f, until[(int)k] - Time.time); }
    public static float Total(BoostKind k) { return k == BoostKind.Aegis ? AegisDuration : Duration; }

    public static float Move { get { return On(BoostKind.Speed) ? 1.45f : 1f; } }
    public static float Damage { get { return On(BoostKind.Power) ? 2f : 1f; } }
    public static float FireDelay { get { return On(BoostKind.Rapid) ? 0.5f : 1f; } }
    public static float VolleyCooldownMul { get { return On(BoostKind.Rapid) ? 0.4f : 1f; } }
    public static float RingSpeed { get { return On(BoostKind.Storm) ? 1.3f : 1f; } }
    public static int ExtraHits { get { return On(BoostKind.Storm) ? 6 : 0; } }
    public static bool Aegis { get { return On(BoostKind.Aegis); } }
    public static bool Any { get { for (int i = 0; i < 5; i++) if (Time.time < until[i]) return true; return false; } }

    public static void Give(BoostKind k) { until[(int)k] = Time.time + Total(k); }
    public static void Clear() { for (int i = 0; i < until.Length; i++) until[i] = 0f; }

    public static string Name(BoostKind k)
    {
        switch (k)
        {
            case BoostKind.Speed: return "SPEED";
            case BoostKind.Power: return "POWER";
            case BoostKind.Rapid: return "RAPID FIRE";
            case BoostKind.Storm: return "RING STORM";
            default: return "AEGIS";
        }
    }
    public static string Blurb(BoostKind k)
    {
        switch (k)
        {
            case BoostKind.Speed: return "RUN 45% FASTER";
            case BoostKind.Power: return "RINGS HIT 2X HARDER";
            case BoostKind.Rapid: return "THROW FAST + QUICK VOLLEYS";
            case BoostKind.Storm: return "RINGS PIERCE + FLY FASTER";
            default: return "INVINCIBLE";
        }
    }
    public static Color Tint(BoostKind k)
    {
        switch (k)
        {
            case BoostKind.Speed: return new Color(0.3f, 0.9f, 1f);
            case BoostKind.Power: return new Color(1f, 0.4f, 0.2f);
            case BoostKind.Rapid: return new Color(1f, 0.9f, 0.3f);
            case BoostKind.Storm: return new Color(0.8f, 0.45f, 1f);
            default: return new Color(0.6f, 0.75f, 1f);
        }
    }
}

/// <summary>
/// Difficulty that scales with kills (smooth, works at any kill rate) plus the recurring FRENZY:
/// every 5:30 of survival, all enemies get 2x speed and 2x damage for 30 seconds.
/// </summary>
public static class Difficulty
{
    public const float FrenzyEvery = 330f, FrenzyLength = 30f, FrenzyWarn = 6f;

    static float Rt { get { return GameState.RunTime; } }
    public static bool Frenzy { get { return !Tutorial.Active && GameState.Mode == GameMode.Playing && Rt >= FrenzyEvery && Rt % FrenzyEvery < FrenzyLength; } }
    public static float FrenzyLeft { get { return Frenzy ? FrenzyLength - Rt % FrenzyEvery : 0f; } }
    public static float UntilFrenzy { get { return FrenzyEvery - (Rt % FrenzyEvery) + (Rt < FrenzyEvery ? 0f : 0f); } }
    public static bool Warning { get { return !Tutorial.Active && !Frenzy && Rt > 0f && UntilFrenzy <= FrenzyWarn; } }
    public static int FrenzyCount { get { return Mathf.FloorToInt(Rt / FrenzyEvery); } }

    /// <summary>Threat level shown in the HUD: grows with kills.</summary>
    public static int Threat { get { return 1 + GameState.Kills / 10; } }

    // Kill-based scaling: smooth and capped, so 10 kills/min or 300 kills/min both stay playable.
    public static float KillHp { get { return 1f + Mathf.Min(3f, GameState.Kills * 0.02f); } }
    public static float KillSpeed { get { return 1f + Mathf.Min(0.45f, GameState.Kills * 0.004f); } }
    public static float KillSpawnRate { get { return 1f + Mathf.Min(1.2f, GameState.Kills * 0.01f); } }

    public static float LiveSpeed { get { return Frenzy ? 2f : 1f; } }     // applied to enemies already alive too
    public static float DamageMul { get { return Frenzy ? 2f : 1f; } }
    public static float SpawnRate { get { return KillSpawnRate * (Frenzy ? 1.6f : 1f); } }
}

/// <summary>Spawns one rare pickup at a time (45s after the last was taken), runs the boost HUD and the frenzy banner.</summary>
public class BoostDirector : MonoBehaviour
{
    const float FirstAt = 25f, Gap = 45f, Lifetime = 60f, ExpireGap = 20f;

    static BoostDirector inst;
    GameObject pickup; BoostKind pickKind; float pickBorn;
    int pickLayer = -1; string pickSort = GameState.ActorLayer;
    float nextAt = FirstAt; BoostKind last = BoostKind.Aegis;
    Transform player;
    bool warned, frenzyShown; int lastFrenzy;
    float toast; string toastTitle = "", toastSub = ""; Color toastCol = Color.white;
    GUIStyle st; Texture2D pix;
    float spin;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; Boosts.Clear(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { var go = new GameObject("BoostDirector"); inst = go.AddComponent<BoostDirector>(); }

    void Start()
    {
        GameState.OnRunStart += Reset;
        GameState.OnReturnToMenu += Reset;
    }
    void OnDestroy() { GameState.OnRunStart -= Reset; GameState.OnReturnToMenu -= Reset; }

    void Reset()
    {
        Boosts.Clear();
        if (pickup != null) Destroy(pickup);
        pickup = null; nextAt = FirstAt; warned = frenzyShown = false; lastFrenzy = 0; toast = 0f;
    }

    void Update()
    {
        if (!GameState.Active || Tutorial.Active || Coop.IsGuest) return;
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) player = p.transform; else return; }
        float t = GameState.RunTime;

        // ---- frenzy events ----
        if (Difficulty.Warning && !warned) { warned = true; Sfx.Play("hurt", 0.6f, 0.6f); Show("FRENZY INCOMING", "ENEMIES GET 2X SPEED + 2X DAMAGE", new Color(1f, 0.3f, 0.25f), 4f); }
        if (Difficulty.Frenzy && !frenzyShown) { frenzyShown = true; lastFrenzy = Difficulty.FrenzyCount; Sfx.Play("gameover", 0.5f, 1.2f); CameraRig.Shake(0.5f, 0.5f); Show("FRENZY!", "SURVIVE 30s  -  SCORE x2", new Color(1f, 0.25f, 0.2f), 4f); }
        if (!Difficulty.Frenzy && frenzyShown) { frenzyShown = false; warned = false; Show("FRENZY OVER", "CATCH YOUR BREATH", new Color(0.6f, 1f, 0.7f), 3f); }

        // ---- pickup lifecycle ----
        if (pickup == null)
        {
            if (t >= nextAt) TrySpawn();
        }
        else
        {
            spin += Time.deltaTime;
            float bob = Mathf.Sin(spin * 3f) * 0.08f;
            var tr = pickup.transform;
            tr.position = new Vector3(tr.position.x, tr.position.y, 0f);
            pickup.transform.GetChild(0).localPosition = new Vector3(0f, 0.25f + bob, 0f);
            pickup.transform.GetChild(1).localScale = Vector3.one * (1.6f + Mathf.Sin(spin * 5f) * 0.25f);

            bool sameLevel = pickLayer < 0 || player.gameObject.layer == pickLayer;
            if ((sameLevel && ((Vector2)player.position - (Vector2)tr.position).sqrMagnitude < (0.9f * GS.S + 0.4f) * (0.9f * GS.S + 0.4f)) || Coop.AvatarNear(tr.position, 0.9f * GS.S + 0.4f, pickLayer)) Collect();
            else if (t - pickBorn > Lifetime) { Vfx.Burst(tr.position, Boosts.Tint(pickKind), 10, 2f, 0.12f, 0.5f); Destroy(pickup); pickup = null; nextAt = t + ExpireGap; }
        }
    }

    void TrySpawn()
    {
        Vector2 pos; string lay;
        if (!Nav.TryFreeSpotNear(player.position, 6f, 18f, out pos, out lay) && !Nav.TryFreeSpotNear(player.position, 3f, 40f, out pos, out lay)) { nextAt = GameState.RunTime + 3f; return; }
        SpawnAt(pos, lay);
    }

    public static void Drop(Vector2 pos) { if (inst == null) return; if (inst.pickup != null) Destroy(inst.pickup); inst.pickup = null; inst.SpawnAt(pos, GameState.ActorLayer); }

    void SpawnAt(Vector2 pos, string lay)
    {
        pickLayer = LayerMask.NameToLayer(lay); pickSort = lay;
        BoostKind k;
        do { k = (BoostKind)Random.Range(0, 5); } while (k == last);
        pickKind = k; last = k; pickBorn = GameState.RunTime;

        pickup = new GameObject("Boost " + k);
        pickup.transform.position = pos;
        Color c = Boosts.Tint(k);
        var orb = new GameObject("Orb"); orb.transform.SetParent(pickup.transform, false);
        orb.transform.localScale = Vector3.one * 0.75f * GS.S * 1.4f;
        var osr = orb.AddComponent<SpriteRenderer>(); osr.sprite = GameAssets.Glow; osr.color = Color.Lerp(c, Color.white, 0.55f);
        osr.sortingLayerName = pickSort; osr.sortingOrder = 6;
        var glow = new GameObject("Glow"); glow.transform.SetParent(pickup.transform, false);
        var gsr = glow.AddComponent<SpriteRenderer>(); gsr.sprite = GameAssets.Glow; gsr.color = new Color(c.r, c.g, c.b, 0.5f);
        gsr.sortingLayerName = pickSort; gsr.sortingOrder = 5;
        Vfx.Shockwave(pos, c, 1.4f, 0.5f);
        FloatingText.Add(pos + Vector2.up * 0.8f, "BOOST!", c, 18, 1.5f);
    }

    void Collect()
    {
        Vector2 pos = pickup.transform.position;
        Boosts.Give(pickKind);
        Color c = Boosts.Tint(pickKind);
        Vfx.Burst(pos, c, 28, 5f, 0.18f, 0.6f);
        Vfx.Shockwave(pos, c, 2.2f, 0.45f);
        Sfx.Play("ring_clang", 0.9f, 1.4f);
        FloatingText.Add((Vector2)player.position + Vector2.up * 1.1f, Boosts.Name(pickKind), c, 20, 1.4f);
        Show(Boosts.Name(pickKind), Boosts.Blurb(pickKind), c, 2.2f);
        Destroy(pickup); pickup = null;
        nextAt = GameState.RunTime + Gap;      // the next one comes 45s after this one was taken
    }

    public static void Announce(string a, string b, Color c, float secs) { if (inst != null) inst.Show(a, b, c, secs); }

    void Show(string a, string b, Color c, float secs) { toastTitle = a; toastSub = b; toastCol = c; toast = Time.unscaledTime + secs; Coop.SendAnnounce(a, b, c, secs); }

    /// <summary>Co-op snapshot: the pickup as "kind,x,y,layer" (or "-").</summary>
    public static string Snap()
    {
        if (inst == null || inst.pickup == null) return "-";
        Vector3 p = inst.pickup.transform.position;
        return (int)inst.pickKind + "," + p.x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "," + p.y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "," + (inst.pickLayer >= 20 ? inst.pickLayer - 20 : 0);
    }

    // ------------------------------------------------------------------ HUD
    void Draw(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, pix); GUI.color = o; }
    void Txt(Rect r, string s, int size, Color c, TextAnchor a)
    {
        st.fontSize = size; st.alignment = a;
        st.normal.textColor = new Color(0, 0, 0, c.a * 0.85f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
        st.normal.textColor = c; GUI.Label(r, s, st);
    }

    void OnGUI()
    {
        if (GameState.Mode != GameMode.Playing || Tutorial.Active || Cinematic.Active || Event.current.type != EventType.Repaint) return;
        if (pix == null) { pix = new Texture2D(1, 1); pix.SetPixel(0, 0, Color.white); pix.Apply(); st = new GUIStyle { font = GameAssets.PixelFont, wordWrap = false }; }
        GUI.depth = -5;
        float sc = Mathf.Min(Screen.height / 720f, Screen.width / 1000f);
        Matrix4x4 m = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
        float W = Screen.width / sc, H = Screen.height / sc;

        // frenzy: red pulse over the whole screen + banner
        if (Difficulty.Frenzy)
        {
            float p = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            Draw(new Rect(0, 0, W, 14), new Color(1f, 0.1f, 0.1f, 0.25f + 0.2f * p));
            Draw(new Rect(0, H - 14, W, 14), new Color(1f, 0.1f, 0.1f, 0.25f + 0.2f * p));
            Draw(new Rect(0, 0, 14, H), new Color(1f, 0.1f, 0.1f, 0.2f + 0.15f * p));
            Draw(new Rect(W - 14, 0, 14, H), new Color(1f, 0.1f, 0.1f, 0.2f + 0.15f * p));
            Txt(new Rect(W / 2 - 200, 66, 400, 24), "FRENZY  " + Mathf.CeilToInt(Difficulty.FrenzyLeft) + "s", 18, new Color(1f, 0.35f, 0.3f), TextAnchor.MiddleCenter);
        }
        else if (Difficulty.Warning)
            Txt(new Rect(W / 2 - 200, 66, 400, 24), "FRENZY IN " + Mathf.CeilToInt(Difficulty.UntilFrenzy), 18, new Color(1f, 0.6f, 0.3f, 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 10f)), TextAnchor.MiddleCenter);

        // threat level + next frenzy
        int nf = Mathf.CeilToInt(Difficulty.UntilFrenzy);
        Txt(new Rect(W / 2 - 200, 42, 400, 20), "THREAT " + Difficulty.Threat + "   |   FRENZY " + (nf / 60) + ":" + (nf % 60).ToString("00"), 11, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);

        // active boost bars (top-left, under the hearts)
        float y = 56;
        for (int i = 0; i < 5; i++)
        {
            var k = (BoostKind)i;
            if (!Boosts.On(k)) continue;
            Color c = Boosts.Tint(k);
            float f = Boosts.Left(k) / Boosts.Total(k);
            Draw(new Rect(18, y, 190, 20), new Color(0, 0, 0, 0.55f));
            Draw(new Rect(19, y + 1, 188 * f, 18), new Color(c.r, c.g, c.b, 0.55f));
            Txt(new Rect(24, y, 180, 20), Boosts.Name(k) + "  " + Mathf.CeilToInt(Boosts.Left(k)) + "s", 11, Color.white, TextAnchor.MiddleLeft);
            y += 24;
        }

        // arrow to the pickup when it is off screen
        if (pickup != null && Camera.main != null)
        {
            Vector3 sp = Camera.main.WorldToScreenPoint(pickup.transform.position);
            Vector2 s = new Vector2(sp.x / sc, (Screen.height - sp.y) / sc);
            bool inside = sp.z > 0 && s.x > 20 && s.x < W - 20 && s.y > 20 && s.y < H - 20;
            if (!inside)
            {
                Vector2 c0 = new Vector2(W / 2, H / 2), d = s - c0;
                if (sp.z < 0) d = -d;
                float k = Mathf.Min((W / 2 - 46) / Mathf.Max(0.01f, Mathf.Abs(d.x)), (H / 2 - 46) / Mathf.Max(0.01f, Mathf.Abs(d.y)));
                Vector2 e = c0 + d * k;
                Color c = Boosts.Tint(pickKind);
                float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f);
                Draw(new Rect(e.x - 14, e.y - 14, 28, 28), new Color(c.r, c.g, c.b, pulse));
                Draw(new Rect(e.x - 9, e.y - 9, 18, 18), new Color(1, 1, 1, 0.8f));
                Txt(new Rect(e.x - 70, e.y + 16, 140, 16), Boosts.Name(pickKind), 10, c, TextAnchor.MiddleCenter);
            }
        }

        // pickup / event toast
        if (Time.unscaledTime < toast)
        {
            float a = Mathf.Clamp01((toast - Time.unscaledTime) * 2f);
            Txt(new Rect(W / 2 - 300, H * 0.22f, 600, 40), toastTitle, 30, new Color(toastCol.r, toastCol.g, toastCol.b, a), TextAnchor.MiddleCenter);
            Txt(new Rect(W / 2 - 300, H * 0.22f + 40, 600, 24), toastSub, 13, new Color(1, 1, 1, a), TextAnchor.MiddleCenter);
        }
        GUI.matrix = m;
    }
}

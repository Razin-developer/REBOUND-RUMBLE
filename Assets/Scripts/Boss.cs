using UnityEngine;

/// <summary>THE HOLLOW KING: a hand-built pixel-art boss (drawn in code, 4 animation frames, no external art).</summary>
public static class BossArt
{
    const int N = 64;
    static Sprite[] frames;
    static Color32[] buf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { frames = null; }

    public static Sprite[] Frames()
    {
        if (frames != null && frames[0] != null) return frames;
        frames = new Sprite[4];
        for (int f = 0; f < 4; f++) frames[f] = Make(f);
        return frames;
    }

    static Color32 C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }
    static Color32 Lerp(Color32 a, Color32 b, float t) { return Color32.Lerp(a, b, t); }
    static void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < N && y < N) buf[y * N + x] = c; }
    static void Box(int x0, int y0, int x1, int y1, Color32 c) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(x, y, c); }

    // shaded ellipse: light from the top-left
    static void Ell(float cx, float cy, float rx, float ry, Color32 c)
    {
        Color32 hi = Lerp(c, C(255, 255, 255), 0.28f), lo = Lerp(c, C(0, 0, 0), 0.38f);
        for (int y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
            for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                if (dx * dx + dy * dy > 1f) continue;
                float light = -dx * 0.5f + dy * 0.7f;
                Set(x, y, light > 0.38f ? hi : (light < -0.4f ? lo : c));
            }
    }
    static void EllM(float cx, float cy, float rx, float ry, Color32 c) { Ell(cx, cy, rx, ry, c); Ell(N - 1 - cx, cy, rx, ry, c); }

    static void Tri(float x0, float y0, float x1, float y1, float x2, float y2, Color32 c)
    {
        int minx = Mathf.FloorToInt(Mathf.Min(x0, Mathf.Min(x1, x2))), maxx = Mathf.CeilToInt(Mathf.Max(x0, Mathf.Max(x1, x2)));
        int miny = Mathf.FloorToInt(Mathf.Min(y0, Mathf.Min(y1, y2))), maxy = Mathf.CeilToInt(Mathf.Max(y0, Mathf.Max(y1, y2)));
        for (int y = miny; y <= maxy; y++)
            for (int x = minx; x <= maxx; x++)
            {
                float d = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
                if (Mathf.Abs(d) < 0.0001f) continue;
                float a = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / d;
                float b = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / d;
                float g = 1f - a - b;
                if (a >= 0f && b >= 0f && g >= 0f) Set(x, y, c);
            }
    }
    static void TriM(float x0, float y0, float x1, float y1, float x2, float y2, Color32 c)
    {
        Tri(x0, y0, x1, y1, x2, y2, c); Tri(N - 1 - x0, y0, N - 1 - x1, y1, N - 1 - x2, y2, c);
    }

    static Sprite Make(int f)
    {
        buf = new Color32[N * N];
        int b = (f == 1 || f == 2) ? 1 : 0;           // body bob
        int liftL = f == 1 ? 3 : 0, liftR = f == 3 ? 3 : 0;
        int sway = (f % 2 == 0) ? 0 : 1;
        bool pulse = f == 1 || f == 2;

        Color32 stone = C(106, 111, 140), armor = C(61, 48, 98), dark = C(44, 37, 64), gold = C(222, 178, 56), goldHi = C(255, 226, 120);
        Color32 bone = C(224, 214, 178), capeA = C(110, 22, 58), capeB = C(78, 14, 44), boot = C(74, 69, 96);
        Color32 glow = pulse ? C(140, 255, 255) : C(70, 220, 240);

        // cape (behind everything), with fold stripes and a swaying hem
        for (int y = 6; y <= 46 + b; y++)
        {
            float k = (46 + b - y) / 40f;
            int half = Mathf.RoundToInt(Mathf.Lerp(15, 25, k));
            int cx = 32 + Mathf.RoundToInt(sway * k * 2f);
            for (int x = cx - half; x <= cx + half; x++) Set(x, y, ((x - cx + 40) % 9 < 3) ? capeB : capeA);
        }
        // jagged cape hem
        for (int x = 8; x < 56; x += 4) { Set(x + sway, 5, capeB); Set(x + 1 + sway, 5, capeB); }

        // boots
        Box(20, 2 + liftL, 29, 12 + liftL, boot); Box(20, 2 + liftL, 29, 4 + liftL, dark); Box(21, 11 + liftL, 24, 12 + liftL, stone);
        Box(34, 2 + liftR, 43, 12 + liftR, boot); Box(34, 2 + liftR, 43, 4 + liftR, dark); Box(35, 11 + liftR, 38, 12 + liftR, stone);

        // arms and fists
        Box(7, 17 + b, 13, 39 + b, dark); Box(50, 17 + b, 56, 39 + b, dark);
        Box(8, 18 + b, 9, 38 + b, armor); Box(51, 18 + b, 52, 38 + b, armor);
        EllM(10, 15 + b, 6, 6, stone);
        for (int i = 0; i < 3; i++) { Set(7 + i * 2, 11 + b, dark); Set(56 - i * 2, 11 + b, dark); }   // knuckles

        // torso, belt, and the rune ring on the chest (the ring is the game's whole theme)
        Ell(32, 28 + b, 15, 16, armor);
        Box(18, 16 + b, 45, 19 + b, gold); Box(18, 19 + b, 45, 19 + b, goldHi);
        Ell(32, 17.5f + b, 3.5f, 3.5f, C(210, 40, 70));
        for (int y = 20 + b; y <= 40 + b; y++)
            for (int x = 22; x <= 41; x++)
            {
                float dx = x - 31.5f, dy = y - (30 + b);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= 4.6f) Set(x, y, glow);
                else if (d <= 6.9f) Set(x, y, d < 5.8f ? gold : goldHi);
            }
        // rune ticks around the ring
        Set(32, 39 + b, goldHi); Set(32, 21 + b, goldHi); Set(23, 30 + b, goldHi); Set(40, 30 + b, goldHi);

        // pauldrons with spikes
        EllM(14, 41 + b, 10, 7, stone);
        TriM(8, 46 + b, 11, 55 + b, 14, 47 + b, stone);
        TriM(14, 47 + b, 17, 54 + b, 20, 47 + b, stone);
        EllM(14, 41 + b, 3, 2, gold);

        // head, helm slit, glowing eyes
        Ell(32, 49 + b, 10, 9, dark);
        Box(24, 47 + b, 39, 50 + b, C(18, 12, 30));
        Box(26, 48 + b, 30, 49 + b, glow); Box(33, 48 + b, 37, 49 + b, glow);
        Set(31, 47 + b, C(18, 12, 30)); Set(32, 47 + b, C(18, 12, 30));
        for (int i = 0; i < 6; i++) Set(26 + i * 2, 44 + b, bone);   // teeth along the jaw

        // horns
        TriM(23, 53 + b, 16, 62, 27, 57 + b, bone);
        TriM(22, 52 + b, 14, 58, 17, 53 + b, bone);

        // crown: gold band, five spikes, red gem
        Box(24, 56 + b, 39, 58 + b, gold); Box(24, 58 + b, 39, 58 + b, goldHi);
        Tri(24, 58 + b, 26, 62, 28, 58 + b, gold); Tri(28, 58 + b, 30, 62, 32, 58 + b, gold);
        Tri(30, 58 + b, 32, 63, 34, 58 + b, goldHi); Tri(31, 58 + b, 33, 62, 35, 58 + b, gold);
        Tri(35, 58 + b, 37, 62, 39, 58 + b, gold);
        Box(31, 56 + b, 32, 57 + b, C(230, 40, 70));

        // 1px dark outline around the whole silhouette
        var o = new Color32[buf.Length];
        System.Array.Copy(buf, o, buf.Length);
        Color32 line = C(14, 8, 24);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                if (buf[y * N + x].a != 0) continue;
                bool edge = (x > 0 && buf[y * N + x - 1].a != 0) || (x < N - 1 && buf[y * N + x + 1].a != 0) || (y > 0 && buf[(y - 1) * N + x].a != 0) || (y < N - 1 && buf[(y + 1) * N + x].a != 0);
                if (edge) o[y * N + x] = line;
            }
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(o); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 32f);
    }
}

/// <summary>Spawns THE HOLLOW KING every 3 minutes, draws its health bar, and rewards the kill.</summary>
public class BossDirector : MonoBehaviour
{
    const float Every = 180f;
    static BossDirector inst;
    int index;                 // bosses spawned so far
    float nextAt = Every;
    bool announced;
    GUIStyle st; Texture2D pix;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; Enemy.Boss = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { var go = new GameObject("BossDirector"); inst = go.AddComponent<BossDirector>(); }

    void Start() { GameState.OnRunStart += Reset; GameState.OnReturnToMenu += Reset; }
    void OnDestroy() { GameState.OnRunStart -= Reset; GameState.OnReturnToMenu -= Reset; }
    void Reset() { index = 0; nextAt = Every; announced = false; }

    public static void OnBossDied(Vector2 pos)
    {
        GameState.Heal(2);
        BoostDirector.Drop(pos);
        BoostDirector.Announce("THE HOLLOW KING FALLS", "+2 HEARTS  -  BOOST DROPPED", new Color(1f, 0.85f, 0.3f), 3.5f);
        if (inst != null) inst.nextAt = Mathf.Max(inst.nextAt, GameState.RunTime + 45f);
    }

    void Update()
    {
        if (!GameState.Active || Tutorial.Active || Enemy.Boss != null || Coop.IsGuest) return;
        float t = GameState.RunTime;
        if (!announced && t >= nextAt - 6f) { announced = true; BoostDirector.Announce("THE HOLLOW KING APPROACHES", "SLAMS THE GROUND  -  SUMMONS MINIONS  -  ENRAGES AT HALF HEALTH", new Color(0.8f, 0.5f, 1f), 5f); Sfx.Play("hurt", 0.7f, 0.5f); }
        if (t < nextAt) return;

        var pl = GameObject.FindWithTag("Player");
        if (pl == null) return;
        Vector2 pos, fb;
        if (!Nav.TryGetSpawnPoint(pl.transform.position, 9f, out pos, out fb)) { if (fb == Vector2.zero) return; pos = fb; }
        Enemy.Spawn(EnemyKind.Boss, pos, 1f + 0.5f * index, 1f);
        index++; announced = false;
        nextAt = t + Every;
        CameraRig.Shake(0.5f, 0.6f);
    }

    void OnGUI()
    {
        var b = Enemy.Boss;
        if (b == null || GameState.Mode != GameMode.Playing || Tutorial.Active || Cinematic.Active || Event.current.type != EventType.Repaint) return;
        if (pix == null) { pix = new Texture2D(1, 1); pix.SetPixel(0, 0, Color.white); pix.Apply(); st = new GUIStyle { font = GameAssets.PixelFont, alignment = TextAnchor.MiddleCenter }; }
        GUI.depth = -6;
        float sc = Mathf.Min(Screen.height / 720f, Screen.width / 1000f);
        Matrix4x4 m = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
        float W = Screen.width / sc;
        float w = Mathf.Min(460f, W - 40f), x = (W - w) / 2f, y = 100f;
        var o = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.7f); GUI.DrawTexture(new Rect(x - 3, y - 3, w + 6, 20), pix);
        GUI.color = b.Enraged ? new Color(1f, 0.25f, 0.2f) : new Color(0.7f, 0.4f, 1f); GUI.DrawTexture(new Rect(x, y, w * b.HpFrac, 14), pix);
        GUI.color = o;
        st.fontSize = 13;
        st.normal.textColor = Color.black; GUI.Label(new Rect(x + 1, y - 24, w, 22), "THE HOLLOW KING" + (b.Enraged ? "  -  ENRAGED" : ""), st);
        st.normal.textColor = b.Enraged ? new Color(1f, 0.5f, 0.4f) : new Color(0.9f, 0.8f, 1f); GUI.Label(new Rect(x, y - 25, w, 22), "THE HOLLOW KING" + (b.Enraged ? "  -  ENRAGED" : ""), st);
        GUI.matrix = m;
    }
}

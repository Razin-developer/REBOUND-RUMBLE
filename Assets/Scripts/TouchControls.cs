using UnityEngine;

/// <summary>
/// On-screen controls for phones/tablets (and touch laptops): a floating joystick on the left half,
/// THROW / VOLLEY / GUARD / TELEPORT buttons on the right. Feeds InputHub, RingLauncher, Guard and the rune teleport.
/// Turns itself off again as soon as a keyboard key is used.
/// </summary>
public class TouchControls : MonoBehaviour
{
    public static bool Active;
    public static Vector2 Move;
    public static bool ThrowHeld, VolleyHeld, GuardHeld, TeleportPressed;

    static bool touchSeen;
    int stickId = -1;
    Vector2 stickBase, stickKnob;     // GUI space (origin top-left, pixels)
    bool tpWas;
    Texture2D disc, ringTex;
    GUIStyle label;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = false; Move = Vector2.zero; ThrowHeld = VolleyHeld = GuardHeld = TeleportPressed = false; touchSeen = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("TouchControls");
        DontDestroyOnLoad(go);
        go.AddComponent<TouchControls>();
    }

    // ---- layout (pixels, y down) ----
    float U { get { return Screen.height * 0.17f; } }
    struct Btn { public Vector2 c; public float r; }
    Btn BThrow { get { return new Btn { c = new Vector2(Screen.width - U * 1.0f, Screen.height * 0.68f), r = U * 0.70f }; } }
    Btn BGuard { get { return new Btn { c = new Vector2(Screen.width - U * 1.15f, Screen.height * 0.68f - U * 1.55f), r = U * 0.52f }; } }
    Btn BVolley { get { return new Btn { c = new Vector2(Screen.width - U * 2.5f, Screen.height * 0.74f), r = U * 0.52f }; } }
    Btn BTele { get { return new Btn { c = new Vector2(Screen.width - U * 2.75f, Screen.height * 0.74f - U * 1.2f), r = U * 0.42f }; } }
    static bool Hit(Btn b, Vector2 p) { return (p - b.c).sqrMagnitude <= (b.r * 1.15f) * (b.r * 1.15f); }
    bool TeleOn { get { return RunePillars.Total > 0 && RunePillars.Attuned > 0; } }

    void Update()
    {
        // keyboard use switches touch controls off on touch laptops (phones keep them on)
        if (!Application.isMobilePlatform && touchSeen &&
            (InputHub.Held(KeyCode.W) || InputHub.Held(KeyCode.A) || InputHub.Held(KeyCode.S) || InputHub.Held(KeyCode.D) || InputHub.Held(KeyCode.Space) || InputHub.Held(KeyCode.E) || InputHub.Held(KeyCode.Q)))
            touchSeen = false;
        if (Input.touchCount > 0 && !Application.isMobilePlatform) touchSeen = true;
        Active = Application.isMobilePlatform || touchSeen;

        Move = Vector2.zero; ThrowHeld = VolleyHeld = GuardHeld = false; TeleportPressed = false;
        bool playing = Active && GameState.Active && !Cinematic.Active;
        if (!playing) { stickId = -1; tpWas = false; return; }

        bool tp = false;
        bool stickAlive = false;
        float H = Screen.height, W = Screen.width;
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            Vector2 p = new Vector2(t.position.x, H - t.position.y);

            if (t.fingerId == stickId)
            {
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                stickAlive = true; stickKnob = p; continue;
            }
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;

            if (Hit(BThrow, p)) { ThrowHeld = true; continue; }
            if (Hit(BVolley, p)) { VolleyHeld = true; continue; }
            if (Hit(BGuard, p)) { GuardHeld = true; continue; }
            if (TeleOn && Hit(BTele, p)) { tp = true; continue; }

            // a new finger in the lower-left zone becomes the joystick
            if (stickId < 0 && t.phase == TouchPhase.Began && p.x < W * 0.5f && p.y > H * 0.30f)
            {
                stickId = t.fingerId; stickBase = p; stickKnob = p; stickAlive = true;
            }
        }
        if (!stickAlive) stickId = -1;
        if (stickId >= 0)
        {
            float R = H * 0.12f;
            Vector2 d = (stickKnob - stickBase) / R;
            d.y = -d.y;                                  // screen y is down, world y is up
            if (d.magnitude > 1f) { d.Normalize(); stickBase = stickKnob - new Vector2(d.x, -d.y) * R; }   // base follows when dragged past the edge
            Move = d.magnitude < 0.18f ? Vector2.zero : d;
        }
        TeleportPressed = tp && !tpWas;
        tpWas = tp;
    }

    // ---- drawing ----
    void Make()
    {
        if (disc != null) return;
        const int N = 96;
        disc = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        ringTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - N / 2f + 0.5f) * (x - N / 2f + 0.5f) + (y - N / 2f + 0.5f) * (y - N / 2f + 0.5f)) / (N / 2f);
                disc.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 24f)));
                ringTex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1f - Mathf.Abs(d - 0.93f) * 22f))));
            }
        disc.Apply(); ringTex.Apply();
        label = new GUIStyle { font = GameAssets.PixelFont, alignment = TextAnchor.MiddleCenter, wordWrap = false };
    }

    void Disc(Vector2 c, float r, Color col) { var o = GUI.color; GUI.color = col; GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2, r * 2), disc); GUI.color = o; }
    void RingAt(Vector2 c, float r, Color col) { var o = GUI.color; GUI.color = col; GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2, r * 2), ringTex); GUI.color = o; }

    void Button(Btn b, string text, bool held, bool primary)
    {
        Color gold = new Color(1f, 0.8f, 0.25f);
        Disc(b.c, b.r, held ? new Color(0.55f, 0.36f, 0.05f, 0.85f) : new Color(0.06f, 0.05f, 0.1f, 0.55f));
        RingAt(b.c, b.r, held ? gold : new Color(1f, 1f, 1f, primary ? 0.8f : 0.55f));
        label.fontSize = Mathf.Max(9, Mathf.RoundToInt(b.r * 0.28f));
        label.normal.textColor = held ? new Color(1f, 0.95f, 0.6f) : new Color(1f, 1f, 1f, 0.9f);
        GUI.Label(new Rect(b.c.x - b.r, b.c.y - b.r * 0.5f, b.r * 2, b.r), text, label);
    }

    void OnGUI()
    {
        if (!Active || Event.current.type != EventType.Repaint) return;
        if (GameState.Mode != GameMode.Playing || GameState.Paused || GameState.Over || Cinematic.Active) return;
        Make();
        GUI.depth = -20;
        Matrix4x4 m = GUI.matrix; GUI.matrix = Matrix4x4.identity;

        float H = Screen.height;
        Vector2 sb = stickId >= 0 ? stickBase : new Vector2(U * 1.5f, H * 0.74f);
        Vector2 sk = stickId >= 0 ? stickKnob : sb;
        float R = H * 0.12f;
        Vector2 off = Vector2.ClampMagnitude(sk - sb, R);
        Disc(sb, R, new Color(0.06f, 0.05f, 0.1f, stickId >= 0 ? 0.45f : 0.22f));
        RingAt(sb, R, new Color(1f, 1f, 1f, stickId >= 0 ? 0.6f : 0.3f));
        Disc(sb + off, R * 0.45f, new Color(1f, 0.85f, 0.4f, stickId >= 0 ? 0.85f : 0.35f));

        Button(BThrow, "THROW", ThrowHeld, true);
        Button(BVolley, "VOLLEY", VolleyHeld, false);
        Button(BGuard, "GUARD", GuardHeld, false);
        if (TeleOn) Button(BTele, "TELEPORT", tpWas, false);
        GUI.matrix = m;
    }
}

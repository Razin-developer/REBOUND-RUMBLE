using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// All UI for REBOUND RUMBLE (IMGUI, auto-installed): sign-in, main menu, leaderboard, settings, controls (Tab),
/// pause (Esc), the death screen, and the in-game HUD.
/// </summary>
public partial class GameUI : MonoBehaviour
{
    enum Page { None, Splash, Loading, Story, Auth, Main, Leaderboard, Settings, Controls, Pause, GameOver, Coop }
    Page page = Page.Main;
    Page settingsReturn = Page.Main;
    int settingsTab;
    static readonly string[] SettingsTabs = { "PROFILE", "SOUND", "VISUALS", "PERFORMANCE", "ACCOUNT" };

    GUIStyle label, title, btn, btnSmall, small, box, field;
    Texture2D white, heartTex;
    float scale = 1f;
    float fps, fpsTimer; int fpsFrames;
    int lastRank = -1;
    float overAt = -1f;
    string user = "", pass = "", serverField;

    static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
    static readonly Color Panel = new Color(0.05f, 0.04f, 0.08f, 0.92f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindAnyObjectByType<GameUI>() == null) new GameObject("GameUI").AddComponent<GameUI>();
    }

    void Awake()
    {
        page = Page.Splash;
        user = Net.User;
        GameState.OnReturnToMenu += () => { page = Page.Main; overAt = -1f; BeginFadeOut(); };
        GameState.OnRunStart += () => { page = Page.None; overAt = -1f; };
    }

    void Update()
    {
        FloatingText.Tick(Time.deltaTime);
        fpsFrames++; fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 0.5f) { fps = fpsFrames / fpsTimer; fpsFrames = 0; fpsTimer = 0f; }

        if (page != lastPage) { lastPage = page; pageStart = Time.unscaledTime; }
        UpdateFlow();
        CoopTick();
        if (page == Page.Auth && Net.State != AccountState.LoggedOut) { pass = ""; AfterAuth(); }
        if (page == Page.Main && Net.State == AccountState.LoggedOut && GameState.Mode == GameMode.Menu) page = Page.Auth;

        if (GameState.Mode == GameMode.Playing)
        {
            if (GameState.Over)
            {
                if (overAt < 0f) { overAt = Time.unscaledTime; page = Page.None; }
                if (page != Page.GameOver && Time.unscaledTime - overAt > 1.1f) { page = Page.GameOver; lastRank = RankOfScore(GameState.Score); }
                return;
            }
            if (InputHub.Pressed(KeyCode.Tab))
            {
                if (page == Page.Controls) Resume();
                else if (page == Page.None) { GameState.SetPaused(true); page = Page.Controls; Sfx.PlayUI("ui_open"); }
            }
            else if (InputHub.Pressed(KeyCode.Escape))
            {
                if (page == Page.None) Pause();
                else if (page == Page.Pause || page == Page.Controls) Resume();
                else if (page == Page.Settings) { GameSettings.Save(); page = settingsReturn; }
            }
        }
        else if (InputHub.Pressed(KeyCode.Escape))
        {
            if (page == Page.Settings) { GameSettings.Save(); page = settingsReturn; }
            else if (page == Page.Leaderboard) page = Page.Main;
            else if (page == Page.Coop) LeaveCoopPage();
        }
    }

    void Pause() { GameState.SetPaused(true); page = Page.Pause; Sfx.PlayUI("ui_open"); }
    void Resume() { GameState.SetPaused(false); page = Page.None; Sfx.PlayUI("ui_close"); }

    int RankOfScore(int score)
    {
        var top = Leaderboard.Top(100);
        for (int i = 0; i < top.Count; i++) if (top[i].mine && top[i].score == score) return i + 1;
        return -1;
    }

    // ------------------------------------------------------------------ styles
    void EnsureStyles()
    {
        if (label != null) return;
        white = Texture2D.whiteTexture;
        Font f = GameAssets.PixelFont;
        label = new GUIStyle(GUI.skin.label) { font = f, fontSize = 12, normal = { textColor = Color.white }, alignment = TextAnchor.MiddleLeft, wordWrap = false };
        small = new GUIStyle(label) { fontSize = 9, normal = { textColor = new Color(0.8f, 0.8f, 0.85f) } };
        title = new GUIStyle(label) { fontSize = 40, alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } };
        btn = new GUIStyle(GUI.skin.button) { font = f, fontSize = 14, alignment = TextAnchor.MiddleCenter };
        btn.normal.background = MakeTex(new Color(0.16f, 0.12f, 0.2f, 1f)); btn.normal.textColor = Color.white;
        btn.hover.background = MakeTex(new Color(0.45f, 0.3f, 0.1f, 1f)); btn.hover.textColor = Gold;
        btn.active.background = MakeTex(new Color(0.7f, 0.45f, 0.1f, 1f)); btn.active.textColor = Color.white;
        btnSmall = new GUIStyle(btn) { fontSize = 10 };
        box = new GUIStyle(GUI.skin.box);
        box.normal.background = MakeTex(Panel);
        field = new GUIStyle(GUI.skin.textField) { font = f, fontSize = 14, alignment = TextAnchor.MiddleLeft };
        heartTex = MakeHeart();
    }

    static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1) { filterMode = FilterMode.Point };
        t.SetPixel(0, 0, c); t.Apply();
        return t;
    }

    static Texture2D MakeHeart()
    {
        string[] rows = { ".##.##.", "#######", "#######", ".#####.", "..###..", "...#..." };
        var t = new Texture2D(9, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < 8; y++) for (int x = 0; x < 9; x++) t.SetPixel(x, y, Color.clear);
        for (int r = 0; r < 6; r++)
            for (int c = 0; c < 7; c++)
            {
                if (rows[r][c] != '#') continue;
                int px = c + 1, py = 6 - r;
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                    if (t.GetPixel(px + dx, py + dy).a < 0.5f) t.SetPixel(px + dx, py + dy, new Color(0.15f, 0.04f, 0.06f));
            }
        for (int r = 0; r < 6; r++)
            for (int c = 0; c < 7; c++)
                if (rows[r][c] == '#') t.SetPixel(c + 1, 6 - r, Color.white);   // tinted by GUI.color
        t.SetPixel(2, 5, new Color(1f, 0.8f, 0.85f)); t.SetPixel(2, 4, new Color(1f, 0.8f, 0.85f));
        t.Apply();
        return t;
    }

    void Rect(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }

    void Text(Rect r, string s, GUIStyle st, Color c, TextAnchor a = TextAnchor.MiddleLeft, int size = 0)
    {
        var oc = st.normal.textColor; var oa = st.alignment; var os = st.fontSize;
        st.alignment = a; if (size > 0) st.fontSize = size;
        var hv = st.hover.textColor; var ac = st.active.textColor;
        // drop shadow only on big text: on small pixel text the offset copy smears the letters and makes them unreadable
        int fs = st.fontSize;
        if (fs >= 15)
        {
            float off = fs >= 28 ? 2f : 1f;
            st.normal.textColor = new Color(0, 0, 0, 0.8f * c.a); NoHover(st);
            GUI.Label(new Rect(r.x + off, r.y + off, r.width, r.height), s, st);
        }
        st.normal.textColor = c; NoHover(st);
        GUI.Label(r, s, st);
        st.normal.textColor = oc; st.alignment = oa; st.fontSize = os; st.hover.textColor = hv; st.active.textColor = ac;
    }

    // IMGUI draws a label with its HOVER colour while the mouse is over its rectangle, so text faded to alpha 0
    // (the splash "RAZIN") popped back into view on hover. Every state of a text style now uses the same colour.
    void NoHover(GUIStyle st)
    {
        if (st == null) return;
        Color n = st.normal.textColor;
        st.hover.textColor = n; st.active.textColor = n; st.focused.textColor = n;
        st.onNormal.textColor = n; st.onHover.textColor = n; st.onActive.textColor = n; st.onFocused.textColor = n;
    }

    bool Button(Rect r, string s, GUIStyle st = null)
    {
        bool click = GUI.Button(r, s, st ?? btn);
        if (click) Sfx.PlayUI("click");
        return click;
    }

    // text input: normal IMGUI fields, but on phone browsers (no IMGUI keyboard) a button that opens the browser prompt
    string Field(Rect r, string val, int max, bool secret, string title)
    {
        if (!WebBridge.IsMobileWeb)
            return secret ? GUI.PasswordField(r, val, '*', max, field) : GUI.TextField(r, val, max, field);
        string shown = secret ? new string('*', val.Length) : val;
        if (GUI.Button(r, shown.Length == 0 ? "TAP TO TYPE" : shown, field))
        {
            string res = WebBridge.Prompt(title, val);
            if (res != null) val = res.Length > max ? res.Substring(0, max) : res;
        }
        return val;
    }

    // ------------------------------------------------------------------ OnGUI
    void OnGUI()
    {
        EnsureStyles();
        scale = Mathf.Min(Screen.height / 720f, Screen.width / 1000f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float W = Screen.width / scale, H = Screen.height / scale;

        if (GameState.Mode == GameMode.Playing) DrawHud(W, H);
        if (GameSettings.ShowFps)
            Text(new Rect(W - 160, H - 30, 150, 24), Mathf.RoundToInt(fps) + " FPS", small, Color.green, TextAnchor.MiddleRight);
        if (GameState.Over && overAt >= 0f) DrawDeathFade(W, H);

        switch (page)
        {
            case Page.Splash: DrawSplash(W, H); break;
            case Page.Loading: DrawLoading(W, H); break;
            case Page.Story: DrawStory(W, H); break;
            case Page.Auth: DrawAuth(W, H); break;
            case Page.Main: DrawMain(W, H); break;
            case Page.Leaderboard: DrawLeaderboard(W, H); break;
            case Page.Settings: DrawSettings(W, H); break;
            case Page.Controls: DrawControls(W, H); break;
            case Page.Pause: DrawPause(W, H); break;
            case Page.GameOver: DrawGameOver(W, H); break;
            case Page.Coop: DrawCoop(W, H); break;
        }
        if (page == Page.Main || page == Page.Coop) DrawInviteToast(W, H);
        DrawTransit(W, H);
    }

    // ------------------------------------------------------------------ HUD
    void DrawHud(float W, float H)
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            foreach (var it in FloatingText.Items)
            {
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(it.pos.x, it.pos.y + it.age * 0.8f, 0f));
                if (sp.z < 0f) continue;
                float a = 1f - Mathf.Clamp01((it.age / it.life - 0.6f) / 0.4f);
                var c = it.color; c.a = a;
                Text(new Rect(sp.x / scale - 160, (Screen.height - sp.y) / scale - 12, 320, 24), it.text, label, c, TextAnchor.MiddleCenter, it.size);
            }
        }

        // hearts (pixel hearts, empty ones are dark)
        var oc = GUI.color;
        for (int i = 0; i < GameState.PlayerMaxHp; i++)
        {
            GUI.color = i < GameState.PlayerHp ? new Color(0.95f, 0.18f, 0.25f) : new Color(0.28f, 0.2f, 0.24f);
            GUI.DrawTexture(new Rect(18 + i * 38, 14, 36, 32), heartTex);
        }
        GUI.color = oc;

        int t = Mathf.FloorToInt(GameState.RunTime);
        Text(new Rect(W / 2 - 150, 12, 300, 30), string.Format("{0:00}:{1:00}", t / 60, t % 60), label, Color.white, TextAnchor.MiddleCenter, 22);
        Text(new Rect(W - 320, 14, 300, 26), "SCORE " + GameState.Score, label, Gold, TextAnchor.MiddleRight, 16);
        Text(new Rect(W - 320, 40, 300, 22), "KILLS " + GameState.Kills, small, Color.white, TextAnchor.MiddleRight);
        {
            bool nb = GameState.Score > Leaderboard.Best && Leaderboard.Best > 0;
            Text(new Rect(W - 330, 60, 216, 20), nb ? "NEW BEST!" : "BEST " + Leaderboard.Best, small, nb ? Gold : new Color(1, 1, 1, 0.65f), TextAnchor.MiddleRight);
        }

        if (page == Page.None && !GameState.Over && Button(new Rect(W - 104, 68, 90, 28), "MENU", btnSmall)) Pause();

        // rings (the real ring sprite)
        int ready = TenRing.ReadyCount;
        Text(new Rect(18, H - 96, 420, 20), "RINGS " + ready + "/" + TenRing.Total, small, Gold);
        Texture2D ringTex = GameAssets.Texture("ring");
        for (int i = 0; i < TenRing.Total; i++)
        {
            var r = new Rect(18 + i * 30, H - 76, 28, 28);
            GUI.color = i < ready ? Color.white : new Color(0.3f, 0.26f, 0.2f, 0.7f);
            if (ringTex != null) GUI.DrawTexture(r, ringTex); else Rect(r, Gold);
        }
        GUI.color = oc;

        // volley meter
        float vk = 1f - Mathf.Clamp01(RingLauncher.VolleyLeft / RingLauncher.VolleyCooldown);
        bool vready = RingLauncher.VolleyReady;
        Text(new Rect(18, H - 44, 300, 18), vready ? "5-RING VOLLEY READY  [Q / RIGHT CLICK]" : (vk < 1f ? "VOLLEY RECHARGING" : "VOLLEY: NEED 5 RINGS"), small, vready ? Gold : new Color(1, 1, 1, 0.6f));
        Rect(new Rect(18, H - 24, 200, 8), new Color(0, 0, 0, 0.7f));
        Rect(new Rect(19, H - 23, 198 * vk, 6), vready ? Gold : new Color(0.8f, 0.45f, 0.1f));

        // guard meter
        {
            float gx = W - 300, gy = H - 130;
            string gl = Guard.Active ? "GUARDING" : (Guard.Ready ? (Guard.Left < Guard.MaxTime - 0.05f ? "GUARD RECHARGING" : "GUARD READY  [HOLD E]") : "GUARD COOLDOWN  " + Mathf.CeilToInt(Guard.CooldownLeft) + "s");
            Text(new Rect(gx, gy, 284, 18), gl, small, Guard.Ready ? new Color(0.6f, 0.9f, 1f) : new Color(1, 1, 1, 0.6f), TextAnchor.MiddleRight);
            Rect(new Rect(gx + 40, gy + 20, 244, 8), new Color(0, 0, 0, 0.7f));
            float gk = Guard.Ready ? Guard.Left / Guard.MaxTime : 1f - Guard.CooldownLeft / Mathf.Max(0.01f, Guard.CooldownTotal);
            Rect(new Rect(gx + 41, gy + 21, 242 * Mathf.Clamp01(gk), 6), Guard.Ready ? new Color(0.45f, 0.85f, 1f) : new Color(0.8f, 0.45f, 0.1f));
        }

        // rune pillar teleport
        if (RunePillars.Total > 0)
        {
            var br = new Rect(W - 300, H - 84, 284, 60);
            if (RunePillars.Attuned == 0)
                Text(new Rect(br.x, br.y + 18, br.width, 24), "FIND A RUNE PILLAR TO ATTUNE IT", small, new Color(0.6f, 0.9f, 1f, 0.8f), TextAnchor.MiddleRight);
            else if (RunePillars.CooldownLeft > 0f)
            {
                Text(new Rect(br.x, br.y, br.width, 20), "RUNE TELEPORT  " + Mathf.CeilToInt(RunePillars.CooldownLeft) + "s", small, new Color(0.6f, 0.9f, 1f, 0.8f), TextAnchor.MiddleRight);
                Rect(new Rect(br.x + 40, br.y + 24, 244, 10), new Color(0, 0, 0, 0.7f));
                Rect(new Rect(br.x + 41, br.y + 25, 242 * (1f - RunePillars.CooldownLeft / RunePillars.Cooldown), 8), new Color(0.4f, 0.8f, 1f));
            }
            else if (Button(br, "RUNE TELEPORT READY [T]", btnSmall)) RunePillars.Teleport();
        }

        // exit direction
        if (PortalArrow.Distance > 0f)
            Text(new Rect(W / 2 - 300, H - 52, 600, 20), "EXIT PORTAL  " + Mathf.RoundToInt(PortalArrow.Distance) + "m", small, new Color(1f, 0.85f, 0.4f, 0.9f), TextAnchor.MiddleCenter);
        Text(new Rect(W / 2 - 330, H - 30, 660, 22), "WASD MOVE   CLICK THROW   Q VOLLEY   E TELEPORT   TAB PAUSE", small, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleCenter);

        if (World.NearPortal)
        {
            Text(new Rect(W / 2 - 250, H - 150, 500, 24), "LEAVING THROUGH THE PORTAL...", label, Gold, TextAnchor.MiddleCenter);
            Rect(new Rect(W / 2 - 150, H - 120, 300, 14), new Color(0, 0, 0, 0.7f));
            Rect(new Rect(W / 2 - 148, H - 118, 296 * Mathf.Clamp01(World.PortalProgress), 10), Gold);
        }
    }

    void Dim(float W, float H, float a) { Rect(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.05f, a)); }
    Rect Center(float W, float H, float w, float h) { return new Rect((W - w) / 2, (H - h) / 2, w, h); }

    // ------------------------------------------------------------------ sign in
    static string Clean(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s) if (char.IsLetterOrDigit(c) && c < 128 || c == '_') sb.Append(c);
        return sb.ToString();
    }

    void DrawAuth(float W, float H)
    {
        Dim(W, H, 0.55f);
        Text(new Rect(0, 60, W, 70), "REBOUND RUMBLE", title, Gold, TextAnchor.MiddleCenter, 48);
        Rect r = Center(W, H, 540, 400); r.y += 40;
        GUI.Box(r, "", box);
        Text(new Rect(r.x, r.y + 14, r.width, 34), "SIGN IN OR CREATE ACCOUNT", label, Color.white, TextAnchor.MiddleCenter, 14);
        Text(new Rect(r.x + 50, r.y + 70, 200, 24), "USERNAME", small, Color.gray);
        user = Clean(Field(new Rect(r.x + 50, r.y + 94, r.width - 100, 38), user, 16, false, "USERNAME"));
        Text(new Rect(r.x + 50, r.y + 146, 200, 24), "PASSWORD", small, Color.gray);
        pass = Field(new Rect(r.x + 50, r.y + 170, r.width - 100, 38), pass, 72, true, "PASSWORD");
        GUI.enabled = !Net.Busy;
        if (Button(new Rect(r.x + 50, r.y + 230, 205, 44), "LOGIN")) Net.Login(user, pass);
        if (Button(new Rect(r.x + 285, r.y + 230, 205, 44), "REGISTER")) Net.Register(user, pass);
        if (Button(new Rect(r.x + 50, r.y + 288, r.width - 100, 44), "PLAY OFFLINE")) Net.PlayOffline();
        GUI.enabled = true;
        Text(new Rect(r.x + 20, r.y + 340, r.width - 40, 24), Net.Message, small, Net.MessageIsError ? new Color(1f, 0.45f, 0.4f) : new Color(0.6f, 1f, 0.6f), TextAnchor.MiddleCenter);
        Text(new Rect(r.x + 20, r.y + 364, r.width - 40, 22), "NO E-MAIL NEEDED. OFFLINE SCORES SYNC WHEN YOU LOG IN.", small, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleCenter);
    }

    // ------------------------------------------------------------------ main menu
    void DrawMain(float W, float H)
    {
        Dim(W, H, 0.35f);
        Text(new Rect(0, 70, W, 70), "REBOUND RUMBLE", title, Gold, TextAnchor.MiddleCenter, 54);
        Text(new Rect(0, 135, W, 30), "THE RINGS  -  ENDLESS", label, new Color(1f, 0.55f, 0.15f), TextAnchor.MiddleCenter, 14);
        Text(new Rect(0, 170, W, 26), "BEST SCORE  " + Leaderboard.Best, label, Gold, TextAnchor.MiddleCenter, 16);

        float bx = W / 2 - 160, by = 240;
        if (Button(new Rect(bx, by, 320, 54), "PLAY")) StartTransit("ENTERING THE ARENA", GameState.StartRun);
        if (Button(new Rect(bx, by + 68, 320, 54), "LEADERBOARD")) { page = Page.Leaderboard; Net.FetchBoard(); }
        if (Button(new Rect(bx, by + 136, 320, 54), "CO-OP  (2 PLAYERS)")) { page = Page.Coop; Coop.Connect(); }
        if (Button(new Rect(bx, by + 204, 320, 54), "SETTINGS")) { settingsReturn = Page.Main; page = Page.Settings; serverField = Net.BaseUrl; }
#if !UNITY_WEBGL
        if (Button(new Rect(bx, by + 272, 320, 54), "QUIT"))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
#endif
        string who = Net.State == AccountState.LoggedIn ? "SIGNED IN AS " + Net.User.ToUpper() : "OFFLINE MODE";
        Text(new Rect(20, H - 56, 500, 22), who, small, Net.State == AccountState.LoggedIn ? GameSettings.Avatar : new Color(1f, 0.7f, 0.3f));
        if (Net.PendingCount > 0) Text(new Rect(20, H - 34, 500, 22), Net.PendingCount + " SCORE(S) WAITING TO SYNC", small, new Color(1, 1, 1, 0.5f));
        if (Button(new Rect(W - 380, H - 60, 170, 38), "STORY", btnSmall)) StartStory(false);
        if (Button(new Rect(W - 570, H - 60, 170, 38), Tutorial.Done ? "TUTORIAL" : "TUTORIAL  *", btnSmall)) StartTransit("PRACTICE ARENA", Tutorial.Begin);
        if (Button(new Rect(W - 190, H - 60, 170, 38), Net.State == AccountState.LoggedIn ? "LOG OUT" : "SIGN IN", btnSmall))
        { Net.Logout(); page = Page.Auth; pass = ""; }
    }

    // ------------------------------------------------------------------ leaderboard
    int lbTab; Vector2 lbScroll;      // 0 = HIGH SCORE (best single run), 1 = TOTAL SCORE (all runs added up)

    bool Tab(Rect r, string text, bool active)
    {
        if (active)
        {
            Rect(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Gold);
            Rect(r, new Color(0.45f, 0.3f, 0.04f, 1f));
            Text(r, text, label, Color.white, TextAnchor.MiddleCenter, 15);
            return false;
        }
        return Button(r, text, btnSmall);
    }

    void DrawLeaderboard(float W, float H)
    {
        Dim(W, H, 0.7f);
        Rect r = Center(W, H, 660, 660);
        GUI.Box(r, "", box);
        bool online = Net.BoardLoaded || Net.BoardCached;
        Text(new Rect(r.x, r.y + 14, r.width, 40), "LEADERBOARD", title, Gold, TextAnchor.MiddleCenter, 26);
        Text(new Rect(r.x, r.y + 52, r.width, 20), Net.BoardLoading ? "LOADING..." : (Net.BoardLoaded ? "GLOBAL - ALL PLAYERS" : (Net.BoardCached ? "OFFLINE - SHOWING LAST SAVED DATA" : "OFFLINE - LOCAL SCORES ONLY")), small, Net.BoardLoaded ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.7f, 0.3f), TextAnchor.MiddleCenter);

        // tabs: the active one is highlighted
        if (Tab(new Rect(r.x + 90, r.y + 80, 230, 38), "HIGH SCORE", lbTab == 0)) { lbTab = 0; lbScroll = Vector2.zero; }
        if (Tab(new Rect(r.x + 340, r.y + 80, 230, 38), "TOTAL SCORE", lbTab == 1)) { lbTab = 1; lbScroll = Vector2.zero; }

        bool total = lbTab == 1;
        Text(new Rect(r.x + 30, r.y + 130, 60, 20), "#", small, Color.gray);
        Text(new Rect(r.x + 90, r.y + 130, 200, 20), "NAME", small, Color.gray);
        Text(new Rect(r.x + 330, r.y + 130, 120, 20), total ? "TOTAL" : "SCORE", small, Color.gray, TextAnchor.MiddleRight);
        Text(new Rect(r.x + 470, r.y + 130, 130, 20), total ? "RUNS" : "TIME", small, Color.gray, TextAnchor.MiddleRight);

        if (Net.State != AccountState.LoggedIn)
        {
            Text(new Rect(r.x, r.y + 230, r.width, 30), "YOU ARE NOT LOGGED IN", label, new Color(1f, 0.7f, 0.3f), TextAnchor.MiddleCenter, 18);
            Text(new Rect(r.x + 40, r.y + 270, r.width - 80, 40), "SIGN IN TO SEE THE GLOBAL LEADERBOARD AND SAVE YOUR SCORES.", small, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter);
            if (Button(new Rect(r.x + r.width / 2 - 100, r.y + 330, 200, 46), "SIGN IN")) { Net.Logout(); page = Page.Auth; pass = ""; }
        }
        else if (online)
        {
            var list = total ? Net.Totals : Net.Board;
            Rect area = new Rect(r.x + 10, r.y + 156, r.width - 20, 326);
            Rect fr = new Rect(-10, -158, r.width, 0);
            lbScroll = GUI.BeginScrollView(area, lbScroll, new Rect(0, 0, area.width - 22, Mathf.Max(area.height, list.Count * 34 + 4)), false, false);
            for (int i = 0; i < list.Count && i < 10; i++)
            {
                var e = list[i];
                bool me = string.Equals(e.username, Net.User, System.StringComparison.OrdinalIgnoreCase);
                int sec = Mathf.FloorToInt(e.seconds);
                DrawRow(fr, i, (i + 1).ToString(), e.username, total ? e.total : e.score, total ? e.runs.ToString() : string.Format("{0:00}:{1:00}", sec / 60, sec % 60), me);
            }
            GUI.EndScrollView();
            if (list.Count == 0) Text(new Rect(r.x, r.y + 260, r.width, 30), "NO SCORES YET - PLAY A RUN!", label, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter, 16);
        }
        else
        {
            var top = Leaderboard.Top(100);
            if (total)
            {
                var sum = new Dictionary<string, int[]>();
                foreach (var t in top) { int[] a; if (!sum.TryGetValue(t.name, out a)) sum[t.name] = a = new int[2]; a[0] += t.score; a[1]++; }
                var rows = new List<KeyValuePair<string, int[]>>(sum);
                rows.Sort((x, y) => y.Value[0].CompareTo(x.Value[0]));
                Rect area2 = new Rect(r.x + 10, r.y + 156, r.width - 20, 330); Rect fr2 = new Rect(-10, -158, r.width, 0);
                lbScroll = GUI.BeginScrollView(area2, lbScroll, new Rect(0, 0, area2.width - 22, Mathf.Max(area2.height, rows.Count * 34 + 4)), false, false);
                for (int i = 0; i < rows.Count && i < 50; i++) DrawRow(fr2, i, (i + 1).ToString(), rows[i].Key, rows[i].Value[0], rows[i].Value[1].ToString(), true);
                GUI.EndScrollView();
            }
            else
            {
                Rect area3 = new Rect(r.x + 10, r.y + 156, r.width - 20, 330); Rect fr3 = new Rect(-10, -158, r.width, 0);
                lbScroll = GUI.BeginScrollView(area3, lbScroll, new Rect(0, 0, area3.width - 22, Mathf.Max(area3.height, top.Count * 34 + 4)), false, false);
                for (int i = 0; i < top.Count && i < 50; i++) { int sec = Mathf.FloorToInt(top[i].seconds); DrawRow(fr3, i, (i + 1).ToString(), top[i].name, top[i].score, string.Format("{0:00}:{1:00}", sec / 60, sec % 60), top[i].mine); }
                GUI.EndScrollView();
            }
        }
        // fixed bar: your own standing (saved on the device, so it is still here offline)
        if (Net.State == AccountState.LoggedIn)
        {
            float by2 = r.y + 490;
            Rect(new Rect(r.x + 20, by2, r.width - 40, 34), new Color(0.55f, 0.37f, 0.05f, 0.85f));
            Rect(new Rect(r.x + 20, by2, r.width - 40, 2), Gold);
            bool have = Net.Me != null && Net.Me.runs > 0;
            int rank = have ? (total ? Net.Me.rankTotal : Net.Me.rankBest) : 0;
            int val = have ? (total ? Net.Me.total : Net.Me.best) : (total ? Leaderboard.Total : Leaderboard.Best);
            int runsN = have ? Net.Me.runs : Leaderboard.Runs;
            Text(new Rect(r.x + 30, by2 + 3, 60, 28), rank > 0 ? "#" + rank : "-", label, Color.white);
            Text(new Rect(r.x + 90, by2 + 3, 240, 28), "YOU  " + Net.User.ToUpper(), label, Gold);
            Text(new Rect(r.x + 330, by2 + 3, 120, 28), val.ToString(), label, Color.white, TextAnchor.MiddleRight);
            Text(new Rect(r.x + 470, by2 + 3, 130, 28), runsN + (runsN == 1 ? " RUN" : " RUNS"), small, new Color(1, 1, 1, 0.8f), TextAnchor.MiddleRight);
        }
        if (Net.State == AccountState.LoggedIn && Net.SyncMsg.Length > 0) Text(new Rect(r.x + 20, r.y + r.height - 104, r.width - 40, 24), Net.SyncMsg + (Net.PendingCount > 0 ? "  [" + Net.PendingCount + " PENDING]" : ""), small, Net.SyncErr ? new Color(1f, 0.5f, 0.4f) : new Color(0.6f, 1f, 0.6f), TextAnchor.MiddleCenter);
        if (Net.State == AccountState.LoggedIn && Button(new Rect(r.x + 120, r.y + r.height - 66, 200, 46), "REFRESH")) { Net.SyncNow(); Net.FetchBoard(); }
        if (Button(new Rect(r.x + r.width - 320, r.y + r.height - 66, 200, 46), "BACK")) page = Page.Main;
    }

    void DrawRow(Rect r, int i, string rank, string name, int score, string right, bool mine)
    {
        float y = r.y + 158 + i * 34;
        if (mine) Rect(new Rect(r.x + 20, y - 2, r.width - 40, 31), new Color(0.6f, 0.4f, 0.05f, 0.45f));
        Color c = i == 0 ? Gold : (mine ? Color.white : new Color(0.85f, 0.85f, 0.9f));
        Text(new Rect(r.x + 30, y, 60, 28), rank, label, c);
        Text(new Rect(r.x + 90, y, 240, 28), name.ToUpper(), label, c);
        Text(new Rect(r.x + 330, y, 120, 28), score.ToString(), label, c, TextAnchor.MiddleRight);
        Text(new Rect(r.x + 470, y, 130, 28), right, label, c, TextAnchor.MiddleRight);
    }

    // ------------------------------------------------------------------ settings
    void DrawSettings(float W, float H)
    {
        Dim(W, H, 0.75f);
        Rect r = Center(W, H, 800, 560);
        GUI.Box(r, "", box);
        Text(new Rect(r.x, r.y + 12, r.width, 40), "SETTINGS", title, Gold, TextAnchor.MiddleCenter, 26);

        float tw = (r.width - 40) / SettingsTabs.Length;
        for (int i = 0; i < SettingsTabs.Length; i++)
        {
            var tr = new Rect(r.x + 20 + i * tw, r.y + 62, tw - 6, 38);
            if (i == settingsTab) Rect(new Rect(tr.x - 2, tr.y - 2, tr.width + 4, tr.height + 4), Gold);
            if (Button(tr, SettingsTabs[i], btnSmall)) settingsTab = i;
        }

        float x = r.x + 50, y = r.y + 124, w = r.width - 100;
        switch (settingsTab)
        {
            case 0: DrawProfile(x, y, w); break;
            case 1:
                GameSettings.Master = Slider(x, y, w, "MASTER", GameSettings.Master); y += 56;
                GameSettings.SfxVol = Slider(x, y, w, "EFFECTS", GameSettings.SfxVol); y += 56;
                GameSettings.MusicVol = Slider(x, y, w, "MUSIC", GameSettings.MusicVol);
                break;
            case 2:
                GameSettings.ParticleDensity = Stepper(x, y, w, "PARTICLES", GameSettings.DensityNames, GameSettings.ParticleDensity); y += 50;
                GameSettings.ScreenShake = Toggle(x, y, w, "SCREEN SHAKE", GameSettings.ScreenShake); y += 50;
                GameSettings.DamageNumbers = Toggle(x, y, w, "DAMAGE NUMBERS", GameSettings.DamageNumbers); y += 50;
                GameSettings.Fullscreen = Toggle(x, y, w, "FULLSCREEN", GameSettings.Fullscreen);
                break;
            case 3:
                string[] fp = new string[GameSettings.FpsOptions.Length];
                for (int i = 0; i < fp.Length; i++) fp[i] = GameSettings.FpsOptions[i] < 0 ? "UNLIMITED" : GameSettings.FpsOptions[i] + " FPS";
                GameSettings.FpsIndex = Stepper(x, y, w, "FPS LIMIT", fp, GameSettings.FpsIndex); y += 50;
                GameSettings.VSync = Toggle(x, y, w, "VSYNC", GameSettings.VSync); y += 50;
                GameSettings.ShowFps = Toggle(x, y, w, "SHOW FPS", GameSettings.ShowFps); y += 50;
                var qn = QualitySettings.names;
                string[] qs = new string[qn.Length + 1];
                qs[0] = "DEFAULT";
                for (int i = 0; i < qn.Length; i++) qs[i + 1] = qn[i].ToUpper();
                GameSettings.Quality = Stepper(x, y, w, "QUALITY", qs, GameSettings.Quality + 1) - 1;
                break;
            case 4: DrawAccount(x, y, w); break;
        }

        if (GUI.changed) { GameSettings.Apply(); MusicPlayer.Refresh(); GUI.changed = false; }

        if (Button(new Rect(r.x + r.width / 2 - 100, r.y + r.height - 66, 200, 46), "BACK"))
        {
            GameSettings.Save();
            if (serverField != null && serverField != Net.BaseUrl) Net.SetServer(serverField);
            page = settingsReturn;
        }
    }

    void DrawProfile(float x, float y, float w)
    {
        Text(new Rect(x, y, 200, 30), "COSTUME", label, Color.white);
        string[] names = GameSettings.CostumeNames;
        for (int i = 0; i < names.Length; i++)
        {
            var cr = new Rect(x + i * 230, y + 34, 210, 250);
            bool sel = i == GameSettings.Costume;
            Rect(new Rect(cr.x - 3, cr.y - 3, cr.width + 6, cr.height + 6), sel ? Gold : new Color(0.3f, 0.25f, 0.3f));
            Rect(cr, new Color(0.1f, 0.09f, 0.14f));
            Texture2D tex = GameAssets.Texture("player_s" + (i == 0 ? "" : i.ToString()));
            if (tex != null)
            {
                float bob = Mathf.Sin(Time.unscaledTime * 3f + i) * 3f;
                GUI.DrawTextureWithTexCoords(new Rect(cr.x + 35, cr.y + 10 + bob, 140, 154), tex, new Rect(0f, 0f, 1f / 6f, 1f));
            }
            Text(new Rect(cr.x, cr.y + 176, cr.width, 24), names[i], small, sel ? Gold : Color.white, TextAnchor.MiddleCenter);
            if (Button(new Rect(cr.x + 25, cr.y + 204, cr.width - 50, 34), sel ? "EQUIPPED" : "EQUIP", btnSmall)) { GameSettings.Costume = i; GameSettings.Save(); }
        }
        // suit colour (tint over the costume): shown to your co-op partner too
        for (int v = 0; v < Suits.Variants; v++)
        {
            var sw = new Rect(x + 450 + v * 58, y + 298, 50, 34);
            bool vs = v == GameSettings.Variant;
            Rect(new Rect(sw.x - 3, sw.y - 3, sw.width + 6, sw.height + 6), vs ? Gold : new Color(0.3f, 0.25f, 0.3f));
            Rect(sw, Suits.VariantTint[v] == Color.white ? new Color(0.9f, 0.85f, 0.7f) : Suits.VariantTint[v]);
            if (GUI.Button(sw, GUIContent.none, GUIStyle.none)) { GameSettings.Variant = v; GameSettings.Save(); Sfx.PlayUI("click"); }
        }
        Text(new Rect(x + 430, y + 336, 260, 18), "SUIT COLOUR", small, Color.white, TextAnchor.MiddleCenter);
        Text(new Rect(x, y + 300, 200, 30), "NAME", label, Color.white);
        if (Net.State == AccountState.LoggedIn) Text(new Rect(x + 120, y + 300, 400, 30), Net.User.ToUpper(), label, Gold);
        else GameSettings.ProfileName = Clean(Field(new Rect(x + 120, y + 298, 300, 34), GameSettings.ProfileName, 12, false, "PROFILE NAME")).ToUpper();
    }

    void DrawAccount(float x, float y, float w)
    {
        string st = Net.State == AccountState.LoggedIn ? "SIGNED IN AS " + Net.User.ToUpper() : (Net.State == AccountState.Offline ? "OFFLINE MODE" : "NOT SIGNED IN");
        Text(new Rect(x, y, w, 30), st, label, Net.State == AccountState.LoggedIn ? Gold : new Color(1f, 0.7f, 0.3f));
        Text(new Rect(x, y + 36, w, 24), "SCORES WAITING TO SYNC: " + Net.PendingCount, small, Color.white);
        if (Button(new Rect(x, y + 90, 220, 42), "SYNC NOW", btnSmall)) { Net.SyncNow(); Net.FetchBoard(); Net.FetchMine(); }
        if (GameState.Mode == GameMode.Menu && Button(new Rect(x + 240, y + 90, 220, 42), Net.State == AccountState.LoggedIn ? "LOG OUT" : "SIGN IN", btnSmall))
        { Net.Logout(); page = Page.Auth; pass = ""; }
        Text(new Rect(x, y + 148, w, 24), Net.Message, small, Net.MessageIsError ? new Color(1f, 0.45f, 0.4f) : new Color(0.6f, 1f, 0.6f));
    }

    float Slider(float x, float y, float w, string name, float v)
    {
        Text(new Rect(x, y, 200, 30), name, label, Color.white);
        v = GUI.HorizontalSlider(new Rect(x + 220, y + 10, w - 330, 20), v, 0f, 1f);
        Text(new Rect(x + w - 90, y, 90, 30), Mathf.RoundToInt(v * 100) + "%", label, Gold, TextAnchor.MiddleRight);
        return v;
    }

    bool Toggle(float x, float y, float w, string name, bool v)
    {
        Text(new Rect(x, y, 300, 34), name, label, Color.white);
        if (Button(new Rect(x + 220, y, 150, 34), v ? "ON" : "OFF")) { v = !v; GUI.changed = true; }
        return v;
    }

    int Stepper(float x, float y, float w, string name, string[] opts, int idx)
    {
        idx = Mathf.Clamp(idx, 0, opts.Length - 1);
        Text(new Rect(x, y, 300, 34), name, label, Color.white);
        if (Button(new Rect(x + 220, y, 40, 34), "<")) { idx = (idx - 1 + opts.Length) % opts.Length; GUI.changed = true; }
        Text(new Rect(x + 266, y, 200, 34), opts[idx], label, Gold, TextAnchor.MiddleCenter);
        if (Button(new Rect(x + 472, y, 40, 34), ">")) { idx = (idx + 1) % opts.Length; GUI.changed = true; }
        return idx;
    }

    // ------------------------------------------------------------------ controls / pause
    void DrawControls(float W, float H)
    {
        Dim(W, H, 0.7f);
        Rect r = Center(W, H, 680, 540);
        GUI.Box(r, "", box);
        Text(new Rect(r.x, r.y + 14, r.width, 40), "PAUSED - CONTROLS", title, Gold, TextAnchor.MiddleCenter, 22);
        string[,] rows = {
            { "W A S D / ARROWS", "MOVE" },
            { "LEFT CLICK / SPACE", "THROW ONE RING (HOLD TO REPEAT)" },
            { "RIGHT CLICK / Q", "5-RING VOLLEY (LONG COOLDOWN)" },
            { "HOLD E", "GUARD - BLOCKS HITS (5s METER, REFILLS WHEN RELEASED)" },
            { "T", "TELEPORT TO A RUNE PILLAR (15s)" },
            { "TAB", "PAUSE / CONTROLS" },
            { "ESC", "PAUSE MENU" },
            { "PORTAL / CHESTS", "WALK NEAR - NO CLICK NEEDED" },
        };
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            Text(new Rect(r.x + 40, r.y + 72 + i * 38, 280, 30), rows[i, 0], label, Gold, TextAnchor.MiddleLeft, 11);
            Text(new Rect(r.x + 330, r.y + 72 + i * 38, 340, 30), rows[i, 1], small, Color.white);
        }
        Text(new Rect(r.x + 40, r.y + 350, r.width - 80, 40), "WALK NEAR A RUNE PILLAR ONCE TO ATTUNE IT. THE GOLDEN ARROW ALWAYS POINTS TO THE EXIT.", small, new Color(1, 1, 1, 0.6f));
        if (Button(new Rect(r.x + 60, r.y + r.height - 70, 260, 46), "RESUME")) Resume();
        if (Button(new Rect(r.x + r.width - 320, r.y + r.height - 70, 260, 46), "MAIN MENU")) GameState.ReturnToMenu(true);
    }

    void DrawPause(float W, float H)
    {
        Dim(W, H, 0.6f);
        Text(new Rect(0, 130, W, 60), "PAUSED", title, Gold, TextAnchor.MiddleCenter, 44);
        float bx = W / 2 - 170, by = 230;
        if (Button(new Rect(bx, by, 340, 52), "RESUME")) Resume();
        if (Button(new Rect(bx, by + 64, 340, 52), "SETTINGS")) { settingsReturn = Page.Pause; page = Page.Settings; serverField = Net.BaseUrl; }
        if (Button(new Rect(bx, by + 128, 340, 52), "CONTROLS")) page = Page.Controls;
        if (Button(new Rect(bx, by + 192, 340, 52), "RESTART RUN")) StartTransit("RESTARTING", () => { GameState.ReturnToMenu(true); GameState.StartRun(); });
        if (Button(new Rect(bx, by + 256, 340, 52), "MAIN MENU (SAVE SCORE)")) GameState.ReturnToMenu(true);
    }

    // ------------------------------------------------------------------ death
    void DrawDeathFade(float W, float H)
    {
        float t = Time.unscaledTime - overAt;
        float a = Mathf.Clamp01(t / 1.2f);
        // red vignette creeping in from the edges
        for (int i = 0; i < 12; i++)
        {
            float e = i * 22f, al = a * 0.5f * (1f - i / 12f);
            Rect(new Rect(0, e, W, 22), new Color(0.45f, 0f, 0.03f, al));
            Rect(new Rect(0, H - e - 22, W, 22), new Color(0.45f, 0f, 0.03f, al));
            Rect(new Rect(e, 0, 22, H), new Color(0.45f, 0f, 0.03f, al * 0.8f));
            Rect(new Rect(W - e - 22, 0, 22, H), new Color(0.45f, 0f, 0.03f, al * 0.8f));
        }
        Rect(new Rect(0, 0, W, H), new Color(0.12f, 0f, 0.02f, a * 0.55f));
    }

    void DrawGameOver(float W, float H)
    {
        float t = Time.unscaledTime - overAt - 1.1f;
        float k = Mathf.Clamp01(t / 0.6f);
        float pop = 1f + (1f - k) * (1f - k) * 1.2f;
        var m = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), new Vector2(W / 2, 170));
        Text(new Rect(0, 130, W, 80), "YOU FELL", title, new Color(1f, 0.25f, 0.2f, k), TextAnchor.MiddleCenter, 60);
        GUI.matrix = m;
        float c = Mathf.Clamp01((t - 0.4f) / 1.2f);
        int t2 = Mathf.FloorToInt(GameState.RunTime);
        Text(new Rect(0, 230, W, 34), "SCORE " + Mathf.RoundToInt(GameState.Score * c), label, new Color(Gold.r, Gold.g, Gold.b, Mathf.Clamp01(t / 0.6f)), TextAnchor.MiddleCenter, 24);
        Text(new Rect(0, 272, W, 26), "KILLS " + Mathf.RoundToInt(GameState.Kills * c) + "    TIME " + string.Format("{0:00}:{1:00}", t2 / 60, t2 % 60), label, new Color(1, 1, 1, Mathf.Clamp01(t / 0.8f)), TextAnchor.MiddleCenter, 14);
        Text(new Rect(0, 300 - 0, W, 26), Leaderboard.LastWasBest ? "NEW PERSONAL BEST!" : "PERSONAL BEST " + Leaderboard.Best, label, Leaderboard.LastWasBest ? new Color(Gold.r, Gold.g, Gold.b, c) : new Color(1, 1, 1, 0.7f * c), TextAnchor.MiddleCenter, 16);
        if (lastRank > 0) Text(new Rect(0, 328, W, 26), "LEADERBOARD RANK #" + lastRank, label, new Color(0.5f, 1f, 0.5f, c), TextAnchor.MiddleCenter, 14);
        if (Net.State != AccountState.LoggedIn) Text(new Rect(0, 356, W, 22), "SAVED OFFLINE - SYNCS WHEN YOU LOG IN", small, new Color(1f, 0.7f, 0.3f, c), TextAnchor.MiddleCenter);
        if (t < 1.4f) return;
        float bx = W / 2 - 160, by = 396;
        if (Coop.IsGuest) Text(new Rect(bx, by + 8, 320, 36), "WAITING FOR THE HOST...", label, new Color(0.5f, 0.75f, 1f), TextAnchor.MiddleCenter, 14);
        else if (Button(new Rect(bx, by, 320, 52), "TRY AGAIN")) StartTransit("ENTERING THE ARENA", GameState.StartRun);
        if (Button(new Rect(bx, by + 66, 320, 52), "MAIN MENU")) GameState.ReturnToMenu(false);
    }

    // ======================================================================== flow: splash / loading / story / transitions
    Page lastPage = (Page)(-1);
    float pageStart, shownProgress, storyT;
    int storyIdx;
    bool storyStartsGame, preloadStarted;
    GUIStyle wrap;
    int tPhase; float tStart; System.Action tAction; string tLabel = "";
    const float FadeIn = 0.25f, Hold = 0.7f, FadeOut = 0.4f;

    void StartTransit(string label, System.Action action)
    {
        if (tPhase != 0) return;
        tPhase = 1; tStart = Time.unscaledTime; tAction = action; tLabel = label;
    }

    void BeginFadeOut() { if (tPhase != 0) return; tPhase = 3; tStart = Time.unscaledTime; tAction = null; }

    void AfterAuth()
    {
        if (!Flow.StorySeen()) StartStory(true);
        else page = Page.Main;
    }

    void StartStory(bool thenPlay) { storyStartsGame = thenPlay; storyIdx = 0; storyT = 0f; page = Page.Story; Cinematic.Begin(FinishStory); }

    void AdvanceStory()
    {
        if (storyT * 38f < Flow.StoryText[storyIdx].Length) { storyT = 999f; return; }
        storyIdx++; storyT = 0f;
        if (storyIdx >= Flow.StoryText.Length) FinishStory();
    }

    void FinishStory()
    {
        Flow.MarkStorySeen();
        if (storyStartsGame) StartTransit("ENTERING THE ARENA", GameState.StartRun);
        else page = Page.Main;
    }

    void UpdateFlow()
    {
        float pt = Time.unscaledTime - pageStart;
        if (page == Page.Splash)
        {
            if (pt > 5.0f || (pt > 0.4f && (InputHub.MouseDown || InputHub.Pressed(KeyCode.Space) || InputHub.Pressed(KeyCode.Return)))) page = Page.Loading;
        }
        else if (page == Page.Loading)
        {
            if (!preloadStarted) { preloadStarted = true; StartCoroutine(Flow.Preload()); }
            shownProgress = Mathf.MoveTowards(shownProgress, Flow.Progress, Time.unscaledDeltaTime * 0.9f);
            if (Flow.Loaded && shownProgress >= 0.999f && pt > 1.4f)
            {
                Net.FetchBoard();
                if (Net.State == AccountState.LoggedOut) page = Page.Auth; else AfterAuth();
            }
        }
        else if (page == Page.Story)
        {
            // the Cinematic component runs the story and calls FinishStory when it ends or is skipped
        }

        if (tPhase != 0)
        {
            float tt = Time.unscaledTime - tStart;
            if (tPhase == 1 && tt >= FadeIn)
            {
                tPhase = 2; tStart = Time.unscaledTime;
                if (tAction != null) { var a = tAction; tAction = null; a(); }
            }
            else if (tPhase == 2 && tt >= Hold) { tPhase = 3; tStart = Time.unscaledTime; }
            else if (tPhase == 3 && tt >= FadeOut) tPhase = 0;
        }
    }

    void DrawRingSprite(Rect r, float angle, Color tint)
    {
        Texture2D t = GameAssets.Texture("ring");
        var m = GUI.matrix; var o = GUI.color;
        GUIUtility.RotateAroundPivot(angle, r.center);
        GUI.color = tint;
        if (t != null) GUI.DrawTexture(r, t); else Rect(r, Gold);
        GUI.color = o; GUI.matrix = m;
    }

    void DrawFrame(string tex, int frames, int frame, Rect r, bool flip = false)
    {
        Texture2D t = GameAssets.Texture(tex);
        if (t == null) return;
        float w = 1f / frames;
        GUI.DrawTextureWithTexCoords(r, t, flip ? new Rect((frame + 1) * w, 0f, -w, 1f) : new Rect(frame * w, 0f, w, 1f));
    }

    void DrawSplash(float W, float H)
    {
        float t = Time.unscaledTime - pageStart;
        Rect(new Rect(0, 0, W, H), Color.black);
        float a1 = Mathf.Clamp01(t / 0.5f) * (1f - Mathf.Clamp01((t - 2.3f) / 0.5f));
        Text(new Rect(0, H / 2 - 50, W, 60), "RAZIN", title, new Color(1f, 1f, 1f, a1), TextAnchor.MiddleCenter, 56);
        Text(new Rect(0, H / 2 + 14, W, 30), "PRESENTS", small, new Color(1f, 0.82f, 0.25f, a1 * Mathf.Clamp01((t - 0.7f) / 0.5f)), TextAnchor.MiddleCenter, 12);
        float a2 = Mathf.Clamp01((t - 2.9f) / 0.7f);
        if (a2 > 0f)
        {
            Text(new Rect(0, H / 2 - 60, W, 80), "REBOUND RUMBLE", title, new Color(Gold.r, Gold.g, Gold.b, a2), TextAnchor.MiddleCenter, 54);
            Text(new Rect(0, H / 2 + 20, W, 30), "THE RINGS", label, new Color(1f, 0.55f, 0.15f, a2), TextAnchor.MiddleCenter, 14);
        }
        Text(new Rect(0, H - 40, W, 22), "CLICK TO SKIP", small, new Color(1, 1, 1, 0.3f), TextAnchor.MiddleCenter);
    }

    void DrawLoading(float W, float H)
    {
        float t = Time.unscaledTime - pageStart;
        Rect(new Rect(0, 0, W, H), new Color(0.03f, 0.02f, 0.06f, 1f));
        Text(new Rect(0, H / 2 - 60, W, 50), "LOADING", title, Gold, TextAnchor.MiddleCenter, 34);
        Rect(new Rect(W / 2 - 252, H / 2 + 6, 504, 22), new Color(0.3f, 0.22f, 0.1f));
        Rect(new Rect(W / 2 - 250, H / 2 + 8, 500, 18), new Color(0.05f, 0.04f, 0.08f));
        Rect(new Rect(W / 2 - 250, H / 2 + 8, 500 * shownProgress, 18), Gold);
        Text(new Rect(0, H / 2 + 36, W, 24), (Flow.Loaded ? "READY" : "LOADING " + Flow.Step) + "  " + Mathf.RoundToInt(shownProgress * 100f) + "%", small, Color.white, TextAnchor.MiddleCenter);
        Text(new Rect(60, H - 90, W - 120, 24), Flow.Tips[Mathf.FloorToInt(t / 2.6f) % Flow.Tips.Length], small, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
    }

    void DrawStory(float W, float H) { }   // the cinematic draws itself (Cinematic.cs)

    void DrawTransit(float W, float H)
    {
        if (tPhase == 0) return;
        float tt = Time.unscaledTime - tStart;
        float a = tPhase == 1 ? Mathf.Clamp01(tt / FadeIn) : (tPhase == 2 ? 1f : 1f - Mathf.Clamp01(tt / FadeOut));
        Rect(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.04f, a));
        if (a > 0.5f && tPhase != 0 && tLabel.Length > 0)
        {
            float k = tPhase == 2 ? Mathf.Clamp01(tt / Hold) : (tPhase == 3 ? 1f : 0f);
            float al = Mathf.Clamp01((a - 0.5f) * 2f);
            DrawRingSprite(new Rect(W / 2 - 36, H / 2 - 120, 72, 72), Time.unscaledTime * 220f, new Color(1, 1, 1, al));
            Text(new Rect(0, H / 2 - 30, W, 36), tLabel, label, new Color(Gold.r, Gold.g, Gold.b, al), TextAnchor.MiddleCenter, 18);
            Rect(new Rect(W / 2 - 150, H / 2 + 20, 300, 12), new Color(0.3f, 0.22f, 0.1f, al));
            Rect(new Rect(W / 2 - 148, H / 2 + 22, 296 * k, 8), new Color(Gold.r, Gold.g, Gold.b, al));
            Text(new Rect(60, H / 2 + 60, W - 120, 24), Flow.Tips[Mathf.FloorToInt(Time.unscaledTime / 2.2f) % Flow.Tips.Length], small, new Color(1, 1, 1, 0.6f * al), TextAnchor.MiddleCenter);
        }
    }
}

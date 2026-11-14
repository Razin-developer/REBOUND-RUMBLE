using UnityEngine;

/// <summary>
/// All UI for REBOUND RUMBLE (IMGUI, auto-installed): sign-in, main menu, leaderboard, settings, controls (Tab),
/// pause (Esc), the death screen, and the in-game HUD.
/// </summary>
public class GameUI : MonoBehaviour
{
    enum Page { None, Splash, Loading, Story, Auth, Main, Leaderboard, Settings, Controls, Pause, GameOver }
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
        if (FindFirstObjectByType<GameUI>() == null) new GameObject("GameUI").AddComponent<GameUI>();
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
        st.normal.textColor = new Color(0, 0, 0, 0.8f * c.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
        st.normal.textColor = c;
        GUI.Label(r, s, st);
        st.normal.textColor = oc; st.alignment = oa; st.fontSize = os;
    }

    bool Button(Rect r, string s, GUIStyle st = null)
    {
        bool click = GUI.Button(r, s, st ?? btn);
        if (click) Sfx.PlayUI("click");
        return click;
    }

    // ------------------------------------------------------------------ OnGUI
    void OnGUI()
    {
        EnsureStyles();
        scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float W = Screen.width / scale, H = 720f;

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
        }
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

        if (page == Page.None && !GameState.Over && Button(new Rect(W - 104, 68, 90, 28), "MENU", btnSmall)) Pause();

        // rings (the real ring sprite)
        int ready = TenRing.ReadyCount;
        Text(new Rect(18, H - 96, 420, 20), "TEN RINGS " + ready + "/" + TenRing.Total, small, Gold);
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
            else if (Button(br, "RUNE TELEPORT READY [E]", btnSmall)) RunePillars.Teleport();
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
        user = Clean(GUI.TextField(new Rect(r.x + 50, r.y + 94, r.width - 100, 38), user, 16, field));
        Text(new Rect(r.x + 50, r.y + 146, 200, 24), "PASSWORD", small, Color.gray);
        pass = GUI.PasswordField(new Rect(r.x + 50, r.y + 170, r.width - 100, 38), pass, '*', 72, field);
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
        Text(new Rect(0, 135, W, 30), "THE TEN RINGS  -  ENDLESS", label, new Color(1f, 0.55f, 0.15f), TextAnchor.MiddleCenter, 14);

        float bx = W / 2 - 160, by = 240;
        if (Button(new Rect(bx, by, 320, 54), "PLAY")) StartTransit("ENTERING THE ARENA", GameState.StartRun);
        if (Button(new Rect(bx, by + 68, 320, 54), "LEADERBOARD")) { page = Page.Leaderboard; Net.FetchBoard(); }
        if (Button(new Rect(bx, by + 136, 320, 54), "SETTINGS")) { settingsReturn = Page.Main; page = Page.Settings; serverField = Net.BaseUrl; }
#if !UNITY_WEBGL
        if (Button(new Rect(bx, by + 204, 320, 54), "QUIT"))
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
        if (Button(new Rect(W - 190, H - 60, 170, 38), Net.State == AccountState.LoggedIn ? "LOG OUT" : "SIGN IN", btnSmall))
        { Net.Logout(); page = Page.Auth; pass = ""; }
    }

    // ------------------------------------------------------------------ leaderboard
    void DrawLeaderboard(float W, float H)
    {
        Dim(W, H, 0.7f);
        Rect r = Center(W, H, 660, 580);
        GUI.Box(r, "", box);
        bool online = Net.BoardLoaded;
        Text(new Rect(r.x, r.y + 14, r.width, 40), "LEADERBOARD", title, Gold, TextAnchor.MiddleCenter, 26);
        Text(new Rect(r.x, r.y + 52, r.width, 20), Net.BoardLoading ? "LOADING..." : (online ? "GLOBAL - ALL PLAYERS" : "OFFLINE - LOCAL SCORES ONLY"), small, online ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.7f, 0.3f), TextAnchor.MiddleCenter);
        Text(new Rect(r.x + 30, r.y + 84, 60, 20), "#", small, Color.gray);
        Text(new Rect(r.x + 90, r.y + 84, 200, 20), "NAME", small, Color.gray);
        Text(new Rect(r.x + 330, r.y + 84, 120, 20), "SCORE", small, Color.gray, TextAnchor.MiddleRight);
        Text(new Rect(r.x + 470, r.y + 84, 130, 20), "TIME", small, Color.gray, TextAnchor.MiddleRight);
        int rows = 0;
        if (online)
        {
            for (int i = 0; i < Net.Board.Count && i < 10; i++)
            {
                var e = Net.Board[i];
                DrawRow(r, i, (i + 1).ToString(), e.username, e.score, e.seconds, Net.State == AccountState.LoggedIn && string.Equals(e.username, Net.User, System.StringComparison.OrdinalIgnoreCase));
                rows++;
            }
        }
        else
        {
            var top = Leaderboard.Top(10);
            for (int i = 0; i < top.Count; i++) { DrawRow(r, i, (i + 1).ToString(), top[i].name, top[i].score, top[i].seconds, top[i].mine); rows++; }
        }
        if (Button(new Rect(r.x + 120, r.y + r.height - 66, 200, 46), "REFRESH")) Net.FetchBoard();
        if (Button(new Rect(r.x + r.width - 320, r.y + r.height - 66, 200, 46), "BACK")) page = Page.Main;
    }

    void DrawRow(Rect r, int i, string rank, string name, int score, float seconds, bool mine)
    {
        float y = r.y + 112 + i * 38;
        if (mine) Rect(new Rect(r.x + 20, y - 2, r.width - 40, 34), new Color(0.6f, 0.4f, 0.05f, 0.45f));
        Color c = i == 0 ? Gold : (mine ? Color.white : new Color(0.85f, 0.85f, 0.9f));
        int s = Mathf.FloorToInt(seconds);
        Text(new Rect(r.x + 30, y, 60, 30), rank, label, c);
        Text(new Rect(r.x + 90, y, 240, 30), name.ToUpper(), label, c);
        Text(new Rect(r.x + 330, y, 120, 30), score.ToString(), label, c, TextAnchor.MiddleRight);
        Text(new Rect(r.x + 470, y, 130, 30), string.Format("{0:00}:{1:00}", s / 60, s % 60), label, c, TextAnchor.MiddleRight);
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
        Text(new Rect(x, y + 300, 200, 30), "NAME", label, Color.white);
        if (Net.State == AccountState.LoggedIn) Text(new Rect(x + 120, y + 300, 400, 30), Net.User.ToUpper(), label, Gold);
        else GameSettings.ProfileName = Clean(GUI.TextField(new Rect(x + 120, y + 298, 300, 34), GameSettings.ProfileName, 12, field)).ToUpper();
    }

    void DrawAccount(float x, float y, float w)
    {
        string st = Net.State == AccountState.LoggedIn ? "SIGNED IN AS " + Net.User.ToUpper() : (Net.State == AccountState.Offline ? "OFFLINE MODE" : "NOT SIGNED IN");
        Text(new Rect(x, y, w, 30), st, label, Net.State == AccountState.LoggedIn ? Gold : new Color(1f, 0.7f, 0.3f));
        Text(new Rect(x, y + 36, w, 24), "SCORES WAITING TO SYNC: " + Net.PendingCount, small, Color.white);
        Text(new Rect(x, y + 74, 200, 24), "SERVER", small, Color.gray);
        if (serverField == null) serverField = Net.BaseUrl;
        serverField = GUI.TextField(new Rect(x, y + 98, w, 36), serverField, 120, field);
        if (Button(new Rect(x, y + 156, 220, 42), "SYNC NOW", btnSmall)) { Net.SetServer(serverField); Net.SyncNow(); Net.FetchBoard(); }
        if (GameState.Mode == GameMode.Menu && Button(new Rect(x + 240, y + 156, 220, 42), Net.State == AccountState.LoggedIn ? "LOG OUT" : "SIGN IN", btnSmall))
        { Net.Logout(); page = Page.Auth; pass = ""; }
        Text(new Rect(x, y + 214, w, 24), Net.Message, small, Net.MessageIsError ? new Color(1f, 0.45f, 0.4f) : new Color(0.6f, 1f, 0.6f));
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
            { "E", "TELEPORT TO A RUNE PILLAR (15s)" },
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
        if (lastRank > 0) Text(new Rect(0, 306, W, 26), "LEADERBOARD RANK #" + lastRank, label, new Color(0.5f, 1f, 0.5f, c), TextAnchor.MiddleCenter, 14);
        if (Net.State != AccountState.LoggedIn) Text(new Rect(0, 334, W, 22), "SAVED OFFLINE - SYNCS WHEN YOU LOG IN", small, new Color(1f, 0.7f, 0.3f, c), TextAnchor.MiddleCenter);
        if (t < 1.4f) return;
        float bx = W / 2 - 160, by = 380;
        if (Button(new Rect(bx, by, 320, 52), "TRY AGAIN")) StartTransit("ENTERING THE ARENA", GameState.StartRun);
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

    void StartStory(bool thenPlay) { storyStartsGame = thenPlay; storyIdx = 0; storyT = 0f; page = Page.Story; }

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
            if (pt > 3.2f || (pt > 0.4f && (InputHub.MouseDown || InputHub.Pressed(KeyCode.Space) || InputHub.Pressed(KeyCode.Return)))) page = Page.Loading;
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
            storyT += Time.unscaledDeltaTime;
            if (InputHub.Pressed(KeyCode.Space) || InputHub.Pressed(KeyCode.Return)) AdvanceStory();
            if (InputHub.Pressed(KeyCode.Escape)) FinishStory();
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
        float a1 = Mathf.Clamp01(t / 0.5f) * (1f - Mathf.Clamp01((t - 1.3f) / 0.4f));
        Text(new Rect(0, H / 2 - 50, W, 60), "DEVB", title, new Color(1f, 1f, 1f, a1), TextAnchor.MiddleCenter, 56);
        Text(new Rect(0, H / 2 + 14, W, 30), "PRESENTS", small, new Color(1f, 0.82f, 0.25f, a1 * Mathf.Clamp01((t - 0.5f) / 0.4f)), TextAnchor.MiddleCenter, 12);
        float a2 = Mathf.Clamp01((t - 1.8f) / 0.6f);
        if (a2 > 0f)
        {
            DrawRingSprite(new Rect(W / 2 - 60, H / 2 - 210, 120, 120), t * 90f, new Color(1f, 1f, 1f, a2));
            Text(new Rect(0, H / 2 - 60, W, 80), "REBOUND RUMBLE", title, new Color(Gold.r, Gold.g, Gold.b, a2), TextAnchor.MiddleCenter, 54);
            Text(new Rect(0, H / 2 + 20, W, 30), "THE TEN RINGS", label, new Color(1f, 0.55f, 0.15f, a2), TextAnchor.MiddleCenter, 14);
        }
        Text(new Rect(0, H - 40, W, 22), "CLICK TO SKIP", small, new Color(1, 1, 1, 0.3f), TextAnchor.MiddleCenter);
    }

    void DrawLoading(float W, float H)
    {
        float t = Time.unscaledTime - pageStart;
        Rect(new Rect(0, 0, W, H), new Color(0.03f, 0.02f, 0.06f, 1f));
        DrawRingSprite(new Rect(W / 2 - 50, H / 2 - 170, 100, 100), t * 200f, Color.white);
        Text(new Rect(0, H / 2 - 60, W, 50), "LOADING", title, Gold, TextAnchor.MiddleCenter, 34);
        Rect(new Rect(W / 2 - 252, H / 2 + 6, 504, 22), new Color(0.3f, 0.22f, 0.1f));
        Rect(new Rect(W / 2 - 250, H / 2 + 8, 500, 18), new Color(0.05f, 0.04f, 0.08f));
        Rect(new Rect(W / 2 - 250, H / 2 + 8, 500 * shownProgress, 18), Gold);
        Text(new Rect(0, H / 2 + 36, W, 24), (Flow.Loaded ? "READY" : "LOADING " + Flow.Step) + "  " + Mathf.RoundToInt(shownProgress * 100f) + "%", small, Color.white, TextAnchor.MiddleCenter);
        Text(new Rect(60, H - 90, W - 120, 24), Flow.Tips[Mathf.FloorToInt(t / 2.6f) % Flow.Tips.Length], small, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
    }

    void DrawStory(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.03f, 0.02f, 0.06f, 1f));
        for (int i = 0; i < 40; i++)
        {
            float x = ((i * 173) % 1000) / 1000f * W;
            float y = H - ((Time.unscaledTime * (18 + (i % 7) * 7) + i * 97f) % (H + 40f));
            Rect(new Rect(x, y, 3, 3), new Color(1f, 0.55f, 0.15f, 0.2f + 0.2f * Mathf.Sin(Time.unscaledTime * 2f + i)));
        }
        if (GUI.Button(new Rect(0, 0, W, H), "", GUIStyle.none)) AdvanceStory();

        float t = Time.unscaledTime;
        int wf = Mathf.FloorToInt(t * 8f) % 4;
        string suf = GameSettings.Costume == 0 ? "" : GameSettings.Costume.ToString();
        Vector2 c = new Vector2(W / 2, 250);
        switch (storyIdx)
        {
            case 0:
                for (int k = 0; k < 10; k++)
                {
                    float a = t * 0.9f + k * 0.628f;
                    DrawRingSprite(new Rect(c.x + Mathf.Cos(a) * 190 - 22, c.y + Mathf.Sin(a) * 70 - 22, 44, 44), t * 120f + k * 36f, new Color(1f, 0.85f, 0.5f, 0.55f + 0.45f * Mathf.Sin(a)));
                }
                break;
            case 1:
                DrawFrame("enemy_slime", 4, wf, new Rect(c.x - 230, c.y - 70, 130, 143), false);
                DrawFrame("enemy_beetle", 4, wf, new Rect(c.x - 65, c.y - 70, 130, 143), false);
                DrawFrame("enemy_golem", 4, wf, new Rect(c.x + 100, c.y - 90, 150, 165), false);
                break;
            case 2:
                DrawFrame("player_s" + suf, 6, wf, new Rect(c.x - 85, c.y - 100, 170, 187), false);
                DrawRingSprite(new Rect(c.x - 20, c.y + 90, 40, 40), t * 90f, new Color(1f, 0.85f, 0.4f, 0.8f));
                break;
            case 3:
                DrawFrame("enemy_bat", 4, wf, new Rect(c.x - 130, c.y - 120 + Mathf.Sin(t * 2f) * 10f, 260, 217), false);
                DrawFrame("enemy_wasp", 4, wf, new Rect(c.x - 250, c.y + 20, 110, 121), false);
                DrawFrame("enemy_ant", 4, wf, new Rect(c.x + 140, c.y + 20, 110, 121), false);
                break;
            default:
                DrawFrame("player_s" + suf, 6, 5, new Rect(c.x - 85, c.y - 100, 170, 187), false);
                for (int k = 0; k < 10; k++)
                {
                    float a = t * 1.6f + k * 0.628f;
                    DrawRingSprite(new Rect(c.x + Mathf.Cos(a) * 130 - 18, c.y + 10 + Mathf.Sin(a) * 40 - 18, 36, 36), t * 200f + k * 36f, new Color(1f, 0.85f, 0.4f, 0.6f + 0.4f * Mathf.Sin(a)));
                }
                break;
        }

        if (wrap == null) { wrap = new GUIStyle(label) { wordWrap = true, alignment = TextAnchor.UpperCenter, fontSize = 14 }; }
        Text(new Rect(0, 400, W, 36), Flow.StoryTitles[storyIdx], title, Gold, TextAnchor.MiddleCenter, 24);
        string full = Flow.StoryText[storyIdx];
        string shown = full.Substring(0, Mathf.Min(full.Length, Mathf.FloorToInt(storyT * 38f)));
        Text(new Rect(W / 2 - 400, 448, 800, 120), shown, wrap, Color.white, TextAnchor.UpperCenter, 14);
        for (int i = 0; i < Flow.StoryText.Length; i++)
            Rect(new Rect(W / 2 - 50 + i * 22, 600, 14, 6), i <= storyIdx ? Gold : new Color(0.3f, 0.25f, 0.3f));
        Text(new Rect(0, 622, W, 22), "CLICK OR SPACE TO CONTINUE", small, new Color(1, 1, 1, 0.4f + 0.3f * Mathf.Sin(t * 4f)), TextAnchor.MiddleCenter);
        if (Button(new Rect(W - 150, H - 56, 130, 36), "SKIP", btnSmall)) FinishStory();
    }

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

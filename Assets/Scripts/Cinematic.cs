using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The opening story: ten shots, about four minutes, built from code-painted silhouette layers (parallax), glows,
/// particles, a synthesised score, letterbox bars, vignette, film grain and a typing-text narrator.
/// It runs on its own camera far from the arena, so it never touches the game world. Hold ESC (or press SKIP) to skip.
/// </summary>
public class Cinematic : MonoBehaviour
{
    public static bool Active;
    static Cinematic inst;

    public static void Begin(Action onDone)
    {
        if (inst != null) return;
        var go = new GameObject("Cinematic");
        inst = go.AddComponent<Cinematic>();
        inst.done = onDone;
        Active = true;
        MusicPlayer.Duck(true);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; Active = false; }

    // ------------------------------------------------------------------ data
    class Line { public float at, extra, typeDur, hold; public string text; public float[] cum; public int ticked; public bool voPlayed; }
    class Shot { public string name; public float dur; public Action build; public Action<float> tick; public Line[] lines; public Color grade; public bool fadeOut = true; }
    class Lyr { public Transform t; public SpriteRenderer sr; public Vector2 pos; public float par; }
    class Mote { public SpriteRenderer sr; public Vector2 p, v; public float age, life, size, grow, stretch; public Color col; public bool alive; }
    class Amb { public Vector2 at; public float sx, sy, jit, size, life, stretch = 1f, rate, acc; public Vector2 vel; public Color col; public bool glow = true; }
    class Fxl { public Lyr l; public float t0, life, s0, s1; public Color c; }

    Action done;
    Camera cam;
    Transform stage;
    CineAudio audio;
    StoryVO story;
    static readonly Vector3 Center = new Vector3(0f, 2000f, 0f);

    List<Shot> shots;
    int shotIdx = -1;
    float shotT, shotDur, clock, dt;
    Vector2 camOff; float camSize = 5f;
    float shakeAmp, flash, dim, cutFade, skipHold, titleK, subK;
    Color flashCol = Color.white, gradeCur = new Color(0, 0, 0, 0);
    string hud = "";
    bool finishing;

    readonly List<Lyr> layers = new List<Lyr>();
    readonly List<UnityEngine.Object> trash = new List<UnityEngine.Object>();
    readonly List<Amb> ambs = new List<Amb>();
    readonly List<Fxl> fxs = new List<Fxl>();
    Mote[] motes;
    Sprite moteSq, shockSpr;

    // gui
    GUIStyle txt, small, big, btn;
    Texture2D vig, grain, white;

    static Color C(float r, float g, float b, float a = 1f) { return new Color(r, g, b, a); }
    static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }
    static float K(float t, float a, float b) { return Mathf.Clamp01((t - a) / (b - a)); }

    // ------------------------------------------------------------------ lifecycle
    void Awake()
    {
        stage = new GameObject("CineStage").transform;
        stage.SetParent(transform, false);
        stage.position = Center;

        var cg = new GameObject("CineCam");
        cg.transform.SetParent(transform, false);
        cam = cg.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.depth = 100f;
        cam.nearClipPlane = -20f; cam.farClipPlane = 20f;
        cam.allowHDR = false; cam.allowMSAA = false;

        audio = CineAudio.Create(transform);
        story = StoryVO.Create(transform);

        white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
        var v = new Cv(64, 36);
        for (int y = 0; y < 36; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 32f, dy = (y - 17.5f) / 18f;
                float d = Mathf.Sqrt(dx * dx * 0.75f + dy * dy);
                float a = Mathf.SmoothStep(0.35f, 1.15f, d) * 0.92f;
                v.Disc(x + 0.5f, y + 0.5f, 0.55f, new Color(0, 0, 0, a));
            }
        vig = v.Done(false);
        var g = new Cv(128, 128);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++) { float n = Cv.Hash(x * 7 + y * 131); g.Box(x, y, x + 1, y + 1, new Color(n, n, n, 0.5f + n * 0.5f)); }
        grain = g.Done(true); grain.wrapMode = TextureWrapMode.Repeat;

        var sq = new Cv(4, 4).Fill(Color.white);
        var sqt = sq.Done(true); moteSq = Sprite.Create(sqt, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        shockSpr = MakeShock();

        motes = new Mote[240];
        for (int i = 0; i < motes.Length; i++)
        {
            var go = new GameObject("m");
            go.transform.SetParent(stage, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 500; sr.enabled = false;
            motes[i] = new Mote { sr = sr };
        }
        BuildShots();
        StartShot(0);
    }

    Sprite MakeShock()
    {
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f));
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Abs(d - 28f) / 3.5f)));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f);
    }

    void OnDestroy()
    {
        Active = false;
        if (inst == this) inst = null;
        MusicPlayer.Duck(false);
        ClearShot();
    }

    // ------------------------------------------------------------------ helpers: layers
    Sprite SpriteOf(Cv c, float worldW, float pivotY, bool smooth)
    {
        var tex = c.Done(!smooth);
        var sp = Sprite.Create(tex, new Rect(0, 0, c.w, c.h), new Vector2(0.5f, pivotY), c.w / worldW);
        trash.Add(tex); trash.Add(sp);
        return sp;
    }

    Lyr AddSprite(Sprite s, float x, float y, float par, int order, Color col, float sx, float sy)
    {
        var go = new GameObject("L");
        go.transform.SetParent(stage, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.color = col; sr.sortingOrder = order;
        go.transform.localScale = new Vector3(sx, sy, 1f);
        var l = new Lyr { t = go.transform, sr = sr, par = par, pos = new Vector2(x, y) };
        layers.Add(l);
        Place(l);
        return l;
    }

    void Place(Lyr l) { l.t.localPosition = new Vector3(l.pos.x + camOff.x * (1f - l.par), l.pos.y + camOff.y * (1f - l.par), 0f); }

    /// <summary>Silhouette layer, `worldW` units wide, pixel-sharp.</summary>
    Lyr Sil(Cv c, float worldW, float x, float y, float par, int order, Color tint, float pivotY = 0.5f)
    {
        return AddSprite(SpriteOf(c, worldW, pivotY, false), x, y, par, order, tint, 1f, 1f);
    }

    /// <summary>Smooth full-size backdrop stretched to wW x wH world units.</summary>
    Lyr Bg(Cv c, float wW, float wH, float par, int order, Color tint, float x = 0f, float y = 0f)
    {
        var tex = c.Done(false);
        var sp = Sprite.Create(tex, new Rect(0, 0, c.w, c.h), new Vector2(0.5f, 0.5f), 1f);
        trash.Add(tex); trash.Add(sp);
        return AddSprite(sp, x, y, par, order, tint, wW / c.w, wH / c.h);
    }

    Lyr Gl(float x, float y, float size, Color col, float par, int order) { return AddSprite(GameAssets.Glow, x, y, par, order, col, size, size); }

    Lyr RingAt(float x, float y, float size, Color col, float par, int order) { return AddSprite(GameAssets.Single("ring"), x, y, par, order, col, size, size); }

    static void A(Lyr l, float a) { var c = l.sr.color; c.a = a; l.sr.color = c; }
    static void Tint(Lyr l, Color c) { l.sr.color = c; }

    Amb AddAmb(Vector2 at, float sx, float sy, Vector2 vel, float jit, Color col, float size, float life, float rate, bool glow = true, float stretch = 1f)
    {
        var a = new Amb { at = at, sx = sx, sy = sy, vel = vel, jit = jit, col = col, size = size, life = life, rate = rate, glow = glow, stretch = stretch };
        ambs.Add(a);
        return a;
    }

    void Emit(Vector2 at, float sx, float sy, Vector2 vel, float jit, Color col, float size, float life, bool glow, float stretch, float grow = 0f)
    {
        for (int i = 0; i < motes.Length; i++)
        {
            var m = motes[i];
            if (m.alive) continue;
            m.alive = true; m.age = 0f; m.life = life * UnityEngine.Random.Range(0.7f, 1.2f);
            m.p = at + new Vector2(UnityEngine.Random.Range(-sx, sx), UnityEngine.Random.Range(-sy, sy));
            m.v = vel + new Vector2(UnityEngine.Random.Range(-jit, jit), UnityEngine.Random.Range(-jit, jit));
            m.col = col; m.size = size * UnityEngine.Random.Range(0.6f, 1.2f); m.stretch = stretch; m.grow = grow;
            m.sr.sprite = glow ? GameAssets.Glow : moteSq;
            m.sr.enabled = true;
            return;
        }
    }

    void Burst(Vector2 at, int n, Color col, float speed, float size, float life)
    {
        for (int i = 0; i < n; i++)
        {
            float a = UnityEngine.Random.value * 6.283f, s = speed * UnityEngine.Random.Range(0.3f, 1f);
            Emit(at, 0.1f, 0.1f, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s, 0f, col, size, life, true, 1f);
        }
    }

    void Shock(Vector2 at, Color c, float s1, float life)
    {
        var l = AddSprite(shockSpr, at.x, at.y, 1f, 480, c, 0.1f, 0.1f);
        fxs.Add(new Fxl { l = l, t0 = clock, life = life, s0 = 0.2f, s1 = s1, c = c });
    }

    void Cam(float x0, float y0, float s0, float x1, float y1, float s1, float t)
    {
        float k = Ease(t / Mathf.Max(0.1f, shotDur));
        camOff = new Vector2(Mathf.Lerp(x0, x1, k), Mathf.Lerp(y0, y1, k));
        camSize = Mathf.Lerp(s0, s1, k);
    }

    void Shake(float a) { shakeAmp = Mathf.Max(shakeAmp, a); }
    void Flash(Color c, float a) { flashCol = c; flash = a; }

    // ------------------------------------------------------------------ shot list
    static Line L(float at, string s)
    {
        var l = new Line { at = at, text = s, cum = CharTimes(s) };
        l.typeDur = l.cum[l.cum.Length - 1];
        l.hold = s.Length / 15f + 1f;
        return l;
    }

    static float[] CharTimes(string s)
    {
        var a = new float[s.Length];
        float t = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            t += 1f / 40f;
            a[i] = t;
            char c = s[i];
            if (c == ',') t += 0.15f;
            else if (c == '.') t += (i + 1 < s.Length && s[i + 1] == '.') ? 0.05f : ((i > 0 && s[i - 1] == '.') ? 0.7f : 0.4f);
            else if (c == '…') t += 0.7f;
        }
        return a;
    }

    void BuildShots()
    {
        shots = new List<Shot>
        {
            new Shot { name = "Prologue", dur = 10f, build = B0, tick = T0, grade = C(.1f, .15f, .3f, .08f), lines = new[] { L(1.5f, "Two thousand years ago.") } },
            new Shot { name = "Rings", dur = 34f, build = B1, tick = T1, grade = C(.6f, .3f, .05f, .07f), lines = new[] { L(1.0f, "He found ten rings."), L(25.5f, "And the rings found him.") } },
            new Shot { name = "Conquest", dur = 29f, build = B2, tick = T2, grade = C(.5f, .15f, .05f, .12f), lines = new[] { L(3.0f, "Kingdoms fell like candle flames.") } },
            new Shot { name = "Centuries", dur = 31f, build = B3, tick = T3, grade = C(.15f, .2f, .35f, .08f), lines = new[] { L(14.0f, "Empires burned. He did not.") } },
            new Shot { name = "Mira", dur = 40f, build = B4, tick = T4, grade = C(.7f, .4f, .15f, .10f), lines = new[] { L(5.0f, "She asked for nothing."), L(17.0f, "That frightened him more than any army.") } },
            new Shot { name = "Choice", dur = 34f, build = B5, tick = T5, grade = C(.5f, .35f, .2f, .08f), lines = new[] { L(23.0f, "Immortality was a long winter. She was the spring.") } },
            new Shot { name = "Rin", dur = 24f, build = B6, tick = T6, grade = C(.35f, .3f, .5f, .08f), lines = new[] { L(3.0f, "And the world got small, and wonderful.") } },
            new Shot { name = "Hunt", dur = 33f, build = B7, tick = T7, grade = C(.8f, .1f, .05f, .14f), lines = new[] { L(7.0f, "They did not come to win. They came without end.") } },
            new Shot { name = "Rin rises", dur = 27f, build = B8, tick = T8, grade = C(.25f, .35f, .4f, .09f), lines = new[] { L(11.0f, "So Rin picks up the ring.") } },
            new Shot { name = "Title", dur = 12f, build = B9, tick = T9, grade = C(.5f, .3f, .05f, .06f), lines = new Line[0], fadeOut = true },
        };
    }

    void ClearShot()
    {
        foreach (var l in layers) if (l != null && l.t != null) Destroy(l.t.gameObject);
        layers.Clear();
        foreach (var o in trash) if (o != null) Destroy(o);
        trash.Clear();
        ambs.Clear(); fxs.Clear();
        if (motes != null) foreach (var m in motes) { m.alive = false; if (m.sr != null) m.sr.enabled = false; }
    }

    void StartShot(int i)
    {
        ClearShot();
        shotIdx = i;
        shotT = 0f; shakeAmp = 0f; flash = 0f; dim = 0f; cutFade = 0f; hud = ""; titleK = 0f; subK = 0f;
        camOff = Vector2.zero; camSize = 5f;
        if (i >= shots.Count) { Finish(); return; }
        var s = shots[i];
        shotDur = s.dur;
        foreach (var ln in s.lines) { ln.extra = 0f; ln.ticked = 0; ln.voPlayed = false; }
        if (story != null) story.Ambience(i);
        s.build();
        foreach (var l in layers) Place(l);
    }

    void Finish()
    {
        if (finishing) return;
        finishing = true;
        audio.Silence();
        if (story != null) story.Stop();
        Active = false;
        var cb = done;
        Destroy(gameObject);
        if (cb != null) cb();
    }

    // ------------------------------------------------------------------ per-frame
    void Update()
    {
        if (finishing || shots == null) return;
        dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        clock += dt;
        shotT += dt;

        // skip: hold ESC
        if (InputHub.Held(KeyCode.Escape)) skipHold += dt; else skipHold = Mathf.Max(0f, skipHold - dt * 2f);
        if (skipHold >= 0.8f) { Finish(); return; }

        if (InputHub.Pressed(KeyCode.M)) GameSettings.ToggleMute();

        // click / space completes the current text line (then fast-forwards its hold)
        if (InputHub.Pressed(KeyCode.Space) || InputHub.Pressed(KeyCode.Return) || InputHub.MouseDown) AdvanceLine();

        if (shotT >= shotDur) { StartShot(shotIdx + 1); if (finishing) return; }
        var shot = shots[shotIdx];

        shakeAmp = Mathf.MoveTowards(shakeAmp, 0f, dt * 0.9f);
        flash = Mathf.MoveTowards(flash, 0f, dt * 1.6f);
        shot.tick(shotT);

        // typing ticks
        foreach (var ln in shot.lines)
        {
            float e = shotT - ln.at + ln.extra;
            if (e >= 0f && !ln.voPlayed) { ln.voPlayed = true; if (story != null) story.Line(shotIdx, System.Array.IndexOf(shot.lines, ln)); }
            if (e < 0f || e > ln.typeDur + 0.05f) continue;
            int n = 0;
            while (n < ln.cum.Length && ln.cum[n] <= e) n++;
            if (n > ln.ticked)
            {
                char ch = ln.text[Mathf.Clamp(n - 1, 0, ln.text.Length - 1)];
                if (ch != ' ') Sfx.PlayUI("tick", 0.22f, UnityEngine.Random.Range(0.9f, 1.35f));
                ln.ticked = n;
            }
        }

        // ambient emitters + motes
        foreach (var a in ambs)
        {
            a.acc += a.rate * dt;
            while (a.acc >= 1f) { a.acc -= 1f; Emit(a.at, a.sx, a.sy, a.vel, a.jit, a.col, a.size, a.life, a.glow, a.stretch); }
        }
        foreach (var m in motes)
        {
            if (!m.alive) continue;
            m.age += dt;
            if (m.age >= m.life) { m.alive = false; m.sr.enabled = false; continue; }
            m.p += m.v * dt;
            float k = m.age / m.life;
            float al = Mathf.Min(1f, m.age / 0.25f) * (1f - k) * m.col.a;
            m.sr.color = new Color(m.col.r, m.col.g, m.col.b, al);
            float s = m.size * (1f + m.grow * k);
            m.sr.transform.localScale = new Vector3(s, s * m.stretch, 1f);
            m.sr.transform.localPosition = m.p;
        }
        for (int i = fxs.Count - 1; i >= 0; i--)
        {
            var f = fxs[i];
            float k = (clock - f.t0) / f.life;
            if (k >= 1f) { if (f.l != null) Destroy(f.l.t.gameObject); fxs.RemoveAt(i); continue; }
            float s = Mathf.Lerp(f.s0, f.s1, 1f - (1f - k) * (1f - k));
            f.l.t.localScale = new Vector3(s, s, 1f);
            A(f.l, f.c.a * (1f - k));
        }

        // camera
        float swayT = clock;
        Vector2 sway = new Vector2(Mathf.Sin(swayT * 0.55f) * 0.06f, Mathf.Cos(swayT * 0.41f) * 0.05f);
        Vector2 sh = shakeAmp > 0.001f ? UnityEngine.Random.insideUnitCircle * shakeAmp * 0.35f : Vector2.zero;
        cam.orthographicSize = camSize;
        cam.transform.position = Center + new Vector3(camOff.x + sway.x + sh.x, camOff.y + sway.y + sh.y, -10f);
        foreach (var l in layers) Place(l);

        gradeCur = Color.Lerp(gradeCur, shot.grade, dt * 1.5f);
    }

    void AdvanceLine()
    {
        if (shotIdx < 0 || shotIdx >= shots.Count) return;
        foreach (var ln in shots[shotIdx].lines)
        {
            float e = shotT - ln.at + ln.extra;
            if (e < 0f || e > ln.typeDur + ln.hold + 0.5f) continue;
            if (e < ln.typeDur - 0.02f) ln.extra += ln.typeDur - e;
            else if (e < ln.typeDur + ln.hold) ln.extra += ln.typeDur + ln.hold - e;
            return;
        }
    }

    // ------------------------------------------------------------------ gui
    void R(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }

    void MakeStyles()
    {
        if (txt != null) return;
        Font f = GameAssets.PixelFont;
        txt = new GUIStyle { font = f, fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
        txt.normal.textColor = Color.white;
        small = new GUIStyle(txt) { fontSize = 11, wordWrap = false };
        big = new GUIStyle(txt) { fontSize = 58, wordWrap = false };
        btn = new GUIStyle(txt) { fontSize = 12, wordWrap = false };
        btn.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        btn.normal.background = white;
        btn.hover.textColor = Color.white; btn.hover.background = white;
    }

    void OnGUI()
    {
        if (finishing || shots == null) return;
        MakeStyles();
        GUI.depth = -1000;
        float sc = Screen.height / 720f;
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
        float W = Screen.width / sc, H = 720f;
        var shot = shots[Mathf.Clamp(shotIdx, 0, shots.Count - 1)];

        float barH = Mathf.Max(0f, (H - W / 2.39f) * 0.5f);
        float intro = 1f - Ease(clock / 2.5f);

        if (Event.current.type == EventType.Repaint)
        {
            R(new Rect(0, 0, W, H), gradeCur);
            if (dim > 0f) R(new Rect(0, 0, W, H), new Color(0, 0, 0, dim));
            float vigA = 0.85f + 0.1f * Mathf.Sin(clock * 1.3f);
            GUI.color = new Color(1, 1, 1, vigA); GUI.DrawTexture(new Rect(0, 0, W, H), vig); GUI.color = Color.white;
            GUI.color = new Color(1, 1, 1, 0.07f);
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, W, H), grain, new Rect(UnityEngine.Random.value, UnityEngine.Random.value, W / 110f, H / 110f));
            GUI.color = Color.white;
            if (flash > 0f) R(new Rect(0, 0, W, H), new Color(flashCol.r, flashCol.g, flashCol.b, flash));

            float fin = 1f - K(shotT, 0f, shotIdx == 0 ? 1.6f : 0.9f);
            float fout = shot.fadeOut ? K(shotT, shotDur - 0.9f, shotDur) : 0f;
            float fade = Mathf.Max(Mathf.Max(fin, fout), cutFade);
            if (shotIdx == 0) fade = Mathf.Max(fade, 0f);
            if (fade > 0f) R(new Rect(0, 0, W, H), new Color(0, 0, 0, fade));

            float bar = barH + intro * H * 0.3f;
            R(new Rect(0, 0, W, bar), Color.black);
            R(new Rect(0, H - bar, W, bar), Color.black);
        }

        if (hud.Length > 0)
        {
            small.normal.textColor = new Color(1f, 0.85f, 0.45f, 0.85f);
            GUI.Label(new Rect(30, barH + 14, 300, 20), hud, small);
            small.alignment = TextAnchor.MiddleLeft;
        }

        // typing narration
        foreach (var ln in shot.lines)
        {
            float e = shotT - ln.at + ln.extra;
            if (e < 0f) continue;
            float end = ln.typeDur + ln.hold;
            float alpha = e < end ? 1f : 1f - (e - end) / 0.6f;
            if (alpha <= 0f) continue;
            int n = 0;
            while (n < ln.cum.Length && ln.cum[n] <= e) n++;
            string shown = ln.text.Substring(0, n), rest = ln.text.Substring(n);
            bool typing = n < ln.text.Length;
            bool blink = Mathf.FloorToInt(clock * 2.5f) % 2 == 0;
            string cursor = (typing || blink) ? "_" : "<color=#00000000>_</color>";
            string body = shown + cursor + "<color=#00000000>" + rest + "</color>";
            Rect box = new Rect((W - 880f) / 2f, H - barH - 210f, 880f, 150f);
            GUI.color = new Color(0, 0, 0, 0.75f * alpha);
            GUI.Label(new Rect(box.x + 2, box.y + 2, box.width, box.height), body, txt);
            GUI.color = new Color(1f, 0.97f, 0.88f, alpha);
            GUI.Label(box, body, txt);
            GUI.color = Color.white;
        }

        // title card
        if (shotIdx == shots.Count - 1)
        {
            const string title = "REBOUND RUMBLE";
            int n = Mathf.Clamp(Mathf.CeilToInt(titleK * title.Length), 0, title.Length);
            string shown = title.Substring(0, n);
            big.normal.textColor = Color.white;
            GUI.color = new Color(0.2f, 0.08f, 0f, 0.9f);
            GUI.Label(new Rect(4, H / 2 - 38 + 4, W, 100), shown, big);
            GUI.color = new Color(1f, 0.82f, 0.35f, 1f);
            GUI.Label(new Rect(0, H / 2 - 38, W, 100), shown, big);
            GUI.color = new Color(1f, 1f, 1f, subK * 0.85f);
            GUI.Label(new Rect(0, H / 2 + 38, W, 30), "THE RINGS REMEMBER", small);
            GUI.color = Color.white;
        }

        // skip UI
        small.alignment = TextAnchor.MiddleLeft;
        small.normal.textColor = new Color(1, 1, 1, 0.5f);
        GUI.Label(new Rect(28, H - Mathf.Max(barH * 0.5f + 12f, 34f), 320, 20), "HOLD ESC TO SKIP   -   CLICK TO SPEED TEXT", small);
        if (skipHold > 0.01f) { R(new Rect(28, H - 14f, 200f, 4f), new Color(1, 1, 1, 0.15f)); R(new Rect(28, H - 14f, 200f * skipHold / 0.8f, 4f), new Color(1f, 0.8f, 0.3f, 0.9f)); }
        GUI.backgroundColor = new Color(0, 0, 0, 0.6f);
        if (GUI.Button(new Rect(W - 150, H - Mathf.Max(barH * 0.5f + 17f, 40f), 120, 34), "SKIP >>", btn)) Finish();
        if (GUI.Button(new Rect(W - 282, H - Mathf.Max(barH * 0.5f + 17f, 40f), 120, 34), GameSettings.Muted ? "UNMUTE (M)" : "MUTE (M)", btn)) GameSettings.ToggleMute();
        small.alignment = TextAnchor.MiddleCenter;
        GUI.matrix = old;
    }

    // ================================================================== SHOTS
    // ---- 0: prologue (dark sky, wind, one line)
    Lyr s0Ridge, s0Stars;
    void B0()
    {
        Bg(new Cv(8, 180).VGrad(C(.01f, .01f, .03f), C(.07f, .08f, .16f)), 36, 16, 0f, 0, Color.white);
        s0Stars = Bg(new Cv(320, 180).Speckle(170, 3, 50, 180, Color.white), 30, 16, 0.05f, 1, Color.white);
        s0Ridge = Sil(new Cv(640, 120).Ridge(30, 50, 0.012f, 2, C(.012f, .016f, .035f)), 38, 0, -5f, 0.3f, 2, new Color(1, 1, 1, 0), 0f);
        AddAmb(new Vector2(0, -1), 10f, 5f, new Vector2(0.4f, 0.1f), 0.2f, C(.6f, .7f, 1f, .35f), .1f, 6f, 5f);
        audio.Mix(0.12f, 0f, 0.6f, 0f);
    }
    void T0(float t)
    {
        Cam(0f, 0f, 5.3f, 0.4f, -0.2f, 4.5f, t);
        s0Stars.sr.color = new Color(1, 1, 1, 0.55f + 0.35f * Mathf.Sin(t * 1.7f));
        A(s0Ridge, Ease(K(t, 3f, 8f)));
    }

    // ---- 1: the rings ignite in a cave
    Lyr[] s1Ring = new Lyr[10], s1Glow = new Lyr[10];
    bool[] s1On = new bool[10];
    Lyr s1Light, s1Man;
    bool s1Final;
    void B1()
    {
        var cave = new Cv(320, 180).VGrad(C(.03f, .02f, .02f), C(.11f, .07f, .05f));
        cave.Glow(160, 80, 130, C(.5f, .3f, .15f, .2f));
        cave.Poly(new[] { new Vector2(0, 0), new Vector2(0, 180), new Vector2(64, 180), new Vector2(40, 120), new Vector2(70, 70), new Vector2(44, 0) }, C(.012f, .008f, .008f));
        cave.Poly(new[] { new Vector2(320, 0), new Vector2(320, 180), new Vector2(256, 180), new Vector2(284, 110), new Vector2(252, 60), new Vector2(278, 0) }, C(.012f, .008f, .008f));
        for (int i = 0; i < 14; i++) { float x = i * 23f + Cv.Hash(i) * 12f, len = 20f + Cv.Hash(i + 50) * 45f; cave.Poly(new[] { new Vector2(x - 8, 180), new Vector2(x + 8, 180), new Vector2(x, 180 - len) }, C(.02f, .014f, .012f)); }
        Bg(cave, 26, 14.6f, 0.4f, 0, Color.white);
        s1Light = Gl(0, 0, 18f, C(1f, .65f, .25f, 0f), 0.5f, 1);
        Sil(new Cv(320, 40).Ridge(8, 22, 0.05f, 9, C(.01f, .008f, .008f)), 26, 0, -5.2f, 0.8f, 3, Color.white, 0f);
        var altar = new Cv(64, 48).Box(14, 0, 50, 10, C(.13f, .1f, .09f)).Box(20, 10, 44, 22, C(.16f, .12f, .1f)).Box(24, 22, 40, 40, C(.19f, .15f, .12f)).Box(22, 40, 42, 44, C(.23f, .18f, .14f));
        Sil(altar, 4.6f, 0, -4.4f, 1f, 4, Color.white, 0f);
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.Lerp(165f, 15f, i / 9f) * Mathf.Deg2Rad;
            Vector2 p = new Vector2(Mathf.Cos(a) * 5.4f, -0.4f + Mathf.Sin(a) * 2.9f);
            s1Glow[i] = Gl(p.x, p.y, 3.6f, C(1f, .72f, .25f, 0f), 1f, 5);
            s1Ring[i] = RingAt(p.x, p.y, 1.05f, C(.33f, .31f, .29f), 1f, 6);
            s1On[i] = false;
        }
        var man = new Cv(96, 170); Art.Man(man, 48, 4, 160, C(.004f, .004f, .008f), true, 30f, 118f);
        s1Man = Sil(man, 3.0f, -6.6f, -5.1f, 1.35f, 8, Color.white, 0f);
        var fg = new Cv(320, 90);
        for (int i = 0; i < 9; i++) { float x = i * 40f + Cv.Hash(i + 9) * 15f, len = 25f + Cv.Hash(i + 70) * 55f; fg.Poly(new[] { new Vector2(x - 12, 90), new Vector2(x + 12, 90), new Vector2(x, 90 - len) }, C(.004f, .003f, .003f)); }
        Sil(fg, 26, 0, 5.6f, 1.7f, 9, Color.white, 1f);
        AddAmb(new Vector2(0, -2), 9f, 2f, new Vector2(0, 0.6f), 0.3f, C(1f, .55f, .15f, .8f), .12f, 3f, 0f);
        AddAmb(new Vector2(0, 1), 10f, 5f, new Vector2(0.05f, 0.05f), 0.1f, C(.9f, .8f, .6f, .25f), .07f, 6f, 6f);
        s1Final = false;
        audio.Mix(0.1f, 0f, 0.25f, 0f);
    }
    void T1(float t)
    {
        Cam(-1.0f, -0.2f, 5.1f, 1.0f, 0.4f, 4.0f, t);
        int lit = 0;
        for (int i = 0; i < 10; i++)
        {
            float ti = 4f + i * 2.1f;
            float a = K(t, ti, ti + 0.7f);
            if (t >= ti) { lit++; if (!s1On[i]) { s1On[i] = true; audio.Boom(0.7f + 0.07f * i, 0.75f); Shake(0.3f); Burst(s1Ring[i].pos, 22, C(1f, .8f, .3f), 4f, .14f, .8f); } }
            if (!s1Final)
            {
                s1Ring[i].sr.color = Color.Lerp(C(.33f, .31f, .29f), C(1f, .85f, .4f), a);
                A(s1Glow[i], a * (0.6f + 0.15f * Mathf.Sin(t * 5f + i)));
                float sc = 1.05f * (1f + 0.12f * Mathf.Sin(t * 4f + i) * a);
                s1Ring[i].t.localScale = new Vector3(sc, sc, 1f);
            }
        }
        if (t >= 26f && !s1Final)
        {
            s1Final = true; audio.Boom(0.45f, 1f); audio.Swell(0.6f); Shake(0.8f); Flash(C(1f, .9f, .6f), 0.8f);
            Burst(new Vector2(0, 0.3f), 70, C(1f, .85f, .4f), 7f, .16f, 1.2f);
            AddAmb(new Vector2(0, 0.2f), 3f, 2f, new Vector2(0, 0.8f), 0.4f, C(1f, .8f, .3f, .9f), .1f, 2.5f, 25f);
        }
        if (s1Final)
        {
            float orb = t - 26f, R = Mathf.Lerp(5.2f, 2.5f, Ease(orb / 1.8f));
            for (int i = 0; i < 10; i++)
            {
                float a = i * 0.6283f + orb * 1.4f;
                Vector2 p = new Vector2(Mathf.Cos(a) * R, 0.3f + Mathf.Sin(a) * R * 0.38f);
                s1Ring[i].pos = p; s1Glow[i].pos = p;
                s1Ring[i].sr.color = C(1f, .9f, .5f); A(s1Glow[i], 0.85f);
                float sc = 1.0f * (0.8f + 0.25f * Mathf.Sin(a));
                s1Ring[i].t.localScale = new Vector3(sc, sc, 1f);
                s1Ring[i].sr.sortingOrder = Mathf.Sin(a) > 0f ? 5 : 7;
            }
            s1Light.pos = new Vector2(0, 0.3f);
        }
        A(s1Light, 0.06f + 0.5f * (lit / 10f) + (s1Final ? 0.15f : 0f));
        audio.Mix(0.12f + 0.07f * lit, 0f, 0.3f, s1Final ? 0.5f : 0f);
        audio.dronePitch = 1f + 0.025f * lit;
    }

    // ---- 2: conquest on a map
    Lyr[] s2Red; Vector2[] s2Pts; Lyr s2Army, s2Can1, s2Can2;
    void B2()
    {
        Bg(new Cv(8, 90).VGrad(C(.05f, .03f, .02f), C(.12f, .07f, .04f)), 36, 16, 0.1f, 0, Color.white);
        var par = new Cv(256, 144).Fill(C(.8f, .68f, .46f));
        for (int i = 0; i < 160; i++) par.Disc(Cv.Hash(i * 3) * 256, Cv.Hash(i * 3 + 1) * 144, 3f + Cv.Hash(i * 3 + 2) * 9f, C(.5f, .35f, .15f, .09f));
        par.Box(0, 0, 256, 5, C(.35f, .22f, .1f, .8f)).Box(0, 139, 256, 144, C(.35f, .22f, .1f, .8f)).Box(0, 0, 5, 144, C(.35f, .22f, .1f, .8f)).Box(251, 0, 256, 144, C(.35f, .22f, .1f, .8f));
        Art.MapLand(par, C(.6f, .47f, .28f), 1.6f);
        var mp = Sil(par, 15f, 0f, 0.3f, 0.9f, 2, Color.white);
        var mc = new Cv(256, 144); Art.MapLand(mc, Color.white, 0f);
        var ml = Sil(mc, 15f, 0f, 0.3f, 0.9f, 3, Color.white);
        var sm = ml.t.gameObject.AddComponent<SpriteMask>(); sm.sprite = ml.sr.sprite; ml.sr.enabled = false;

        var pts = new List<Vector2>(Art.LandPoints());
        Vector2 origin = new Vector2(0.18f, 0.62f);
        pts.Sort((a, b) => (a - origin).sqrMagnitude.CompareTo((b - origin).sqrMagnitude));
        s2Pts = pts.ToArray();
        s2Red = new Lyr[s2Pts.Length];
        for (int i = 0; i < s2Pts.Length; i++)
        {
            float x = (s2Pts[i].x - 0.5f) * 15f, y = 0.3f + (s2Pts[i].y - 0.5f) * 15f * 144f / 256f;
            s2Red[i] = Gl(x, y, 0.1f, C(.9f, .06f, .05f, .8f), 0.9f, 4);
            s2Red[i].sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        }
        var army = new Cv(640, 110);
        for (int i = 0; i < 30; i++)
        {
            float x = 14f + i * 21f + Cv.Hash(i) * 6f, h = 52f + Cv.Hash(i + 5) * 16f;
            Art.Soldier(army, x, 0, h, C(.003f, .003f, .006f), Cv.Hash(i + 9) > 0.35f, C(.9f, .1f, .05f));
            if (i % 7 == 3) { army.Line(x + 12, 0, x + 12, 100, 2f, C(.003f, .003f, .006f)); army.Poly(new[] { new Vector2(x + 12, 100), new Vector2(x + 34, 92), new Vector2(x + 12, 80) }, C(.45f, .02f, .02f)); }
        }
        s2Army = Sil(army, 32f, 8f, -5.2f, 1.5f, 8, Color.white, 0f);
        s2Can1 = Gl(-7.5f, 3.2f, 9f, C(1f, .6f, .25f, .4f), 0.9f, 1);
        s2Can2 = Gl(7.5f, -3f, 9f, C(1f, .6f, .25f, .35f), 0.9f, 1);
        AddAmb(new Vector2(-7.5f, 3f), .2f, .1f, new Vector2(0, 1.2f), .2f, C(1f, .6f, .2f, .9f), .09f, 2.5f, 6f);
        AddAmb(new Vector2(7.5f, -3.2f), .2f, .1f, new Vector2(0, 1.2f), .2f, C(1f, .6f, .2f, .9f), .09f, 2.5f, 6f);
        audio.Mix(0.28f, 0f, 0.12f, 0f);
    }
    void T2(float t)
    {
        Cam(-3f, 0.3f, 4.6f, 3f, 0.3f, 4f, t);
        for (int i = 0; i < s2Red.Length; i++)
        {
            float ti = 2f + i * 1.15f;
            float k = Ease(K(t, ti, ti + 4.5f));
            float s = 6.5f * k + 0.1f;
            s2Red[i].t.localScale = new Vector3(s, s, 1f);
            if (t >= ti && t < ti + 0.05f && i % 5 == 0) { audio.Boom(0.55f, 0.55f); Shake(0.15f); }
        }
        s2Army.pos = new Vector2(8f - t * 0.55f, -5.2f);
        A(s2Can1, 0.38f + 0.1f * Mathf.Sin(t * 13f) * Mathf.Sin(t * 7.3f)); A(s2Can2, 0.33f + 0.1f * Mathf.Sin(t * 11f + 2f));
        audio.Mix(0.28f, K(t, 6f, 12f) * 0.35f, 0.12f, 0f);
        audio.heartPitch = 0.75f; audio.dronePitch = 0.9f + 0.2f * (t / shotDur);
    }

    // ---- 3: centuries (time-lapse, the Ringbearer never changes)
    Lyr s3Sky, s3Sun, s3Halo, s3Stars, s3Ridge, s3Man, s3Glow;
    Lyr[] s3City = new Lyr[3], s3Win = new Lyr[3], s3Fire = new Lyr[3];
    Amb[] s3Ember = new Amb[3];
    static readonly Color[] SkyK = { new Color(.03f, .04f, .1f), new Color(.95f, .5f, .3f), new Color(.45f, .7f, .95f), new Color(.9f, .4f, .25f), new Color(.03f, .04f, .1f) };
    static readonly float[] SkyAt = { 0f, .2f, .45f, .72f, 1f };
    static Color SkyColor(float u)
    {
        for (int i = 0; i < SkyAt.Length - 1; i++)
            if (u <= SkyAt[i + 1]) return Color.Lerp(SkyK[i], SkyK[i + 1], Ease((u - SkyAt[i]) / (SkyAt[i + 1] - SkyAt[i])));
        return SkyK[0];
    }
    void B3()
    {
        s3Sky = Bg(new Cv(8, 180).VGrad(C(.7f, .7f, .75f), Color.white), 36, 16, 0f, 0, Color.white);
        s3Stars = Bg(new Cv(320, 180).Speckle(150, 11, 60, 180, Color.white), 30, 16, 0.05f, 1, Color.white);
        s3Halo = Gl(0, 0, 9f, C(1f, .9f, .6f, .5f), 0.05f, 1);
        s3Sun = Gl(0, 0, 1.8f, C(1f, .95f, .8f, 1f), 0.05f, 2);
        s3Ridge = Sil(new Cv(640, 120).Ridge(32, 48, 0.011f, 4, Color.white), 38, 0, -5f, 0.2f, 2, C(.04f, .05f, .09f), 0f);
        for (int i = 0; i < 3; i++)
        {
            var sil = new Cv(512, 170); Art.City(sil, 100 * i + 7, Color.white, 50f, 150f, C(1, .8f, .4f), 0f);
            var win = new Cv(512, 170); Art.City(win, 100 * i + 7, Color.clear, 50f, 150f, C(1f, .82f, .45f), 0.3f);
            s3City[i] = Sil(sil, 26f, (i - 1) * 1.5f, -5f, 0.6f, 4 + i * 2, C(.04f, .04f, .07f), 0f);
            s3Win[i] = Sil(win, 26f, (i - 1) * 1.5f, -5f, 0.6f, 5 + i * 2, new Color(1, 1, 1, 0), 0f);
            s3City[i].t.localScale = new Vector3(1f, 0f, 1f); s3Win[i].t.localScale = new Vector3(1f, 0f, 1f);
            s3Fire[i] = Gl((i - 1) * 4.5f, -2.6f, 12f, C(1f, .3f, .08f, 0f), 0.6f, 3);
            s3Ember[i] = AddAmb(new Vector2((i - 1) * 4.5f, -2.5f), 4f, .5f, new Vector2(0, 2.2f), .6f, C(1f, .5f, .15f, .9f), .1f, 2.5f, 0f);
        }
        var man = new Cv(96, 170); Art.Man(man, 48, 4, 160, C(.004f, .004f, .008f), true, 22f, 18f);
        s3Man = Sil(man, 2.6f, 0f, -5.3f, 1.1f, 12, Color.white, 0f);
        s3Glow = Gl(0, -2.6f, 5.5f, C(1f, .75f, .3f, .3f), 1.1f, 11);
        AddAmb(new Vector2(0, -1), 10f, 5f, new Vector2(-1.6f, 0.1f), .3f, C(.9f, .85f, .7f, .35f), .07f, 5f, 6f);
        audio.Mix(0.35f, 0.15f, 0.25f, 0f);
    }
    void T3(float t)
    {
        Cam(0f, 0f, 5.1f, 0f, 0.25f, 4.6f, t);
        float u = (t / 4.6f) % 1f;
        Color sky = SkyColor(u);
        float burnAny = 0f;
        for (int i = 0; i < 3; i++)
        {
            float st = 1f + i * 9.4f;
            float rise = Ease(K(t, st, st + 1.8f)), burn = K(t, st + 6.2f, st + 7.4f), col = Ease(K(t, st + 8.2f, st + 9.8f));
            float sy = rise * (1f - 0.88f * col);
            s3City[i].t.localScale = new Vector3(1f, sy, 1f); s3Win[i].t.localScale = new Vector3(1f, sy, 1f);
            float b = burn * (1f - col);
            burnAny = Mathf.Max(burnAny, b);
            s3City[i].sr.color = Color.Lerp(C(.04f, .04f, .07f), C(.28f, .05f, .02f), b);
            float night = 1f - Mathf.Clamp01(sky.b * 2f - 0.2f);
            s3Win[i].sr.color = new Color(1, 1, 1, Mathf.Clamp01(night + b * 0.5f) * rise * (1f - col));
            A(s3Fire[i], b * (0.65f + 0.25f * Mathf.Sin(t * 14f + i * 2f)));
            s3Ember[i].rate = b * 45f;
        }
        s3Sky.sr.color = Color.Lerp(sky, C(.45f, .08f, .05f), burnAny * 0.55f);
        s3Ridge.sr.color = Color.Lerp(C(.03f, .04f, .08f), C(.14f, .04f, .04f), burnAny) ;
        float night2 = 1f - Mathf.Clamp01(sky.b * 2.2f);
        A(s3Stars, night2 * 0.9f);
        float ang = Mathf.Lerp(0.15f, 0.85f, u);
        Vector2 sp = new Vector2(Mathf.Cos(Mathf.PI * (1f - ang)) * 8f, -1.5f + Mathf.Sin(Mathf.PI * ang) * 5.5f);
        s3Sun.pos = sp; s3Halo.pos = sp;
        bool dayish = sky.b > 0.35f && sky.r > 0.35f;
        s3Sun.sr.color = dayish ? C(1f, .95f, .75f, 1f) : C(.85f, .9f, 1f, 1f);
        A(s3Halo, 0.45f);
        s3Man.t.localScale = new Vector3(1f + 0.012f * Mathf.Sin(t * 3f), 1f, 1f);
        A(s3Glow, 0.25f + 0.1f * Mathf.Sin(t * 2f));
        hud = "YEAR " + (Mathf.RoundToInt(Mathf.Lerp(0f, 2000f, K(t, 0f, shotDur - 3f)) / 10f) * 10);
        audio.Mix(0.35f, 0.18f, 0.2f, 0f);
    }

    // ---- 4: Mira, the quiet warm village
    Lyr s4Man, s4Mira, s4Glow;
    int s4Note = -1;
    static readonly float[] Mel = { 440, 523, 659, 587, 523, 440, 392, 440, 523, 587, 659, 784, 659, 587, 523, 440 };
    void B4()
    {
        var sky = new Cv(8, 180).VGrad(C(.33f, .22f, .5f), C(1f, .72f, .42f)); sky.Glow(5.8f, 40, 120, C(1f, .78f, .4f, .8f));
        Bg(sky, 36, 16, 0f, 0, Color.white);
        Gl(5.5f, -0.6f, 16f, C(1f, .7f, .3f, .5f), 0.05f, 1);
        Gl(5.5f, -0.6f, 3.6f, C(1f, .95f, .7f, .95f), 0.05f, 2);
        Sil(new Cv(640, 120).Ridge(40, 42, 0.011f, 6, C(.55f, .33f, .45f)), 38, 0, -4.4f, 0.25f, 3, Color.white, 0f);
        Sil(new Cv(640, 110).Ridge(34, 36, 0.016f, 12, C(.33f, .19f, .3f)), 38, 0, -4.8f, 0.5f, 4, Color.white, 0f);
        Sil(MakeHuts(), 30f, 0, -5f, 0.8f, 5, Color.white, 0f);
        Sil(new Cv(640, 60).Ridge(12, 26, 0.07f, 8, C(.05f, .03f, .06f)), 38, 0, -5.3f, 1.25f, 12, Color.white, 0f);
        var man = new Cv(96, 170); Art.Man(man, 48, 4, 160, C(.04f, .025f, .05f), true, 18f, 18f);
        s4Man = Sil(man, 96f / 170f * 4.5f, -9f, -5f, 1f, 8, Color.white, 0f);
        var mira = new Cv(96, 170); Art.Man(mira, 48, 4, 150, C(.05f, .03f, .06f), true, 16f, 16f, true);
        s4Mira = Sil(mira, 96f / 170f * 4.4f, 9f, -5f, 1f, 8, Color.white, 0f);
        s4Man.sr.flipX = false; s4Mira.sr.flipX = false;
        s4Glow = Gl(0, -2.6f, 7f, C(1f, .8f, .5f, 0f), 1f, 7);
        AddAmb(new Vector2(0, -2), 9f, 2.5f, new Vector2(0.1f, 0.25f), .15f, C(1f, .85f, .4f, .9f), .12f, 5f, 8f);
        AddAmb(new Vector2(2, -3), 8f, 1f, new Vector2(0, 0.7f), .25f, C(1f, .6f, .25f, .85f), .18f, 7f, 1.2f);
        s4Note = -1;
        audio.Mix(0.04f, 0f, 0.1f, 0.55f);
    }
    Cv MakeHuts() { var c = new Cv(640, 100); Art.Huts(c, 21, C(.17f, .09f, .15f), C(1f, .82f, .4f)); return c; }
    void T4(float t)
    {
        Cam(-1.5f, 0.2f, 5.3f, 0f, -0.7f, 3.4f, t);
        float k = Ease(K(t, 2f, 17.5f));
        bool moving = k > 0.01f && k < 0.99f;
        float bob = moving ? Mathf.Abs(Mathf.Sin(t * 5f)) * 0.07f : 0f;
        s4Man.pos = new Vector2(Mathf.Lerp(-9f, -1.15f, k), -5f + bob);
        s4Mira.pos = new Vector2(Mathf.Lerp(9f, 1.15f, k), -5f + (moving ? Mathf.Abs(Mathf.Sin(t * 5f + 1f)) * 0.07f : 0f));
        A(s4Glow, K(t, 17f, 23f) * (0.45f + 0.08f * Mathf.Sin(t * 2f)));
        int idx = Mathf.FloorToInt((t - 3f) / 1.25f);
        if (t > 3f && idx != s4Note && idx < 30) { s4Note = idx; audio.Pluck(Mel[idx % Mel.Length], 0.45f); }
        audio.Mix(0.04f, 0f, 0.1f, 0.55f);
    }

    // ---- 5: the choice (hands, rings come off, hands age)
    Lyr[] s5Ring = new Lyr[10], s5Glow = new Lyr[10];
    Lyr[] s5Hand = new Lyr[2], s5Old = new Lyr[2];
    Lyr s5Mira, s5Warm, s5Bloom;
    float[] s5Vy = new float[10]; bool[] s5Landed = new bool[10]; Vector2[] s5Land = new Vector2[10], s5Start = new Vector2[10];
    bool s5Quiet;
    void B5()
    {
        Bg(new Cv(8, 90).VGrad(C(.05f, .035f, .04f), C(.13f, .09f, .08f)), 36, 16, 0f, 0, Color.white);
        s5Warm = Gl(0, -3.5f, 16f, C(1f, .65f, .3f, .3f), 0.8f, 1);
        Bg(new Cv(8, 40).VGrad(C(.05f, .03f, .03f), C(.02f, .015f, .015f)), 36, 2.3f, 1f, 3, Color.white, 0f, -4.4f);
        Cv young = new Cv(128, 192), young2 = new Cv(128, 192), old = new Cv(128, 192), old2 = new Cv(128, 192);
        Color skin = C(.62f, .43f, .32f), oldSkin = C(.7f, .62f, .56f);
        Art.Hand(young, 64, 70, 100, skin, false, false);
        Vector2[] fl = (Vector2[])Art.FingerPts.Clone();
        Art.Hand(young2, 64, 70, 100, skin, true, false);
        Vector2[] fr = (Vector2[])Art.FingerPts.Clone();
        Art.Hand(old, 64, 70, 100, oldSkin, false, true);
        Art.Hand(old2, 64, 70, 100, oldSkin, true, true);
        float wW = 5f, pp = 128f / wW;
        Vector2 cl = new Vector2(-3.2f, -0.3f), cr = new Vector2(3.2f, -0.3f);
        s5Hand[0] = Sil(young, wW, cl.x, cl.y, 1f, 5, Color.white); s5Hand[1] = Sil(young2, wW, cr.x, cr.y, 1f, 5, Color.white);
        s5Old[0] = Sil(old, wW, cl.x, cl.y, 1f, 6, new Color(1, 1, 1, 0)); s5Old[1] = Sil(old2, wW, cr.x, cr.y, 1f, 6, new Color(1, 1, 1, 0));
        for (int i = 0; i < 10; i++)
        {
            Vector2 f = i < 5 ? fl[i] : fr[i - 5];
            Vector2 c = i < 5 ? cl : cr;
            Vector2 p = c + new Vector2((f.x - 64f) / pp, (f.y - 96f) / pp);
            s5Start[i] = p;
            s5Glow[i] = Gl(p.x, p.y, 2.4f, C(1f, .72f, .25f, .8f), 1f, 7);
            s5Ring[i] = RingAt(p.x, p.y, 0.62f, C(1f, .85f, .4f), 1f, 8);
            s5Landed[i] = false; s5Vy[i] = 0f;
            s5Land[i] = new Vector2(-3.6f + i * 0.8f + (Cv.Hash(i * 5) - 0.5f) * 0.4f, -4.35f + Cv.Hash(i * 9) * 0.35f);
        }
        var mh = new Cv(128, 192); Art.Hand(mh, 64, 70, 100, C(.66f, .46f, .36f), false, false);
        s5Mira = Sil(mh, wW * 0.8f, 11f, -0.4f, 1f, 9, new Color(1, 1, 1, 0));
        s5Mira.t.rotation = Quaternion.Euler(0, 0, 92f);
        s5Bloom = Gl(3f, -0.5f, 9f, C(1f, .8f, .5f, 0f), 1f, 10);
        s5Quiet = false;
        AddAmb(new Vector2(0, -1), 9f, 4f, new Vector2(0, 0.2f), .1f, C(1f, .8f, .5f, .3f), .05f, 5f, 5f);
        audio.Mix(0.5f, 0.2f, 0.12f, 0f);
    }
    void T5(float t)
    {
        Cam(0f, 0f, 5.0f, 0f, -0.2f, 4.1f, t);
        float lit = 0f;
        int removed = 0;
        for (int i = 0; i < 10; i++)
        {
            float ti = 3f + i * 2f;
            float lt = t - ti;
            if (lt < 0f) { lit += 1f; continue; }
            removed++;
            if (lt < 1f)
            {
                float k = Ease(lt / 1f);
                s5Ring[i].pos = s5Start[i] + new Vector2(0f, 0.9f * k);
                s5Glow[i].pos = s5Ring[i].pos;
                A(s5Glow[i], 0.8f * (1f - k)); lit += 1f - k;
                float sc = 0.62f * (1f + 0.25f * k); s5Ring[i].t.localScale = new Vector3(sc, sc, 1f);
                if (!s5Landed[i] && lt > 0.02f && s5Vy[i] == 0f) { s5Vy[i] = 0.001f; }
            }
            else if (!s5Landed[i])
            {
                s5Vy[i] += 16f * dt;
                Vector2 p = s5Ring[i].pos; p.y -= s5Vy[i] * dt; p.x = Mathf.Lerp(p.x, s5Land[i].x, dt * 2.5f);
                s5Ring[i].pos = p; s5Glow[i].pos = p; A(s5Glow[i], 0f);
                s5Ring[i].sr.color = Color.Lerp(s5Ring[i].sr.color, C(.42f, .38f, .33f), dt * 6f);
                if (p.y <= s5Land[i].y)
                {
                    s5Landed[i] = true; p.y = s5Land[i].y; s5Ring[i].pos = p;
                    s5Ring[i].t.localScale = new Vector3(0.62f, 0.25f, 1f);
                    Sfx.PlayUI("ring_clang", 0.5f, 0.6f + 0.05f * i); Shake(0.08f);
                }
            }
        }
        float lightK = Mathf.Clamp01(lit / 10f);
        float ageK = 1f - lightK;
        Color tint = Color.Lerp(C(.18f, .21f, .3f), C(1f, .88f, .72f), lightK);
        foreach (var h in s5Hand) h.sr.color = tint;
        foreach (var h in s5Old) h.sr.color = new Color(tint.r, tint.g, tint.b, ageK);
        A(s5Warm, 0.08f + 0.28f * lightK);
        float quietAt = 23.0f;
        if (t >= quietAt && !s5Quiet) { s5Quiet = true; audio.Swell(0.6f); }
        float mk = Ease(K(t, 24.5f, 28.5f));
        s5Mira.pos = new Vector2(Mathf.Lerp(11f, 4.7f, mk), -0.4f);
        s5Mira.sr.color = new Color(1f, 0.92f, 0.8f, mk);
        A(s5Bloom, K(t, 27f, 31f) * 0.55f);
        float heart = Mathf.Clamp01((1f - lightK) * 1.3f) * (t < quietAt ? 0.5f : 0f);
        audio.Mix(t < quietAt ? 0.55f * lightK : 0f, heart, 0.1f, t >= 24f ? 0.5f : 0f);
        audio.dronePitch = 0.85f + 0.2f * lightK; audio.heartPitch = 0.75f;
        dim = Mathf.Lerp(0.0f, 0.2f, ageK) * (t < quietAt ? 1f : 1f - K(t, quietAt, 28f));
    }

    // ---- 6: Rin (cradle, then tiny hands)
    List<Lyr> s6A = new List<Lyr>(), s6B = new List<Lyr>();
    Lyr s6Ring, s6RingGlow, s6Ring2, s6Ring2Glow, s6HandL, s6HandR, s6P1, s6P2;
    int s6Note = -1; bool s6Cut;
    static readonly float[] Lull = { 659, 587, 523, 587, 659, 659, 659, 587, 587, 587, 659, 784, 784 };
    void B6()
    {
        s6A.Clear(); s6B.Clear(); s6Cut = false; s6Note = -1;
        s6A.Add(Bg(new Cv(8, 180).VGrad(C(.04f, .05f, .1f), C(.12f, .1f, .15f)), 36, 16, 0f, 0, Color.white));
        s6A.Add(Gl(5f, 2.6f, 11f, C(.6f, .75f, 1f, .35f), 0.2f, 1));
        s6A.Add(Gl(5f, 2.6f, 1.7f, C(.9f, .95f, 1f, 1f), 0.2f, 2));
        s6A.Add(Sil(new Cv(128, 180).Poly(new[] { new Vector2(40, 180), new Vector2(88, 180), new Vector2(124, 0), new Vector2(0, 0) }, C(.7f, .8f, 1f, .1f)), 7f, 3.2f, -1.5f, 0.6f, 2, Color.white, 0.5f));
        s6A.Add(Bg(new Cv(8, 30).VGrad(C(.03f, .025f, .03f), C(.015f, .012f, .015f)), 36, 2.2f, 1f, 3, Color.white, 0f, -4.4f));
        var cr = new Cv(160, 100); Art.Cradle(cr, 80, 8, 120, C(.16f, .1f, .08f), C(.55f, .5f, .62f));
        s6A.Add(Sil(cr, 5f, 0f, -3.9f, 1f, 6, Color.white, 0f));
        s6A.Add(Gl(0, -2.6f, 7f, C(1f, .7f, .4f, .3f), 1f, 5));
        var pa = new Cv(96, 170); Art.Man(pa, 48, 4, 160, C(.02f, .02f, .03f), true, 15f, 15f);
        var pb = new Cv(96, 170); Art.Man(pb, 48, 4, 150, C(.025f, .02f, .03f), true, 15f, 15f, true);
        s6P1 = Sil(pa, 3.4f, -7.4f, -5.3f, 0.8f, 3, Color.white, 0f); s6P1.t.rotation = Quaternion.Euler(0, 0, -4f);
        s6P2 = Sil(pb, 3.2f, 7.4f, -5.3f, 0.8f, 3, Color.white, 0f); s6P2.t.rotation = Quaternion.Euler(0, 0, 4f);
        s6A.Add(s6P1); s6A.Add(s6P2);
        s6Ring = RingAt(0.2f, 2.0f, 0.9f, C(1f, .85f, .4f), 1f, 8); s6RingGlow = Gl(0.2f, 2.0f, 3.2f, C(1f, .75f, .3f, .6f), 1f, 7);
        s6A.Add(s6Ring); s6A.Add(s6RingGlow);
        s6A.Add(Bg(new Cv(2, 2).Fill(C(.8f, .7f, .5f, .8f)), 0.04f, 3.4f, 1f, 7, Color.white, 0.2f, 3.7f));

        s6B.Add(Bg(new Cv(8, 180).VGrad(C(.06f, .04f, .05f), C(.16f, .11f, .1f)), 36, 16, 0f, 0, Color.white));
        s6B.Add(Gl(0, 0, 15f, C(1f, .6f, .3f, .35f), 0.5f, 1));
        var h1 = new Cv(128, 192); Art.Hand(h1, 64, 60, 70, C(.78f, .55f, .45f), false, false);
        var h2 = new Cv(128, 192); Art.Hand(h2, 64, 60, 70, C(.78f, .55f, .45f), true, false);
        s6HandL = Sil(h1, 4.2f, -1.9f, -3.5f, 1f, 5, C(1f, .85f, .75f)); s6HandL.t.rotation = Quaternion.Euler(0, 0, 10f);
        s6HandR = Sil(h2, 4.2f, 1.9f, -3.5f, 1f, 5, C(1f, .85f, .75f)); s6HandR.t.rotation = Quaternion.Euler(0, 0, -10f);
        s6Ring2Glow = Gl(0, 2.4f, 5f, C(1f, .75f, .3f, .7f), 1f, 6); s6Ring2 = RingAt(0, 2.4f, 1.8f, C(1f, .88f, .45f), 1f, 7);
        s6B.Add(s6HandL); s6B.Add(s6HandR); s6B.Add(s6Ring2); s6B.Add(s6Ring2Glow);
        foreach (var l in s6B) l.sr.enabled = false;
        AddAmb(new Vector2(0, 0), 9f, 4f, new Vector2(0.05f, 0.2f), .1f, C(1f, .85f, .5f, .5f), .06f, 6f, 6f);
        audio.Mix(0.03f, 0f, 0.08f, 0.5f);
    }
    void T6(float t)
    {
        bool groupB = t >= 11.7f;
        if (groupB && !s6Cut)
        {
            s6Cut = true;
            foreach (var l in s6A) l.sr.enabled = false;
            foreach (var l in s6B) l.sr.enabled = true;
        }
        cutFade = Ease(K(t, 10.7f, 11.6f)) * (1f - Ease(K(t, 11.8f, 12.7f)));
        if (!groupB) Cam(0f, -1.2f, 5.3f, 0.3f, -2.2f, 3.5f, t * (shotDur / 11.7f) );
        else { camOff = new Vector2(0f, -0.3f); camSize = Mathf.Lerp(5f, 4.3f, K(t, 11.7f, shotDur)); }
        s6Ring.pos = new Vector2(0.2f + Mathf.Sin(t * 1.3f) * 0.25f, 2.0f);
        s6RingGlow.pos = s6Ring.pos; A(s6RingGlow, 0.45f + 0.15f * Mathf.Sin(t * 3f));
        float sw = Mathf.Sin(t * 3f);
        s6HandL.pos = new Vector2(-1.9f, -3.5f + 0.18f * Mathf.Sin(t * 2.2f)); s6HandR.pos = new Vector2(1.9f, -3.5f + 0.18f * Mathf.Sin(t * 2.2f + 1.5f));
        s6HandL.t.localScale = new Vector3(1f + 0.03f * sw, 1f, 1f); s6HandR.t.localScale = new Vector3(1f - 0.03f * sw, 1f, 1f);
        s6Ring2.pos = new Vector2(Mathf.Sin(t * 1.1f) * 0.3f, 2.4f); s6Ring2Glow.pos = s6Ring2.pos;
        A(s6Ring2Glow, 0.55f + 0.2f * Mathf.Sin(t * 3.4f));
        int idx = Mathf.FloorToInt((t - 1.5f) / 1.5f);
        if (t > 1.5f && idx != s6Note && idx < Lull.Length * 2) { s6Note = idx; audio.Pluck(Lull[idx % Lull.Length], 0.4f); }
        audio.Mix(0.03f, 0f, 0.08f, 0.5f);
    }

    // ---- 7: the hunt (endless horde)
    const int Horde = 70;
    Lyr[] s7E = new Lyr[Horde]; float[] s7Z = new float[Horde], s7X = new float[Horde], s7Sp = new float[Horde]; int[] s7Kind = new int[Horde];
    bool[] s7Live = new bool[Horde];
    Sprite s7Sol, s7Sol2, s7Bat;
    Lyr s7Sky, s7Fam; float s7Acc, s7NextPulse; bool s7Start;
    void B7()
    {
        s7Sky = Bg(new Cv(8, 180).VGrad(C(.12f, .02f, .06f), C(.5f, .15f, .1f)), 36, 16, 0f, 0, Color.white);
        Gl(0, -1.2f, 24f, C(1f, .4f, .1f, .55f), 0.1f, 1);
        Sil(MakeBurn(), 36f, 0, -2.6f, 0.4f, 2, Color.white, 0f);
        Bg(new Cv(8, 40).VGrad(C(.05f, .02f, .03f), C(.015f, .01f, .015f)), 36, 3.8f, 0.8f, 3, Color.white, 0f, -3.9f);
        var soldier = new Cv(48, 80); Art.Soldier(soldier, 24, 2, 66, C(.01f, .005f, .01f), true, C(1f, .15f, .08f));
        var soldier2 = new Cv(48, 80); Art.Soldier(soldier2, 24, 2, 66, C(.01f, .005f, .01f), false, C(1f, .15f, .08f));
        var bat = new Cv(96, 56); Art.Bat(bat, 48, 24, 90, C(.01f, .005f, .015f), C(1f, .2f, .5f));
        s7Sol = SpriteOf(soldier, 48f / 40f, 0f, false); s7Sol2 = SpriteOf(soldier2, 48f / 40f, 0f, false); s7Bat = SpriteOf(bat, 96f / 40f, 0.5f, false);
        for (int i = 0; i < Horde; i++)
        {
            s7E[i] = AddSprite(s7Sol, 0, 0, 1f, 20, Color.white, 1f, 1f);
            s7E[i].sr.enabled = false; s7Live[i] = false;
        }
        var fam = new Cv(200, 170);
        Art.Man(fam, 44, 4, 160, C(.003f, .003f, .006f), true, 75f, 75f);
        Art.Man(fam, 128, 4, 138, C(.003f, .003f, .006f), true, 25f, 40f, true);
        fam.Ellipse(142, 92, 9, 11, C(.003f, .003f, .006f));
        s7Fam = Sil(fam, 200f / 170f * 4.8f, -5.6f, -5.2f, 1.25f, 150, Color.white, 0f);
        AddAmb(new Vector2(0, -2.5f), 10f, 1f, new Vector2(0.4f, 2.6f), 0.7f, C(1f, .45f, .12f, .95f), .1f, 3f, 40f);
        AddAmb(new Vector2(0, 0), 10f, 5f, new Vector2(-0.6f, -0.7f), .3f, C(.2f, .2f, .2f, .5f), .05f, 5f, 8f, false);
        s7Acc = 0f; s7Start = false; s7NextPulse = 8f;
        audio.Silence(); audio.Mix(0f, 0f, 0f, 0f);
    }
    Cv MakeBurn() { var c = new Cv(640, 100); Art.Huts(c, 33, C(.02f, .01f, .02f), C(1f, .35f, .08f)); return c; }
    void T7(float t)
    {
        Cam(0f, 0f, 5f, 0f, -0.35f, 4.2f, t);
        dim = t < 3.5f ? 0.8f : Mathf.Max(0f, 0.8f * (1f - K(t, 3.5f, 4.1f)));
        if (!s7Start && t >= 3.6f)
        {
            s7Start = true; Flash(C(1f, .1f, .05f), 0.9f); Shake(1f); audio.Boom(0.6f, 1f); audio.Roar(0.8f);
        }
        if (s7Start && t >= s7NextPulse) { s7NextPulse = t + 4.6f; Flash(C(1f, .1f, .05f), 0.3f); Shake(0.45f); audio.Roar(0.5f); audio.Boom(0.7f, 0.5f); }
        if (s7Start) shakeAmp = Mathf.Max(shakeAmp, 0.18f);
        if (s7Start)
        {
            float rate = Mathf.Lerp(2f, 17f, K(t, 3.6f, 27f));
            s7Acc += rate * dt;
            while (s7Acc >= 1f)
            {
                s7Acc -= 1f;
                for (int i = 0; i < Horde; i++)
                    if (!s7Live[i])
                    {
                        s7Live[i] = true; s7Z[i] = 0f;
                        s7Kind[i] = UnityEngine.Random.value < 0.22f ? 2 : (UnityEngine.Random.value < 0.6f ? 0 : 1);
                        s7X[i] = UnityEngine.Random.Range(-1f, 1f);
                        s7Sp[i] = s7Kind[i] == 2 ? UnityEngine.Random.Range(0.4f, 0.6f) : UnityEngine.Random.Range(0.2f, 0.34f);
                        s7E[i].sr.sprite = s7Kind[i] == 2 ? s7Bat : (s7Kind[i] == 0 ? s7Sol : s7Sol2);
                        s7E[i].sr.enabled = true;
                        break;
                    }
            }
        }
        for (int i = 0; i < Horde; i++)
        {
            if (!s7Live[i]) continue;
            s7Z[i] += s7Sp[i] * dt;
            float z = s7Z[i];
            if (z > 1.2f) { s7Live[i] = false; s7E[i].sr.enabled = false; continue; }
            float zz = Mathf.Pow(z, 1.6f);
            float sc = Mathf.Lerp(0.14f, 2.0f, zz);
            float x = s7X[i] * 10f * (0.2f + z * 1.1f);
            float y;
            if (s7Kind[i] == 2) y = Mathf.Lerp(0.3f, 2.2f, z) + Mathf.Sin(t * 7f + i) * 0.25f * sc;
            else y = Mathf.Lerp(-1.9f, -4.6f, Mathf.Pow(z, 1.3f)) + Mathf.Abs(Mathf.Sin(t * 9f + i)) * 0.1f * sc;
            s7E[i].pos = new Vector2(x, y);
            s7E[i].t.localScale = new Vector3(sc * (s7X[i] > 0f ? -1f : 1f), sc * (s7Kind[i] == 2 ? 0.85f + 0.25f * Mathf.Sin(t * 14f + i) : 1f), 1f);
            s7E[i].sr.sortingOrder = 20 + Mathf.RoundToInt(z * 100f);
            s7E[i].sr.color = new Color(1, 1, 1, Mathf.Clamp01(z * 8f));
        }
        s7Fam.pos = new Vector2(-5.6f + Mathf.Min(t, 20f) * 0.05f, -5.2f);
        s7Sky.sr.color = Color.Lerp(C(.7f, .7f, .8f), Color.white, K(t, 3.5f, 4.5f));
        audio.Mix(s7Start ? 0.5f : 0f, s7Start ? 0.7f : 0f, s7Start ? 0.3f : 0f, 0f);
        audio.heartPitch = 1.45f; audio.dronePitch = 1.1f;
    }

    // ---- 8: Rin picks up the ring
    Lyr s8Rin, s8Ring, s8Pool, s8Light; bool s8Lit;
    void B8()
    {
        var sky = new Cv(8, 180).VGrad(C(.04f, .06f, .09f), C(.2f, .12f, .12f));
        Bg(sky, 36, 16, 0f, 0, Color.white);
        Gl(0, -1.5f, 22f, C(.9f, .25f, .1f, .3f), 0.1f, 1);
        var ruins = new Cv(640, 110); Art.City(ruins, 61, C(.025f, .035f, .05f), 15f, 60f, C(1, .4f, .1f), 0.02f);
        Sil(ruins, 36f, 0, -3.6f, 0.5f, 3, Color.white, 0f);
        Bg(new Cv(8, 40).VGrad(C(.03f, .04f, .05f), C(.01f, .015f, .02f)), 36, 2.6f, 0.9f, 4, Color.white, 0f, -4.1f);
        var spears = new Cv(320, 180);
        for (int i = 0; i < 6; i++) { float x = 30f + i * 50f + Cv.Hash(i) * 16f, lean = (Cv.Hash(i + 4) - 0.5f) * 30f; spears.Line(x, 0, x + lean, 120f + Cv.Hash(i + 8) * 50f, 3f, C(.005f, .007f, .01f)); spears.Poly(new[] { new Vector2(x + lean - 5, 150), new Vector2(x + lean + 5, 150), new Vector2(x + lean, 172) }, C(.005f, .007f, .01f)); }
        Sil(spears, 22f, -2f, -5.2f, 1.5f, 9, Color.white, 0f);
        s8Pool = Gl(0, -3.4f, 5.5f, C(1f, .75f, .3f, .45f), 1f, 6);
        s8Ring = RingAt(0, -3.4f, 1.1f, C(.9f, .75f, .35f), 1f, 7); s8Ring.t.localScale = new Vector3(1.1f, 0.38f, 1f);
        var rin = new Cv(96, 170); Art.Man(rin, 48, 4, 150, C(.02f, .02f, .035f), false, 20f, 20f, false, true);
        s8Rin = Sil(rin, 96f / 170f * 3.8f, -9f, -5f, 1f, 10, Color.white, 0f);
        s8Light = Gl(0, -2f, 14f, C(1f, .8f, .4f, 0f), 1f, 11);
        AddAmb(new Vector2(0, 5.5f), 12f, 0.5f, new Vector2(-1.8f, -14f), 1f, C(.65f, .75f, .95f, .4f), .028f, .8f, 95f, false, 28f);
        AddAmb(new Vector2(0, -3), 9f, 1f, new Vector2(0, 0.8f), .3f, C(1f, .4f, .1f, .8f), .08f, 3f, 5f);
        s8Lit = false;
        audio.Mix(0.2f, 0.08f, 0.4f, 0f);
    }
    void T8(float t)
    {
        Cam(-1.2f, -0.4f, 5f, 0.4f, 0.1f, 3.5f, t);
        float walk = Ease(K(t, 1.5f, 11f));
        float x = Mathf.Lerp(-9f, -1.6f, walk);
        bool walking = walk > 0.01f && walk < 0.99f;
        float crouch = Ease(K(t, 11.2f, 12.8f)) * (1f - Ease(K(t, 13f, 14.8f)));
        s8Rin.pos = new Vector2(x, -5f + (walking ? Mathf.Abs(Mathf.Sin(t * 4.5f)) * 0.07f : 0f));
        s8Rin.t.localScale = new Vector3(1f, 1f - 0.22f * crouch, 1f);
        float lift = Ease(K(t, 13f, 15.4f));
        Vector2 hand = new Vector2(s8Rin.pos.x + 0.55f, 0.2f);
        if (!s8Lit) { s8Ring.pos = Vector2.Lerp(new Vector2(0f, -3.4f), hand, lift); s8Ring.t.localScale = new Vector3(1.1f, Mathf.Lerp(0.38f, 1.1f, lift), 1f); }
        s8Pool.pos = new Vector2(0f, -3.4f); A(s8Pool, (0.45f + 0.12f * Mathf.Sin(t * 3f)) * (1f - lift * 0.8f));
        if (t >= 15.5f && !s8Lit)
        {
            s8Lit = true;
            Flash(C(1f, .88f, .5f), 1f); Shake(0.9f); audio.Boom(0.55f, 1f); audio.Swell(0.9f);
            Burst(hand, 80, C(1f, .85f, .4f), 8f, .16f, 1.3f);
            for (int i = 0; i < 3; i++) Shock(hand, C(1f, .85f, .4f, .9f), 9f + i * 4f, 1.1f + i * 0.25f);
            AddAmb(hand, 0.5f, 0.4f, new Vector2(0, 1.2f), 1f, C(1f, .85f, .4f, .9f), .12f, 2.5f, 40f);
        }
        if (s8Lit)
        {
            float o = t - 15.5f;
            float a = o * 5f;
            s8Ring.pos = hand + new Vector2(Mathf.Cos(a) * 0.8f, Mathf.Sin(a) * 0.3f + 0.2f);
            s8Ring.t.localScale = new Vector3(1.15f, 1.15f, 1f);
            s8Ring.sr.color = C(1f, .92f, .55f);
            A(s8Light, 0.55f * (1f - Ease(K(o, 4f, 10f)) * 0.3f));
            s8Light.pos = hand;
            if (UnityEngine.Random.value < 0.5f) Emit(s8Ring.pos, 0.1f, 0.1f, new Vector2(0, 0.6f), 1.2f, C(1f, .85f, .4f, 1f), .1f, .8f, true, 1f);
            audio.Mix(0.7f, 0.05f, 0.15f, 0.55f);
            audio.dronePitch = 1.15f;
        }
        else audio.Mix(0.2f, 0.08f, 0.4f, 0f);
        if (!s8Lit) s8Ring.sr.color = Color.Lerp(C(.9f, .75f, .35f), C(1f, .9f, .5f), lift);
    }

    // ---- 9: title card
    Lyr s9Ring, s9Glow;
    void B9()
    {
        Bg(new Cv(8, 8).Fill(C(.01f, .008f, .015f)), 40, 20, 0f, 0, Color.white);
        s9Glow = Gl(0, 0.6f, 20f, C(1f, .7f, .25f, 0f), 1f, 1);
        s9Ring = RingAt(0, 0.6f, 7f, C(1f, .85f, .4f, 0f), 1f, 2);
        AddAmb(new Vector2(0, -4), 9f, 0.5f, new Vector2(0, 1.4f), .5f, C(1f, .7f, .25f, .9f), .12f, 5f, 18f);
        audio.Silence(); audio.Swell(0.9f); audio.Mix(0.15f, 0f, 0.1f, 0.3f);
    }
    void T9(float t)
    {
        Cam(0f, 0f, 5f, 0f, 0f, 4.4f, t);
        float a = Ease(K(t, 0.4f, 2.2f));
        A(s9Ring, a * 0.55f); A(s9Glow, a * (0.3f + 0.08f * Mathf.Sin(t * 2f)));
        float flip = 0.4f + 0.6f * Mathf.Abs(Mathf.Cos(t * 0.9f));
        s9Ring.t.localScale = new Vector3(7f * flip, 7f, 1f);
        titleK = K(t, 2.0f, 4.6f);
        subK = Ease(K(t, 5.2f, 6.6f));
        if (t > 2f && t < 2.05f) Shake(0.25f);
        audio.Mix(0.15f, 0f, 0.1f, 0.3f * (1f - K(t, 9f, 12f)));
    }
}

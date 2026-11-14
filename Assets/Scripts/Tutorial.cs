using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Guided practice level (runs in the arena with the spawner switched off): movement, throwing, holding, the volley,
/// guarding (and its 5s limit), every enemy type, then a short real fight. Hearts cannot run out here.
/// </summary>
public class Tutorial : MonoBehaviour
{
    public static bool Active;
    public static bool Done { get { return PlayerPrefs.GetInt("rr_tut_done", 0) == 1; } }
    static Tutorial inst;

    class Step
    {
        public string title, body;
        public string[] keys;
        public Action setup, maintain;
        public Func<float> progress;      // 0..1 (>= 1 completes)
        public Func<string> info;         // text beside the bar
        public Func<Vector2?> target;     // optional: draw a guide arrow toward this world point
        public bool manual, last;
    }
    class Lab { public Enemy e; public string text; public Color col; }

    readonly List<Step> steps = new List<Step>();
    readonly List<Enemy> mine = new List<Enemy>();
    readonly List<Lab> labels = new List<Lab>();
    readonly List<GameObject> props = new List<GameObject>();
    Transform player;
    int idx = -1;
    float doneTimer, stepT, manualGo, praise;
    float moved; Vector2 lastPos;
    int b0, bThrow, bVolley, bBlock, bDeplete;
    bool volleyDone;
    float prog, dimRelease = 99f;

    GUIStyle titleSt, bodySt, smallSt, btnSt, capSt, bigSt;
    Texture2D white, btnN, btnH;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = false; inst = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { if (inst == null) new GameObject("Tutorial").AddComponent<Tutorial>(); }

    public static void Begin()
    {
        if (inst == null) Install();
        Active = true;
        GameState.StartRun();       // fresh run at the entrance (spawner is held off while Active)
        inst.Restart();
    }

    void Awake()
    {
        inst = this;
        white = Texture2D.whiteTexture;
        GameState.OnReturnToMenu += End;
        BuildSteps();
    }

    void OnDestroy() { GameState.OnReturnToMenu -= End; if (inst == this) inst = null; }

    void End()
    {
        Active = false;
        ClearEnemies();
        idx = -1;
    }

    void Restart()
    {
        ClearEnemies();
        var p = GameObject.FindWithTag("Player");
        player = p != null ? p.transform : null;
        idx = -1; doneTimer = 0f; praise = 0f;
        Next();
    }

    // ------------------------------------------------------------------ helpers
    Vector2 PP { get { return player != null ? (Vector2)player.position : Vector2.zero; } }

    void ClearEnemies()
    {
        foreach (var e in mine) if (e != null) Destroy(e.gameObject);
        mine.Clear(); labels.Clear();
        foreach (var g in props) if (g != null) Destroy(g);
        props.Clear();
    }

    int Alive() { int n = 0; foreach (var e in mine) if (e != null) n++; return n; }

    Enemy Sp(EnemyKind k, Vector2 off, bool passive, string label = null)
    {
        Vector2 from = PP, want = from + off, p;
        var taken = new List<Vector2>();
        foreach (var o in mine) if (o != null) taken.Add(o.transform.position);
        // open ground only: never inside props, trees or walls (loosen the clearance step by step, never fall back to the raw spot)
        if (!(Nav.TryOpenCellNear(want, 6f, 0.95f, from, taken, 1.0f, out p)
           || Nav.TryOpenCellNear(want, 9f, 0.7f, from, taken, 0.8f, out p)
           || Nav.TryOpenCellNear(from, 12f, 0.55f, from, taken, 0.7f, out p))) return null;
        var e = Enemy.Spawn(k, p);
        e.passive = passive;
        mine.Add(e);
        if (label != null) labels.Add(new Lab { e = e, text = label, col = passive ? new Color(1f, 0.9f, 0.5f) : Color.white });
        Vfx.Burst(p + Vector2.up * 0.3f, new Color(0.7f, 0.9f, 1f), 12, 3f * GS.S, 0.12f, 0.4f);
        Sfx.Play("spawn", 0.35f, 1.4f);
        return e;
    }

    int Kills { get { return GameState.Kills - b0; } }

    void Baselines()
    {
        b0 = GameState.Kills; bThrow = RingLauncher.Throws; bVolley = RingLauncher.Volleys;
        bBlock = Guard.Blocks; bDeplete = Guard.Depletions; volleyDone = false;
    }

    // ------------------------------------------------------------------ steps
    bool portalUsed;
    int bOpen, bTele;
    public static void PortalUsed() { if (inst != null) inst.portalUsed = true; }

    void BuildSteps()
    {
        steps.Clear();
        steps.Add(new Step
        {
            title = "WELCOME TO THE ARENA",
            body = "This short guide walks you through moving, throwing rings, the volley, guarding, chests, rune pillars and the exit portal. Your hearts cannot run out here, and you can skip any step.",
            keys = new string[0], manual = true,
            progress = () => manualGo,
            info = () => "PRESS CONTINUE"
        });
        steps.Add(new Step
        {
            title = "MOVE",
            body = "Move with W A S D or the ARROW KEYS. Walk around until the bar fills.",
            keys = new[] { "W", "A", "S", "D" },
            setup = () => { moved = 0f; lastPos = PP; },
            maintain = () => { Vector2 p = PP; moved += (p - lastPos).magnitude; lastPos = p; },
            progress = () => moved / 4f,
            info = () => Mathf.RoundToInt(Mathf.Clamp01(moved / 4f) * 100f) + "%"
        });
        steps.Add(new Step
        {
            title = "THROW A RING",
            body = "Press LEFT CLICK or SPACE to throw a ring. Rings aim at the nearest enemy, smash through everything on their path and fly back to you.",
            keys = new[] { "LMB", "or", "SPACE" },
            setup = () => { Baselines(); Sp(EnemyKind.Slime, new Vector2(3.8f, 0.6f), true, "TARGET DUMMY"); },
            progress = () => (RingLauncher.Throws > bThrow || TenRing.OutCount > 0) ? 1f : 0f,
            info = () => "THROW A RING"
        });
        steps.Add(new Step
        {
            title = "KILL THE SLIME",
            body = "Hit the slime until it falls. Each hit flashes it and shows its health bar; a slime has 2 HP.",
            keys = new[] { "LMB", "or", "SPACE" },
            setup = () => { Baselines(); Sp(EnemyKind.Slime, new Vector2(3.8f, 0.6f), true, "SLIME"); },
            maintain = () => { if (Alive() == 0 && Kills < 1) Sp(EnemyKind.Slime, new Vector2(3.8f, 0.6f), true, "SLIME"); },
            progress = () => Kills / 1f,
            info = () => Mathf.Min(Kills, 1) + "/1 SLIME"
        });
        steps.Add(new Step
        {
            title = "HOLD TO KEEP THROWING",
            body = "HOLD the button to throw again and again. You carry 10 rings: the RINGS counter (bottom-left) shows how many are ready, and every ring returns after its throw. Clear the three slimes.",
            keys = new[] { "HOLD LMB", "or", "HOLD SPACE" },
            setup = () => { Baselines(); Sp(EnemyKind.Slime, new Vector2(4f, 1.8f), true); Sp(EnemyKind.Slime, new Vector2(4.2f, -1.6f), true); Sp(EnemyKind.Slime, new Vector2(-4f, 0f), true); },
            progress = () => Kills / 3f,
            info = () => Mathf.Min(Kills, 3) + "/3 SLIMES"
        });
        steps.Add(new Step
        {
            title = "5-RING VOLLEY",
            body = "Press RIGHT CLICK or Q to launch a VOLLEY: five rings in a fast fan. It needs 5 ready rings and then recharges for 4 seconds (the bar bottom-left). Try it on the ant swarm!",
            keys = new[] { "RMB", "or", "Q" },
            setup = () => { Baselines(); SpawnAnts(); },
            maintain = () =>
            {
                if (RingLauncher.Volleys > bVolley || RingLauncher.VolleyLeft > 0f) volleyDone = true;
                if (Alive() == 0 && !volleyDone) SpawnAnts();
            },
            progress = () => volleyDone ? 1f : 0f,
            info = () => volleyDone ? "VOLLEY USED" : "USE THE VOLLEY"
        });
        steps.Add(new Step
        {
            title = "GUARD",
            body = "Ants rush you! Touching an enemy hurts. HOLD E to raise your guard: it blocks every hit (you move slower while it is up). Hold E and block at least one hit.",
            keys = new[] { "HOLD E" },
            setup = () => { Baselines(); SpawnRushers(); },
            maintain = () => { if (Alive() == 0 && Guard.Blocks - bBlock < 1) SpawnRushers(); },
            progress = () => (Guard.Blocks - bBlock) >= 1 ? 1f : 0f,
            info = () => Mathf.Min(Guard.Blocks - bBlock, 1) + "/1 BLOCK"
        });
        steps.Add(new Step
        {
            title = "GUARD LIMIT",
            body = "Your guard is a 5 second meter (bottom-right): it drains while you hold E and refills quickly once you let go, so short guards come back almost at once. Run it completely dry and it locks for about 1.5 seconds. HOLD E until the meter runs dry to see it.",
            keys = new[] { "HOLD E" },
            setup = () => { Baselines(); Sp(EnemyKind.Slime, new Vector2(4f, 0.4f), false); },
            maintain = () => { if (Alive() == 0) Sp(EnemyKind.Slime, new Vector2(4f, 0.4f), false); },
            progress = () => (Guard.Depletions - bDeplete) >= 1 ? 1f : Mathf.Clamp01(1f - Guard.Left / Guard.MaxTime),
            info = () => Guard.Active ? (Guard.Left.ToString("0.0") + "s LEFT") : (Guard.Ready ? "HOLD E UNTIL IT RUNS DRY" : "LOCKED - REFILLING")
        });
        steps.Add(new Step
        {
            title = "KNOW YOUR ENEMIES",
            body = "Six kinds of enemy hunt you in a real run. These are frozen dummies with a label each: defeat all six.",
            keys = new[] { "LMB", "RMB", "HOLD E" },
            setup = () =>
            {
                Baselines();
                Sp(EnemyKind.Slime, new Vector2(-4.4f, 0.8f), true, "SLIME - hops toward you. 2 HP.");
                Sp(EnemyKind.Ant, new Vector2(-4.4f, -1.8f), true, "ANT - fast, arrives in swarms. 1 HP.");
                Sp(EnemyKind.Bat, new Vector2(-1.7f, -2.2f), true, "BAT - weaves as it flies. 1 HP.");
                Sp(EnemyKind.Wasp, new Vector2(4.4f, 0.8f), true, "WASP - flashes, then DASHES. Guard it!");
                Sp(EnemyKind.Beetle, new Vector2(4.4f, -1.8f), true, "BEETLE - slow and tough. 3 HP.");
                Sp(EnemyKind.Golem, new Vector2(1.8f, -2.2f), true, "GOLEM - huge, hits for 2. 6 HP.");
            },
            progress = () => Kills / 6f,
            info = () => Mathf.Min(Kills, 6) + "/6 DEFEATED"
        });
        steps.Add(new Step
        {
            title = "REAL FIGHT",
            body = "Now they fight back! Combine everything: throw, volley and guard. Defeat the whole group. Hearts refill automatically in this practice level.",
            keys = new[] { "LMB", "RMB", "HOLD E" },
            setup = () =>
            {
                Baselines();
                Sp(EnemyKind.Slime, new Vector2(5f, 1f), false);
                Sp(EnemyKind.Ant, new Vector2(-5f, 1.5f), false); Sp(EnemyKind.Ant, new Vector2(-5.2f, 0.6f), false); Sp(EnemyKind.Ant, new Vector2(-5f, -0.3f), false);
                Sp(EnemyKind.Wasp, new Vector2(0f, 3.4f), false);
                Sp(EnemyKind.Beetle, new Vector2(0f, -3.4f), false);
            },
            progress = () => Kills / 6f,
            info = () => Mathf.Min(Kills, 6) + "/6 DEFEATED"
        });
        Vector2 chestPos = Vector2.zero;
        steps.Add(new Step
        {
            title = "OPEN A CHEST",
            body = "Chests heal you or give treasure, and you open them just by walking close. Follow the gold arrow to the chest.",
            keys = new string[0],
            setup = () =>
            {
                Baselines(); bOpen = Chest.Opens;
                Vector2 p = PP + new Vector2(7f, 3f), q;
                if (Nav.TryOpenCellNear(p, 8f, 1.2f, PP, null, 0f, out q) || Nav.TryOpenCellNear(PP, 12f, 0.9f, PP, null, 0f, out q)) p = q;
                chestPos = p;
                props.Add(Chest.Create(p, ChestKind.Treasure).gameObject);
            },
            target = () => chestPos,
            progress = () => Chest.Opens > bOpen ? 1f : 0f,
            info = () => Chest.Opens > bOpen ? "OPENED" : "WALK TO THE CHEST"
        });
        steps.Add(new Step
        {
            title = "ATTUNE A RUNE PILLAR",
            body = "Rune pillars are teleport anchors. Walk close to the glowing pillar to attune it; it lights up gold when it is ready.",
            keys = new string[0],
            setup = () => { Baselines(); },
            target = () => { Vector2 q; return RunePillars.TryPillarPos(out q) ? (Vector2?)q : null; },
            progress = () => (RunePillars.Total == 0 || RunePillars.Attuned > 0) ? 1f : 0f,
            info = () => RunePillars.Attuned > 0 ? "ATTUNED" : "WALK TO THE PILLAR"
        });
        steps.Add(new Step
        {
            title = "RUNE TELEPORT",
            body = "Walk away from the pillar, then press T to teleport back to it from anywhere. It has a 15 second cooldown (the button bottom-right).",
            keys = new[] { "T" },
            setup = () => { Baselines(); bTele = RunePillars.Teleports; },
            progress = () => (RunePillars.Total == 0 || RunePillars.Teleports > bTele) ? 1f : 0f,
            info = () => RunePillars.CooldownLeft > 0f ? "COOLDOWN" : "WALK AWAY, THEN PRESS T"
        });
        steps.Add(new Step
        {
            title = "FOLLOW THE GOLDEN ARROW",
            body = "The golden arrow beside your character always points to the EXIT portal (the altar). Follow it and stand in the glow for a moment. In a real run this leaves the arena and banks your score; here it just finishes the lesson.",
            keys = new string[0],
            setup = () => { Baselines(); portalUsed = false; },
            target = () => Portal.Current != null ? (Vector2?)Portal.Current.Anchor : null,
            progress = () => portalUsed ? 1f : 0f,
            info = () => portalUsed ? "EXIT USED" : "REACH THE ALTAR"
        });
        steps.Add(new Step
        {
            title = "YOU ARE READY",
            body = "In a real run enemies never stop and get stronger the longer you survive. Ten rings, a guard and a volley are all you need. Good luck!",
            keys = new string[0], manual = true, last = true,
            progress = () => manualGo,
            info = () => ""
        });
    }

    void SpawnRushers()
    {
        Sp(EnemyKind.Ant, new Vector2(4.5f, 0.8f), false); Sp(EnemyKind.Ant, new Vector2(4.8f, -0.4f), false);
        Sp(EnemyKind.Ant, new Vector2(-4.5f, 0.4f), false); Sp(EnemyKind.Ant, new Vector2(-4.8f, -0.8f), false);
    }

    void SpawnAnts()
    {
        for (int i = 0; i < 5; i++) Sp(EnemyKind.Ant, new Vector2(3.8f + (i % 3) * 0.45f, -0.7f + (i / 3) * 0.7f + (i % 2) * 0.2f), true);
    }

    void Next()
    {
        ClearEnemies();
        idx++; stepT = 0f; manualGo = 0f; doneTimer = 0f; prog = 0f; dimRelease = 99f;
        if (idx >= steps.Count) { idx = steps.Count - 1; return; }
        if (steps[idx].last) PlayerPrefs.SetInt("rr_tut_done", 1);
        if (steps[idx].setup != null) steps[idx].setup();
    }

    void Complete()
    {
        if (doneTimer > 0f) return;
        doneTimer = steps[idx].last ? 0f : 1.6f;
        praise = 1.4f;
        Sfx.Play("chest_open", 0.8f, 1.3f);
        Vfx.Burst(PP + Vector2.up * 0.4f, new Color(1f, 0.85f, 0.4f), 26, 4.5f * GS.S, 0.14f, 0.6f);
        Vfx.Shockwave(PP + Vector2.up * 0.4f, new Color(1f, 0.85f, 0.4f), 2.4f, 0.5f);
        foreach (var e in mine) if (e != null) { Vfx.Burst(e.transform.position, Color.white, 8, 3f * GS.S, 0.1f, 0.3f); Destroy(e.gameObject); }
        mine.Clear(); labels.Clear();
    }

    // ------------------------------------------------------------------ update
    void Update()
    {
        if (!Active || GameState.Mode != GameMode.Playing || !GameState.Active || idx < 0) return;
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) player = p.transform; return; }
        float dt = Time.deltaTime;
        stepT += dt; praise = Mathf.Max(0f, praise - dt);

        if (GameState.PlayerHp <= 1)
        {
            GameState.Heal(GameState.PlayerMaxHp);
            FloatingText.Add(PP + Vector2.up * 1.1f, "HEARTS REFILLED", new Color(1f, 0.5f, 0.55f), 14, 1.4f);
        }

        var s = steps[idx];
        if (doneTimer > 0f)
        {
            doneTimer -= dt;
            if (doneTimer <= 0f) Next();
            return;
        }
        if (s.maintain != null) s.maintain();
        if (s.manual && InputHub.Pressed(KeyCode.Return) && stepT > 0.4f) manualGo = 1f;
        prog = Mathf.Clamp01(s.progress());
        if (InputHub.Pressed(KeyCode.N) && !s.manual) { Complete(); doneTimer = 0.05f; return; }
        if (prog >= 1f && !s.last) Complete();
    }

    // ------------------------------------------------------------------ gui
    void Styles()
    {
        if (titleSt != null) return;
        Font f = GameAssets.PixelFont;
        titleSt = new GUIStyle { font = f, fontSize = 16, alignment = TextAnchor.MiddleLeft };
        titleSt.normal.textColor = new Color(1f, 0.82f, 0.25f);
        bodySt = new GUIStyle { font = f, fontSize = 11, alignment = TextAnchor.UpperLeft, wordWrap = true, richText = true };
        bodySt.normal.textColor = Color.white;
        smallSt = new GUIStyle { font = f, fontSize = 9, alignment = TextAnchor.MiddleCenter };
        smallSt.normal.textColor = new Color(0.85f, 0.85f, 0.9f);
        capSt = new GUIStyle { font = f, fontSize = 11, alignment = TextAnchor.MiddleCenter };
        capSt.normal.textColor = Color.white;
        bigSt = new GUIStyle { font = f, fontSize = 34, alignment = TextAnchor.MiddleCenter };
        btnN = Tex(new Color(0.16f, 0.12f, 0.2f, 0.95f)); btnH = Tex(new Color(0.5f, 0.33f, 0.1f, 1f));
        btnSt = new GUIStyle { font = f, fontSize = 11, alignment = TextAnchor.MiddleCenter };
        btnSt.normal.background = btnN; btnSt.normal.textColor = Color.white;
        btnSt.hover.background = btnH; btnSt.hover.textColor = new Color(1f, 0.82f, 0.25f);
        btnSt.active.background = btnH; btnSt.active.textColor = Color.white;
    }

    static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

    void R(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }

    static bool KeyHeld(string k)
    {
        switch (k)
        {
            case "W": return InputHub.Held(KeyCode.W) || InputHub.Held(KeyCode.UpArrow);
            case "A": return InputHub.Held(KeyCode.A) || InputHub.Held(KeyCode.LeftArrow);
            case "S": return InputHub.Held(KeyCode.S) || InputHub.Held(KeyCode.DownArrow);
            case "D": return InputHub.Held(KeyCode.D) || InputHub.Held(KeyCode.RightArrow);
            case "SPACE": case "HOLD SPACE": return InputHub.Held(KeyCode.Space);
            case "E": case "HOLD E": return InputHub.Held(KeyCode.E);
            case "Q": return InputHub.Held(KeyCode.Q);
            case "LMB": case "HOLD LMB": return InputHub.MouseHeld;
            case "RMB": return InputHub.RightHeld;
        }
        return false;
    }

    // button: padded, bordered, hover-lit; returns true when clicked
    bool Btn(Rect r, string label, bool primary)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        if (Event.current.type == EventType.Repaint)
        {
            Color edge = primary ? new Color(1f, 0.75f, 0.2f, ga) : new Color(0.55f, 0.5f, 0.65f, ga);
            Color fill = hover ? (primary ? new Color(0.55f, 0.36f, 0.08f, ga) : new Color(0.3f, 0.26f, 0.4f, ga)) : (primary ? new Color(0.32f, 0.2f, 0.06f, ga) : new Color(0.14f, 0.12f, 0.2f, ga));
            R(new Rect(r.x, r.y + 3, r.width, r.height), new Color(0, 0, 0, 0.5f * ga));
            R(r, edge);
            R(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), fill);
            capSt.normal.textColor = hover || primary ? new Color(1f, 0.9f, 0.55f, ga) : new Color(0.9f, 0.9f, 0.95f, ga);
            GUI.Label(r, label, capSt);
        }
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    float ga = 1f;   // panel alpha (fades in with each step)

    void OnGUI()
    {
        if (!Active || GameState.Mode != GameMode.Playing || GameState.Paused || GameState.Over || idx < 0 || idx >= steps.Count) return;
        Styles();
        GUI.depth = -50;
        float sc = Screen.height / 720f;
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
        float W = Screen.width / sc, H = 720f;
        var s = steps[idx];
        bool rep = Event.current.type == EventType.Repaint;

        // new step: the world dims first so the text can be read, then clears (sooner if you start playing)
        float hold = Mathf.Clamp(s.body.Length / 40f, 1.4f, 5f);
        if (stepT > 0.9f && (InputHub.Held(KeyCode.W) || InputHub.Held(KeyCode.A) || InputHub.Held(KeyCode.S) || InputHub.Held(KeyCode.D) || InputHub.MouseHeld || InputHub.RightHeld || InputHub.Held(KeyCode.Space) || InputHub.Held(KeyCode.E)))
            dimRelease = Mathf.Min(dimRelease, stepT);
        float h2 = Mathf.Min(hold, dimRelease);
        float dimA = s.last || s.manual ? 0.5f : 0.6f * (1f - Smooth((stepT - h2) / 0.9f));
        if (s.manual && !s.last) dimA = 0.5f * (1f - manualGo);
        ga = Smooth(stepT / 0.4f);
        if (rep && dimA > 0.01f) R(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.05f, dimA * Smooth(stepT / 0.25f)));

        // labels above frozen dummies
        var cam = Camera.main;
        if (cam != null && rep)
            foreach (var l in labels)
            {
                if (l.e == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(l.e.transform.position + Vector3.up * 0.9f);
                if (sp.z < 0f) continue;
                float lx = sp.x / sc, ly = (Screen.height - sp.y) / sc;
                float lw = Mathf.Max(70f, l.text.Length * 7.2f + 16f);
                Rect lr = new Rect(lx - lw / 2f, ly - 18, lw, 20);
                R(lr, new Color(0.04f, 0.03f, 0.08f, 0.8f * ga));
                R(new Rect(lr.x, lr.yMax - 2, lr.width, 2), new Color(1f, 0.75f, 0.2f, 0.8f * ga));
                smallSt.normal.textColor = new Color(l.col.r, l.col.g, l.col.b, ga);
                GUI.Label(lr, l.text, smallSt);
            }
        smallSt.normal.textColor = new Color(0.85f, 0.85f, 0.9f);

        // ---- panel (laid out top to bottom with a cursor, so nothing can overlap)
        float pw = Mathf.Min(720f, W - 40f), pad = 24f;
        float bh = bodySt.CalcHeight(new GUIContent(s.body), pw - pad * 2f) + 4f;
        float keysH = s.keys.Length > 0 ? 52f : 0f;
        float footH = s.manual ? 58f : 46f;
        float ph = 50f + bh + 8f + keysH + footH;
        float slide = (1f - ga) * -16f;
        Rect p = new Rect((W - pw) / 2f, 54f + slide, pw, ph);
        if (rep)
        {
            R(new Rect(p.x + 3, p.y + 5, p.width, p.height), new Color(0, 0, 0, 0.5f * ga));
            R(p, new Color(0.05f, 0.04f, 0.09f, 0.94f * ga));
            R(new Rect(p.x, p.y, p.width, 3f), new Color(1f, 0.7f, 0.2f, ga));
            R(new Rect(p.x, p.yMax - 1, p.width, 1f), new Color(1f, 0.7f, 0.2f, 0.25f * ga));
        }
        GUI.color = new Color(1f, 1f, 1f, ga);
        GUI.Label(new Rect(p.x + pad, p.y + 12, p.width - 200, 26), s.title, titleSt);
        smallSt.alignment = TextAnchor.MiddleRight;
        GUI.Label(new Rect(p.xMax - pad - 170, p.y + 14, 170, 22), "TUTORIAL  " + (idx + 1) + "/" + steps.Count, smallSt);
        smallSt.alignment = TextAnchor.MiddleCenter;
        if (rep) R(new Rect(p.x + pad, p.y + 42, p.width - pad * 2f, 1f), new Color(1f, 1f, 1f, 0.12f * ga));
        GUI.Label(new Rect(p.x + pad, p.y + 50, pw - pad * 2f, bh), s.body, bodySt);

        float y = p.y + 50f + bh + 8f;
        if (s.keys.Length > 0)
        {
            float tw = 0f;
            var ws = new float[s.keys.Length];
            for (int i = 0; i < s.keys.Length; i++) { ws[i] = s.keys[i] == "or" ? 30f : Mathf.Max(42f, 24f + KeyLabel(s.keys[i]).Length * 9f); tw += ws[i] + 8f; }
            float kx = p.x + (pw - (tw - 8f)) / 2f;
            for (int i = 0; i < s.keys.Length; i++)
            {
                Rect cr = new Rect(kx, y + 2f, ws[i], 34f);
                if (s.keys[i] == "or") GUI.Label(cr, "or", smallSt);
                else if (rep)
                {
                    bool held = KeyHeld(s.keys[i]);
                    R(new Rect(cr.x, cr.y + 3, cr.width, cr.height), new Color(0, 0, 0, 0.6f * ga));
                    R(cr, held ? new Color(1f, 0.78f, 0.2f, ga) : new Color(0.3f, 0.27f, 0.4f, ga));
                    R(new Rect(cr.x + 2, cr.y + 2, cr.width - 4, cr.height - 4), held ? new Color(0.5f, 0.33f, 0.05f, ga) : new Color(0.12f, 0.1f, 0.18f, ga));
                    capSt.normal.textColor = held ? new Color(1f, 0.92f, 0.55f, ga) : new Color(1f, 1f, 1f, ga);
                    GUI.Label(cr, KeyLabel(s.keys[i]), capSt);
                }
                kx += ws[i] + 8f;
            }
            y += keysH;
        }

        // footer
        float fy = p.yMax - footH + 6f;
        if (s.manual)
        {
            if (s.last)
            {
                if (Btn(new Rect(p.x + pw / 2f - 224, fy, 214, 40), "START A RUN", true)) { Active = false; ClearEnemies(); idx = -1; GameState.StartRun(); }
                if (Btn(new Rect(p.x + pw / 2f + 10, fy, 214, 40), "BACK TO MENU", false)) GameState.ReturnToMenu(false);
            }
            else
            {
                if (Btn(new Rect(p.x + pw / 2f - 224, fy, 214, 40), "CONTINUE  [ENTER]", true)) manualGo = 1f;
                if (Btn(new Rect(p.x + pw / 2f + 10, fy, 214, 40), "EXIT TUTORIAL", false)) GameState.ReturnToMenu(false);
            }
        }
        else
        {
            float bw = pw - pad * 2f - 250f;
            Rect bar = new Rect(p.x + pad, fy + 6f, bw, 14f);
            if (rep)
            {
                R(bar, new Color(0, 0, 0, 0.75f * ga));
                R(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * Mathf.Clamp01(prog), bar.height - 4), prog >= 1f ? new Color(0.4f, 1f, 0.5f, ga) : new Color(1f, 0.75f, 0.2f, ga));
            }
            smallSt.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(bar.x, bar.yMax + 2f, bw, 16), s.info != null ? s.info() : "", smallSt);
            smallSt.alignment = TextAnchor.MiddleCenter;
            if (Btn(new Rect(p.xMax - pad - 232, fy, 114, 30), "SKIP  [N]", false)) { Complete(); doneTimer = 0.05f; }
            if (Btn(new Rect(p.xMax - pad - 110, fy, 110, 30), "EXIT", false)) GameState.ReturnToMenu(false);
        }
        GUI.color = Color.white;

        // guide arrow toward the step's target
        if (s.target != null && cam != null && player != null && rep)
        {
            Vector2? tg = s.target();
            if (tg.HasValue)
            {
                Vector3 ps = cam.WorldToScreenPoint(player.position + Vector3.up * 0.4f), ts = cam.WorldToScreenPoint((Vector3)tg.Value);
                Vector2 pg = new Vector2(ps.x / sc, (Screen.height - ps.y) / sc), tgp = new Vector2(ts.x / sc, (Screen.height - ts.y) / sc);
                float meters = Vector2.Distance(PP, tg.Value);
                bool onScreen = tgp.x > 40 && tgp.x < W - 40 && tgp.y > p.yMax + 50 && tgp.y < H - 60;
                float bob = Mathf.Sin(Time.unscaledTime * 6f) * 5f;
                bigSt.fontSize = 22; bigSt.normal.textColor = new Color(1f, 0.85f, 0.3f, 0.95f);
                Vector2 lp;
                if (onScreen && meters < 9f)
                {
                    GUI.Label(new Rect(tgp.x - 30, tgp.y - 70 + bob, 60, 30), "v", bigSt);
                    lp = new Vector2(tgp.x, tgp.y - 82f);
                }
                else
                {
                    Vector2 d = (tgp - pg).normalized;
                    Vector2 c = pg + d * 110f;
                    float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    Matrix4x4 m = GUI.matrix;
                    GUIUtility.RotateAroundPivot(ang, c);
                    GUI.Label(new Rect(c.x - 30 + bob, c.y - 15, 60, 30), ">>", bigSt);
                    GUI.matrix = m;
                    lp = pg + d * 150f;
                }
                smallSt.normal.textColor = new Color(1f, 0.9f, 0.5f, 0.95f);
                GUI.Label(new Rect(lp.x - 50, lp.y - 8, 100, 18), Mathf.RoundToInt(meters) + " M", smallSt);
                smallSt.normal.textColor = new Color(0.85f, 0.85f, 0.9f);
                bigSt.fontSize = 34;
            }
        }

        // "step complete" banner
        if (praise > 0f && !s.last && rep)
        {
            float a2 = Mathf.Clamp01(praise / 0.5f);
            bigSt.normal.textColor = new Color(0.2f, 0.08f, 0f, a2 * 0.9f);
            GUI.Label(new Rect(3, 343, W, 60), "STEP COMPLETE!", bigSt);
            bigSt.normal.textColor = new Color(1f, 0.85f, 0.35f, a2);
            GUI.Label(new Rect(0, 340, W, 60), "STEP COMPLETE!", bigSt);
        }
        GUI.matrix = old;
    }

    static float Smooth(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    static string KeyLabel(string k)
    {
        return k == "LMB" ? "LEFT CLICK" : (k == "RMB" ? "RIGHT CLICK" : (k == "HOLD LMB" ? "HOLD CLICK" : k));
    }
}

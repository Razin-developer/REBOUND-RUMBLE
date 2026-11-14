import io, sys, os
root = sys.argv[1]

def patch(fn, edits):
    p = os.path.join(root, fn)
    with io.open(p, 'r', encoding='utf-8', newline='') as f:
        raw = f.read()
    crlf = '\r\n' in raw
    t = raw.replace('\r\n', '\n')
    for old, new, *opt in edits:
        if new in t and old not in t:
            print('  already applied:', fn, repr(old[:40])); continue
        n = t.count(old)
        if n != 1:
            print('  !! %s: expected 1 match, found %d for %r' % (fn, n, old[:60])); sys.exit(1)
        t = t.replace(old, new)
    if crlf: t = t.replace('\n', '\r\n')
    with io.open(p, 'w', encoding='utf-8', newline='') as f:
        f.write(t)
    print('patched', fn)

# ---------------------------------------------------------------- Nav
patch('Nav.cs', [(
"    /// <summary>A free walkable cell whose distance from `target` is within [minD, maxD].</summary>\n",
"""    /// <summary>A free walkable cell near a map edge (`outward` = +x / -x / +y / -y) with room around it, away from `avoid`.</summary>
    public static bool TryEdgeCell(Vector2 outward, Vector2 avoid, float avoidR, out Vector2 point)
    {
        point = Vector2.zero;
        if (!Ready || spawnCells.Count == 0) return false;
        float best = float.MinValue;
        for (int i = 0; i < spawnCells.Count; i++) { float s = Vector2.Dot(spawnCells[i], outward); if (s > best) best = s; }
        var f = new ContactFilter2D(); f.useTriggers = false; f.useLayerMask = false;
        var buf = new Collider2D[4];
        var cand = new List<Vector2>();
        for (float slack = 3f; slack <= 18f; slack += 3f)
        {
            cand.Clear();
            for (int i = 0; i < spawnCells.Count; i++)
                if (Vector2.Dot(spawnCells[i], outward) >= best - slack && Vector2.Distance(spawnCells[i], avoid) >= avoidR) cand.Add(spawnCells[i]);
            for (int t = 0; t < 120 && cand.Count > 0; t++)
            {
                Vector2 p = cand[Random.Range(0, cand.Count)];
                if (Physics2D.OverlapCircle(p, 1.6f, f, buf) > 0) continue;
                point = p; return true;
            }
        }
        return false;
    }

    /// <summary>Remove a disc of ground from navigation (the harbour lagoon is water).</summary>
    public static void CarveOut(Vector2 c, float r)
    {
        if (!Ready) return;
        float r2 = r * r;
        spawnCells.RemoveAll(p => (p - c).sqrMagnitude < r2);
        var gone = new List<Vector2Int>();
        foreach (var w in walkable) if ((CenterOf(w) - c).sqrMagnitude < r2) gone.Add(w);
        foreach (var w in gone) walkable.Remove(w);
        lastPlayerCell = new Vector2Int(int.MinValue, 0); lastCell2 = new Vector2Int(int.MinValue, 0);
    }

    /// <summary>A free walkable cell whose distance from `target` is within [minD, maxD].</summary>
""")])

# ---------------------------------------------------------------- World
patch('World.cs', [("        Shrine.Scan();\n    }\n}", "        Shrine.Scan();\n        ShipDock.Place();\n    }\n}")])

# ---------------------------------------------------------------- GameUI
patch('GameUI.cs', [
("        page = Page.Splash;\n        user = Net.User;\n", "        uiInst = this;\n        page = Page.Splash;\n        user = Net.User;\n"),
("        if (GameState.Mode == GameMode.Playing) DrawHud(W, H);\n", "        if (GameState.Mode == GameMode.Playing) { DrawHud(W, H); if (!Naval.Active) DrawDockPrompt(W, H); }\n"),
("        if (page == Page.None && !GameState.Over && Button(new Rect(W - 104, 100, 90, 28), GameSettings.Muted ? \"UNMUTE\" : \"MUTE\", btnSmall)) GameSettings.ToggleMute();\n",
 "        if (page == Page.None && !GameState.Over && Button(new Rect(W - 104, 100, 90, 28), GameSettings.Muted ? \"UNMUTE\" : \"MUTE\", btnSmall)) GameSettings.ToggleMute();\n\n        if (Naval.Active) { DrawNavalHud(W, H); return; }   // the sea battle has its own HUD\n"),
])

# ---------------------------------------------------------------- InputHub
patch('InputHub.cs', [("KeyCode.Space, KeyCode.Tab, KeyCode.Escape, KeyCode.R, KeyCode.Q, KeyCode.E\n",
 "KeyCode.Space, KeyCode.Tab, KeyCode.Escape, KeyCode.R, KeyCode.Q, KeyCode.E,\n        KeyCode.I, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3\n")])

# ---------------------------------------------------------------- CameraRig
patch('CameraRig.cs', [
("    static CameraRig instance;\n", "    static CameraRig instance;\n    /// <summary>Sea battle: the camera follows the ship faster and zooms out.</summary>\n    public static bool NavalCam;\n    static float zoomTarget = 1f, zoomCur = 1f;\n    float baseLerp = 1f;\n    public static void SetZoom(float z, bool smooth) { zoomTarget = z; if (!smooth) zoomCur = z; }\n    public static void Snap() { if (instance != null) instance.SnapToPlayer(); }\n"),
("static void ResetStatics() { instance = null; }", "static void ResetStatics() { instance = null; NavalCam = false; zoomTarget = zoomCur = 1f; }"),
("        camRef.orthographicSize = Mathf.Max(baseSize, need);\n", "        camRef.orthographicSize = Mathf.Max(baseSize, need) * zoomCur;\n"),
("        follow = GetComponent<Cainos.PixelArtTopDown_Basic.CameraFollow>();\n", "        follow = GetComponent<Cainos.PixelArtTopDown_Basic.CameraFollow>();\n        if (follow != null) baseLerp = follow.lerpSpeed;\n"),
("    void Update()\n    {\n        transform.position -= shakeOffset;", "    void Update()\n    {\n        if (follow != null) follow.lerpSpeed = NavalCam ? 6f : baseLerp;\n        zoomCur = Mathf.MoveTowards(zoomCur, zoomTarget, Time.unscaledDeltaTime * 2.5f);\n        transform.position -= shakeOffset;"),
])

# ---------------------------------------------------------------- MusicPlayer
patch('MusicPlayer.cs', [("    static bool ducked;\n", """    /// <summary>Switch the looping track (sea battles use their own music).</summary>
    public static void Use(string clipName)
    {
        Start();
        var c = GameAssets.Clip(clipName);
        if (c == null || src == null || src.clip == c) return;
        src.clip = c;
        src.Play();
    }

    static bool ducked;
""")])

# ---------------------------------------------------------------- TenRing
patch('TenRing.cs', [
("public enum RingState { Orbit, Flying, Returning }", "public enum RingState { Orbit, Flying, Returning, Lent }"),
("    public RingState State { get; private set; }\n", "    public RingState State { get; private set; }\n    public Vector2 LoanVel = Vector2.right;      // sea battle: direction the lent ring is travelling (for its spin)\n"),
("    void StartReturn()\n", """    /// <summary>Sea battle: something else (Naval) drives this ring's position; physics and enemy hits are off.</summary>
    public void BeginLoan(Vector2 at)
    {
        State = RingState.Lent;
        rb.simulated = false; col.enabled = false;
        transform.position = new Vector3(at.x, at.y, transform.position.z);
        lastHit.Clear();
        trail.Clear(); trail.emitting = true;
        if (sparks != null) { var se = sparks.emission; se.enabled = true; }
        vsr.color = Color.white;
        gsr.color = new Color(1f, 0.55f, 0.1f, 0.5f);
        SetSorting(topLayerId, 502);
    }

    public void EndLoan() { if (State == RingState.Lent) Catch(); }

    void StartReturn()
"""),
("        Vector2 v = rb.linearVelocity;\n        if (v.sqrMagnitude > 0.01f) visual.localRotation", "        Vector2 v = State == RingState.Lent ? LoanVel : rb.linearVelocity;\n        if (v.sqrMagnitude > 0.01f) visual.localRotation"),
("        if (State == RingState.Orbit) return;\n        if (owner == null) { Destroy(gameObject); return; }", "        if (State == RingState.Orbit || State == RingState.Lent) return;\n        if (owner == null) { Destroy(gameObject); return; }"),
])

# ---------------------------------------------------------------- gating: nothing on land may act while at sea
patch('RingLauncher.cs', [("        if (!GameState.Active || Time.time < nextThrow || Coop.IsGuest) return;\n", "        if (Naval.Active) return;                    // at sea the rings belong to the ship\n        if (!GameState.Active || Time.time < nextThrow || Coop.IsGuest) return;\n")])
patch('EnemySpawner.cs', [("        if (player == null) return;\n        Nav.Refresh(player.position);\n", "        if (player == null) return;\n        if (Naval.Active) return;                    // the island is frozen while at sea\n        Nav.Refresh(player.position);\n")])
patch('Boost.cs', [("    void Update()\n    {\n        if (!GameState.Active || Tutorial.Active || Coop.IsGuest) return;\n        if (player == null)", "    void Update()\n    {\n        if (!GameState.Active || Tutorial.Active || Coop.IsGuest || Naval.Active) return;\n        if (player == null)")])
patch('Boss.cs', [("if (!GameState.Active || Tutorial.Active || Enemy.Boss != null || Coop.IsGuest) return;", "if (!GameState.Active || Tutorial.Active || Enemy.Boss != null || Coop.IsGuest || Naval.Active) return;")])
patch('Guard.cs', [("bool canUse = GameState.Active && !GameState.Over && player != null;", "bool canUse = GameState.Active && !GameState.Over && player != null && !Naval.Active;")])
patch('Extras.cs', [("public static bool CanUse { get { return GameState.Active && Attuned > 0 && CooldownLeft <= 0f; } }", "public static bool CanUse { get { return GameState.Active && !Naval.Active && Attuned > 0 && CooldownLeft <= 0f; } }")])
patch('PlayerHealth.cs', [("        if (Coop.IsGuest) return;\n        if (!GameState.Active || Time.time < invulnUntil) return;\n        if (Boosts.Aegis)", "        if (Coop.IsGuest || Naval.Active) return;\n        if (!GameState.Active || Time.time < invulnUntil) return;\n        if (Boosts.Aegis)")])
print('ALL OK')

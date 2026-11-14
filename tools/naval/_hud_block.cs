    // ------------------------------------------------------------------ small drawing helpers
    int navTab;                                   // tab shown on the naval controls page

    void NavPanel(Rect r, float alpha = 0.82f)
    {
        Rect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0.55f, 0.46f, 0.2f, 0.9f * alpha));
        Rect(r, new Color(0.03f, 0.07f, 0.13f, alpha));
        Rect(new Rect(r.x, r.y, r.width, 2), new Color(1f, 0.85f, 0.4f, 0.35f * alpha));
    }

    void DrawArrow(Vector2 p, float angleDeg, Color c, float size)
    {
        var om = GUI.matrix;
        GUIUtility.RotateAroundPivot(angleDeg, p * scale);      // pivot is in screen space, our matrix scales the GUI
        for (int i = 0; i < 5; i++)
            Rect(new Rect(p.x - size + i * size * 0.4f, p.y - (5 - i) * size * 0.14f, size * 0.4f + 1f, (5 - i) * size * 0.28f), c);
        GUI.matrix = om;
    }

    void NavalTile(Rect r, string icon, string key, string nm, int rings, float cdLeft, float cdTotal, bool enough, Color tint)
    {
        bool ready = cdLeft <= 0f && enough;
        Rect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), ready ? new Color(tint.r, tint.g, tint.b, 0.9f) : new Color(0.25f, 0.25f, 0.3f, 0.8f));
        Rect(r, new Color(0.05f, 0.08f, 0.14f, 0.94f));
        Texture2D t = GameAssets.Texture(icon);
        var oc = GUI.color;
        GUI.color = ready ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        if (t != null) GUI.DrawTexture(new Rect(r.x + r.width / 2f - 22, r.y + 10, 44, 44), t);
        GUI.color = oc;
        if (cdLeft > 0f)
        {
            float k = Mathf.Clamp01(cdLeft / Mathf.Max(0.1f, cdTotal));
            Rect(new Rect(r.x, r.y, r.width, r.height * k), new Color(0f, 0f, 0f, 0.55f));
            Text(new Rect(r.x, r.y + 14, r.width, 36), Mathf.CeilToInt(cdLeft) + "s", label, Color.white, TextAnchor.MiddleCenter, 18);
        }
        Text(new Rect(r.x + 5, r.y + 3, 70, 14), key, small, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperLeft);
        Text(new Rect(r.x, r.y + r.height - 34, r.width, 16), nm, small, ready ? tint : new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleCenter);
        for (int i = 0; i < rings; i++)
            Rect(new Rect(r.x + r.width / 2f - rings * 7f + i * 14f + 2f, r.y + r.height - 14, 10, 8), enough ? Gold : new Color(0.9f, 0.3f, 0.25f));
    }

    // ------------------------------------------------------------------ helm panel: throttle ladder, speed, compass, rudder
    void DrawHelm(PlayerShip P, float W, float H)
    {
        var r = new Rect(W - 214, H - 234, 200, 218);
        NavPanel(r);
        Text(new Rect(r.x, r.y + 5, r.width, 16), "HELM", label, Gold, TextAnchor.MiddleCenter, 10);

        // throttle ladder (click a step to set it)
        string[] names = { "FULL", "3/4", "1/2", "1/4", "STOP", "ASTERN" };
        float[] vals = { 1f, 0.75f, 0.5f, 0.25f, 0f, -0.35f };
        float lx = r.x + 10, ly = r.y + 26, lh = 26;
        int nearest = 0; float best = 9f;
        for (int i = 0; i < vals.Length; i++) { float d = Mathf.Abs(P.throttle - vals[i]); if (d < best) { best = d; nearest = i; } }
        for (int i = 0; i < vals.Length; i++)
        {
            var row = new Rect(lx, ly + i * (lh + 2), 78, lh);
            bool on = i == nearest;
            Rect(row, on ? new Color(0.5f, 0.42f, 0.14f, 0.95f) : new Color(1f, 1f, 1f, 0.07f));
            Text(row, names[i], small, on ? Color.white : new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);
            if (GUI.Button(row, "", GUIStyle.none)) { P.throttle = vals[i]; Sfx.PlayUI("click"); }
        }

        // speed read-out
        float knots = P.speed * 2.1f;
        Text(new Rect(r.x + 96, r.y + 24, 98, 34), Mathf.Abs(knots).ToString("0.0"), title, P.speed < -0.05f ? new Color(1f, 0.6f, 0.4f) : Color.white, TextAnchor.MiddleCenter, 22);
        Text(new Rect(r.x + 96, r.y + 56, 98, 14), P.speed < -0.05f ? "KNOTS (REVERSE)" : "KNOTS", small, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);

        // compass
        float hdg = Mathf.Repeat(90f - P.angle, 360f);
        string[] pts = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        string card = pts[Mathf.RoundToInt(hdg / 45f) % 8];
        var cc = new Vector2(r.x + 146, r.y + 106);
        for (int i = 0; i < 24; i++)
        {
            float a = i * 15f * Mathf.Deg2Rad;
            float rr = i % 6 == 0 ? 33f : 29f;
            Rect(new Rect(cc.x + Mathf.Sin(a) * rr - 1, cc.y - Mathf.Cos(a) * rr - 1, i % 6 == 0 ? 3f : 2f, i % 6 == 0 ? 3f : 2f), new Color(1f, 1f, 1f, i % 6 == 0 ? 0.9f : 0.4f));
        }
        float ha = hdg * Mathf.Deg2Rad;
        for (int i = 1; i <= 9; i++)
            Rect(new Rect(cc.x + Mathf.Sin(ha) * i * 3f - 1.5f, cc.y - Mathf.Cos(ha) * i * 3f - 1.5f, 3f, 3f), Color.Lerp(Gold, Color.white, i / 9f));
        Text(new Rect(cc.x - 30, cc.y - 9, 60, 18), card, label, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, 9);
        Text(new Rect(r.x + 96, r.y + 144, 98, 14), Mathf.RoundToInt(hdg).ToString("000") + "°", small, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);

        // rudder bar
        float rx = r.x + 12, rw = r.width - 24, ry = r.y + r.height - 34;
        Text(new Rect(rx, ry - 16, rw, 14), "RUDDER   A  <   >  D", small, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);
        Rect(new Rect(rx, ry, rw, 12), new Color(0f, 0f, 0f, 0.7f));
        Rect(new Rect(rx + rw / 2 - 1, ry - 2, 2, 16), new Color(1f, 1f, 1f, 0.5f));
        float nx = rx + rw / 2 + (-P.rudder) * (rw / 2 - 6);        // starboard (D) is to the right
        Rect(new Rect(nx - 5, ry - 2, 10, 16), Mathf.Abs(P.rudder) > 0.05f ? new Color(0.45f, 0.85f, 1f) : Gold);
        Text(new Rect(r.x, r.y + r.height - 16, r.width, 14), "X ALL STOP  -  HOLD W/S FOR SPEED", small, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleCenter);
    }

    // ------------------------------------------------------------------ naval HUD
    void DrawNavalHud(float W, float H)
    {
        var P = Naval.Player;
        if (P == null) return;
        Camera cam = Camera.main;
        var oc = GUI.color;
        float hk = Mathf.Clamp01(P.hp / Mathf.Max(1f, P.maxHp));

        // ---- hull + crew (top left panel)
        var hp = new Rect(16, 14, 290, 84);
        NavPanel(hp);
        Text(new Rect(hp.x + 10, hp.y + 6, hp.width - 20, 16), "HULL  " + Mathf.CeilToInt(P.hp) + " / " + Mathf.CeilToInt(P.maxHp) + (Boosts.Aegis ? "   AEGIS" : ""), small, Boosts.Aegis ? new Color(0.6f, 0.85f, 1f) : Color.white);
        Rect(new Rect(hp.x + 10, hp.y + 26, hp.width - 20, 16), new Color(0f, 0f, 0f, 0.75f));
        Rect(new Rect(hp.x + 12, hp.y + 28, (hp.width - 24) * hk, 12), Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.35f, 0.85f, 0.35f), hk));
        Text(new Rect(hp.x + 10, hp.y + 48, hp.width - 20, 16), "CREW " + P.crew.Count + "     REPAIRS " + (P.regenPerSec > 0f ? "+" + P.regenPerSec.ToString("0.0") + "/s" : "-"), small, new Color(1f, 1f, 1f, 0.75f));
        Text(new Rect(hp.x + 10, hp.y + 64, hp.width - 20, 16), "DOUBLOONS  " + Naval.Loot, small, Gold);

        // ---- wave + enemies (top centre)
        float cw = Mathf.Min(360f, W - 700f);
        var wp = new Rect(W / 2 - cw / 2, 14, cw, 54);
        NavPanel(wp);
        Text(new Rect(wp.x, wp.y + 6, wp.width, 22), "WAVE " + Naval.Wave + " / " + Naval.WaveCount, label, Gold, TextAnchor.MiddleCenter, 14);
        Text(new Rect(wp.x, wp.y + 30, wp.width, 18), "PIRATE SHIPS  " + Naval.Enemies.Count, small, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter);

        // ---- boss bar
        EnemyShip boss = null;
        foreach (var e in Naval.Enemies) if (e != null && e.IsBoss && !e.dead) { boss = e; break; }
        if (boss != null)
        {
            float bw = Mathf.Min(520f, W - 700f), bx = W / 2 - bw / 2;
            Text(new Rect(bx, 74, bw, 18), "THE DREAD GALLEON", label, new Color(1f, 0.45f, 0.35f), TextAnchor.MiddleCenter, 11);
            Rect(new Rect(bx - 2, 92, bw + 4, 16), new Color(0f, 0f, 0f, 0.8f));
            Rect(new Rect(bx, 94, bw * Mathf.Clamp01(boss.hp / boss.maxHp), 12), new Color(0.85f, 0.2f, 0.2f));
        }

        // ---- top-right buttons: sail home, controls, pause
        float bxr = W - 140, byr = 62;
        bool canLeave = Naval.Phase == NavalPhase.Fight || Naval.Phase == NavalPhase.Break;
        if (canLeave)
        {
            if (GUI.RepeatButton(new Rect(bxr, byr, 124, 30), "EXIT (HOLD I)", btnSmall)) Naval.RetreatHeldUI = true;
        }
        if (Button(new Rect(bxr, byr + 36, 124, 30), "CONTROLS (H)", btnSmall)) { GameState.SetPaused(true); page = Page.Controls; navTab = 0; }
        if (Button(new Rect(bxr, byr + 72, 124, 30), "PAUSE (ESC)", btnSmall)) Pause();

        // ---- ring rack (bottom left)
        int ready = TenRing.ReadyCount;
        var rp = new Rect(16, H - 112, Mathf.Max(190f, TenRing.Total * 30f + 18f), 96);
        NavPanel(rp);
        Text(new Rect(rp.x + 10, rp.y + 6, 200, 16), "RINGS " + ready + "/" + TenRing.Total, small, Gold);
        Texture2D ringTex = GameAssets.Texture("ring");
        for (int i = 0; i < TenRing.All.Count && i < TenRing.Total; i++)
        {
            var ring = TenRing.All[i];
            var rr = new Rect(rp.x + 10 + i * 30, rp.y + 26, 28, 28);
            Color c = new Color(1f, 1f, 1f, 1f);
            if (ring == null || ring.State != TenRing.RingState.Orbit)
            {
                int role = 0; if (ring != null) Naval.RingRole.TryGetValue(ring, out role);
                c = role == 2 ? new Color(0.4f, 0.85f, 1f, 0.85f) : (role == 3 ? new Color(1f, 0.4f, 0.25f, 0.85f) : new Color(0.55f, 0.5f, 0.35f, 0.7f));
            }
            GUI.color = c;
            if (ringTex != null) GUI.DrawTexture(rr, ringTex); else Rect(rr, Gold);
        }
        GUI.color = oc;
        Text(new Rect(rp.x + 10, rp.y + 62, rp.width - 12, 28), "GOLD FIRE   CYAN DASH   RED BLAST", small, new Color(1f, 1f, 1f, 0.5f));

        // ---- helm (bottom right)
        DrawHelm(P, W, H);

        // ---- ability tiles (bottom centre, between the panels)
        float tw = 88, th = 96, gap = 12, tx = W / 2 - (tw * 3 + gap * 2) / 2, ty = H - th - 32;
        NavalTile(new Rect(tx, ty, tw, th), "ico_fire", "CLICK", "FIRE", 1, Mathf.Max(0f, Naval.NextShotAt - Time.time), 0.3f, ready >= 1, Gold);
        NavalTile(new Rect(tx + tw + gap, ty, tw, th), "ico_dash", "SHIFT", "RING DASH", 2, Naval.DashLeft, Naval.DashTotal, ready >= 2, new Color(0.45f, 0.85f, 1f));
        NavalTile(new Rect(tx + (tw + gap) * 2, ty, tw, th), "ico_blast", "R-CLICK", "BLAST", 3, Naval.BlastLeft, Naval.BlastTotal, ready >= 3, new Color(1f, 0.5f, 0.3f));
        Text(new Rect(W / 2 - 300, H - 28, 600, 20), "W/S THROTTLE   A/D RUDDER   MOUSE AIMS   X ALL STOP   H CONTROLS", small, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);

        // ---- retreat progress
        if (Naval.RetreatHold > 0.02f)
        {
            Text(new Rect(W / 2 - 250, H - 200, 500, 24), "SAILING HOME...", label, new Color(0.6f, 0.9f, 1f), TextAnchor.MiddleCenter, 14);
            Rect(new Rect(W / 2 - 150, H - 172, 300, 14), new Color(0f, 0f, 0f, 0.7f));
            Rect(new Rect(W / 2 - 148, H - 170, 296 * Naval.RetreatHold, 10), new Color(0.45f, 0.85f, 1f));
        }

        // ---- arrows to pirates that are off-screen
        if (cam != null)
        {
            foreach (var e in Naval.Enemies)
            {
                if (e == null || e.dead) continue;
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(e.pos.x, e.pos.y, 0f));
                float sx = sp.x / scale, sy = (Screen.height - sp.y) / scale;
                if (sx > 30 && sx < W - 30 && sy > 30 && sy < H - 30) continue;
                Vector2 c = new Vector2(W / 2f, H / 2f);
                Vector2 d = new Vector2(sx, sy) - c;
                if (d.sqrMagnitude < 1f) continue;
                float sc = Mathf.Min((W / 2f - 36f) / Mathf.Max(1f, Mathf.Abs(d.x)), (H / 2f - 36f) / Mathf.Max(1f, Mathf.Abs(d.y)));
                Vector2 p = c + d * sc;
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                DrawArrow(p, ang, e.IsBoss ? new Color(1f, 0.25f, 0.2f, 0.95f) : new Color(1f, 0.75f, 0.3f, 0.85f), e.IsBoss ? 18f : 12f);
            }
        }

        // ---- banner
        if (Naval.BannerT < Naval.BannerLife && !string.IsNullOrEmpty(Naval.BannerText))
        {
            float a = Mathf.Min(1f, Naval.BannerT / 0.25f, (Naval.BannerLife - Naval.BannerT) / 0.6f);
            Color bc = Naval.BannerColor; bc.a = a;
            Rect(new Rect(0, H * 0.22f - 8, W, 80), new Color(0f, 0.03f, 0.1f, 0.5f * a));
            Text(new Rect(0, H * 0.22f, W, 44), Naval.BannerText, title, bc, TextAnchor.MiddleCenter, 30);
            if (!string.IsNullOrEmpty(Naval.BannerSub)) Text(new Rect(0, H * 0.22f + 44, W, 22), Naval.BannerSub, label, new Color(1f, 1f, 1f, a * 0.85f), TextAnchor.MiddleCenter, 12);
        }

        // ---- upgrade cards
        if (Naval.CardsOpen) DrawCards(W, H);
    }

    // ------------------------------------------------------------------ tabbed controls page (naval)
    void DrawNavalControls(float W, float H)
    {
        Dim(W, H, 0.72f);
        Rect r = Center(W, H, 720, 520);
        GUI.Box(r, "", box);
        Text(new Rect(r.x, r.y + 12, r.width, 34), "SEA BATTLE - CONTROLS", title, Gold, TextAnchor.MiddleCenter, 20);
        string[] tabs = { "HELM", "WEAPONS", "RINGS", "TIPS" };
        float tw = (r.width - 80) / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            var tr = new Rect(r.x + 40 + i * tw, r.y + 54, tw - 6, 34);
            if (i == navTab) Rect(new Rect(tr.x - 2, tr.y - 2, tr.width + 4, tr.height + 4), Gold);
            if (Button(tr, tabs[i], btnSmall)) navTab = i;
        }
        string[,] rows;
        switch (navTab)
        {
            case 0:
                rows = new string[,] {
                    { "W / UP", "RAISE THE THROTTLE (HOLD)" },
                    { "S / DOWN", "LOWER THE THROTTLE - GOES ASTERN BELOW STOP" },
                    { "A / LEFT", "RUDDER TO PORT - TURNING NEEDS WATER FLOW" },
                    { "D / RIGHT", "RUDDER TO STARBOARD" },
                    { "X", "ALL STOP" },
                    { "HELM PANEL", "CLICK A THROTTLE STEP TO SET IT" },
                };
                break;
            case 1:
                rows = new string[,] {
                    { "MOUSE", "AIM - YOUR CREW TRAIN THE GUNS TO THE RETICLE" },
                    { "LEFT CLICK / SPACE", "FIRE A RING FROM THE CANNONS (1 RING)" },
                    { "RIGHT CLICK / Q", "BLAST - HEAVY BOMB (3 RINGS)" },
                    { "BROADSIDE", "SWING THE SHIP SIDEWAYS TO THE ENEMY FOR MORE GUNS" },
                    { "RAMMING", "AT SPEED THE BOW DEALS DAMAGE - DASH TO RAM HARD" },
                };
                break;
            case 2:
                rows = new string[,] {
                    { "GOLD RING", "ON BOARD - READY TO FIRE" },
                    { "SHIFT", "RING DASH - A SURGE OF SPEED (2 RINGS)" },
                    { "CYAN RING", "LENT TO THE DASH, RETURNS WHEN IT ENDS" },
                    { "RED RING", "LENT TO THE BLAST, RETURNS AFTER IT BURSTS" },
                    { "DOUBLOONS", "LOOT FROM WRECKS AND BARRELS - KEPT IF YOU SAIL HOME" },
                };
                break;
            default:
                rows = new string[,] {
                    { "THE KEEL", "THE SHIP SKIDS OUTWARD IN A HARD TURN" },
                    { "SPEED", "TURN SLOWLY AT LOW SPEED - KEEP SOME WAY ON" },
                    { "ROCKS / WALL", "SCRAPING THEM COSTS HULL AT SPEED" },
                    { "EXIT", "HOLD I OR USE THE EXIT BUTTON BETWEEN WAVES" },
                    { "H / TAB", "OPEN THIS PAGE" },
                };
                break;
        }
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            Text(new Rect(r.x + 40, r.y + 110 + i * 46, 250, 36), rows[i, 0], label, Gold, TextAnchor.MiddleLeft, 11);
            Text(new Rect(r.x + 300, r.y + 110 + i * 46, 400, 36), rows[i, 1], small, Color.white, TextAnchor.MiddleLeft);
        }
        if (Button(new Rect(r.x + 40, r.y + r.height - 66, 200, 44), "RESUME")) Resume();
        bool canLeave = Naval.Phase == NavalPhase.Fight || Naval.Phase == NavalPhase.Break;
        if (Button(new Rect(r.x + 260, r.y + r.height - 66, 200, 44), canLeave ? "SAIL HOME" : "SAIL HOME (LATER)", btnSmall) && canLeave) { Resume(); Naval.Retreat(); }
        if (Button(new Rect(r.x + r.width - 240, r.y + r.height - 66, 200, 44), "MAIN MENU")) GameState.ReturnToMenu(true);
    }

    void DrawNavalPause(float W, float H)
    {
        Dim(W, H, 0.6f);
        Text(new Rect(0, 130, W, 60), "PAUSED AT SEA", title, Gold, TextAnchor.MiddleCenter, 40);
        float bx = W / 2 - 170, by = 230;
        bool canLeave = Naval.Phase == NavalPhase.Fight || Naval.Phase == NavalPhase.Break;
        if (Button(new Rect(bx, by, 340, 52), "RESUME")) Resume();
        if (Button(new Rect(bx, by + 64, 340, 52), "CONTROLS")) { page = Page.Controls; navTab = 0; }
        if (Button(new Rect(bx, by + 128, 340, 52), "SETTINGS")) { settingsReturn = Page.Pause; page = Page.Settings; serverField = Net.BaseUrl; }
        if (Button(new Rect(bx, by + 192, 340, 52), canLeave ? "SAIL HOME - KEEP THE LOOT" : "SAIL HOME (NOT NOW)")) { if (canLeave) { Resume(); Naval.Retreat(); } }
        if (Button(new Rect(bx, by + 256, 340, 52), "MAIN MENU (SAVE SCORE)")) GameState.ReturnToMenu(true);
    }


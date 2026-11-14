import io, os, re, sys
root = sys.argv[1]
def patch(fn, edits, regex=()):
    p = os.path.join(root, fn)
    raw = io.open(p, 'r', encoding='utf-8', newline='').read()
    crlf = '\r\n' in raw
    t = raw.replace('\r\n', '\n')
    for old, new in edits:
        n = t.count(old)
        if n != 1:
            print('!! %s: %d matches for %r' % (fn, n, old[:70])); sys.exit(1)
        t = t.replace(old, new)
    for pat, rep in regex:
        t, n = re.subn(pat, rep, t)
        print('  regex', fn, pat[:40], n)
    if crlf: t = t.replace('\n', '\r\n')
    io.open(p, 'w', encoding='utf-8', newline='').write(t)
    print('patched', fn)

patch('Coop.cs', [
("""        if (want && Role == CoopRole.Host && sentMap != GameSettings.MapIndex)
        {
            sentMap = GameSettings.MapIndex;
            Send("{\\"t\\":\\"map\\",\\"map\\":" + GameSettings.MapIndex + "}");
        }
""", ""),
("                RoomMap = m.map; RoomStarted = m.started;\n", "                RoomStarted = m.started;\n"),
("                RoomMap = m.map; RoomStarted = true;\n                if (Role == CoopRole.Guest) MapSystem.Override = Mathf.Clamp(m.map, 0, Mathf.Max(0, MapSystem.Count - 1));   // the host's map\n", "                RoomStarted = true;\n"),
("        sb.Append('|').Append(MapSystem.Index);      // the host's map: the guest switches to it\n", ""),
("""        if (p.Length > 3)
        {
            int hostMap = Pi(p[3]);
            if (hostMap >= 0 && hostMap < MapSystem.Count && hostMap != MapSystem.Index)
            {
                MapSystem.Override = hostMap;
                MapSystem.Apply(hostMap);       // chests / portal come with the host's next world message
                return;
            }
        }
""", ""),
("        RoomMap = 0; RoomStarted", "        RoomStarted"),
], regex=[(r"\n[^\n]*public static int RoomMap;[^\n]*", ""), (r"\n[^\n]*static int sentMap = -1;[^\n]*", ""), (r" sentMap = -1;", "")])

patch('EnemySpawner.cs', [], regex=[
 (r" \+ MapSystem\.CapBonus", ""), (r" \* MapSystem\.Pressure", ""), (r" \* MapSystem\.Bias\(EnemyKind\.\w+\)", "")])
patch('GameState.cs', [("        MapSystem.EnsureApplied();       // the chosen map is active and the player stands at its start before anything resets\n", "")])
patch('CameraRig.cs', [("Vector2 mc = MapSystem.Center, me = MapSystem.Extent;", "Vector2 mc = new Vector2(2f, 4f), me = new Vector2(14f, 17f);   // the arena's centre and half-size")])
patch('GameUI.cs', [("        DrawMapPicker(bx, 201, 320);\n", "")])
patch('GameUI.Coop.cs', [("""            if (Coop.Role == CoopRole.Host) DrawMapPicker(lx, r.y + 366, lw);
            else
            {
                int gm = Mathf.Clamp(Coop.RoomMap, 0, Mathf.Max(0, MapSystem.Count - 1));
                string gn = MapSystem.Count > gm ? MapSystem.Maps[gm].Name : "";
                Text(new Rect(lx, r.y + 372, lw, 24), "MAP: " + gn.ToUpper() + "  (HOST CHOOSES)", small, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter);
            }
""", "")])
patch('GameSettings.cs', [
("    public static int MapIndex = 0;            // chosen map (0 = the original)\n", ""),
("        MapIndex = Mathf.Clamp(PlayerPrefs.GetInt(\"rr_map\", 0), 0, 8);\n", ""),
("        PlayerPrefs.SetInt(\"rr_map\", MapIndex);\n", "")])
patch('Naval.cs', [("new System.Random(1000 + MapSystem.Index * 17)", "new System.Random(1000)")])
patch('ShipDock.cs', [
("        int first = Mathf.Abs(MapSystem.Index + 1) % 4;\n", "        int first = 0;                                   // the harbour sits on the east edge (falls back to the other sides)\n"),
("Random.InitState(1000 + MapSystem.Index);", "Random.InitState(1000);"),
("        var parent = MapSystem.Current != null && MapSystem.Current.Obj != null ? MapSystem.Current.Obj.transform : null;\n        if (parent != null) transform.SetParent(parent, true);\n", "")])
patch('Shrine.cs', [("(Kind)((i + MapSystem.Index) % 5)", "(Kind)(i % 5)"), ("Safe to call again after a map change.", "Safe to call again.")])
patch('Tutorial.cs', [("(MapSystem forces the original map while the tutorial is active) ", "")])
patch('Arena.cs', [("; MapSystem calls SetBounds() when the map changes.", ".")])
patch('World.cs', [("/// <summary>The active map changed: drop the old chests / portal, rescan navigation, and place everything again on the new map.</summary>", "/// <summary>Drop the old chests / portal, rescan navigation, and place everything again.</summary>")])
print('DONE')

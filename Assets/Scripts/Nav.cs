using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Navigation on the built map: finds the walkable ground (Layer 1 grass/stone minus walls),
/// keeps only the area connected to the player's start, gives enemies a path toward the player,
/// and finds free spots for spawning, the chests and the portal.
/// </summary>
public static class Nav
{
    static Tilemap ground, stone, wall;
    static Tilemap[] allMaps;
    static readonly HashSet<Vector2Int> walkable = new HashSet<Vector2Int>();
    static readonly List<Tilemap> groundMaps = new List<Tilemap>();
    static readonly List<Tilemap> wallMaps = new List<Tilemap>();
    static readonly Dictionary<Vector2Int, int> dist = new Dictionary<Vector2Int, int>();
    static readonly List<Vector2> spawnCells = new List<Vector2>();
    struct Spot { public Vector2 pos; public string layer; }
    static readonly List<Spot> upperCells = new List<Spot>();     // open floor on the raised levels (Layer 2 / Layer 3)
    static readonly RaycastHit2D[] hits = new RaycastHit2D[16];
    static readonly Vector2Int[] Dir4 = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
    static readonly Vector2Int[] Dir8 =
    {
        new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1),
        new Vector2Int(1,1), new Vector2Int(1,-1), new Vector2Int(-1,1), new Vector2Int(-1,-1)
    };
    static Vector2Int lastPlayerCell = new Vector2Int(int.MinValue, 0);
    static readonly Dictionary<Vector2Int, int> dist2 = new Dictionary<Vector2Int, int>();   // co-op: distance-to-partner field
    static Vector2Int lastCell2 = new Vector2Int(int.MinValue, 0);
    struct Stair { public Vector2 pos; public int upper, lower; public Dictionary<Vector2Int, int> field; }
    static readonly List<Stair> stairs = new List<Stair>();
    /// <summary>Where the player stood when the map was scanned: fixed reference for the portal.</summary>
    public static Vector2 InitialStart;

    public static bool Ready { get { return ground != null && walkable.Count > 0; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        ground = stone = wall = null; allMaps = null;
        walkable.Clear(); dist.Clear(); dist2.Clear(); spawnCells.Clear(); upperCells.Clear(); stairs.Clear();
        lastPlayerCell = new Vector2Int(int.MinValue, 0); lastCell2 = new Vector2Int(int.MinValue, 0);
    }

    static Tilemap Find(string n)
    {
        foreach (var t in Object.FindObjectsByType<Tilemap>())
            if (t.name == n) return t;
        return null;
    }

    static bool IsGround(Vector2Int c)
    {
        Vector3 wp = ground.GetCellCenterWorld(new Vector3Int(c.x, c.y, 0));
        for (int i = 0; i < groundMaps.Count; i++) if (groundMaps[i].HasTile(groundMaps[i].WorldToCell(wp))) return true;
        return false;
    }

    static bool IsWall(Vector2Int c)
    {
        Vector3 wp = ground.GetCellCenterWorld(new Vector3Int(c.x, c.y, 0));
        for (int i = 0; i < wallMaps.Count; i++) if (wallMaps[i].HasTile(wallMaps[i].WorldToCell(wp))) return true;
        return false;
    }

    public static Vector2Int CellOf(Vector2 p)
    {
        Vector3Int c = ground.WorldToCell(p);
        return new Vector2Int(c.x, c.y);
    }

    static Vector2 CenterOf(Vector2Int c)
    {
        return ground.GetCellCenterWorld(new Vector3Int(c.x, c.y, 0));
    }

    public static void EnsureInit(Vector2 playerPos) { if (!Ready) Init(playerPos); }

    /// <summary>True if any map tile exists under this world position (false = empty void outside the map).</summary>
    public static bool IsOverMap(Vector2 p)
    {
        if (allMaps == null || ground == null) return true;
        Vector3Int c = ground.WorldToCell(p);
        for (int i = 0; i < allMaps.Length; i++)
            if (allMaps[i] != null && allMaps[i].HasTile(c)) return true;
        return false;
    }

    static bool PropBlocked(Vector2Int c, ContactFilter2D f, Collider2D[] buf)
    {
        int n = Physics2D.OverlapCircle(CenterOf(c), 0.32f, f, buf);
        for (int i = 0; i < n; i++)
        {
            Collider2D col = buf[i];
            if (col == null) continue;
            if (col.GetComponentInParent<Enemy>() != null || col.GetComponentInParent<PlayerMovement>() != null || col.GetComponentInParent<TenRing>() != null) continue;
            return true;
        }
        return false;
    }

    /// <summary>Call once after the scene loads (player position = flood-fill start). Covers ALL levels, minus walls and solid props.</summary>
    public static void Init(Vector2 playerPos)
    {
        Physics2D.SyncTransforms();
        walkable.Clear(); dist.Clear(); spawnCells.Clear(); upperCells.Clear(); groundMaps.Clear(); wallMaps.Clear(); stairs.Clear();
        InitialStart = playerPos;
        ground = Find("Layer 1 - Grass");
        stone = Find("Layer 1 - Stone Ground");
        wall = Find("Layer 1 - Wall");
        allMaps = Object.FindObjectsByType<Tilemap>();
        if (ground == null) { Debug.LogWarning("[Nav] 'Layer 1 - Grass' tilemap not found; enemies will chase in straight lines."); return; }
        foreach (var t in allMaps)
        {
            if (t.name.Contains("Shadow")) continue;
            if (t.name.Contains("Wall")) wallMaps.Add(t);
            else if (t.name.Contains("Grass") || t.name.Contains("Ground")) groundMaps.Add(t);
        }
        var f = new ContactFilter2D(); f.useTriggers = false; f.useLayerMask = false;
        var buf = new Collider2D[8];

        Vector2Int start = CellOf(playerPos);
        var q = new Queue<Vector2Int>();
        walkable.Add(start); q.Enqueue(start);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in Dir4)
            {
                var n = c + d;
                if (walkable.Contains(n)) continue;
                if (!IsGround(n) || IsWall(n) || PropBlocked(n, f, buf)) continue;
                walkable.Add(n); q.Enqueue(n);
            }
        }
        foreach (var c in walkable)
        {
            var v = new Vector3Int(c.x, c.y, 0);
            if (ground.HasTile(v) || (stone != null && stone.HasTile(v))) spawnCells.Add(CenterOf(c));   // spawns/chests stay on the main level
        }
        spawnCells.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));   // same order every run
        BuildAvoid();

        // raised floors: every floor tile of Layer 2 / Layer 3 that is not a wall
        foreach (var t in groundMaps)
        {
            if (!t.name.StartsWith("Layer 2") && !t.name.StartsWith("Layer 3")) continue;
            var trr = t.GetComponent<TilemapRenderer>();
            string ln = trr != null ? trr.sortingLayerName : t.name.Substring(0, 7);
            if (LayerMask.NameToLayer(ln) < 0) continue;
            var b = t.cellBounds;
            for (int x = b.xMin; x < b.xMax; x++)
                for (int y = b.yMin; y < b.yMax; y++)
                {
                    var tc = new Vector3Int(x, y, 0);
                    if (!t.HasTile(tc)) continue;
                    Vector3 wp = t.GetCellCenterWorld(tc);
                    Vector2Int gc = CellOf(wp);
                    if (IsWall(gc)) continue;
                    upperCells.Add(new Spot { pos = wp, layer = ln });
                }
        }

        // stairs: a flood-fill field per staircase so enemies on another level can walk to it
        foreach (var st in Object.FindObjectsByType<Cainos.PixelArtTopDown_Basic.StairsLayerTrigger>())
        {
            var c2 = st.GetComponent<Collider2D>();
            Vector2 sp = c2 != null ? (Vector2)c2.bounds.center : (Vector2)st.transform.position;
            var field = new Dictionary<Vector2Int, int>();
            Vector2Int seed = CellOf(sp), s2;
            if (walkable.Contains(seed)) s2 = seed; else if (!NearestIn(seed, 4, false, out s2)) continue;
            Flood(s2, field, 45);
            stairs.Add(new Stair { pos = sp, upper = LayerMask.NameToLayer(st.layerUpper), lower = LayerMask.NameToLayer(st.layerLower), field = field });
        }
    }

    static bool NearestIn(Vector2Int c, int r, bool useDist, out Vector2Int found)
    {
        for (int rad = 1; rad <= r; rad++)
            for (int dx = -rad; dx <= rad; dx++)
                for (int dy = -rad; dy <= rad; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != rad) continue;
                    var n = new Vector2Int(c.x + dx, c.y + dy);
                    if (useDist ? dist.ContainsKey(n) : walkable.Contains(n)) { found = n; return true; }
                }
        found = c; return false;
    }

    /// <summary>Rebuild the distance-to-player map (8-way flood fill) when the player changes cell.</summary>
    public static void Refresh(Vector2 playerPos)
    {
        if (!Ready) return;
        Vector2Int pc = CellOf(playerPos);
        if (pc == lastPlayerCell) return;
        lastPlayerCell = pc;

        dist.Clear();
        Vector2Int seed = pc;
        if (!walkable.Contains(seed) && !NearestIn(pc, 4, false, out seed)) return;
        Flood(seed, dist, 70);
    }

    /// <summary>Co-op: the same flood fill, centred on the partner, so enemies can path to either hero.</summary>
    public static void RefreshPartner(Vector2 pos)
    {
        if (!Ready) return;
        Vector2Int pc = CellOf(pos);
        if (pc == lastCell2) return;
        lastCell2 = pc;
        dist2.Clear();
        Vector2Int seed = pc;
        if (!walkable.Contains(seed) && !NearestIn(pc, 4, false, out seed)) return;
        Flood(seed, dist2, 70);
    }

    public static Vector2 DirectionPartner(Vector2 pos, Vector2 target, float radius) { return DirField(dist2, pos, target, radius); }

    static void Flood(Vector2Int seed, Dictionary<Vector2Int, int> d, int limit = 0)
    {
        var q = new Queue<Vector2Int>();
        d[seed] = 0; q.Enqueue(seed);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            int dc = d[c];
            if (limit > 0 && dc >= limit) continue;
            foreach (var dir in Dir8)
            {
                var n = c + dir;
                if (d.ContainsKey(n) || !walkable.Contains(n)) continue;
                if (dir.x != 0 && dir.y != 0 && (!walkable.Contains(c + new Vector2Int(dir.x, 0)) || !walkable.Contains(c + new Vector2Int(0, dir.y)))) continue;
                d[n] = dc + 1; q.Enqueue(n);
            }
        }
    }

    static bool LineClear(Vector2 a, Vector2 b, float radius)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        var f = new ContactFilter2D();
        f.useTriggers = false;
        f.useLayerMask = false;
        int n = Physics2D.CircleCast(a, radius, d / len, f, hits, len);
        for (int i = 0; i < n; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == null) continue;
            if (c.GetComponentInParent<Enemy>() != null) continue;
            if (c.GetComponentInParent<PlayerMovement>() != null) continue;
            if (c.GetComponentInParent<TenRing>() != null) continue;
            if (c.GetComponentInParent<RemoteBody>() != null) continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Direction an enemy at `pos` should walk to reach `target`: straight if the way is clear, otherwise along the
    /// real path (flood-fill gradient), aiming at the furthest path point it can already see (string pulling).
    /// </summary>
    public static Vector2 Direction(Vector2 pos, Vector2 target, float radius) { return DirField(dist, pos, target, radius); }

    /// <summary>Direction toward the nearest staircase that leads from `layer` up (or down). False if there is none.</summary>
    public static bool StairDirection(Vector2 pos, int layer, bool up, float radius, out Vector2 dir)
    {
        dir = Vector2.zero;
        if (!Ready || stairs.Count == 0) return false;
        Vector2Int c = CellOf(pos);
        int bi = -1; float best = float.MaxValue;
        for (int i = 0; i < stairs.Count; i++)
        {
            if ((up ? stairs[i].lower : stairs[i].upper) != layer) continue;
            int d;
            float score = stairs[i].field.TryGetValue(c, out d) ? d : 1000f + (stairs[i].pos - pos).sqrMagnitude;
            if (score < best) { best = score; bi = i; }
        }
        if (bi < 0) return false;
        dir = DirField(stairs[bi].field, pos, stairs[bi].pos, radius);
        return true;
    }

    static Vector2 DirField(Dictionary<Vector2Int, int> dist, Vector2 pos, Vector2 target, float radius)
    {
        Vector2 direct = (target - pos);
        if (direct.sqrMagnitude < 0.0001f) return Vector2.zero;
        direct.Normalize();
        if (!Ready || dist.Count == 0) return direct;
        if (LineClear(pos, target, radius)) return direct;

        Vector2Int c = CellOf(pos);
        int cur;
        if (!dist.TryGetValue(c, out cur))
        {
            Vector2Int near;
            if (!NearestIn(c, 3, true, out near)) return direct;
            c = near; cur = dist[c];
        }

        Vector2Int cell = c;
        Vector2 aim = Vector2.zero;
        bool haveAim = false;
        for (int step = 0; step < 16 && cur > 0; step++)
        {
            Vector2Int bestCell = cell; int best = cur;
            foreach (var d in Dir8)
            {
                var n = cell + d;
                int dn;
                if (!dist.TryGetValue(n, out dn) || dn >= best) continue;
                if (d.x != 0 && d.y != 0 && (!walkable.Contains(cell + new Vector2Int(d.x, 0)) || !walkable.Contains(cell + new Vector2Int(0, d.y)))) continue;
                best = dn; bestCell = n;
            }
            if (bestCell == cell) break;
            cell = bestCell; cur = best;
            Vector2 p = CenterOf(cell);
            if (!haveAim) { aim = p; haveAim = true; }
            else if (LineClear(pos, p, radius * 0.9f)) aim = p;
            else if (step >= 3) break;
        }
        if (!haveAim) return direct;
        Vector2 v = aim - pos;
        return v.sqrMagnitude < 0.0001f ? direct : v.normalized;
    }

    /// <summary>A free walkable cell whose distance from `target` is within [minD, maxD].</summary>
    public static bool TryFreeCellNear(Vector2 target, float minD, float maxD, out Vector2 point)
    {
        point = Vector2.zero;
        if (!Ready || spawnCells.Count == 0) return false;
        var f = new ContactFilter2D(); f.useTriggers = false; f.useLayerMask = false;
        var buf = new Collider2D[4];
        for (int i = 0; i < 400; i++)            // random sampling: the map is huge, never scan every cell
        {
            Vector2 p = spawnCells[Random.Range(0, spawnCells.Count)];
            float d = Vector2.Distance(p, target);
            if (d < minD || d > maxD) continue;
            if (Physics2D.OverlapCircle(p, 0.7f, f, buf) > 0) continue;
            point = p; return true;
        }
        return false;
    }

    /// <summary>A free spot for pickups: main floor or a raised floor. `layer` is the level's sorting/physics layer name.</summary>
    public static bool TryFreeSpotNear(Vector2 target, float minD, float maxD, out Vector2 point, out string layer)
    {
        layer = GameState.ActorLayer; point = Vector2.zero;
        if (!Ready) return false;
        var f = new ContactFilter2D(); f.useTriggers = false;
        var buf = new Collider2D[4];
        for (int i = 0; i < 80; i++)
        {
            bool up = upperCells.Count > 0 && Random.value < 0.4f;
            Vector2 p; string ln = GameState.ActorLayer;
            if (up) { var s = upperCells[Random.Range(0, upperCells.Count)]; p = s.pos; ln = s.layer; }
            else p = spawnCells[Random.Range(0, spawnCells.Count)];
            float d = Vector2.Distance(p, target);
            if (d < minD || d > maxD) continue;
            f.useLayerMask = up; if (up) f.layerMask = 1 << LayerMask.NameToLayer(ln);
            if (Physics2D.OverlapCircle(p, 0.7f, f, buf) > 0) continue;
            point = p; layer = ln; return true;
        }
        return false;
    }

    /// <summary>Spawn spot for enemies: anywhere off-screen on the main floor or the raised floors.</summary>
    public static bool TryGetSpawnSpot(Vector2 from, float minDist, out Vector2 point, out string layer, out Vector2 onScreenFallback, float maxDist = 26f)
    {
        point = Vector2.zero; layer = GameState.ActorLayer; onScreenFallback = Vector2.zero;
        if (!Ready) return false;
        var f = new ContactFilter2D(); f.useTriggers = false;
        var buf = new Collider2D[4];
        Rect view = CameraRect(1.5f);
        for (int i = 0; i < 120; i++)
        {
            bool up = upperCells.Count > 0 && Random.value < 0.35f;
            Vector2 p; string ln = GameState.ActorLayer;
            if (up) { var s = upperCells[Random.Range(0, upperCells.Count)]; p = s.pos; ln = s.layer; }
            else p = spawnCells[Random.Range(0, spawnCells.Count)];
            if ((p - from).sqrMagnitude < minDist * minDist || (p - from).sqrMagnitude > maxDist * maxDist) continue;
            f.useLayerMask = up; if (up) f.layerMask = 1 << LayerMask.NameToLayer(ln);
            if (Physics2D.OverlapCircle(p, 0.45f, f, buf) > 0) continue;
            if (view.width > 0f && view.Contains(p)) { onScreenFallback = p; continue; }
            point = p; layer = ln;
            return true;
        }
        return false;
    }

    /// <summary>The free walkable cell farthest from `from` (used for the exit portal).</summary>
    public static bool TryFarthestFreeCell(Vector2 from, out Vector2 point) { return TryFarthestFreeCell(from, Vector2.zero, 0f, out point); }

    /// <summary>Farthest free cell from `from`, skipping everything within `avoidR` of `avoid`.</summary>
    public static bool TryFarthestFreeCell(Vector2 from, Vector2 avoid, float avoidR, out Vector2 point)
    {
        point = Vector2.zero;
        if (!Ready) return false;
        var f = new ContactFilter2D(); f.useTriggers = false; f.useLayerMask = false;
        var buf = new Collider2D[4];
        float best = -1f; bool found = false;
        foreach (var p in spawnCells)
        {
            float d = Vector2.Distance(p, from);
            if (d <= best) continue;
            if (avoidR > 0f && Vector2.Distance(p, avoid) < avoidR) continue;
            if (InAvoid(p)) continue;
            if (Physics2D.OverlapCircle(p, 1.0f, f, buf) > 0) continue;
            best = d; point = p; found = true;
        }
        return found;
    }

    /// <summary>Areas under/around trees and bushes (canopies have no collider): portal and chests keep out of them.</summary>
    static readonly List<Rect> avoid = new List<Rect>();

    static bool Hidden(string n)
    {
        return n.IndexOf("Tree", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Bush", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Plant", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void BuildAvoid()
    {
        avoid.Clear();
        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>())
        {
            bool hit = false;
            for (var t = sr.transform; t != null && !hit; t = t.parent) hit = Hidden(t.name);
            if (!hit) continue;
            Bounds b = sr.bounds;
            avoid.Add(new Rect(b.min.x - 1.6f, b.min.y - 1.6f, b.size.x + 3.2f, b.size.y + 3.2f));
        }
    }

    static bool InAvoid(Vector2 p) { foreach (var r in avoid) if (r.Contains(p)) return true; return false; }

    /// <summary>
    /// Nearest open spot to `target`: walkable, outside tree canopies, no solid prop/wall within `clear`, not within `sep` of anything in `taken`,
    /// and (preferably) reachable in a straight line from `from`. Actors (player, enemies, rings) do not count as obstacles.
    /// </summary>
    public static bool TryOpenCellNear(Vector2 target, float maxD, float clear, Vector2 from, List<Vector2> taken, float sep, out Vector2 point)
    {
        point = Vector2.zero;
        if (!Ready) return false;
        var f = new ContactFilter2D(); f.useTriggers = false; f.useLayerMask = false;
        var buf = new Collider2D[10];
        float best = float.MaxValue; bool found = false;
        foreach (var p in spawnCells)
        {
            float d = Vector2.Distance(p, target);
            if (d > maxD || d >= best) continue;
            if (InAvoid(p)) continue;
            bool close = false;
            if (taken != null) foreach (var t in taken) if (Vector2.Distance(t, p) < sep) { close = true; break; }
            if (close) continue;
            int n = Physics2D.OverlapCircle(p, clear, f, buf);
            bool solid = false;
            for (int i = 0; i < n && !solid; i++)
            {
                var c = buf[i];
                if (c == null) continue;
                if (c.GetComponentInParent<Enemy>() != null || c.GetComponentInParent<PlayerMovement>() != null || c.GetComponentInParent<TenRing>() != null) continue;
                solid = true;
            }
            if (solid) continue;
            float score = d + (LineClear(from, p, 0.25f) ? 0f : 3f);
            if (score < best) { best = score; point = p; found = true; }
        }
        return found;
    }

    /// <summary>Roomiest free spot (largest obstacle-free circle) at least `minFrac` of the way to the farthest point; deterministic.</summary>
    public static bool TryRoomyCell(Vector2 from, float minFrac, out Vector2 point)
    {
        point = Vector2.zero;
        if (!Ready || spawnCells.Count == 0) return false;
        var f = new ContactFilter2D(); f.useTriggers = false; f.useLayerMask = false;
        var buf = new Collider2D[2];
        float maxD = 0f;
        foreach (var p in spawnCells) maxD = Mathf.Max(maxD, Vector2.Distance(p, from));
        float minD = maxD * minFrac, bestClear = -1f, bestD = -1f; bool found = false;
        foreach (var p in spawnCells)
        {
            float d = Vector2.Distance(p, from);
            if (d < minD) continue;
            if (InAvoid(p)) continue;
            float clear = 0f;
            for (float rr = 1f; rr <= 7f; rr += 0.75f) { if (Physics2D.OverlapCircle(p, rr, f, buf) > 0) break; clear = rr; }
            if (clear > bestClear + 0.01f || (Mathf.Abs(clear - bestClear) <= 0.01f && d > bestD)) { bestClear = clear; bestD = d; point = p; found = true; }
        }
        return found && bestClear >= 1f;
    }

    static Rect CameraRect(float margin)
    {
        Camera cam = Camera.main;
        if (cam == null) return new Rect(0, 0, 0, 0);
        float h = cam.orthographicSize * 2f, w = h * cam.aspect;
        Vector3 c = cam.transform.position;
        return new Rect(c.x - w / 2f - margin, c.y - h / 2f - margin, w + margin * 2f, h + margin * 2f);
    }

    /// <summary>
    /// A free walkable spot away from `from` and OUTSIDE the camera view, so enemies never pop in on screen.
    /// Returns false if none was found (point stays zero); `onScreenFallback` is a valid but visible spot, if any.
    /// </summary>
    public static bool TryGetSpawnPoint(Vector2 from, float minDist, out Vector2 point, out Vector2 onScreenFallback, float maxDist = 40f)
    {
        point = Vector2.zero;
        onScreenFallback = Vector2.zero;
        if (!Ready) return false;
        var f = new ContactFilter2D();
        f.useTriggers = false;
        f.useLayerMask = false;
        var buf = new Collider2D[4];
        Rect view = CameraRect(1.5f);
        for (int i = 0; i < 200; i++)
        {
            Vector2 p = spawnCells[Random.Range(0, spawnCells.Count)];
            if ((p - from).sqrMagnitude < minDist * minDist || (p - from).sqrMagnitude > maxDist * maxDist) continue;
            if (Physics2D.OverlapCircle(p, 0.45f, f, buf) > 0) continue;
            if (view.width > 0f && view.Contains(p)) { onScreenFallback = p; continue; }
            point = p;
            return true;
        }
        return false;
    }
}

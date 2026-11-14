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
    static readonly RaycastHit2D[] hits = new RaycastHit2D[16];
    static readonly Vector2Int[] Dir4 = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
    static readonly Vector2Int[] Dir8 =
    {
        new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1),
        new Vector2Int(1,1), new Vector2Int(1,-1), new Vector2Int(-1,1), new Vector2Int(-1,-1)
    };
    static Vector2Int lastPlayerCell = new Vector2Int(int.MinValue, 0);

    public static bool Ready { get { return ground != null && walkable.Count > 0; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        ground = stone = wall = null; allMaps = null;
        walkable.Clear(); dist.Clear(); spawnCells.Clear();
        lastPlayerCell = new Vector2Int(int.MinValue, 0);
    }

    static Tilemap Find(string n)
    {
        foreach (var t in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
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
        walkable.Clear(); dist.Clear(); spawnCells.Clear(); groundMaps.Clear(); wallMaps.Clear();
        ground = Find("Layer 1 - Grass");
        stone = Find("Layer 1 - Stone Ground");
        wall = Find("Layer 1 - Wall");
        allMaps = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
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
        var q = new Queue<Vector2Int>();
        dist[seed] = 0; q.Enqueue(seed);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            int dc = dist[c];
            foreach (var d in Dir8)
            {
                var n = c + d;
                if (dist.ContainsKey(n) || !walkable.Contains(n)) continue;
                if (d.x != 0 && d.y != 0 && (!walkable.Contains(c + new Vector2Int(d.x, 0)) || !walkable.Contains(c + new Vector2Int(0, d.y)))) continue;
                dist[n] = dc + 1; q.Enqueue(n);
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
            return false;
        }
        return true;
    }

    /// <summary>
    /// Direction an enemy at `pos` should walk to reach `target`: straight if the way is clear, otherwise along the
    /// real path (flood-fill gradient), aiming at the furthest path point it can already see (string pulling).
    /// </summary>
    public static Vector2 Direction(Vector2 pos, Vector2 target, float radius)
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
        if (!Ready) return false;
        var f = new ContactFilter2D(); f.useTriggers = false; f.useLayerMask = false;
        var buf = new Collider2D[4];
        var good = new List<Vector2>();
        foreach (var p in spawnCells)
        {
            float d = Vector2.Distance(p, target);
            if (d < minD || d > maxD) continue;
            if (Physics2D.OverlapCircle(p, 0.7f, f, buf) > 0) continue;
            good.Add(p);
        }
        if (good.Count == 0) return false;
        point = good[Random.Range(0, good.Count)];
        return true;
    }

    /// <summary>The free walkable cell farthest from `from` (used for the exit portal).</summary>
    public static bool TryFarthestFreeCell(Vector2 from, out Vector2 point)
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
            if (Physics2D.OverlapCircle(p, 1.0f, f, buf) > 0) continue;
            best = d; point = p; found = true;
        }
        return found;
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
    public static bool TryGetSpawnPoint(Vector2 from, float minDist, out Vector2 point, out Vector2 onScreenFallback)
    {
        point = Vector2.zero;
        onScreenFallback = Vector2.zero;
        if (!Ready) return false;
        var f = new ContactFilter2D();
        f.useTriggers = false;
        f.useLayerMask = false;
        var buf = new Collider2D[4];
        Rect view = CameraRect(1.5f);
        for (int i = 0; i < 60; i++)
        {
            Vector2 p = spawnCells[Random.Range(0, spawnCells.Count)];
            if ((p - from).sqrMagnitude < minDist * minDist) continue;
            if (Physics2D.OverlapCircle(p, 0.45f, f, buf) > 0) continue;
            if (view.width > 0f && view.Contains(p)) { onScreenFallback = p; continue; }
            point = p;
            return true;
        }
        return false;
    }
}

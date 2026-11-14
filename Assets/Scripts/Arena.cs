using UnityEngine;

/// <summary>
/// Closed rectangular arena. Builds four solid, perfectly bouncy walls around the current map's bounds.
/// Created automatically when the scene loads.
/// </summary>
public class Arena : MonoBehaviour
{
    public static Arena Instance { get; private set; }

    // The original map's inner edge: x -12..16, y -13..21.
    public const float MinX = -12f, MinY = -13f, Width = 28f, Height = 34f;
    const float WallThickness = 3f;

    public Rect Bounds { get; private set; }
    public PhysicsMaterial2D BounceMaterial { get; private set; }
    Transform wallsRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Instance != null) return;

        var go = new GameObject("Arena (runtime)");
        var arena = go.AddComponent<Arena>();
        arena.Build();
    }

    void Build()
    {
        Instance = this;
        BounceMaterial = new PhysicsMaterial2D("ArenaBounce") { bounciness = 1f, friction = 0f };
        // the scene's own "Arena Walls" are replaced by walls we rebuild for whichever map is active
        var old = GameObject.Find("Arena Walls");
        if (old != null) old.SetActive(false);
        Rebuild(new Rect(MinX, MinY, Width, Height));
    }

    /// <summary>Move the arena to a new map's bounds.</summary>
    public static void SetBounds(Rect r)
    {
        if (Instance == null) Install();
        if (Instance != null) Instance.Rebuild(r);
    }

    void Rebuild(Rect r)
    {
        Bounds = r;
        if (wallsRoot != null) Destroy(wallsRoot.gameObject);
        wallsRoot = new GameObject("Arena Walls (runtime)").transform;
        wallsRoot.SetParent(transform, false);
        Vector2 center = Bounds.center;
        float hw = r.width / 2f, hh = r.height / 2f, t = WallThickness;
        // Walls sit just outside the inner rectangle so the playable area is exactly the bounds.
        MakeWall("Wall Top",    center + new Vector2(0,  hh + t / 2f), new Vector2(r.width + t * 2f, t));
        MakeWall("Wall Bottom", center + new Vector2(0, -hh - t / 2f), new Vector2(r.width + t * 2f, t));
        MakeWall("Wall Left",   center + new Vector2(-hw - t / 2f, 0), new Vector2(t, r.height));
        MakeWall("Wall Right",  center + new Vector2( hw + t / 2f, 0), new Vector2(t, r.height));
    }

    void MakeWall(string wallName, Vector2 pos, Vector2 size)
    {
        var w = new GameObject(wallName);
        w.transform.SetParent(wallsRoot, false);
        w.transform.position = pos;
        var col = w.AddComponent<BoxCollider2D>();
        col.size = size;
        col.sharedMaterial = BounceMaterial;
    }

    /// <summary>Clamp a point inside the arena, keeping `margin` units from the walls.</summary>
    public Vector2 Clamp(Vector2 p, float margin = 0f)
    {
        return new Vector2(
            Mathf.Clamp(p.x, Bounds.xMin + margin, Bounds.xMax - margin),
            Mathf.Clamp(p.y, Bounds.yMin + margin, Bounds.yMax - margin));
    }
}

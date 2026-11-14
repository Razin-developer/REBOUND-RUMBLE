using UnityEngine;

/// <summary>
/// Closed rectangular arena. Builds four solid, perfectly bouncy walls plus a visible border,
/// centred on the player's start position. Created automatically when the scene loads.
/// Change width/height below (make them equal for a perfect square).
/// </summary>
public class Arena : MonoBehaviour
{
    public static Arena Instance { get; private set; }

    // Matches the "Arena Walls" object in GameScene (inner edge of the walls).
    // Covers the whole built map: x -12..16, y -13..21.
    public const float MinX = -12f, MinY = -13f, Width = 28f, Height = 34f;
    const float WallThickness = 3f;

    public Rect Bounds { get; private set; }
    public PhysicsMaterial2D BounceMaterial { get; private set; }

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
        Bounds = new Rect(MinX, MinY, Width, Height);
        Vector2 center = Bounds.center;

        BounceMaterial = new PhysicsMaterial2D("ArenaBounce") { bounciness = 1f, friction = 0f };

        // Walls already placed in the scene? Then nothing to build.
        if (GameObject.Find("Arena Walls") != null) return;

        float hw = Width / 2f, hh = Height / 2f, t = WallThickness;
        // Walls sit just outside the inner rectangle so the playable area is exactly Width x Height.
        MakeWall("Wall Top",    center + new Vector2(0,  hh + t / 2f), new Vector2(Width + t * 2f, t));
        MakeWall("Wall Bottom", center + new Vector2(0, -hh - t / 2f), new Vector2(Width + t * 2f, t));
        MakeWall("Wall Left",   center + new Vector2(-hw - t / 2f, 0), new Vector2(t, Height));
        MakeWall("Wall Right",  center + new Vector2( hw + t / 2f, 0), new Vector2(t, Height));

    }

    void MakeWall(string wallName, Vector2 pos, Vector2 size)
    {
        var w = new GameObject(wallName);
        w.transform.SetParent(transform, false);
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

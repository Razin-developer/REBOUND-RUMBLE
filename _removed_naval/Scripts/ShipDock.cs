using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The harbour: a pier + the player's ship. You place it yourself: select the "Harbor" object in the scene and move / rotate / scale it
/// directly at the sea. Everything below is tweakable in the Inspector (or from script). Local +X is "towards the land" (where the pier goes).
/// Stand next to the ship (or on the landing spot) and press I (or tap the prompt) to set sail into the sea battle (see Naval).
/// </summary>
public class ShipDock : MonoBehaviour
{
    // ------------------------------------------------------------------ INSPECTOR OPTIONS
    [Header("Parts")]
    public bool showPier = true;
    [Tooltip("The little pond drawn under the ship. Off: you place the harbour on the sea yourself.")]
    public bool showLagoon = false;
    [Tooltip("Wall the lagoon in with a solid ring (only used when Show Lagoon is on).")]
    public bool solidWater = false;

    [Header("Ship")]
    public Vector2 shipOffset = new Vector2(-0.6f, -0.45f);   // local position of the docked ship
    [Tooltip("Local rotation of the docked ship in degrees. 180 = bow points to local -X (out to sea).")]
    public float shipAngle = 180f;
    public float shipScale = 0.85f;
    public bool shipBobs = true;

    [Header("Pier")]
    public Vector2 pierOffset = new Vector2(4.8f, 0.9f);       // local position of the pier
    public float pierAngle = 0f;

    [Header("Where you stand / press I")]
    [Tooltip("Optional: drag an empty child object here and put it on the shore. Overrides Land Offset.")]
    public Transform landingPoint;
    [Tooltip("Local position of the landing spot (where you step off when you come back from the sea).")]
    public Vector2 landOffset = new Vector2(6.8f, 0.9f);
    public float reachShip = 5.2f;                              // press I within this distance of the ship...
    public float reachLand = 3.4f;                              // ...or of the landing spot

    [Header("Drawing")]
    public string sortingLayer = "Layer 1";
    public int sortingOrder = 1;                                // above the ground, below the player (raise it if the sea tiles cover it)
    [Header("Placement")]
    public bool forceWorldPosition = true;                       // move this object to worldPosition when the game starts (ignores a stale scene value)
    public Vector3 worldPosition = new Vector3(-14.7f, 6.74f, -9.54f);
    public float lagoonZ = 0.02f, pierZ = 0f, shipZ = -0.02f;

    [Header("Navigation")]
    [Tooltip("Remove the ground around the harbour from enemy navigation / spawn spots.")]
    public bool carveNavigation = false;
    public float carveRadius = 4f;

    // ------------------------------------------------------------------ state
    public static readonly List<ShipDock> All = new List<ShipDock>();
    public static ShipDock Current;               // the dock the player is standing at (null = none in reach)
    public static string Prompt = "";
    public static Vector2 PromptPos;
    public static bool PromptClickable;
    public static float RepairUntil;              // the ship can't sail again before this time

    const float A = 5.6f, B = 3.1f;               // half axes of the lagoon ring

    public Vector2 LandSpot { get; private set; }
    public Vector2 ShipPos { get { return shipCenter; } }
    Vector2 shipCenter;
    Transform hull, rig;
    SpriteRenderer hullSr, rigSr, waterSr;
    Vector2 hullBase;
    float phase;
    bool built;

    static Transform player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear(); Current = null; Prompt = ""; PromptClickable = false; RepairUntil = 0f; player = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        GameState.OnRunStart += () => { RepairUntil = 0f; };
    }

    /// <summary>Set up every Harbor object in the scene (called when the world is built).</summary>
    public static void Place()
    {
        if (Tutorial.Active) return;
        Current = null; Prompt = ""; PromptClickable = false;
        All.RemoveAll(d => d == null);
        var found = Object.FindObjectsByType<ShipDock>();
        foreach (var d in found)
        {
            if (d == null) continue;
            if (d.forceWorldPosition) d.transform.position = d.worldPosition;
            if (!d.built) d.Build();
            if (!All.Contains(d)) All.Add(d);
            if (d.carveNavigation && Nav.Ready) Nav.CarveOut(d.transform.position, d.carveRadius * Mathf.Max(0.1f, Mathf.Abs(d.transform.lossyScale.x)));
            Debug.Log("[ShipDock] harbour '" + d.name + "' ready at " + d.transform.position + ", landing " + d.LandSpot);
        }
        if (found.Length == 0) Debug.LogWarning("[ShipDock] there is no Harbor object in the scene - add an empty GameObject with the ShipDock component and put it at the sea");
    }

    // ------------------------------------------------------------------ building
    void Build()
    {
        built = true;
        Vector3 land = landingPoint != null ? landingPoint.position : transform.TransformPoint(new Vector3(landOffset.x, landOffset.y, 0f));
        LandSpot = new Vector2(land.x, land.y);

        if (showLagoon) waterSr = Make("Lagoon", NavalArt.Get("harbor"), Vector3.zero, 0f, lagoonZ, 0.97f);
        if (showPier) Make("Pier", NavalArt.Get("pier"), new Vector3(pierOffset.x, pierOffset.y, 0f), pierAngle, pierZ, 1f);

        // the ship
        var h = new GameObject("DockedShip");
        h.transform.SetParent(transform, false);
        hullBase = shipOffset;
        h.transform.localPosition = new Vector3(hullBase.x, hullBase.y, shipZ);
        h.transform.localRotation = Quaternion.Euler(0f, 0f, shipAngle);
        h.transform.localScale = Vector3.one * shipScale;
        hull = h.transform;
        hullSr = Attach(h, NavalArt.Get("ship_player_hull"), sortingOrder);
        var r = new GameObject("Rig");
        r.transform.SetParent(h.transform, false);
        r.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        rig = r.transform;
        rigSr = Attach(r, NavalArt.Get("ship_player_rig"), sortingOrder);
        shipCenter = (Vector2)h.transform.position;

        if (showLagoon && solidWater)
        {
            var col = gameObject.GetComponent<EdgeCollider2D>();
            if (col == null) col = gameObject.AddComponent<EdgeCollider2D>();
            var pts = new Vector2[41];
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f * Mathf.PI * 2f;
                pts[i] = new Vector2(Mathf.Cos(t) * A, Mathf.Sin(t) * B);
            }
            col.points = pts;
            var pgo = GameObject.FindWithTag("Player");
            if (pgo != null) gameObject.layer = pgo.layer;
        }
        phase = Random.value * 6f;
    }

    SpriteRenderer Attach(GameObject g, Sprite s, int order)
    {
        var sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder = order;
        return sr;
    }

    SpriteRenderer Make(string nm, Sprite s, Vector3 local, float rot, float z, float scale)
    {
        var g = new GameObject(nm);
        g.transform.SetParent(transform, false);
        g.transform.localPosition = new Vector3(local.x, local.y, z);
        g.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
        g.transform.localScale = Vector3.one * scale;
        return Attach(g, s, sortingOrder);
    }

    void OnDestroy() { All.Remove(this); if (Current == this) { Current = null; Prompt = ""; PromptClickable = false; } }

    // ------------------------------------------------------------------ editor helpers (select the Harbor to see them)
    void OnDrawGizmosSelected()
    {
        Vector3 land = landingPoint != null ? landingPoint.position : transform.TransformPoint(new Vector3(landOffset.x, landOffset.y, 0f));
        Vector3 ship = transform.TransformPoint(new Vector3(shipOffset.x, shipOffset.y, 0f));
        float s = Mathf.Max(0.1f, Mathf.Abs(transform.lossyScale.x));
        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.9f);
        Gizmos.DrawWireSphere(land, 0.35f);
        Gizmos.DrawWireSphere(land, reachLand);
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(ship, 0.35f);
        Gizmos.DrawWireSphere(ship, reachShip * Mathf.Max(0.7f, s));
        Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.9f);
        Gizmos.DrawLine(transform.position, transform.position + transform.right * 2f * s);   // +X = towards the land
    }

    // ------------------------------------------------------------------ gameplay
    public void Rest(float seconds) { RepairUntil = Mathf.Max(RepairUntil, Time.time + seconds); }

    public void Sail()
    {
        string why = Naval.WhyNot();
        if (why != null)
        {
            FloatingText.Add(shipCenter + new Vector2(0f, 2.6f), why, new Color(1f, 0.7f, 0.4f), 12, 1.8f);
            Sfx.PlayUI("click", 0.5f, 0.7f);
            return;
        }
        Sfx.Play("naval_horn", 0.8f, 1f);
        GameUI.Transit("SETTING SAIL", () => Naval.Begin(this));
    }

    void Update()
    {
        if (!built) return;
        // idle animation: the ship bobs and the sails sway
        phase += Time.deltaTime;
        if (hull != null)
        {
            float b = shipBobs ? 1f : 0f;
            hull.localPosition = new Vector3(hullBase.x + Mathf.Sin(phase * 0.9f) * 0.06f * b, hullBase.y + Mathf.Sin(phase * 1.3f) * 0.09f * b, hull.localPosition.z);
            hull.localRotation = Quaternion.Euler(0f, 0f, shipAngle + Mathf.Sin(phase * 1.1f) * 1.4f * b);
            shipCenter = (Vector2)hull.position;
        }
        if (rig != null) rig.localRotation = Quaternion.Euler(0f, 0f, shipBobs ? Mathf.Sin(phase * 1.7f) * 1.2f : 0f);
        bool repairing = Time.time < RepairUntil;
        if (hullSr != null) { var c = repairing ? new Color(0.55f, 0.55f, 0.6f) : Color.white; hullSr.color = c; rigSr.color = c; }
        if (waterSr != null) waterSr.color = Color.Lerp(Color.white, new Color(0.92f, 0.98f, 1f), 0.5f + 0.5f * Mathf.Sin(phase * 0.8f));

        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) player = p.transform; }
        if (player == null || Naval.Active || !GameState.Active || GameState.Over || Cinematic.Active)
        {
            if (Current == this) { Current = null; Prompt = ""; PromptClickable = false; }
            return;
        }
        Vector2 pp = player.position;
        float s = Mathf.Max(0.7f, Mathf.Abs(transform.lossyScale.x));
        bool near = Vector2.Distance(pp, shipCenter) < reachShip * s || Vector2.Distance(pp, LandSpot) < reachLand;
        if (!near)
        {
            if (Current == this) { Current = null; Prompt = ""; PromptClickable = false; }
            return;
        }
        Current = this;
        string why = Naval.WhyNot();
        PromptPos = shipCenter + new Vector2(0f, 2.4f);
        if (why == null) { Prompt = "PRESS I  -  SET SAIL"; PromptClickable = true; }
        else { Prompt = why; PromptClickable = false; }
        if (why == null && InputHub.Pressed(KeyCode.I) && GUIUtility.keyboardControl == 0) Sail();
    }
}

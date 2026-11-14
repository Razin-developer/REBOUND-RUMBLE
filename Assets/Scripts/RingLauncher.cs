using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Auto-attached to the Player. Owns the rings.
/// Click / hold (or Space): throw ONE ring at a time with a short delay - all 10 can be out at once.
/// Right click / Q: VOLLEY - throw 5 rings together in a fan. Strong, so it has a long cooldown.
/// </summary>
public class RingLauncher : MonoBehaviour
{
    public const int MaxOut = TenRing.Total;
    public const int VolleySize = 5;
    public const float VolleyCooldown = 4f;
    static float volleyReadyAt;
    public static int Throws, Volleys;   // counters (used by the tutorial)

    public static float VolleyLeft { get { return Mathf.Max(0f, volleyReadyAt - Time.time); } }
    public static bool VolleyReady { get { return VolleyLeft <= 0f && TenRing.ReadyCount >= VolleySize; } }

    public float throwDelay = 0.3f;
    float nextThrow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { volleyReadyAt = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null && player.GetComponent<RingLauncher>() == null)
            player.AddComponent<RingLauncher>();
    }

    void Start()
    {
        for (int i = 0; i < TenRing.Total; i++) TenRing.Create(transform, i);
        GameState.OnRunStart += OnRunStart;
    }

    void OnDestroy() { GameState.OnRunStart -= OnRunStart; }

    void OnRunStart() { nextThrow = Time.time + 0.5f; volleyReadyAt = 0f; } // ignore the click that pressed PLAY / RETRY

    void Update()
    {
        if (GameState.Over)
        {
            foreach (var r in TenRing.All) if (r != null && r.State != TenRing.RingState.Orbit) r.ForceOrbit();
            return;
        }
        if (!GameState.Active || Time.time < nextThrow) return;

        if (InputHub.RightHeld || InputHub.Held(KeyCode.Q))
        {
            if (VolleyReady) Volley();
            return;
        }

        if (!(InputHub.MouseHeld || InputHub.Held(KeyCode.Space))) return;
        TenRing ring = TenRing.FirstReady();
        if (ring == null) return;

        nextThrow = Time.time + throwDelay;
        Vector2 dir = ChooseDirection(0f);
        PlayerVisual.Throw(dir);
        StartCoroutine(ReleaseSingle(ring, dir));
    }

    IEnumerator ReleaseSingle(TenRing ring, Vector2 dir)
    {
        yield return new WaitForSeconds(0.12f);   // matches the wind-up pose
        if (ring == null || !GameState.Active || ring.State != TenRing.RingState.Orbit) yield break;
        ring.Launch(dir);
        Throws++;
        Sfx.Play("ring_throw", 0.7f, 0.9f + Random.value * 0.3f);
    }

    void Volley()
    {
        var rings = new List<TenRing>();
        foreach (var r in TenRing.All)
            if (r != null && r.State == TenRing.RingState.Orbit && rings.Count < VolleySize) rings.Add(r);
        if (rings.Count < VolleySize) return;

        volleyReadyAt = Time.time + VolleyCooldown;
        Volleys++;
        nextThrow = Time.time + 0.7f;
        Vector2 baseDir = ChooseDirection(0f);
        PlayerVisual.Throw(baseDir);
        Fx.Slash((Vector2)transform.position + new Vector2(0f, 0.5f) + baseDir * 0.8f, baseDir, new Color(1f, 0.6f, 0.15f, 1f), 1.5f, 0.3f);
        Vfx.Shockwave((Vector2)transform.position + new Vector2(0f, 0.45f), new Color(1f, 0.7f, 0.2f), 1.8f, 0.4f);
        CameraRig.Shake(0.18f, 0.2f);
        StartCoroutine(ReleaseVolley(rings, Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg));
    }

    IEnumerator ReleaseVolley(List<TenRing> rings, float baseAngle)
    {
        yield return new WaitForSeconds(0.14f);
        if (!GameState.Active) yield break;
        Sfx.Play("ring_throw", 0.9f, 0.7f);
        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];
            if (r == null || r.State != TenRing.RingState.Orbit) continue;
            float a = (baseAngle + (i - (rings.Count - 1) / 2f) * 22f + Random.Range(-5f, 5f)) * Mathf.Deg2Rad;
            r.Launch(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
        }
    }

    /// <summary>Roughly toward the closest enemy (with a random offset up to `spread` degrees), or anywhere if none.</summary>
    Vector2 ChooseDirection(float spread)
    {
        Enemy nearest = null;
        float bd = float.MaxValue;
        foreach (var e in Enemy.All)
        {
            if (e == null) continue;
            float d = ((Vector2)e.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (d < bd) { bd = d; nearest = e; }
        }
        if (nearest == null) return Random.insideUnitCircle.normalized;
        Vector2 toward = ((Vector2)nearest.transform.position - (Vector2)transform.position).normalized;
        float a = Random.Range(-spread, spread) * Mathf.Deg2Rad;
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector2(toward.x * c - toward.y * s, toward.x * s + toward.y * c);
    }
}

using UnityEngine;

/// <summary>
/// Added to the Main Camera. Slowly pans over the map behind the main menu, and adds screen shake in play.
/// The Cainos CameraFollow keeps doing the normal follow while playing.
/// </summary>
[DefaultExecutionOrder(-100)]
public class CameraRig : MonoBehaviour
{
    static CameraRig instance;
    Cainos.PixelArtTopDown_Basic.CameraFollow follow;
    Vector3 baseOffset = new Vector3(0f, 0f, -10f);
    Vector3 shakeOffset;
    float shakeAmp, shakeLeft, shakeTotal;
    Transform player;
    bool wasMenu = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.GetComponent<CameraRig>() == null) cam.gameObject.AddComponent<CameraRig>();
    }

    public static void Shake(float amplitude, float seconds)
    {
        if (instance == null || !GameSettings.ScreenShake) return;
        float current = instance.shakeAmp * (instance.shakeLeft / Mathf.Max(0.01f, instance.shakeTotal));
        if (amplitude < current) return;
        instance.shakeAmp = amplitude; instance.shakeLeft = instance.shakeTotal = seconds;
    }

    float baseSize;
    Camera camRef;

    // keep at least ~8 world units visible horizontally so tall/narrow screens (phones in portrait, odd windows) still see around the player
    void FitAspect()
    {
        if (camRef == null) camRef = GetComponent<Camera>();
        if (camRef == null || !camRef.orthographic || baseSize <= 0f) return;
        float need = 8f * GS.S / Mathf.Max(0.3f, camRef.aspect);
        camRef.orthographicSize = Mathf.Max(baseSize, need);
    }

    void Awake()
    {
        instance = this;
        var cam = GetComponent<Camera>();
        if (cam != null && cam.orthographic) { cam.orthographicSize *= GS.S; baseSize = cam.orthographicSize; }
        follow = GetComponent<Cainos.PixelArtTopDown_Basic.CameraFollow>();
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        if (player != null) baseOffset = transform.position - player.position;
        GameState.OnRunStart += SnapToPlayer;
    }

    void OnDestroy() { GameState.OnRunStart -= SnapToPlayer; if (instance == this) instance = null; }

    void SnapToPlayer()
    {
        if (player == null) return;
        transform.position = player.position + baseOffset;
        shakeOffset = Vector3.zero; shakeLeft = 0f;
    }

    void Update()
    {
        transform.position -= shakeOffset; // undo last frame's shake before the follow script moves us
        shakeOffset = Vector3.zero;

        if (GameState.Mode == GameMode.Menu)
        {
            if (follow != null) follow.enabled = false;
            float t = Time.unscaledTime;
            Vector3 center = new Vector3(2.5f + Mathf.Cos(t * 0.13f) * 4f, 4f + Mathf.Sin(t * 0.09f) * 6f, baseOffset.z);
            transform.position = Vector3.Lerp(transform.position, center, 1.5f * Time.unscaledDeltaTime);
            wasMenu = true;
        }
        else if (wasMenu)
        {
            wasMenu = false;
            if (follow != null) follow.enabled = true;
        }
    }

    void LateUpdate()
    {
        FitAspect();
        if (GameState.Mode != GameMode.Playing || shakeLeft <= 0f) return;
        shakeLeft -= Time.deltaTime;
        float k = Mathf.Clamp01(shakeLeft / Mathf.Max(0.01f, shakeTotal));
        Vector2 r = Random.insideUnitCircle * shakeAmp * k;
        shakeOffset = new Vector3(r.x, r.y, 0f);
        transform.position += shakeOffset;
    }
}

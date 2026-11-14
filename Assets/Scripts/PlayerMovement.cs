using UnityEngine;

/// <summary>
/// Top-down WASD / arrow-key movement for REBOUND RUMBLE.
/// Installs itself on the GameObject tagged "Player" when the scene loads (no scene edit needed),
/// replaces the Cainos controller, and keeps the player inside the Arena.
/// Animator parameters: Direction (0=S, 1=N, 2=E, 3=W) and IsMoving.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;

    Rigidbody2D rb;
    Animator anim;
    Vector2 input;
    Vector2 knock;

    public void Push(Vector2 v) { knock = v; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[PlayerMovement] No GameObject tagged 'Player' in the scene.");
            return;
        }
        if (player.GetComponent<PlayerMovement>() == null)
            player.AddComponent<PlayerMovement>();
    }

    void Awake()
    {
        anim = GetComponent<Animator>();
        // keep the scale authored on the Player prefab/scene (e.g. X 1.25 = wider sprite) and apply the global size factor on top;
        // if nobody widened it in the editor yet (x == y), widen it here so the result is the same either way
        Vector3 ls = transform.localScale;
        if (Mathf.Approximately(ls.x, ls.y)) ls.x *= 1.25f;
        transform.localScale = new Vector3(ls.x * GS.S, ls.y * GS.S, 1f);

        // Switch off the Cainos controller so two scripts don't fight over velocity.
        var cainos = GetComponent<Cainos.PixelArtTopDown_Basic.TopDownCharacterController>();
        if (cainos != null) cainos.enabled = false;

        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    void Update()
    {
        input = GameState.Active ? InputHub.Move() : Vector2.zero;

        if (anim != null)
        {
            int dir = -1;
            if (input.x < 0) dir = 3; else if (input.x > 0) dir = 2;
            if (input.y > 0) dir = 1; else if (input.y < 0) dir = 0;
            if (dir >= 0) anim.SetInteger("Direction", dir);
            anim.SetBool("IsMoving", input.sqrMagnitude > 0f);
        }
    }

    void FixedUpdate()
    {
        knock = Vector2.Lerp(knock, Vector2.zero, 10f * Time.fixedDeltaTime);
        rb.linearVelocity = (input.normalized * speed * (Guard.Active ? Guard.MoveMul : 1f) + knock) * GS.S;

        // Safety net: never leave the arena, even if a collider is missed.
        if (Arena.Instance != null)
        {
            Vector2 p = Arena.Instance.Clamp(rb.position, 0.4f);
            if (p != rb.position) rb.position = p;
        }
    }
}

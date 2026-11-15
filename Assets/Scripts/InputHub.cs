using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One place for keyboard + mouse input. Reads Unity's GUI events (which work reliably in the Game view)
/// AND the legacy Input Manager, so controls work either way.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class InputHub : MonoBehaviour
{
    static InputHub instance;
    static readonly HashSet<KeyCode> guiHeld = new HashSet<KeyCode>();
    static readonly HashSet<KeyCode> polledHeld = new HashSet<KeyCode>();
    static readonly HashSet<KeyCode> pendingPressed = new HashSet<KeyCode>();
    static readonly HashSet<KeyCode> pressed = new HashSet<KeyCode>();
    static bool guiMouseHeld, pendingMouseDown, mouseDown, haveGuiMouse, guiRightHeld, pendingRightDown, rightDown;
    static Vector2 guiMouse;

    static readonly KeyCode[] Polled =
    {
        KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
        KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
        KeyCode.Space, KeyCode.Tab, KeyCode.Escape, KeyCode.R, KeyCode.Q, KeyCode.E,
        KeyCode.I, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.LeftControl, KeyCode.RightControl
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        guiHeld.Clear(); polledHeld.Clear(); pendingPressed.Clear(); pressed.Clear();
        guiMouseHeld = pendingMouseDown = mouseDown = haveGuiMouse = guiRightHeld = pendingRightDown = rightDown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("InputHub");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<InputHub>();
    }

    // ---------- public API ----------
    public static bool Held(KeyCode k) { return guiHeld.Contains(k) || polledHeld.Contains(k); }
    public static bool Pressed(KeyCode k) { return pressed.Contains(k); }
    public static bool MouseHeld { get; private set; }
    public static bool MouseDown { get { return mouseDown; } }
    public static bool RightHeld { get; private set; }
    public static bool RightDown { get { return rightDown; } }

    public static Vector2 MouseScreen
    {
        get
        {
            if (haveGuiMouse)
            {
                return new Vector2(guiMouse.x, Screen.height - guiMouse.y);
            }
            return Input.mousePosition;
        }
    }

    public static Vector2 Move()
    {
        if (TouchControls.Move.sqrMagnitude > 0.001f) return TouchControls.Move;
        float x = 0f, y = 0f;
        if (Held(KeyCode.A) || Held(KeyCode.LeftArrow)) x -= 1f;
        if (Held(KeyCode.D) || Held(KeyCode.RightArrow)) x += 1f;
        if (Held(KeyCode.S) || Held(KeyCode.DownArrow)) y -= 1f;
        if (Held(KeyCode.W) || Held(KeyCode.UpArrow)) y += 1f;
        return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
    }

    // ---------- internals ----------
    void Update()
    {
        pressed.Clear();
        foreach (var k in pendingPressed) pressed.Add(k);
        pendingPressed.Clear();

        mouseDown = pendingMouseDown;
        pendingMouseDown = false;

        polledHeld.Clear();
        foreach (var k in Polled)
        {
            if (Input.GetKey(k)) polledHeld.Add(k);
            if (Input.GetKeyDown(k)) pressed.Add(k);
        }
        if (Input.GetMouseButtonDown(0)) mouseDown = true;
        MouseHeld = guiMouseHeld || Input.GetMouseButton(0);
        rightDown = pendingRightDown; pendingRightDown = false;
        if (Input.GetMouseButtonDown(1)) rightDown = true;
        RightHeld = guiRightHeld || Input.GetMouseButton(1);
    }

    void OnGUI()
    {
        Event e = Event.current;
        guiMouse = e.mousePosition;
        haveGuiMouse = true;

        if (e.isKey && e.keyCode != KeyCode.None)
        {
            if (e.type == EventType.KeyDown) { if (guiHeld.Add(e.keyCode)) pendingPressed.Add(e.keyCode); }
            else if (e.type == EventType.KeyUp) guiHeld.Remove(e.keyCode);
        }
        else if (e.button == 0)
        {
            if (e.type == EventType.MouseDown) { guiMouseHeld = true; pendingMouseDown = true; }
            else if (e.type == EventType.MouseUp) guiMouseHeld = false;
        }
        else if (e.button == 1)
        {
            if (e.type == EventType.MouseDown) { guiRightHeld = true; pendingRightDown = true; }
            else if (e.type == EventType.MouseUp) guiRightHeld = false;
        }
    }

    void OnApplicationFocus(bool focus)
    {
        if (!focus) { guiHeld.Clear(); guiMouseHeld = false; guiRightHeld = false; }
    }
}

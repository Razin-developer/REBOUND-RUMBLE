using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Bridge to the browser (WebGL): native text prompt for mobile browsers; harmless elsewhere.</summary>
public static class WebBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern IntPtr RR_Prompt(string msg, string def);
    public static string Prompt(string msg, string def)
    {
        IntPtr p = RR_Prompt(msg, def);
        return p == IntPtr.Zero ? def : Marshal.PtrToStringUTF8(p);
    }
#else
    public static string Prompt(string msg, string def) { return def; }
#endif
    /// <summary>True in a phone/tablet browser: IMGUI text fields cannot open a keyboard there.</summary>
    public static bool IsMobileWeb { get { return Application.platform == RuntimePlatform.WebGLPlayer && Application.isMobilePlatform; } }
}

/// <summary>Landscape lock on phones, a "rotate your device" screen in portrait (the game pauses meanwhile), and platform tuning.</summary>
public class Responsive : MonoBehaviour
{
    static bool autoPaused;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { autoPaused = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("Responsive");
        DontDestroyOnLoad(go);
        go.AddComponent<Responsive>();
        if (Application.isMobilePlatform)
        {
            Screen.autorotateToPortrait = false; Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true; Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }

    static bool Portrait { get { return Application.isMobilePlatform && Screen.height > Screen.width * 1.05f; } }

    void Update()
    {
        if (Portrait)
        {
            if (GameState.Mode == GameMode.Playing && !GameState.Paused && !GameState.Over) { GameState.SetPaused(true); autoPaused = true; }
        }
        else if (autoPaused)
        {
            autoPaused = false;
            // resume only if nothing else paused it meanwhile (the pause menu stays up so the player resumes on purpose)
        }
    }

    GUIStyle st;
    void OnGUI()
    {
        if (!Portrait) return;
        GUI.depth = -9000;
        if (st == null)
        {
            st = new GUIStyle { font = GameAssets.PixelFont, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            st.normal.textColor = Color.white;
        }
        Matrix4x4 m = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        var o = GUI.color;
        GUI.color = new Color(0.03f, 0.02f, 0.06f, 1f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        st.fontSize = Mathf.Max(14, Screen.width / 18);
        st.normal.textColor = new Color(1f, 0.82f, 0.25f);
        GUI.Label(new Rect(20, Screen.height * 0.30f, Screen.width - 40, Screen.height * 0.2f), "ROTATE YOUR DEVICE", st);
        st.fontSize = Mathf.Max(10, Screen.width / 30);
        st.normal.textColor = new Color(1, 1, 1, 0.75f);
        GUI.Label(new Rect(20, Screen.height * 0.50f, Screen.width - 40, Screen.height * 0.2f), "REBOUND RUMBLE PLAYS IN LANDSCAPE", st);
        GUI.color = o; GUI.matrix = m;
    }
}

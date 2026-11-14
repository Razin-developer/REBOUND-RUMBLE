#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>One-click WebGL build + zip that is ready to upload to itch.io (index.html at the zip root).</summary>
public static class BuildTools
{
    const string ScenePath = "Assets/Scenes/GameScene.unity";

    [MenuItem("REBOUND RUMBLE/1. Add GameScene to Build Settings")]
    public static void AddScene()
    {
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("[Build] GameScene is now in Build Settings.");
    }

    [MenuItem("REBOUND RUMBLE/2. Build WebGL zip for itch.io")]
    public static void BuildWebGL()
    {
        AddScene();
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;   // itch.io-friendly (no server header setup needed)
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.template = "APPLICATION:Default";   // Minimal leaves a small canvas on a white page
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.runInBackground = false;

        // The old build folder is often locked (a local web server, a browser tab or an Explorer window is using it): then build into a fresh folder instead of failing.
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string dir = Path.GetFullPath("Builds/WebGL");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (System.Exception e)
        {
            dir = Path.GetFullPath("Builds/WebGL_" + stamp);
            Debug.LogWarning("[Build] Builds/WebGL is in use (" + e.Message + "). Building into " + dir + " instead.");
        }
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = dir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError("[Build] WebGL build failed. Is the WebGL Build Support module installed (Unity Hub > Installs > Add modules)?");
            return;
        }
        string zip = Path.GetFullPath("Builds/ReboundRumble-itch.zip");
        try { if (File.Exists(zip)) File.Delete(zip); }
        catch (System.Exception) { zip = Path.GetFullPath("Builds/ReboundRumble-itch_" + stamp + ".zip"); }
        System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip, System.IO.Compression.CompressionLevel.Optimal, false);
        Debug.Log("[Build] Upload this to itch.io (kind of project: HTML): " + zip);
        EditorUtility.RevealInFinder(zip);
    }
}
#endif

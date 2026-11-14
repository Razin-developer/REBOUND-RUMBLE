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
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.runInBackground = false;

        string dir = Path.GetFullPath("Builds/WebGL");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
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
        if (File.Exists(zip)) File.Delete(zip);
        System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip, System.IO.Compression.CompressionLevel.Optimal, false);
        Debug.Log("[Build] Upload this to itch.io (kind of project: HTML): " + zip);
        EditorUtility.RevealInFinder(zip);
    }
}
#endif

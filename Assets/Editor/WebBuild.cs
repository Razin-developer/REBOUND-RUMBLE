#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class WebBuild
{
    [MenuItem("Rebound Rumble/Build WebGL")]
    public static void Build()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.template = "APPLICATION:Default";
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.runInBackground = false;
        PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

        string[] scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path);
        if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/GameScene.unity" };

        var opts = new BuildPlayerOptions { scenes = scenes, locationPathName = "Builds/Web", target = BuildTarget.WebGL, options = BuildOptions.None };
        var rep = BuildPipeline.BuildPlayer(opts);
        Debug.Log("WebGL build: " + rep.summary.result + " -> Builds/Web");
        if (rep.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorUtility.RevealInFinder("Builds/Web/index.html");
    }
}
#endif

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Makes every WebGL build fill the whole browser window on a dark page (the old "Minimal" template left a small canvas on a WHITE page).
/// Works for any way of building (the REBOUND RUMBLE menus or File > Build Profiles).
/// </summary>
[InitializeOnLoad]
public class WebFill : IPostprocessBuildWithReport
{
    static WebFill()
    {
        // never use the Minimal template (white page, canvas does not fill)
        if (PlayerSettings.WebGL.template == "APPLICATION:Minimal") PlayerSettings.WebGL.template = "APPLICATION:Default";
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
    }

    public int callbackOrder { get { return 1000; } }

    const string Css =
        "<style id=\"rr-fill\">" +
        "html,body{margin:0!important;padding:0!important;width:100%!important;height:100%!important;background:#0f0c1a!important;overflow:hidden!important}" +
        "#unity-container,#unity-container.unity-desktop,#unity-container.unity-mobile{position:fixed!important;left:0!important;top:0!important;right:0!important;bottom:0!important;width:100%!important;height:100%!important;transform:none!important}" +
        "#unity-canvas{position:absolute!important;left:0!important;top:0!important;width:100%!important;height:100%!important;display:block!important;background:#0f0c1a!important}" +
        "#unity-footer,#unity-build-title,#unity-fullscreen-button,#unity-logo{display:none!important}" +
        "</style>";

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL) return;
        string index = Path.Combine(report.summary.outputPath, "index.html");
        if (!File.Exists(index)) return;
        string html = File.ReadAllText(index);
        if (html.Contains("id=\"rr-fill\"")) return;
        int i = html.IndexOf("</head>");
        html = i >= 0 ? html.Insert(i, Css) : Css + html;
        File.WriteAllText(index, html);
        Debug.Log("[WebFill] index.html patched: the game now fills the whole browser window.");
    }
}
#endif

using UnityEngine;

/// <summary>Global scale of everything that is not the map (player, enemies, rings, chests, portal, camera).</summary>
public static class GS { public const float S = 0.7f; }

/// <summary>Light-weight runtime settings: steady frame rate, no shadows/AA, quiet logs in builds.</summary>
public static class PerfBoot
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.antiAliasing = 0;
        Time.fixedDeltaTime = 0.02f;
        Physics2D.autoSyncTransforms = false;
        if (!Debug.isDebugBuild) Debug.unityLogger.logEnabled = false;
    }
}

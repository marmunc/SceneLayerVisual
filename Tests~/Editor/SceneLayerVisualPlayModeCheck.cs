using System;
using UnityEditor;
using UnityEngine;

internal static class SceneLayerVisualPlayModeCheck
{
    private static int _completedCycles;
    private static int _settleTicks;
    private static bool _wasOptionsEnabled;
    private static EnterPlayModeOptions _previousOptions;
    private static double _deadline;

    internal static void Begin()
    {
        _wasOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        _previousOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        _deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.playModeStateChanged += OnStateChanged;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void OnStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) _settleTicks = 10;
        if (state != PlayModeStateChange.EnteredEditMode) return;
        _completedCycles++;
        if (_completedCycles < 2) EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        else
        {
            RestoreSettings();
            Debug.Log("SLV_PLAYMODE_PASS: two cycles with Domain Reload and Scene Reload disabled");
            Debug.Log("SLV_PACKAGE_PASS");
            EditorApplication.Exit(0);
        }
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("Repeated Play Mode check timed out");
            if (!EditorApplication.isPlaying || _settleTicks <= 0) return;
            _settleTicks--;
            if (_settleTicks != 0) return;
            Debug.Log("SLV_PLAYMODE_BASELINE: cycle=" + (_completedCycles + 1) + " " + SceneLayerVisualBaseline.Run());
            SceneLayerVisualCaptureIsolationChecks.Run();
            EditorApplication.ExitPlaymode();
        }
        catch (Exception exception)
        {
            RestoreSettings();
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void RestoreSettings()
    {
        EditorApplication.playModeStateChanged -= OnStateChanged;
        EditorApplication.update -= Tick;
        EditorSettings.enterPlayModeOptions = _previousOptions;
        EditorSettings.enterPlayModeOptionsEnabled = _wasOptionsEnabled;
    }
}

using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class CliBuild
{
    // args: -mono  -> fast Mono build for iterating; -dev -> development build (profiler markers); default IL2CPP release
    public static void Android()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.Android.useCustomKeystore = false; // debug-sign: original author keystore isn't part of the repo
        var args = System.Environment.GetCommandLineArgs();
        bool mono = System.Array.IndexOf(args, "-mono") >= 0;
        bool dev = System.Array.IndexOf(args, "-dev") >= 0;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, mono ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = mono ? AndroidArchitecture.ARMv7 : (AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64);
        var scenes = new[] { "Assets/Scenes/MainScene.unity" };
        var report = BuildPipeline.BuildPlayer(scenes, "C:/rv/build/RevoTV.apk", BuildTarget.Android, dev ? BuildOptions.Development : BuildOptions.None);
        Debug.Log("BUILD RESULT: " + report.summary.result);
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}

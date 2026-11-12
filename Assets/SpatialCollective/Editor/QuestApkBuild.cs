using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Reproducible sideload APK build; does not start or shorten a study session.
public static class QuestApkBuild
{
    public static void Build()
    {
        Directory.CreateDirectory("Builds");
        Directory.CreateDirectory("../Documentation");
        if (!SpatialCollectiveEditor.QuestProjectConfigurator.TryConfigureForQuest3())
            throw new BuildFailedException("Quest configuration needs an editor restart.");
        SpatialCollectiveEditor.CollectiveProject.ValidateImported();
        EditorSceneManager.OpenScene("Assets/SpatialCollective/Scenes/Spatial_Collective_Quest3.unity");
        if (UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            throw new BuildFailedException("No camera in the startup scene.");
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;
        PlayerSettings.Android.splitApplicationBinary = false;
        PlayerSettings.Android.useCustomKeystore = false;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
        PlayerSettings.SetAdditionalIl2CppArgs("--jobs=1 --bee-jobs=1");
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.allowDebugging = false;
        AssetDatabase.SaveAssets();
        string output = Path.GetFullPath("Builds/Mars_Spatial_Collective_Quest3_v0.9.7.apk");
        int previousHeap = AndroidExternalToolsSettings.maxJvmHeapSize;
        BuildReport report;
        try
        {
            // Unity passes this as a command-line override of gradle.properties.
            AndroidExternalToolsSettings.maxJvmHeapSize = 1024;
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/SpatialCollective/Scenes/Spatial_Collective_Quest3.unity" },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
        }
        finally { AndroidExternalToolsSettings.maxJvmHeapSize = previousHeap; }
        var s = report.summary;
        File.WriteAllText("Builds/Build_Result.txt", $"UTC: {DateTime.UtcNow:O}\nUnity: {Application.unityVersion}\nResult: {s.result}\nVersion: {PlayerSettings.bundleVersion}\nVersionCode: {PlayerSettings.Android.bundleVersionCode}\nErrors: {s.totalErrors}\nWarnings: {s.totalWarnings}\nBytes: {s.totalSize}\nDuration: {s.totalTime}\nOutput: {output}\nPhysicalQuestValidated: false\n");
        File.WriteAllLines("Builds/Build_Messages.txt", report.steps.SelectMany(step => step.messages.Select(m => $"{step.name}: {m.type}: {m.content}")));
        if (s.result != BuildResult.Succeeded || s.totalErrors != 0)
            throw new BuildFailedException("APK build failed; inspect Builds/Build_Messages.txt.");
        Debug.Log("QUEST_APK_BUILD_PASS " + output);
    }
}

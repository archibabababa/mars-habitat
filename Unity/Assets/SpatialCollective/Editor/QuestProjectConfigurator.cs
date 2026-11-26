using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace SpatialCollectiveEditor
{
    public static class QuestProjectConfigurator
    {

        [MenuItem("Spatial Collective/Run mode/Meta XR Simulator (Windows OpenXR)")]
        public static void UseMetaSimulator()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before changing XR settings.");
            var group = BuildTargetGroup.Standalone;
            XRGeneralSettingsPerBuildTarget container;
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out container) || container == null)
                throw new InvalidOperationException("XR settings asset missing. Reimport the supplied Assets/XR folder.");
            var general = container.SettingsForBuildTarget(group);
            if (general == null || general.Manager == null)
            {
                container.CreateDefaultManagerSettingsForBuildTarget(group);
                general = container.SettingsForBuildTarget(group);
            }
            if (!general.Manager.activeLoaders.Any(l => l != null && l.GetType().FullName == "UnityEngine.XR.OpenXR.OpenXRLoader") &&
                !XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                throw new InvalidOperationException("Could not assign Windows OpenXR loader.");
            general.InitManagerOnStart = true;
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            foreach (var feature in xr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>())
                if (feature is HandTracking || feature is MetaHandTrackingAim || feature is HandInteractionProfile || feature is OculusTouchControllerProfile)
                { feature.enabled = true; EditorUtility.SetDirty(feature); }
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.Direct3D11 });
            EditorUtility.SetDirty(container);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);
            EditorUtility.SetDirty(xr);
            AssetDatabase.SaveAssets();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                EditorUserBuildSettings.SwitchActiveBuildTarget(group, BuildTarget.StandaloneWindows64);
            Debug.Log("Windows OpenXR enabled. Start Meta XR Simulator, then Play. Confirm the project image appears in the simulator.");
        }

        [MenuItem("Spatial Collective/Run mode/Desktop rehearsal (no headset)")]
        public static void UseDesktopRehearsal()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before changing XR settings.");
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (general != null) { general.InitManagerOnStart = false; EditorUtility.SetDirty(general); }
            AssetDatabase.SaveAssets();
            Debug.Log("Desktop rehearsal selected. Android Quest settings remain available.");
        }

        private const string XrSettingsFolder = "Assets/XR/Settings";
        private const string XrSettingsPath = XrSettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset";
        private const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        private const string InputRestartRequiredKey = "SpatialCollective.InputRestartRequired";

        [MenuItem("Spatial Collective/Configure Quest 3 OpenXR")]
        public static void ConfigureForQuest3()
        {
            TryConfigureForQuest3();
        }

        public static bool TryConfigureForQuest3()
        {
            if (SessionState.GetBool(InputRestartRequiredKey, false))
            {
                const string pending = "Input System Package (New) has been selected, but Unity must be restarted before Quest configuration or building can continue.";
                if (Application.isBatchMode) throw new BuildFailedException(pending);
                EditorUtility.DisplayDialog("Restart Unity required", pending + "\n\nSave the project, close Unity, reopen it, then run Configure Quest 3 OpenXR again.", "OK");
                Debug.LogWarning("Spatial Collective: " + pending);
                return false;
            }

            if (ConfigureInputSystem())
            {
                AssetDatabase.SaveAssets();
                SessionState.SetBool(InputRestartRequiredKey, true);
                const string changed = "Active Input Handling was changed to Input System Package (New). Unity must be restarted once before configuration continues.";
                if (Application.isBatchMode) throw new BuildFailedException(changed + " Re-run the same command after restarting Unity.");
                EditorUtility.DisplayDialog("Restart Unity required", changed + "\n\nSave the project, close Unity, reopen it, then run Configure Quest 3 OpenXR again.", "OK");
                Debug.LogWarning("Spatial Collective: " + changed);
                return false;
            }

            EnsureAndroidXrSettings();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            ValidateOrThrow();
            Debug.Log("Spatial Collective: Quest 3 OpenXR configuration is present.");
            return true;
        }

        public static void ValidateOrThrow()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Active build target is not Android. Start Unity with -buildTarget Android or switch the platform first.");
            if (ReadActiveInputHandler() != 1)
                throw new BuildFailedException("Active Input Handling must be Input System Package (New). Run Configure Quest 3 OpenXR and restart Unity when prompted.");

            XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (general == null || general.Manager == null)
                throw new BuildFailedException("Android XR Plug-in Management settings are missing.");
            bool hasOpenXr = general.Manager.activeLoaders.Any(loader => loader != null && loader.GetType().FullName == OpenXrLoaderType);
            if (!hasOpenXr)
                throw new BuildFailedException("The OpenXR loader is not assigned for Android.");

            OpenXRSettings openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (openXr == null)
                throw new BuildFailedException("Android OpenXR settings were not generated. Re-run Configure Quest 3 OpenXR after package import finishes.");
            string[] questAssets = AssetDatabase.FindAssets("t:MetaQuestFeature");
            if (questAssets.Length != 1)
                throw new BuildFailedException("Expected one OpenXR settings asset containing MetaQuestFeature. Remove unused duplicate OpenXR settings assets before building.");
            RequireEnabled<MetaQuestFeature>(openXr, "Meta Quest Support");
            RequireEnabled<HandTracking>(openXr, "Hand Tracking Subsystem");
            RequireEnabled<MetaHandTrackingAim>(openXr, "Meta Hand Tracking Aim");
            RequireEnabled<HandInteractionProfile>(openXr, "Hand Interaction Profile");
            RequireEnabled<OculusTouchControllerProfile>(openXr, "Oculus Touch Controller Profile");

            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
                throw new BuildFailedException("ARM64 is not enabled for Android.");
            if (PlayerSettings.Android.applicationEntry != AndroidApplicationEntry.GameActivity)
                throw new BuildFailedException("Quest OpenXR requires Android GameActivity for this Unity 6 pilot.");
            string identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (identifier != "org.fanhuang.spatialcollective")
                throw new BuildFailedException("Unexpected application identifier: " + identifier);
            if (!File.Exists(AndroidManifestPath))
                throw new BuildFailedException("Quest hand-tracking AndroidManifest.xml is missing.");
            string manifest = File.ReadAllText(AndroidManifestPath);
            if (!manifest.Contains("com.oculus.permission.HAND_TRACKING") || !manifest.Contains("oculus.software.handtracking"))
                throw new BuildFailedException("AndroidManifest.xml lacks the explicit Quest hand-tracking permission or feature.");
        }

        private static void EnsureAndroidXrSettings()
        {
            XRGeneralSettingsPerBuildTarget container = null;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out container);
            if (container == null)
            {
                Directory.CreateDirectory(XrSettingsFolder);
                container = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(container, XrSettingsPath);
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, container, true);
            }

            XRGeneralSettings general = container.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (general == null || general.Manager == null)
            {
                container.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
                general = container.SettingsForBuildTarget(BuildTargetGroup.Android);
            }
            if (general == null || general.Manager == null)
                throw new BuildFailedException("Could not create Android XR Plug-in Management settings.");

            general.InitManagerOnStart = true;
            bool alreadyAssigned = general.Manager.activeLoaders.Any(loader => loader != null && loader.GetType().FullName == OpenXrLoaderType);
            if (!alreadyAssigned && !XRPackageMetadataStore.AssignLoader(general.Manager, OpenXrLoaderType, BuildTargetGroup.Android))
                throw new BuildFailedException("Could not assign Unity OpenXR loader for Android.");
            EditorUtility.SetDirty(container);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);
            AssetDatabase.SaveAssets();

            OpenXRSettings openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (openXr == null)
                throw new BuildFailedException("OpenXR package settings are not ready. Let package import finish, then run the build again.");
            EnableFeature<MetaQuestFeature>(openXr);
            EnableFeature<HandTracking>(openXr);
            EnableFeature<MetaHandTrackingAim>(openXr);
            EnableFeature<HandInteractionProfile>(openXr);
            EnableFeature<OculusTouchControllerProfile>(openXr);
            openXr.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
            EditorUtility.SetDirty(openXr);
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "org.fanhuang.spatialcollective");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;
            PlayerSettings.companyName = "Fan Huang";
            PlayerSettings.productName = "Spatial Collective";
            PlayerSettings.bundleVersion = "0.9.12";
            PlayerSettings.Android.bundleVersionCode = 102;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        }

        private static bool ConfigureInputSystem()
        {
            UnityEngine.Object[] settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settingsAssets == null || settingsAssets.Length == 0)
                throw new BuildFailedException("ProjectSettings.asset could not be loaded.");
            SerializedObject serialized = new SerializedObject(settingsAssets[0]);
            serialized.Update();
            SerializedProperty inputHandler = serialized.FindProperty("activeInputHandler");
            if (inputHandler == null)
                throw new BuildFailedException("Unity activeInputHandler setting was not found.");
            if (inputHandler.intValue == 1) return false;
            inputHandler.intValue = 1; // Input System Package (New)
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settingsAssets[0]);
            return true;
        }

        private static int ReadActiveInputHandler()
        {
            UnityEngine.Object[] settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settingsAssets == null || settingsAssets.Length == 0) return -1;
            SerializedObject serialized = new SerializedObject(settingsAssets[0]);
            serialized.Update();
            SerializedProperty inputHandler = serialized.FindProperty("activeInputHandler");
            return inputHandler != null ? inputHandler.intValue : -1;
        }

        private static void EnableFeature<T>(OpenXRSettings settings) where T : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            T feature = settings.GetFeature<T>();
            if (feature == null)
                throw new BuildFailedException("Required OpenXR feature is unavailable: " + typeof(T).FullName);
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        private static void RequireEnabled<T>(OpenXRSettings settings, string label) where T : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            T feature = settings.GetFeature<T>();
            if (feature == null || !feature.enabled)
                throw new BuildFailedException(label + " is not enabled in Android OpenXR settings.");
        }
    }
}

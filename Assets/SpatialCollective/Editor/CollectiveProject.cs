using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using SpatialCollective;

namespace SpatialCollectiveEditor
{
    [InitializeOnLoad]
    public static class CollectiveProject
    {
        static CollectiveProject()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SessionState.GetBool("SpatialCollective.StartSceneOpened", false)) return;
                SessionState.SetBool("SpatialCollective.StartSceneOpened", true);
                var active = SceneManager.GetActiveScene();
                if (!active.isDirty && string.IsNullOrEmpty(active.path) && File.Exists(ScenePath))
                    EditorSceneManager.OpenScene(ScenePath);
            };
        }
        private const string Root="Assets/SpatialCollective";
        private const string Generated=Root+"/Materials";
        private const string ScenePath=Root+"/Scenes/Spatial_Collective_Quest3.unity";
        public const string DataPath=Root+"/SourceData/Collective.scmodel";
        private static bool rebuildInteractive;
        public static void UpgradeAndVerify()
        {
            UpgradeInteractiveScene();SpatialGestureVerification.Run();
        }
        public static void UpgradeInteractiveScene()
        {
            rebuildInteractive=true;
            try { PrepareScene(); QuestProjectConfigurator.ConfigureForQuest3(); }
            finally { rebuildInteractive=false; }
        }
        private static readonly Color[] Colors={new Color(.22f,.84f,.81f),new Color(.97f,.62f,.26f),new Color(.74f,.52f,.97f),new Color(.3f,.58f,.97f)};

        [MenuItem("Spatial Collective/Scene/Create Demo Scene If Missing")]
        public static void PrepareScene()
        {
            if (File.Exists(ScenePath) && !rebuildInteractive)
            {
                if (!Application.isBatchMode && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(ScenePath);
                Debug.Log("Existing scene preserved. Source reimport does not rebuild scene layouts.");
                return;
            }
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Directory.CreateDirectory(Generated);Directory.CreateDirectory(Root+"/Scenes");AssetDatabase.Refresh();
            CollectiveData data = AssetDatabase.LoadAssetAtPath<CollectiveData>(DataPath);
            if (data == null) throw new InvalidDataException("Model import failed. Check the first Console error.");
            Directory.CreateDirectory(Root+"/Prefabs");
            var importedAssets = AssetDatabase.LoadAllAssetsAtPath(DataPath);
            ValidateKinematics(data);
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientLight=new Color(.3f,.35f,.42f);

            var model=new GameObject("Spatial Collective - deformable geometry");model.layer=8;
            var rig=model.AddComponent<CollectiveRig>();rig.data=data;rig.filters=new MeshFilter[data.batches.Length];rig.renderers=new MeshRenderer[data.batches.Length];
            rig.flightPathsJson=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Configuration/FlightPaths.json");
            Matrix4x4[] matrices=new Matrix4x4[data.drivers.Length];CollectiveKinematics.Evaluate(data,new[]{.5f,.5f,.5f,.5f},matrices);
            for(int i=0;i<data.batches.Length;i++)
            {
                GeometryBatch b=data.batches[i];
                Mesh neutral = System.Array.Find(importedAssets, asset => asset is Mesh && asset.name == b.mesh.name+"_Neutral") as Mesh;
                if (neutral == null) throw new InvalidDataException("Neutral preview mesh missing: " + b.mesh.name);
                var go=new GameObject(b.mesh.name);go.layer=8;go.transform.SetParent(model.transform,false);
                rig.filters[i]=go.AddComponent<MeshFilter>();rig.filters[i].sharedMesh=neutral;
                var renderer=go.AddComponent<MeshRenderer>();rig.renderers[i]=renderer;renderer.sharedMaterial=GeometryMaterial(b);
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                renderer.enabled=(b.kind!="inner" && b.kind!="floor") || b.node==0;
            }
            model.transform.localScale=Vector3.one*.026f;
            model.transform.position=new Vector3(0,1.25f,2.5f)-data.overviewCenter*.026f;

            var xr=new GameObject("XR Origin");var origin=xr.AddComponent<XROrigin>();
            var offset=new GameObject("Camera Offset");offset.transform.SetParent(xr.transform,false);
            var camObject=new GameObject("Main Camera");camObject.tag="MainCamera";camObject.transform.SetParent(offset.transform,false);camObject.transform.localPosition=new Vector3(0,1.7f,0);
            Camera camera=camObject.AddComponent<Camera>();camera.nearClipPlane=.04f;camera.farClipPlane=260;camera.clearFlags=CameraClearFlags.Skybox;camera.allowHDR=false;
            camera.fieldOfView=74f;
            camera.cullingMask&=~(1<<10);
            camera.gameObject.AddComponent<CollectiveViewFilter>().rig=rig;
            camObject.AddComponent<AudioListener>();
            var pose=camObject.AddComponent<TrackedPoseDriver>();
            pose.positionInput=new InputActionProperty(new InputAction("Head position",InputActionType.PassThrough,"<XRHMD>/centerEyePosition"));
            pose.rotationInput=new InputActionProperty(new InputAction("Head rotation",InputActionType.PassThrough,"<XRHMD>/centerEyeRotation"));
            // Read a stationary headset immediately after enabling or resuming tracking.
            pose.positionInput.action.wantsInitialStateCheck = true;
            pose.rotationInput.action.wantsInitialStateCheck = true;
            pose.trackingType=TrackedPoseDriver.TrackingType.RotationAndPosition;
            pose.updateType=TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            // Preserve the desktop preview pose until an HMD supplies tracked data.
            pose.ignoreTrackingState=false;
            pose.trackingStateInput=new InputActionProperty(new InputAction("Head tracking state",InputActionType.PassThrough,"<XRHMD>/trackingState"));
            origin.Camera=camera;origin.CameraFloorOffsetObject=offset;origin.CameraYOffset=0;
            origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;
            Material hands=ColorMaterial("Hands",new Color(.48f,.94f,1,1),true);
            hands.renderQueue=3520;hands.SetFloat("_ZTest",8);hands.SetFloat("_ZWrite",0);
            Material trackedHand=SaveMaterial("TrackedHand",new Material(RequiredShader("SpatialCollective/TrackedHand")));
            foreach(string side in new[]{"Left","Right"})
            {
                string handPath=Root+"/OfficialHands/"+side+" Hand Tracking.prefab";
                GameObject handPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(handPath);
                if(handPrefab==null)throw new InvalidDataException("Official XR Hands prefab missing: "+handPath);
                var hand=(GameObject)PrefabUtility.InstantiatePrefab(handPrefab,offset.transform);
                hand.transform.localPosition=Vector3.zero;hand.transform.localRotation=Quaternion.identity;
                foreach(Transform t in hand.GetComponentsInChildren<Transform>(true))t.gameObject.layer=11;
                var skin=hand.GetComponentInChildren<SkinnedMeshRenderer>();skin.sharedMaterial=trackedHand;
                skin.shadowCastingMode=ShadowCastingMode.Off;skin.receiveShadows=false;skin.enabled=false;
                var events=hand.GetComponent<XRHandTrackingEvents>();events.updateType=XRHandTrackingEvents.UpdateTypes.Dynamic|XRHandTrackingEvents.UpdateTypes.BeforeRender;
                PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
            }
            var experience=new GameObject("Experience - regions and embodiment").AddComponent<CollectiveExperience>();
            experience.roomScale=experience.gameObject.AddComponent<CollectiveRoomScale>();
            TextAsset interaction = AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Configuration/Interaction.json");
            var state = experience.gameObject.AddComponent<CollectiveStateController>(); state.rig=rig; state.interactionJson=interaction;
            state.authoredStateJson=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Configuration/AuthoredState.json");
            var reader = xr.AddComponent<CollectiveInputReader>(); reader.trackingOrigin=offset.transform; reader.interactionJson=interaction;
            experience.stateController=state; experience.inputReader=reader;
            var study=experience.gameObject.AddComponent<CollectiveMotionStudy>();study.rig=rig;
            study.sequenceJson=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Configuration/MotionStudy.json");
            experience.study=study;
            experience.rehearsal=experience.gameObject.AddComponent<CollectiveGestureRehearsal>();
            experience.rehearsal.experience=experience;
            experience.linkedViews=experience.gameObject.AddComponent<CollectiveLinkedViews>();
            experience.rig=rig;experience.head=camera;experience.trackingOrigin=offset.transform;experience.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            experience.uiMaterial=ColorMaterial("Interface",new Color(.026f,.049f,.065f,1),false);
            experience.uiMaterial.renderQueue=3500;
            experience.uiMaterial.SetFloat("_ZTest",8);
            experience.handMaterial=hands;
            experience.insetMaterial=SaveMaterial("ContextInset",new Material(RequiredShader("SpatialCollective/Monitor")));
            experience.cabinSky=Panorama("Cabin","Mars_Cabin_360_Concept.png");experience.marsSky=Panorama("Mars","Mars_Exterior_360_Concept.png");
            RenderSettings.skybox=experience.cabinSky;
            QualitySettings.antiAliasing=2;QualitySettings.shadows=ShadowQuality.Disable;
            ConfigureLayers();
            experience.CreateMenu();
            PrefabUtility.SaveAsPrefabAssetAndConnect(model,Root+"/Prefabs/CollectiveModel.prefab",InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAssetAndConnect(xr,Root+"/Prefabs/CollectiveXROrigin.prefab",InteractionMode.AutomatedAction);
            var menu = GameObject.Find("World-anchored interaction panel");
            menu.transform.position = new Vector3(0,1.7f,2.8f);
            PrefabUtility.SaveAsPrefabAssetAndConnect(menu,Root+"/Prefabs/CollectiveMenu.prefab",InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();Selection.activeGameObject=model;
            Debug.Log("Spatial Collective scene prepared. Press Play for the desktop preview. The saved scene is ready for normal Inspector editing.");
            if(!Application.isBatchMode)EditorUtility.DisplayDialog("Scene prepared","The model, materials, panoramas, XR Origin and interaction scene are ready.\n\nPress Play to inspect. Build Quest 3 APK is the next menu command. Headset testing is still required.","OK");
        }

        [MenuItem("Spatial Collective/Build/Quest 3 APK")]
        public static void BuildApk()=>RunBuild(false);

        [MenuItem("Spatial Collective/Build/Build and Run on Connected Quest")]
        public static void BuildAndRun()=>RunBuild(true);

        private static void RunBuild(bool runOnDevice)
        {
            if (Path.GetFullPath(".").Length > 120)
                throw new BuildFailedException("Move the project to a shorter path, such as C:/UnityProjects/Mars/UnityProject, before Android building. Windows Gradle/CMake helper paths can exceed 260 characters.");
            if(EditorApplication.isPlaying)throw new BuildFailedException("Exit Play mode before building.");
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android))
                throw new BuildFailedException("Install Android Build Support, Android SDK & NDK Tools, and OpenJDK for this Unity Editor in Unity Hub.");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
            {
                if(!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,BuildTarget.Android))
                    throw new BuildFailedException("Could not switch to Android. Wait for import to finish and run this menu command again.");
            }
            if(!File.Exists(ScenePath)){PrepareScene();if(!File.Exists(ScenePath))return;}
            if(!QuestProjectConfigurator.TryConfigureForQuest3())return;
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorUserBuildSettings.buildAppBundle=false;
            Directory.CreateDirectory("Builds");
            string path=Path.GetFullPath("Builds/Spatial_Collective_Quest3.apk");
            BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=path,target=BuildTarget.Android,options=runOnDevice?BuildOptions.AutoRunPlayer:BuildOptions.None});
            File.WriteAllText("Builds/Build_Result.txt",$"UTC: {DateTime.UtcNow:O}\nUnity: {Application.unityVersion}\nResult: {report.summary.result}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nBytes: {report.summary.totalSize}\nOutput: {path}\n");
            if(report.summary.result!=BuildResult.Succeeded || report.summary.totalErrors != 0)throw new BuildFailedException("APK build reported errors. Read the first red Console error and Builds/Build_Result.txt before using the APK.");
            Debug.Log("Quest APK built successfully: "+path);
            if(!Application.isBatchMode){EditorUtility.RevealInFinder(path);EditorUtility.DisplayDialog("APK built","Build succeeded.\n\n"+path+"\n\nConnect Quest 3 in developer mode to install with adb or your usual device tool.","OK");}
        }

        [MenuItem("Spatial Collective/Validate/Source Geometry Fixtures")]
        public static void ValidateImported()
        {
            var data=AssetDatabase.LoadAssetAtPath<CollectiveData>(DataPath);
            if(data==null)throw new InvalidOperationException("Run Prepare Scene first.");
            ValidateKinematics(data);Debug.Log("Geometry fixtures passed in Unity. This does not validate Android compilation or headset performance.");
        }
        // Batch mode entry point, after packages have resolved: -executeMethod SpatialCollectiveEditor.CollectiveProject.BuildBatch
        public static void BuildBatch(){PrepareScene();BuildApk();}

        [MenuItem("Spatial Collective/Scene/Open Demo Scene")]
        public static void OpenScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Spatial Collective/Import/Reimport Model and JSON")]
        public static void ReimportSource()
        {
            AssetDatabase.ImportAsset(DataPath, ImportAssetOptions.ForceUpdate);
            ValidateImported();
        }

        private static void ConfigureLayers()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = settings.FindProperty("layers");
            layers.GetArrayElementAtIndex(8).stringValue = "CollectiveGeometry";
            layers.GetArrayElementAtIndex(9).stringValue = "CollectiveUI";
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Shader RequiredShader(string name){Shader s=Shader.Find(name);if(s==null)throw new InvalidOperationException("Missing shader: "+name);return s;}
        private static Material SaveMaterial(string name,Material material)
        {
            string path=Generated+"/"+name+".mat";Material existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            material.name=name;
            if(existing!=null){EditorUtility.CopySerialized(material,existing);UnityEngine.Object.DestroyImmediate(material);return existing;}
            AssetDatabase.CreateAsset(material,path);return material;
        }
        private static Material ColorMaterial(string name,Color color,bool opaque)
        {
            Material material=new Material(RequiredShader("SpatialCollective/SpatialTint"));material.color=color;material.enableInstancing=true;
            material.SetFloat("_ZWrite",opaque?1:0);material.SetFloat("_SrcBlend",opaque?(float)BlendMode.One:(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",opaque?(float)BlendMode.Zero:(float)BlendMode.OneMinusSrcAlpha);
            material.renderQueue=opaque?2000:3000;return SaveMaterial(name,material);
        }
        private static Material GeometryMaterial(GeometryBatch b)
        {
            Color color=Colors[b.region];bool opaque=false;
            switch(b.kind)
            {
                case "shell":color.a=.16f;break;
                case "glass":color=new Color(.42f,.78f,.95f,.08f);break;
                case "soft":color=Color.Lerp(color,new Color(1,.61f,.32f),.65f);color.a=.28f;break;
                case "inner":color.a=.08f;break;
                case "floor":color=Color.Lerp(color,new Color(.24f,.3f,.34f),.66f);color.a=.35f;break;
                case "wire":color.a=.60f;break;
            }
            Material material=ColorMaterial(b.kind+"_R"+b.region,color,opaque);
            material.SetFloat("_SCGeometry",1);
            material.SetFloat("_SCCurrentOpacity",b.kind=="glass"?0:b.kind=="floor"?.98f:b.kind=="shell"?.94f:b.kind=="soft"?.90f:b.kind=="inner"?.62f:.92f);
            material.SetFloat("_SCWhiteShading",b.kind=="glass"||b.kind=="wire"?0:1);
            material.SetFloat("_SCInteriorOpacity",b.kind=="shell"?.88f:b.kind=="soft"?.7f:b.kind=="inner"?.35f:b.kind=="floor"?.92f:color.a);
            return material;
        }
        private static Material Panorama(string name,string file)
        {
            string path=Root+"/Textures/"+file;
            TextureImporter importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new FileNotFoundException("Panorama missing",path);
            importer.textureType=TextureImporterType.Default;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;importer.maxTextureSize=2048;importer.npotScale=TextureImporterNPOTScale.None;
            importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
            var material=new Material(RequiredShader("Skybox/Panoramic"));material.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));material.SetFloat("_Exposure",name=="Mars"?.9f:.65f);material.SetFloat("_Mapping",1);material.SetFloat("_ImageType",0);
            if(name=="Cabin"){material.SetFloat("_Rotation",90);material.SetFloat("_Exposure",.8f);}
            return SaveMaterial(name+"Panorama",material);
        }
        [Serializable] private class FixtureSet {public Fixture[] fixtures = null;}
        [Serializable] private class Fixture {public string state = null;public float[] p = null;public int driver = 0;public float[] input = null,expected = null;}
        private static void ValidateKinematics(CollectiveData data)
        {
            FixtureSet set=JsonUtility.FromJson<FixtureSet>(File.ReadAllText(Root+"/Editor/SourceData/TransformFixtures.json"));
            var matrices=new Matrix4x4[data.drivers.Length];float max=0;
            foreach(Fixture f in set.fixtures)
            {
                CollectiveKinematics.Evaluate(data,f.p,matrices);Vector3 p=new Vector3(f.input[0],f.input[1],f.input[2]);Vector3 expected=new Vector3(f.expected[0],f.expected[1],f.expected[2]);
                max=Mathf.Max(max,Vector3.Distance(matrices[f.driver].MultiplyPoint3x4(p),expected));
            }
            if(max>.0001f)throw new InvalidDataException("Kinematic fixture mismatch, metres: "+max);
            Debug.Log("Native-source kinematic fixtures passed. Maximum Unity float error (m): "+max);
        }
    }
}

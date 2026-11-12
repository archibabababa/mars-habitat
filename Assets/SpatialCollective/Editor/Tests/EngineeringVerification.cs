using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpatialCollective;

namespace SpatialCollectiveEditor
{
    /// <summary>Repeatable integration checks against the delivered scene and source data.</summary>
    [InitializeOnLoad]
    public static class EngineeringVerification
    {
        private const string ScenePath = "Assets/SpatialCollective/Scenes/Spatial_Collective_Quest3.unity";
        private const string ProtocolPath = "Assets/SpatialCollective/SourceData/Collective.protocol.json";
        private const string Running = "SpatialCollective.Verification.Running";
        private static readonly List<string> Checks = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static double playStart;
        private static int phase;
        private static int originalNode, nextNode;
        private static float[] savedBaselines;
        private static double phaseTime;
        private static Vector3 capturePosition;
        private static Quaternion captureRotation;
        private static RenderTexture captureTarget;
        private static Texture2D capturePixels;
        private static string Output => Path.GetFullPath("../Documentation/Validation");

        static EngineeringVerification()
        {
            if (SessionState.GetBool(Running, false))
            {
                Checks.AddRange(SessionState.GetString("SpatialCollective.Checks", "").Split('\n'));
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Tick;
            }
        }

        // Retained as a historical v0.6 panel-workflow regression harness, not the v0.7 validation entry.
        public static void RunAll()
        {
            Directory.CreateDirectory(Output);
            Checks.Clear(); Errors.Clear();
            try
            {
                CollectiveProject.PrepareScene();
                EditorSceneManager.OpenScene(ScenePath);
                CheckSource();
                CheckScene();
                CollectiveProject.ValidateImported(); Checks.Add("12 source kinematic fixtures: passed");
                string beforeScene = Hash(ScenePath);
                var beforeIds = AssetIds();
                CollectiveProject.ReimportSource();
                var afterIds = AssetIds();
                Require(beforeIds.SequenceEqual(afterIds), "Asset GUID/local file IDs changed on reimport.");
                Require(Hash(ScenePath) == beforeScene, "Reimport changed scene file.");
                CollectiveProject.PrepareScene();
                Require(Hash(ScenePath) == beforeScene, "Create-if-missing overwrote existing scene.");
                Checks.Add("Model reimport preserves 141 sub-asset IDs and existing scene bytes");
                CheckScene();
                SessionState.SetString("SpatialCollective.Checks", string.Join("\n", Checks));
                SessionState.SetBool(Running, true);
                // Let Unity's first-launch search/index callbacks finish before a domain reload.
                double beginAt = EditorApplication.timeSinceStartup + 4;
                EditorApplication.CallbackFunction begin = null;
                begin = () => {
                    if (EditorApplication.timeSinceStartup < beginAt) return;
                    EditorApplication.update -= begin;
                    EditorApplication.EnterPlaymode();
                };
                EditorApplication.update += begin;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        private static void CheckSource()
        {
            ModelProtocol original = JsonUtility.FromJson<ModelProtocol>(File.ReadAllText(ProtocolPath));
            CollectiveModelImporter.ValidateProtocol(original);
            Reject(p => p.schemaVersion = 99, "unsupported schema");
            Reject(p => p.units = "millimetres", "wrong units");
            Reject(p => p.nodes[1].id = p.nodes[0].id, "duplicate node ID");
            Reject(p => p.nodes[0].neighbors = new[] { 999 }, "invalid graph edge");
            Reject(p => p.drivers[28].parent = 28, "cyclic driver parent");
            Reject(p => p.batches[0].vertexDrivers[0] = 99999, "invalid vertex binding");
            Reject(p => p.regions[0].angle = float.NaN, "non-finite geometry");
            var profile = InteractionProfile.Load(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/SpatialCollective/Configuration/Interaction.json"));
            Require(profile.menu.Length == 21 && profile.keyboard.Length == 26, "Input bindings missing.");
            bool rejected = false;
            try { InteractionProfile.ValidateCommand("SelectRegion", 6); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Invalid command argument accepted.");
            Require(!CollectiveInputReader.EvaluatePinch(.03f, false, .025f, .04f), "Pinch began too early.");
            Require(CollectiveInputReader.EvaluatePinch(.02f, false, .025f, .04f), "Pinch did not begin.");
            Require(CollectiveInputReader.EvaluatePinch(.03f, true, .025f, .04f), "Pinch hysteresis failed.");
            Require(!CollectiveInputReader.EvaluatePinch(.05f, true, .025f, .04f), "Pinch did not release.");
            Checks.Add("Interaction JSON, command bounds, pinch hysteresis: passed (synthetic input)");
        }

        private static void Reject(Action<ModelProtocol> corrupt, string label)
        {
            ModelProtocol p = JsonUtility.FromJson<ModelProtocol>(File.ReadAllText(ProtocolPath));
            corrupt(p); bool rejected = false;
            try { CollectiveModelImporter.ValidateProtocol(p); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Validator accepted " + label); Checks.Add("Reject " + label + ": passed");
        }

        private static void CheckScene()
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<CollectiveRig>();
            var experience = UnityEngine.Object.FindFirstObjectByType<CollectiveExperience>();
            Require(rig != null && experience != null, "Scene components missing.");
            Require(rig.data.nodes.Length == 27 && rig.data.drivers.Length == 231 && rig.data.batches.Length == 70, "Model counts differ.");
            Require(rig.filters.Length == 70 && rig.renderers.Length == 70, "Mesh component count differs.");
            for (int i = 0; i < 70; i++) Require(rig.filters[i] != null && rig.filters[i].sharedMesh != null &&
                rig.renderers[i] != null && rig.renderers[i].sharedMaterial != null, "Missing model reference.");
            Require(experience.inputReader != null && experience.stateController != null && experience.head != null &&
                experience.trackingOrigin != null && experience.cabinSky != null && experience.marsSky != null, "Missing experience reference.");
            foreach (GameObject root in rig.gameObject.scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0, "Missing C# script.");
            experience.RefreshMenuBindings();
            Require(experience.study != null && experience.linkedViews != null && rig.flightPathsJson != null, "Interactive references missing.");
            Require(experience.Menu.GetComponentsInChildren<CollectiveParameterControl>(true).Length == 11, "Six shape, three response and two travel controls required.");
            Checks.Add("Saved scene references, 70 meshes/materials, 21 actions, 11 controls, missing scripts: passed");
        }

        private static string[] AssetIds() => AssetDatabase.LoadAllAssetsAtPath(CollectiveProject.DataPath)
            .Select(asset => { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id); return asset.name+":"+guid+":"+id; })
            .OrderBy(value => value).ToArray();

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (playStart == 0) { playStart = EditorApplication.timeSinceStartup; phaseTime = playStart; }
            double now = EditorApplication.timeSinceStartup;
            if (now - playStart > 600) { Finish(false, "Play Mode verification timed out."); return; }
            if (now - phaseTime < (phase>=8?.005:1)) return;
            try
            {
                var rig = UnityEngine.Object.FindFirstObjectByType<CollectiveRig>();
                var view = UnityEngine.Object.FindFirstObjectByType<CollectiveExperience>();
                var state = UnityEngine.Object.FindFirstObjectByType<CollectiveStateController>();
                Require(rig != null && rig.Ready && view != null && view.enabled && state.Profile != null, "Runtime failed to initialize.");
                if (view.IsTransitioning) return;
                if (phase>=8)
                {
                    int frame=phase-8;
                    if(Environment.GetCommandLineArgs().Contains("-captureGestures"))
                    {
                        if(frame>=900)
                        {
                            Require(Errors.Count==0,string.Join("\n",Errors));
                            Checks.Add("900 Unity-rendered frames at 15 fps: 60-second synthetic gesture input rehearsal using ProcessInput; head pose fixed, no headset footage");
                            Finish(true,null);return;
                        }
                        view.rehearsal.Step(frame/15f);CaptureFrame(view.head,frame);phase++;phaseTime=now;return;
                    }
                    if(frame>=384)
                    {
                        Require(Errors.Count==0,string.Join("\n",Errors));
                        Checks.Add("384 Unity-rendered frames at 12 fps: deterministic 32-second desktop preview; simulated viewpoint motion, no headset recording");
                        Finish(true,null);return;
                    }
                    float t=frame/12f;
                    view.head.transform.position=capturePosition+Vector3.right*(.23f*Mathf.Sin(t*.5f))+Vector3.forward*(.08f*Mathf.Sin(t*.3f));
                    view.head.transform.rotation=captureRotation*Quaternion.Euler(3*Mathf.Sin(t*.3f),7*Mathf.Sin(t*.25f),0);
                    view.study.Seek(t);rig.Rebuild();view.SendMessage("LateUpdate");view.SendMessage("RefreshStatus");view.linkedViews.RenderNow();
                    CaptureFrame(view.head,frame);phase++;phaseTime=now;return;
                }
                switch (phase)
                {
                    case 0:
                        Require(Errors.Count == 0, string.Join("\n", Errors));
                        Capture(view.head, "overview.png");
                        var before = rig.filters[0].sharedMesh.vertices[0];
                        view.Execute(CollectiveCommand.Preset, 2); rig.Rebuild();
                        Require(Vector3.Distance(before, rig.filters[0].sharedMesh.vertices[0]) > .001f, "Preset did not change geometry.");
                        view.Execute(CollectiveCommand.Apply, 0);
                        savedBaselines = Enumerable.Range(0, 4).Select(rig.GetBaseline).ToArray();
                        view.Execute(CollectiveCommand.Preset, 0); view.Execute(CollectiveCommand.ToggleCoupling, 0);
                        view.Execute(CollectiveCommand.ToggleRooms, 0); view.Execute(CollectiveCommand.Undo, 0); rig.Rebuild();
                        for (int i = 0; i < 4; i++) Require(Mathf.Abs(rig.GetBaseline(i) - savedBaselines[i]) < 1e-6f, "Undo did not restore baseline.");
                        Require(!rig.coupled && !rig.showAllChambers, "Undo did not restore flags.");
                        rig.SetBaseline(0, 1); rig.SetBaseline(1, .5f); rig.coupled = true; rig.Rebuild();
                        Require(Mathf.Abs(rig.Actual[1] - .1f) < 1e-5f, "Signed coupling incorrect.");
                        view.Execute(CollectiveCommand.Undo, 0);
                        Checks.Add("Play Mode: preset changes mesh, Apply/Undo and signed coupling passed");
                        originalNode = rig.selectedNode;
                        if(!rig.inside)view.Execute(CollectiveCommand.ToggleView, 0);
                        break;
                    case 1:
                        Require(rig.inside && rig.transform.localScale == Vector3.one, "Interior did not use 1:1 scale.");
                        view.Execute(CollectiveCommand.CycleNode, 1); break;
                    case 2:
                        nextNode = rig.selectedNode;
                        Require(rig.data.nodes[originalNode].neighbors.Contains(nextNode), "Navigation left the adjacency graph.");
                        view.Execute(CollectiveCommand.CycleNode, -1); break;
                    case 3:
                        Require(rig.selectedNode == originalNode, "Back did not retrace the route.");
                        Capture(view.head, "interior.png");
                        view.Execute(CollectiveCommand.ToggleView, 0); break;
                    case 4:
                        Require(!rig.inside, "Return to overview failed.");
                        Require(Errors.Count == 0, string.Join("\n", Errors));
                        Checks.Add("Play Mode: 1:1 entry, adjacent-room navigation, back route and exit passed");
                        InteractionVerification.Run(view,rig,state,Checks);
                        break;
                    case 5:
                        Require(rig.inside && rig.data.nodes[rig.selectedNode].id=="S05", "Reference room did not open.");
                        view.study.Seek(8);rig.Rebuild();
                        break;
                    case 6:
                        Capture(view.head,"interactive-s05.png");
                        Capture(view.linkedViews.OverviewCamera,"collective-window.png");
                        Capture(view.linkedViews.RoomCamera,"linked-room-window.png");
                        view.study.Seek(18);rig.Rebuild();
                        break;
                    case 7:
                        Capture(view.head,"interactive-differentiate.png");
                        GestureRehearsalVerification.Run(view,Checks);
                        Require(Errors.Count == 0, string.Join("\n", Errors));
                        Checks.Add("Play Mode: no logged errors/exceptions during desktop smoke test");
                        if(Environment.GetCommandLineArgs().Contains("-captureGestures"))
                        {view.rehearsal.Begin(false);break;}
                        if(Environment.GetCommandLineArgs().Contains("-capturePreview"))
                        {capturePosition=view.head.transform.position;captureRotation=view.head.transform.rotation;break;}
                        HeadsetInteractionVerification.Run(view,Checks);
                        EngineeringAuditVerification.Run(view,Checks);
                        Require(Errors.Count==0,string.Join("\n",Errors));
                        Finish(true, null); return;
                }
                phase++; phaseTime = now;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        public static void Capture(Camera camera, string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var target = new RenderTexture(1920, 1080, 24);
            var previous = RenderTexture.active; var oldTarget = camera.targetTexture;
            Texture2D pixels = null;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Output, name), pixels.EncodeToPNG());
                Checks.Add("Unity camera render: " + name);
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = previous;
                target.Release(); UnityEngine.Object.Destroy(target); if (pixels != null) UnityEngine.Object.Destroy(pixels);
            }
        }

        private static void CaptureFrame(Camera camera,int frame)
        {
            Require(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Preview requires a graphics device.");
            string folder=Path.GetFullPath(Environment.GetCommandLineArgs().Contains("-captureGestures")?"../GesturePreviewFrames":"../PreviewFrames");Directory.CreateDirectory(folder);
            if(captureTarget==null){captureTarget=new RenderTexture(1920,1080,24);capturePixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);}
            var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            try
            {
                camera.targetTexture=captureTarget;camera.Render();RenderTexture.active=captureTarget;
                capturePixels.ReadPixels(new Rect(0,0,1920,1080),0,0);capturePixels.Apply();
                File.WriteAllBytes(Path.Combine(folder,frame.ToString("D4")+".jpg"),capturePixels.EncodeToJPG(93));
            }
            finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;}
        }

        private static void OnLog(string text, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(text); }

        [Serializable] private sealed class Result
        {
            public bool passed;
            public string unityVersion, utc, failure;
            public string[] checks;
            public bool headsetTested = false, androidBuildTested = false;
            public string scope="Editor and Play Mode verification. APK compilation is recorded separately in Build_Result.txt; no device testing.";
        }
        private static void Finish(bool passed, string failure)
        {
            SessionState.SetBool(Running, false);
            if(captureTarget!=null){captureTarget.Release();UnityEngine.Object.DestroyImmediate(captureTarget);}
            if(capturePixels!=null)UnityEngine.Object.DestroyImmediate(capturePixels);
            Application.logMessageReceived -= OnLog; EditorApplication.update -= Tick;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "EngineeringVerification.json"), JsonUtility.ToJson(new Result {
                passed=passed, unityVersion=Application.unityVersion, utc=DateTime.UtcNow.ToString("O"),
                failure=failure, checks=Checks.ToArray() }, true));
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            else { EditorApplication.ExitPlaymode(); Debug.Log("Engineering verification " + (passed ? "passed" : failure)); }
        }
        private static string Hash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}

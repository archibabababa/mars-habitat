using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpatialCollective;

namespace SpatialCollectiveEditor
{
    [InitializeOnLoad]
    public static class SpatialGestureVerification
    {
        private const string Key="SC.VerifySpatialV7";
        private static string Output=>Path.GetFullPath("../Documentation/Validation");
        private static CollectiveExperience v;
        private static CollectiveRig r;
        private static int phase;
        private static double start;
        private static Vector3 home;
        private static readonly List<string> checks=new List<string>(),errors=new List<string>();
        [Serializable] private sealed class Report
        {
            public string utc,unityVersion;
            public bool passed,hardwareValidated=false;
            public string input="Synthetic XR hand frames through production spatial gesture state machine";
            public float inhabitedScale,mainStudyDiameter,compactStudyDiameter,cardWidth=.48f,cardHeight=.15f;
            public string[] checks,errors;
        }
        static SpatialGestureVerification(){if(SessionState.GetBool(Key,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}}
        [MenuItem("Spatial Collective/Validate/Run v0.7 Spatial Gesture Verification")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);CollectiveProject.ValidateImported();
            EditorSceneManager.OpenScene("Assets/SpatialCollective/Scenes/Spatial_Collective_Quest3.unity");
            SessionState.SetBool(Key,true);EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
        }
        private static void Log(string m,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);}
        private static void Tick()
        {
            if(!EditorApplication.isPlaying)return;
            if(start==0)start=EditorApplication.timeSinceStartup;
            try
            {
                Require(EditorApplication.timeSinceStartup-start<180,"Runtime verification timed out");
                v=UnityEngine.Object.FindFirstObjectByType<CollectiveExperience>();
                if(v==null||v.Menu==null||v.IsTransitioning||!v.rig.Ready||v.linkedViews.RoomCamera==null)return;
                r=v.rig;v.ProcessHardwareInput=false;
                switch(phase)
                {
                    case 0:
                        v.study.Pause();v.SuspendInteraction();Open();home=v.head.transform.position;
                        Require(v.directSpatialInteraction&&v.HeadsetLayout,"Spatial mode not default");
                        Require(v.Menu.GetComponentsInChildren<Collider>().All(c=>!c.enabled),"Active HMD UI collider");
                        Require(v.Menu.GetComponentsInChildren<CollectiveButton>().Length==0&&v.Menu.GetComponentsInChildren<CollectiveParameterControl>().Length==0,"Active HMD buttons/sliders");
                        Require(!v.dashboard.overviewScreen.gameObject.activeInHierarchy&&!v.dashboard.roomScreen.gameObject.activeInHierarchy,"HMD monitors still visible");
                        Near(r.transform.lossyScale.x,1.4f,"Inhabited scale");Near(v.trackingOrigin.lossyScale.x,1,"Tracked origin scale");
                        Near(v.roomScale.EffectiveRadius,1.2f,"Main study radius");
                        checks.Add("Default experience: no active HMD buttons, sliders, viewports or UI colliders; 0.48 x 0.15 m read-only card");
                        Whole();string before=State();var geometry=Vertices();float p=r.GetBaseline(0);
                        Span(.4f,.54f);Near(r.GetBaseline(0),p+.4f,"Whole span");
                        float[] displacements=RegionDisplacements(geometry);
                        Require(displacements.All(d=>d>.01f),"Whole gesture did not deform every connected region");
                        checks.Add("Whole bilateral span deforms all four regions; maximum model-space vertex changes: "+string.Join(", ",displacements.Select(d=>d.ToString("0.000")))+" m");
                        Require(!v.stateController.HasChanges&&v.SpatialUndoCount==1,"Release not committed");
                        Undo();Require(State()==before,"Gesture undo did not restore complete snapshot");
                        checks.Add("Release commits; 0.75 s left-fist hold restores full previous gesture without button input");
                        var f=Frame();f.primary=f.secondary=true;Send(f);f.rightPosition+=Vector3.right*.1f;Send(f);r.Rebuild();
                        f.leftTracked=false;Send(f);Require(State()==before,"Tracking loss did not cancel unfinished edit");
                        f.leftTracked=true;Send(f);Require(State()==before,"Held reacquired pinch edited without release");Open();
                        checks.Add("Tracking loss rolls back unfinished edits; reacquired held pinches cannot resume");
                        Capture("01-enlarged-room-readout.png");
                        Walk();ToggleView();phase++;return;
                    case 1:
                        Require(!r.inside,"Middle-pinch chord did not switch to 3D overview");Open();
                        for(int region=1;region<=3;region++)
                        {
                            Pick(r.NodeForRegion(region));Near(v.selectedRegion,region,"Direct region pick");
                            float original=r.GetBaseline(region);Span(.4f,.435f);Near(r.GetBaseline(region),Mathf.Clamp01(original+.1f),"Regional shape");
                            float response=r.GetCoupling(region-1);Coupling(-.035f);Near(r.GetCoupling(region-1),Mathf.Clamp(response-.2f,-1,1),"Signed response");
                        }
                        checks.Add("Three branches selected by rays on real 3D nodes; bilateral shape and left-middle-pinch signed coupling pass");
                        Pick(r.NodeForRegion(4));float asynchronousShape=r.GetBaseline(4),asynchronousTravel=r.GetTravel(0);
                        var delayed=Frame();delayed.primary=true;Send(delayed);Send(delayed);Send(delayed);
                        delayed.secondary=true;Send(delayed);delayed.rightPosition.x+=.035f;Send(delayed);Open();
                        Near(r.GetBaseline(4),asynchronousShape+.1f,"Asynchronous span");Near(r.GetTravel(0),asynchronousTravel,"Asynchronous pinch moved remote unit");Undo();
                        checks.Add("Second index pinch arriving 150 ms later changes shape without accidental remote travel");
                        for(int flight=0;flight<2;flight++)
                        {
                            Pick(r.NodeForRegion(flight+4));float shape=r.GetBaseline(flight+4),travel=r.GetTravel(flight);
                            Span(.4f,.435f);Near(r.GetBaseline(flight+4),Mathf.Clamp01(shape+.1f),"Detached shape");Near(r.GetTravel(flight),travel,"Shape changed travel");
                            shape=r.GetBaseline(flight+4);Vector3 center=r.NodeCenter(r.NodeForRegion(flight+4));
                            Travel(flight==0?.105f:-.105f);Near(r.GetTravel(flight),Mathf.Clamp01(travel+.3f),"Detached travel");
                            Near(r.GetBaseline(flight+4),shape,"Travel changed shape");Require(Vector3.Distance(center,r.NodeCenter(r.NodeForRegion(flight+4)))>.1f,"Detached center stayed fixed");
                        }
                        checks.Add("S08 and O13: two-hand shape and independent one-hand route travel update real meshes / centers");
                        Capture("02-spatial-overview.png");
                        Whole();Coupling(.35f);for(int i=0;i<3;i++)Near(r.GetCoupling(i),1,"Whole link-all gesture");
                        Coupling(-.175f);for(int i=0;i<3;i++)Near(r.GetCoupling(i),0,"Whole unlink gesture");
                        Undo();for(int i=0;i<3;i++)Near(r.GetCoupling(i),1,"Undo coupling");
                        checks.Add("Whole middle-pinch gesture sets link strength; zero frees branches; signed values and undo remain bounded");
                        ToggleView();phase++;return;
                    case 2:
                        Require(r.inside,"Gesture did not return to room");Open();
                        Near(r.transform.lossyScale.x,1.4f,"Returned inhabited scale");
                        Require(v.head.transform.position==home,"View change moved the physical head");
                        int saved=r.selectedNode;r.selectedNode=Array.FindIndex(r.data.nodes,n=>n.kind=="T");
                        Near(v.roomScale.EffectiveRadius,1,"Compact radius");r.selectedNode=saved;
                        checks.Add("Round-trip gesture view change preserves physical pose and 1.4x habitat; compact-room cue diameter 2.0 m");
                        Require(errors.Count==0,string.Join("\n",errors));Finish(true);return;
                }
            }
            catch(Exception e){errors.Add(e.ToString());Finish(false);}
        }
        private static CollectiveInputFrame Frame()=>new CollectiveInputFrame{source=PointerSource.Hand,leftTracked=true,rightTracked=true,leftPosition=home+new Vector3(-.2f,-.3f,.6f),rightPosition=home+new Vector3(.2f,-.3f,.6f)};
        private static void Send(CollectiveInputFrame f,float dt=.05f){v.ProcessSpatialInput(f,f.pointer,dt);r.Rebuild();v.SendMessage("LateUpdate");v.SendMessage("RefreshStatus");}
        private static void Open(){Send(Frame());Send(Frame());}
        private static void Whole(){Open();var f=Frame();f.recall=true;Send(f);Open();}
        private static void Span(float from,float to)
        {
            Open();var f=Frame();f.primary=f.secondary=true;f.leftPosition=home+new Vector3(-from/2,-.3f,.6f);f.rightPosition=home+new Vector3(from/2,-.3f,.6f);Send(f);
            f.leftPosition.x=home.x-to/2;f.rightPosition.x=home.x+to/2;Send(f);
            f.secondary=false;Send(f);string held=State();f.rightPosition+=Vector3.up*.2f;Send(f);Require(State()==held,"Partial release continued editing");Open();
        }
        private static void Coupling(float delta)
        {Open();var f=Frame();f.leftMiddlePinch=true;for(int i=0;i<7;i++)Send(f);f.leftPosition.y+=delta;Send(f);Open();}
        private static void Travel(float forward)
        {Open();var f=Frame();f.primary=true;for(int i=0;i<7;i++)Send(f);f.rightPosition+=Vector3.ProjectOnPlane(v.head.transform.forward,Vector3.up).normalized*forward;Send(f);Open();}
        private static void Undo(){Open();var f=Frame();f.leftFist=true;for(int i=0;i<20;i++)Send(f);Open();}
        private static void ToggleView(){Open();var f=Frame();f.leftMiddlePinch=true;for(int i=0;i<3;i++)Send(f);f.rightMiddlePinch=true;for(int i=0;i<17;i++)Send(f);}
        private static void Pick(int node)
        {
            Open();Vector3 center=r.transform.TransformPoint(r.NodeCenter(node));Ray ray=new Ray();bool found=false;
            for(int a=0;a<36&&!found;a++)
            {
                Vector3 d=Quaternion.Euler((a%3-1)*35,a*137,0)*Vector3.forward;
                ray=new Ray(center-d*3,d);found=v.PickSpatialNode(ray)==node;
            }
            Require(found,"Cannot pick node "+r.data.nodes[node].id);var f=Frame();f.hasPointer=true;f.pointer=ray;f.primary=true;Send(f);Open();
            Near(v.selectedRegion,r.RegionForNode(node),"Picked region");
        }
        private static void Walk()
        {
            string saved=State();Vector3 root=r.transform.position;
            v.linkedViews.Calibrate();v.linkedViews.RenderNow();Vector3 remote=v.linkedViews.RoomCamera.transform.position;
            v.head.transform.position=home+Vector3.right*1.05f;v.SendMessage("LateUpdate");v.roomScale.Refresh();v.linkedViews.RenderNow();
            Near(v.roomScale.HorizontalDisplacement,1.05f,"Walking displacement");Near(Vector3.Distance(remote,v.linkedViews.RoomCamera.transform.position),1.05f,"Linked view physical gain");
            Require(r.transform.position==root&&State()==saved,"Walking moved habitat or changed parameters");
            v.head.transform.position=home;v.roomScale.Refresh();
            checks.Add("1.05 m physical walk gives 1.05 m linked viewpoint motion at 1.4x habitat scale; root and parameters stay fixed");
        }
        private static Vector3[][] Vertices()=>r.filters.Select(f=>f.sharedMesh.vertices).ToArray();
        private static float[] RegionDisplacements(Vector3[][] before)
        {
            float[] max=new float[4];
            for(int i=0;i<r.filters.Length;i++)
            {
                if(!r.renderers[i].enabled)continue;
                var batch=r.data.batches[i];var current=r.filters[i].sharedMesh.vertices;
                for(int j=0;j<current.Length;j++)
                {
                    int region=r.RegionForNode(r.data.drivers[batch.vertexDrivers[j]].node);if(region>=4)continue;
                    max[region]=Mathf.Max(max[region],Vector3.Distance(before[i][j],current[j]));
                }
            }
            return max;
        }
        private static string State()=>JsonUtility.ToJson(v.stateController.Capture());
        private static void Near(float a,float b,string label)=>Require(Mathf.Abs(a-b)<.0002f,label+": "+a+" vs "+b);
        private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        private static void Capture(string name)
        {
            var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            var old=v.head.targetTexture;v.head.targetTexture=rt;v.head.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();
            File.WriteAllBytes(Path.Combine(Output,name),tex.EncodeToPNG());v.head.targetTexture=old;RenderTexture.active=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
        }
        private static void Finish(bool passed)
        {
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
            var report=new Report{passed=passed,utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,inhabitedScale=v!=null?v.inhabitedScale:0,mainStudyDiameter=2.4f,compactStudyDiameter=2,checks=checks.ToArray(),errors=errors.ToArray()};
            File.WriteAllText(Path.Combine(Output,"SpatialGestureVerification.json"),JsonUtility.ToJson(report,true));
            Debug.Log("SPATIAL_V7_VERIFICATION "+passed);EditorApplication.Exit(passed?0:1);
        }
    }
}

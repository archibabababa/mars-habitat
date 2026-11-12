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
    public static class WorkspaceVerification
    {
        private const string Key="SC.VerifyWorkspace";
        private static string Output=>Path.GetFullPath("Builds/WorkspaceQA");
        private static CollectiveExperience v;
        private static CollectiveRig r;
        private static int phase,initialRenders,jumpIndex=-1;
        private static string beforeJumpState;
        private static double start,nextTick;
        private static Vector3 home;
        private static readonly List<string> checks=new List<string>(),errors=new List<string>();
        [Serializable] private sealed class Report
        {
            public string utc,unityVersion;
            public bool passed,hardwareValidated=false;
            public string input="Synthetic XR hand frames through production spatial gesture state machine";
            public float inhabitedScale,mainStudyDiameter,compactStudyDiameter,cardWidth=.78f,cardHeight=.19f;
            public string[] checks,errors;
        }
        static WorkspaceVerification(){if(SessionState.GetBool(Key,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}}
        [MenuItem("Spatial Collective/Validate/Run Three View and Direct Gesture Verification")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"WorkspaceVerification.json"),"{\"passed\":false,\"scope\":\"Current run incomplete\"}");CollectiveProject.ValidateImported();
            EditorSceneManager.OpenScene("Assets/SpatialCollective/Scenes/Spatial_Collective_Quest3.unity");
            SessionState.SetBool(Key,true);EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
        }
        private static void Log(string m,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);}
        private static void Tick()
        {
            if(!EditorApplication.isPlaying)return;
            if(EditorApplication.timeSinceStartup<nextTick)return;
            nextTick=EditorApplication.timeSinceStartup+.10;
            EditorApplication.QueuePlayerLoopUpdate();
            if(start==0)start=EditorApplication.timeSinceStartup;
            try
            {
                Require(EditorApplication.timeSinceStartup-start<180,"Runtime verification timed out");
                v=UnityEngine.Object.FindFirstObjectByType<CollectiveExperience>();
                if(v==null||v.Menu==null||v.IsTransitioning||!v.rig.Ready||v.linkedViews.RoomCamera==null)return;
                r=v.rig;v.ProcessHardwareInput=false;
                File.WriteAllText(Path.Combine(Output,"Progress.txt"),DateTime.UtcNow.ToString("O")+" phase="+phase+" transitioning="+v.IsTransitioning);
                switch(phase)
                {
                    case 0:
                        v.study.Pause();v.SuspendInteraction();Application.targetFrameRate=10;v.head.enabled=false;v.linkedViews.enabled=false;QualitySettings.antiAliasing=0;Open();home=v.head.transform.position;
                        Require(v.directSpatialInteraction&&v.HeadsetLayout,"Spatial mode not default");
                        Require(v.Menu.GetComponentsInChildren<Collider>().All(c=>!c.enabled),"Active HMD UI collider");
                        Require(v.Menu.GetComponentsInChildren<CollectiveButton>().Length==0&&v.Menu.GetComponentsInChildren<CollectiveParameterControl>().Length==0,"Active HMD buttons/sliders");
                        Require(v.dashboard.ParticipantViewsVisible&&v.dashboard.overviewScreen.gameObject.activeInHierarchy&&v.dashboard.roomScreen.gameObject.activeInHierarchy,"Participant previews hidden");
                        v.dashboard.presentation=CollectivePresentation.Desktop;v.head.aspect=16f/9;v.dashboard.RecenterWorkspace();v.linkedViews.RenderNow();initialRenders=v.linkedViews.RenderCount;
                        Require(v.linkedViews.OverviewCamera.clearFlags==CameraClearFlags.Skybox&&v.linkedViews.OverviewCamera.GetComponent<Skybox>().material==v.marsSky,"Whole view missing Mars background");
                        Require(RenderSettings.skybox==v.marsSky&&v.linkedViews.RoomCamera.GetComponent<Skybox>().material==v.marsSky,"Duplicated photographed interior behind deformable geometry");
                        var glass=v.Menu.Find("Participant three-view workspace/Read-only parameter glass").GetComponent<Renderer>().sharedMaterial;
                        Require(glass.color.a>.3f&&glass.color.a<.8f,"Parameter panel is not translucent");
                        Require(v.dashboard.overviewScreen.sharedMaterial.GetFloat("_Opacity")<1,"Viewport blending disabled");
                        FrameBounds();Capture("01-desktop-three-views.png");
                        v.dashboard.presentation=CollectivePresentation.Headset;v.dashboard.RecenterWorkspace();Capture("02-headset-layout-simulated.png",720,720);
                        v.dashboard.presentation=CollectivePresentation.Desktop;v.dashboard.RecenterWorkspace();
                        checks.Add("Production three-view desktop and headset presentations; Mars camera skybox; translucent read-only UI; no interactive panel hit targets");
                        Near(r.transform.lossyScale.x,1.4f,"Inhabited scale");Near(v.trackingOrigin.lossyScale.x,1,"Tracked origin scale");
                        Near(v.roomScale.EffectiveRadius,1.2f,"Main study radius");
                        checks.Add("Direct gesture workflow retained: no active UI buttons, sliders or colliders");
                        Near(v.handTravel,.22f,"Quest gesture travel calibration");
                        Whole();string before=State();var geometry=Vertices();float p=r.GetBaseline(0);
                        Span(.4f,.4f+v.handTravel*.4f);Near(r.GetBaseline(0),p+.4f,"Whole span");
                        float[] displacements=RegionDisplacements(geometry);
                        Require(displacements.All(d=>d>.01f),"Whole gesture did not deform every connected region");
                        checks.Add("Whole bilateral span deforms all four regions; maximum model-space vertex changes: "+string.Join(", ",displacements.Select(d=>d.ToString("0.000")))+" m");
                        Require(!v.stateController.HasChanges&&v.SpatialUndoCount==1,"Release not committed");
                        Undo();Require(State()==before,"Gesture undo did not restore complete snapshot");
                        checks.Add("Release commits; 0.75 s left-fist hold restores full previous gesture without button input");
                        var f=Frame();f.primary=f.secondary=true;Send(f);f.rightPosition+=Vector3.right*.1f;Send(f);r.Rebuild();
                        f.leftTracked=false;Send(f);Require(State()==before,"Tracking loss did not cancel unfinished edit");
                        f.leftTracked=true;Send(f);Require(State()==before,"Held reacquired pinch edited without release");Open();
                        Require(v.SpatialReady&&v.Feedback.Contains("Tracking restored"),"Tracking recovery did not explicitly re-arm");
                        checks.Add("Tracking loss rolls back unfinished edits; held pinches cannot resume; open hands explicitly restore readiness");
                        // Remote editing must also work while the observer stays inside the inhabited room.
                        int inhabitedNode=r.selectedNode;
                        for(int flight=0;flight<2;flight++)
                        {
                            int remoteNode=r.NodeForRegion(flight+4);
                            v.linkedViews.UpdateViewState();
                            Vector3 uv=v.linkedViews.OverviewCamera.WorldToViewportPoint(r.transform.TransformPoint(r.NodeCenter(remoteNode)));
                            Require(uv.z>0&&uv.x>0&&uv.x<1&&uv.y>0&&uv.y<1,"Remote node outside overview");
                            Vector3 point=v.dashboard.overviewScreen.transform.TransformPoint(new Vector3(uv.x-.5f,uv.y-.5f,0));
                            Ray pointer=new Ray(home,(point-home).normalized);
                            Require(v.dashboard.TryOverviewRay(pointer,out _,out _),"Overview ray did not intersect model image");
                            Require(v.PickSpatialNode(pointer)==remoteNode,"Overview ray selected the wrong remote node");
                            Open();var pick=Frame();pick.hasPointer=true;pick.pointer=pointer;pick.primary=true;Send(pick);pick.primary=false;Send(pick);Open();
                            Require(v.InspectionNode==remoteNode&&v.selectedRegion==flight+4,"Remote selection not retained");
                            Require(r.inside&&r.selectedNode==inhabitedNode&&v.head.transform.position==home,"Remote selection moved the observer");
                            CheckSelectionJitter(flight);
                            float shape=r.GetBaseline(flight+4),travel=r.GetTravel(flight);
                            Span(.4f,.4f+v.handTravel*.1f);Near(r.GetBaseline(flight+4),Mathf.Clamp01(shape+.1f),"Remote shape from room");
                            Travel((flight==0?1:-1)*v.handTravel*.3f);Near(r.GetTravel(flight),Mathf.Clamp01(travel+.3f),"Remote travel from room");
                            Undo();Undo();
                        }
                        Whole();
                        checks.Add("Inside the inhabited room: point/pinch actual S08 and O13 geometry through the live overview; bilateral shape and independent travel preserve the physical viewpoint");
                        Capture("01-enlarged-room-readout.png");
                        Walk();CheckStableWorkspace();CheckFullRange();ToggleView();phase++;return;
                    case 1:
                        Require(!r.inside,"Middle-pinch chord did not switch to 3D overview");Open();
                        Require(v.linkedViews.RenderCount>initialRenders,"Always-visible views did not refresh");
                        Require(RenderSettings.skybox==v.marsSky,"Primary spatial overview retained cabin sky");Capture("03-overview-with-Mars.png");
                        for(int region=1;region<=3;region++)
                        {
                            Pick(r.NodeForRegion(region));Near(v.selectedRegion,region,"Direct region pick");
                            float original=r.GetBaseline(region);Span(.4f,.4f+v.handTravel*.1f);Near(r.GetBaseline(region),Mathf.Clamp01(original+.1f),"Regional shape");
                            float response=r.GetCoupling(region-1);Coupling(-v.handTravel*.1f);Near(r.GetCoupling(region-1),Mathf.Clamp(response-.2f,-1,1),"Signed response");
                        }
                        checks.Add("Three branches selected by rays on real 3D nodes; bilateral shape and left-middle-pinch signed coupling pass");
                        Pick(r.NodeForRegion(4));float asynchronousShape=r.GetBaseline(4),asynchronousTravel=r.GetTravel(0);
                        var delayed=Frame();delayed.primary=true;Send(delayed);Send(delayed);Send(delayed);
                        delayed.secondary=true;Send(delayed);delayed.rightPosition.x+=v.handTravel*.1f;Send(delayed);Open();
                        Near(r.GetBaseline(4),asynchronousShape+.1f,"Asynchronous span");Near(r.GetTravel(0),asynchronousTravel,"Asynchronous pinch moved remote unit");Undo();
                        checks.Add("Second index pinch arriving 150 ms later changes shape without accidental remote travel");
                        for(int flight=0;flight<2;flight++)
                        {
                            Pick(r.NodeForRegion(flight+4));CheckSelectionJitter(flight);
                            float shape=r.GetBaseline(flight+4),travel=r.GetTravel(flight);
                            Span(.4f,.4f+v.handTravel*.1f);Near(r.GetBaseline(flight+4),Mathf.Clamp01(shape+.1f),"Detached shape");Near(r.GetTravel(flight),travel,"Shape changed travel");
                            Capture(flight==0?"04-S08-shape.png":"06-O13-shape.png");
                            Require(v.linkedViews.RoomCamera.orthographic&&!v.linkedViews.DetailIsRoute,"Remote shape view is not a stable close-up");
                            shape=r.GetBaseline(flight+4);Vector3 center=r.NodeCenter(r.NodeForRegion(flight+4));
                            Travel((flight==0?1:-1)*v.handTravel*.3f);Near(r.GetTravel(flight),Mathf.Clamp01(travel+.3f),"Detached travel");
                            Near(r.GetBaseline(flight+4),shape,"Travel changed shape");Require(Vector3.Distance(center,r.NodeCenter(r.NodeForRegion(flight+4)))>.1f,"Detached center stayed fixed");
                            Require(v.linkedViews.DetailIsRoute,"Travel did not select the fixed route view");Capture(flight==0?"05-S08-route.png":"07-O13-route.png");
                        }
                        checks.Add("S08 and O13: two-hand shape and independent one-hand route travel update real meshes / centers");
                        Capture("02-spatial-overview.png");
                        Whole();Coupling(v.handTravel);for(int i=0;i<3;i++)Near(r.GetCoupling(i),1,"Whole link-all gesture");
                        Coupling(-v.handTravel*.5f);for(int i=0;i<3;i++)Near(r.GetCoupling(i),0,"Whole unlink gesture");
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
                        Require(RenderSettings.skybox==v.marsSky,"Returning inside must retain Mars outside the actual deformable cabin");
                        Capture("08-desktop-4x3.png",720,540);
                        v.dashboard.presentation=CollectivePresentation.Headset;Capture("09-headset-final-simulated.png",720,720);v.dashboard.presentation=CollectivePresentation.Automatic;v.dashboard.RecenterWorkspace();
                        checks.Add("Linked views remain visible after gesture view transitions; detached shape and travel use context-appropriate cameras; 16:9, 4:3 and headset layouts fit camera bounds");
                        v.linkedViews.enabled=true;initialRenders=v.linkedViews.RenderCount;start=EditorApplication.timeSinceStartup;phase++;return;
                    case 3:
                        if(EditorApplication.timeSinceStartup-start<.5)return;
                        Require(v.linkedViews.RenderCount>initialRenders,"Production previews did not refresh autonomously");
                        checks.Add("Production LateUpdate refreshed both previews without a manual RenderNow call");
                        int target=r.NodeForRegion(4);
                        Vector3 destinationUv=v.linkedViews.OverviewCamera.WorldToViewportPoint(r.transform.TransformPoint(r.NodeCenter(target)));
                        Vector3 destinationPoint=v.dashboard.overviewScreen.transform.TransformPoint(new Vector3(destinationUv.x-.5f,destinationUv.y-.5f,0));
                        Open();var destinationPick=Frame();destinationPick.hasPointer=true;destinationPick.pointer=new Ray(home,(destinationPoint-home).normalized);destinationPick.primary=true;Send(destinationPick);Open();
                        Require(v.InspectionNode==target&&r.selectedNode!=target,"Destination selection must preserve current room until the view gesture");
                        ToggleView();phase++;return;
                    case 4:
                        Require(r.inside&&r.selectedNode==r.NodeForRegion(4),"View gesture did not directly enter selected remote cabin");
                        Require(v.head.transform.position==home,"Cabin entry moved the tracked physical head");
                        checks.Add("Selecting S08 in the overview then using the view gesture directly enters S08 from inside another cabin; physical head pose is preserved");
                        CheckCabinAppearance();Capture("12-white-S08-cabin.png");
                        beforeJumpState=State();
                        CheckJumpCancellation();start=EditorApplication.timeSinceStartup;phase++;return;
                    case 5:
                        if(jumpIndex>=0)
                        {
                            Require(r.inside&&r.selectedNode==jumpIndex,"One-hand jump entered incorrect cabin: "+jumpIndex);
                            Require(v.head.transform.position==home,"Jump changed tracked head pose");
                            Require(State()==beforeJumpState,"Jump changed deformation parameters");
                            Vector3 floor=r.transform.TransformPoint(r.NodeFloor(jumpIndex));
                            Near(floor.x,home.x,"Landing floor x");Near(floor.z,home.z,"Landing floor z");Near(floor.y,v.trackingOrigin.position.y,"Landing floor height");
                            CheckCabinAppearance();
                            if(r.data.nodes[jumpIndex].id=="S05"||r.data.nodes[jumpIndex].id=="O13")
                                Capture("13-white-"+r.data.nodes[jumpIndex].id+".png");
                        }
                        jumpIndex++;
                        if(jumpIndex<r.data.nodes.Length){Jump(jumpIndex);return;}
                        checks.Add("Right middle pinch with 0.65 second hold jumps to each of the 27 cabins; alternating open/closed off-hand, destination, physical pose, floor height, geometry state, white current-cabin ID, clear Mars exterior and all three views remain correct");
                        Require(errors.Count==0,string.Join("\n",errors));Finish(true);return;
                }
            }
            catch(Exception e){errors.Add(e.ToString());Finish(false);}
        }
        private static Ray OverviewPointer(int node)
        {
            v.linkedViews.RenderNow();
            Vector3 uv=v.linkedViews.OverviewCamera.WorldToViewportPoint(r.transform.TransformPoint(r.NodeCenter(node)));
            Vector3 point=v.dashboard.overviewScreen.transform.TransformPoint(new Vector3(uv.x-.5f,uv.y-.5f,0));
            return new Ray(home,(point-home).normalized);
        }
        private static void Jump(int node)
        {
            Open();var f=Frame();f.hasPointer=true;f.pointer=OverviewPointer(node);
            Require(v.PickSpatialNode(f.pointer)==node,"Overview cannot select cabin centre "+r.data.nodes[node].id);
            f.rightMiddlePinch=true;f.leftFist=(node%2)==0; // Natural off-hand closure must not block jumping or invoke undo.
            for(int i=0;i<5;i++)Send(f);
            Require(!v.IsTransitioning,"Jump triggered before hold completed");
            // Jitter off target after onset must keep the initially confirmed destination.
            f.pointer=new Ray(home,Vector3.up);
            for(int i=0;i<10;i++)Send(f);
            Require(v.IsTransitioning,"Deliberate middle hold did not start jump");
        }
        private static void CheckJumpCancellation()
        {
            int current=r.selectedNode;Open();var f=Frame();f.hasPointer=true;f.pointer=OverviewPointer(r.NodeForRegion(5));f.rightMiddlePinch=true;f.leftFist=true; // Natural off-hand closure must not block jumping or invoke undo.
            for(int i=0;i<5;i++)Send(f);Open();Require(!v.IsTransitioning&&r.selectedNode==current,"Released short jump changed room");
            f.rightMiddlePinch=true;for(int i=0;i<5;i++)Send(f);f.rightTracked=false;Send(f);f.rightTracked=true;
            for(int i=0;i<20;i++)Send(f);Require(!v.IsTransitioning&&r.selectedNode==current,"Tracking recovery resumed a held jump without release");Open();
            Require(!v.JumpToCabin(-1)&&!v.JumpToCabin(r.data.nodes.Length),"Invalid jump destination accepted");
            checks.Add("Short middle pinch cancels; tracking loss cancels dwell and requires release; invalid destinations rejected; hold target stays locked against late pointer jitter");
        }
        private static void CheckCabinAppearance()
        {
            var filter=v.head.GetComponent<CollectiveViewFilter>();filter.Apply();
            Near(Shader.GetGlobalFloat("_SCCurrentNode"),r.selectedNode,"Current white cabin ID");
            for(int n=0;n<r.data.nodes.Length;n++)Near(filter.Weight(n),n==r.selectedNode?1:0,"Window exterior context mask");
            var overview=v.linkedViews.OverviewCamera.GetComponent<CollectiveViewFilter>();overview.Apply();
            for(int n=0;n<r.data.nodes.Length;n++)Near(overview.Weight(n),1,"Whole context visibility");
            Near(Shader.GetGlobalFloat("_SCCurrentNode"),r.selectedNode,"Overview white cabin ID");
            for(int b=0;b<r.renderers.Length;b++)
            {
                var kind=r.data.batches[b].kind;var material=r.renderers[b].sharedMaterial;
                if(kind=="glass")Require(material.GetFloat("_SCCurrentOpacity")==0&&material.color.a<=.12f,"Glass painted white or became opaque");
                if(kind=="shell"||kind=="floor")Require(material.GetFloat("_SCCurrentOpacity")>=.9f,"Current cabin solid surface too transparent");
            }
            Require(v.head.clearFlags==CameraClearFlags.Skybox&&v.head.GetComponent<Skybox>().material==v.marsSky,"Head window exterior is not Mars");
            v.Execute(CollectiveCommand.ToggleBackground,0);Require(RenderSettings.skybox==v.marsSky,"Legacy background key replaced Mars");
            Require(v.dashboard.ParticipantViewsVisible,"Jump hid participant views");
        }
        private static CollectiveInputFrame Frame()=>new CollectiveInputFrame{source=PointerSource.Hand,leftTracked=true,rightTracked=true,leftPosition=home+new Vector3(-.2f,-.3f,.6f),rightPosition=home+new Vector3(.2f,-.3f,.6f)};
        private static void Send(CollectiveInputFrame f,float dt=.05f){v.ProcessSpatialInput(f,f.pointer,dt);r.EvaluatePose();v.SendMessage("LateUpdate");v.SendMessage("RefreshStatus");v.linkedViews.UpdateViewState();}
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
        {Open();var f=Frame();f.primary=true;for(int i=0;i<7;i++)Send(f);f.rightPosition+=Vector3.ProjectOnPlane(v.head.transform.forward,Vector3.up).normalized*(forward+Mathf.Sign(forward)*CollectiveExperience.OneHandDeadZone);Send(f);Open();}
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
        private static void CheckSelectionJitter(int flight)
        {
            string before=State();int undo=v.SpatialUndoCount;Open();
            var f=Frame();f.primary=true;Send(f);f.rightPosition+=Vector3.up*.02f;
            for(int i=0;i<7;i++)Send(f);
            f.rightPosition+=Vector3.ProjectOnPlane(v.head.transform.forward,Vector3.up).normalized*.004f;
            Send(f);Open();
            Require(State()==before&&v.SpatialUndoCount==undo,"Selecting or holding a cabin with small pinch jitter changed its state");
            checks.Add("Cabin "+flight+": pinch closure and 4 mm hold jitter do not move the route or create undo history");
        }
        private static void CheckFullRange()
        {
            Whole();string before=State();
            Span(.4f,.4f-v.handTravel);Near(r.GetBaseline(0),0,"Whole lower endpoint");
            var folded=Vertices();Capture("10-whole-minimum.png");
            Span(.4f,.4f+v.handTravel);Near(r.GetBaseline(0),1,"Whole upper endpoint");
            var displacement=RegionDisplacements(folded);
            Require(displacement.All(d=>d>.01f),"Full-range gesture did not affect every connected region");
            Capture("11-whole-maximum.png");
            Span(.4f,.4f+v.handTravel);Near(r.GetBaseline(0),1,"Whole upper bound remains clamped");
            Require(v.SpatialHint.Contains("MIDDLE"),"Released edit returns to navigation guidance");
            Undo();Undo();Require(State()==before,"Full-range check did not restore original state");
            checks.Add("22 cm bilateral separation covers p=0 to p=1; per-region maximum vertex displacement (source metres): "+string.Join(", ",displacement.Select(d=>d.ToString("0.000"))));
        }
        private static void CheckStableWorkspace()
        {
            v.dashboard.presentation=CollectivePresentation.Headset;v.dashboard.RecenterWorkspace();
            Quaternion rotation=v.head.transform.rotation, panelRotation=v.Menu.rotation;
            v.head.transform.rotation=Quaternion.LookRotation(v.dashboard.overviewScreen.transform.position-v.head.transform.position);
            v.dashboard.SendMessage("LateUpdate");
            Require(Quaternion.Angle(v.Menu.rotation,panelRotation)<.001f,"Preview chased the viewer while reading/aiming");
            var readOnlyPoint=v.dashboard.roomScreen.transform.position;
            Require(v.PickSpatialNode(new Ray(v.head.transform.position,readOnlyPoint-v.head.transform.position))==-1,"Read-only detail screen clicked through to geometry");
            v.head.transform.rotation=rotation;v.dashboard.presentation=CollectivePresentation.Desktop;v.dashboard.RecenterWorkspace();
            checks.Add("Headset preview remains stable while viewed; read-only detail/parameter surfaces do not select geometry behind them");
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
        private static void FrameBounds()
        {
            foreach(var screen in new[]{v.dashboard.overviewScreen,v.dashboard.roomScreen})
            foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f})
            {
                Vector3 p=v.head.WorldToViewportPoint(screen.transform.TransformPoint(new Vector3(x,y,0)));
                Require(p.z>0&&p.x>.005f&&p.x<.995f&&p.y>.005f&&p.y<.995f,"Live viewport clipped: "+screen.name+" "+p);
            }
        }
        private static void Capture(string name,int width=960,int height=540)
        {
            var rt=new RenderTexture(width,height,24);var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
            var old=v.head.targetTexture;float aspect=v.head.aspect;var active=RenderTexture.active;
            v.head.targetTexture=rt;v.head.aspect=width/(float)height;v.dashboard.RecenterWorkspace();FrameBounds();v.linkedViews.RenderNow();v.head.Render();
            RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(Output,name),tex.EncodeToPNG());
            v.head.targetTexture=old;v.head.aspect=aspect;v.dashboard.RecenterWorkspace();RenderTexture.active=active;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
        }
        private static void Finish(bool passed)
        {
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
            var report=new Report{passed=passed,utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,inhabitedScale=v!=null?v.inhabitedScale:0,mainStudyDiameter=2.4f,compactStudyDiameter=2,checks=checks.ToArray(),errors=errors.ToArray()};
            File.WriteAllText(Path.Combine(Output,"WorkspaceVerification.json"),JsonUtility.ToJson(report,true));
            Debug.Log("WORKSPACE_VERIFICATION "+passed);EditorApplication.Exit(passed?0:1);
        }
    }
}

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
        private static bool InteriorRepair=>Environment.GetEnvironmentVariable("SC_VERIFY_SCOPE")=="InteriorRepair";
        private static CollectiveExperience v;
        private static CollectiveRig r;
        private static int phase,initialRenders,jumpIndex=-1;
        private static string beforeJumpState;
        private static int previousRoomBeforeClick;
        private static Vector3 previousEyeOffset;
        private static Quaternion previousEyeRotation;
        private static double start,nextTick;
        private static Vector3 home;
        private static readonly List<string> checks=new List<string>(),errors=new List<string>();
        [Serializable] private sealed class Report
        {
            public string utc,unityVersion;
            public bool passed,hardwareValidated=false;
            public string input="Synthetic XR hand frames through production spatial gesture state machine";
            public string scope;
            public float inhabitedScale,mainStudyDiameter,compactStudyDiameter,cardWidth=CollectiveDashboard.ReadoutWidth,cardHeight=CollectiveDashboard.ReadoutHeight;
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
                        Require(CollectiveInputReader.EvaluateMiddlePinch(.009f,.020f,false,.022f,.032f),"Nearby index finger blocked deliberate middle pinch");
                        Require(!CollectiveInputReader.EvaluateMiddlePinch(.020f,.009f,false,.022f,.032f),"Index pinch was misread as a cabin jump");
                        Require(!CollectiveInputReader.EvaluateMiddlePinch(.015f,.015f,false,.022f,.032f),"Ambiguous finger contact triggered navigation");
                        checks.Add("Middle/index overlap resolves to the closer finger; ambiguous contact cannot initiate a jump");
                        Require(v.directSpatialInteraction&&v.HeadsetLayout,"Spatial mode not default");
                        Require(v.Menu.GetComponentsInChildren<Collider>().All(c=>!c.enabled),"Active HMD UI collider");
                        Require(v.Menu.GetComponentsInChildren<CollectiveButton>().Length==0&&v.Menu.GetComponentsInChildren<CollectiveParameterControl>().Length==0,"Active HMD buttons/sliders");
                        Require(v.dashboard.ParticipantViewsVisible&&v.dashboard.overviewScreen.gameObject.activeInHierarchy&&v.dashboard.roomScreen.gameObject.activeInHierarchy,"Participant previews hidden");
                        v.dashboard.presentation=CollectivePresentation.Desktop;v.head.aspect=16f/9;v.dashboard.RecenterWorkspace();v.linkedViews.RenderNow();initialRenders=v.linkedViews.RenderCount;
                        Require(v.linkedViews.OverviewCamera.clearFlags==CameraClearFlags.Skybox&&v.linkedViews.OverviewCamera.GetComponent<Skybox>().material==v.marsSky,"Whole view missing Mars background");
                        Require(v.head.GetComponent<Skybox>().material==v.cabinSky&&(v.head.cullingMask&(1<<8))!=0,"Physical cabin backdrop or remote interior missing");
                        Require(RenderSettings.skybox==v.marsSky&&v.linkedViews.RoomCamera.GetComponent<Skybox>().material==v.marsSky,"Linked views lost Mars background");
                        var glass=v.Menu.Find("Participant three-view workspace/Read-only parameter glass").GetComponent<Renderer>().sharedMaterial;
                        Require(glass.color.a>.3f&&glass.color.a<.8f,"Parameter panel is not translucent");
                        Require(v.dashboard.overviewScreen.sharedMaterial.GetFloat("_Opacity")<1,"Viewport blending disabled");
                        FrameBounds();Capture("01-desktop-three-views.png");
                        checks.Add("Unified right-column readout stays below both views; enlarged overview keeps model vertices in frame");
                        v.dashboard.presentation=CollectivePresentation.Headset;v.dashboard.RecenterWorkspace();
                        CheckStatusPosition();Capture("02-headset-layout-simulated.png",720,720);
                        v.dashboard.presentation=CollectivePresentation.Desktop;v.dashboard.RecenterWorkspace();
                        checks.Add("Production three-view desktop and headset presentations; Mars camera skybox; translucent read-only UI; no interactive panel hit targets");
                        Near(r.transform.lossyScale.x,1.4f,"Inhabited scale");Near(v.trackingOrigin.lossyScale.x,1,"Tracked origin scale");
                        Near(v.roomScale.EffectiveRadius,1.2f,"Main study radius");
                        checks.Add("Direct gesture workflow retained: no active UI buttons, sliders or colliders");
                        Near(v.handTravel,.22f,"Authored travel calibration");CheckShapeResponse();
                        Whole();string before=State();var geometry=Vertices();float p=r.GetBaseline(0);
                        Span(.4f,.4f+v.shapeHandTravel*.4f);Near(r.GetBaseline(0),p+.4f,"Whole span");
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
                            Open();var hover=Frame();hover.hasPointer=true;hover.pointer=pointer;Send(hover);
                            Require(v.SpatialHoverNode==remoteNode&&v.SpatialHint.Contains("AIM "+r.data.nodes[remoteNode].id),"Pointed cabin has no target-name feedback");
                            var line=UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None).Single(l=>l.name=="Hand or controller pointer");
                            Require(line.enabled&&Vector3.Distance(line.GetPosition(1),point)<.02f,"Visible hand ray did not reach the overview hit");
                            // Exercise the retained region API without navigating; participant clicks are tested below.
                            v.Execute(CollectiveCommand.SelectRegion,flight+4);Open();
                            Require(v.InspectionNode==remoteNode&&v.selectedRegion==flight+4,"Remote selection not retained");
                            Require(r.inside&&r.selectedNode==inhabitedNode&&v.head.transform.position==home,"Remote selection moved the observer");
                            int detailNode=remoteNode==inhabitedNode?r.NodeForRegion(flight==0?5:4):remoteNode;
                            Require(v.linkedViews.RoomId==r.data.nodes[detailNode].id,"Detail view does not show the other remote cabin");
                            CheckSelectionJitter(flight);
                            float shape=r.GetBaseline(flight+4),travel=r.GetTravel(flight);
                            Span(.4f,.4f+v.shapeHandTravel*.1f);Near(r.GetBaseline(flight+4),Mathf.Clamp01(shape+.1f),"Remote shape from room");
                            Travel((flight==0?1:-1)*v.handTravel*.3f);Near(r.GetTravel(flight),Mathf.Clamp01(travel+.3f),"Remote travel from room");
                            Require(!v.linkedViews.RoomCamera.orthographic&&!v.linkedViews.DetailIsRoute,"Remote interior switched to an exterior route view");
                            Undo();Undo();
                        }
                        Whole();
                        checks.Add("Inside the inhabited room: visible ray and AIM name identify S08/O13 before pinch; bilateral shape and independent travel preserve the physical viewpoint");
                        Capture("01-enlarged-room-readout.png");
                        Walk();CheckStableWorkspace();CheckFullRange();
                        // Select the occupied cabin before the original view chord requests overview.
                        Open();var occupiedPick=Frame();occupiedPick.hasPointer=true;occupiedPick.pointer=OverviewPointer(r.selectedNode);occupiedPick.primary=true;Send(occupiedPick);Open();
                        Require(v.InspectionNode==r.selectedNode,"Overview exit target is not the occupied cabin");
                        ToggleView();phase++;return;
                    case 1:
                        Require(!r.inside,"Middle-pinch chord did not switch to 3D overview");Open();
                        Require(v.linkedViews.RenderCount>initialRenders,"Always-visible views did not refresh");
                        Require(RenderSettings.skybox==v.marsSky,"Primary spatial overview retained cabin sky");Capture("03-overview-with-Mars.png");
                        for(int region=1;region<=3;region++)
                        {
                            Pick(r.NodeForRegion(region));Near(v.selectedRegion,region,"Direct region pick");
                            float original=r.GetBaseline(region);Span(.4f,.4f+v.shapeHandTravel*.1f);Near(r.GetBaseline(region),Mathf.Clamp01(original+.1f),"Regional shape");
                            float response=r.GetCoupling(region-1);Coupling(-v.handTravel*.1f);Near(r.GetCoupling(region-1),Mathf.Clamp(response-.2f,-1,1),"Signed response");
                        }
                        checks.Add("Three branches selected by rays on real 3D nodes; bilateral shape and left-middle-pinch signed coupling pass");
                        Pick(r.NodeForRegion(4));float asynchronousShape=r.GetBaseline(4),asynchronousTravel=r.GetTravel(0);
                        var delayed=Frame();delayed.primary=true;Send(delayed);Send(delayed);Send(delayed);
                        delayed.secondary=true;Send(delayed);delayed.rightPosition.x+=v.shapeHandTravel*.1f+CollectiveExperience.OneHandDeadZone;HoldShape(delayed);Open();
                        Near(r.GetBaseline(4),asynchronousShape+.1f,"Asynchronous span");Near(r.GetTravel(0),asynchronousTravel,"Asynchronous pinch moved remote unit");Undo();
                        checks.Add("Second index pinch arriving 150 ms later changes shape without accidental remote travel");
                        for(int flight=0;flight<2;flight++)
                        {
                            Pick(r.NodeForRegion(flight+4));CheckSelectionJitter(flight);
                            float shape=r.GetBaseline(flight+4),travel=r.GetTravel(flight);
                            Span(.4f,.4f+v.shapeHandTravel*.1f);Near(r.GetBaseline(flight+4),Mathf.Clamp01(shape+.1f),"Detached shape");Near(r.GetTravel(flight),travel,"Shape changed travel");
                            Capture(flight==0?"04-S08-shape.png":"06-O13-shape.png");
                            Require(!v.linkedViews.RoomCamera.orthographic&&!v.linkedViews.DetailIsRoute,"Previous interior changed into an exterior close-up");
                            shape=r.GetBaseline(flight+4);Vector3 center=r.NodeCenter(r.NodeForRegion(flight+4));
                            Travel((flight==0?1:-1)*v.handTravel*.3f);Near(r.GetTravel(flight),Mathf.Clamp01(travel+.3f),"Detached travel");
                            Near(r.GetBaseline(flight+4),shape,"Travel changed shape");Require(Vector3.Distance(center,r.NodeCenter(r.NodeForRegion(flight+4)))>.1f,"Detached center stayed fixed");
                            Require(!v.linkedViews.DetailIsRoute,"Previous interior changed into a route view");Capture(flight==0?"05-S08-route.png":"07-O13-route.png");
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
                        Require(v.head.GetComponent<Skybox>().material==v.cabinSky&&(v.head.cullingMask&(1<<8))!=0,"Returning inside did not restore the physical cabin backdrop");
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
                        Require(v.IsTransitioning&&r.selectedNode!=target,"Overview click did not begin direct cabin entry");
                        phase++;return;
                    case 4:
                        Require(r.inside&&r.selectedNode==r.NodeForRegion(4),"View gesture did not directly enter selected remote cabin");
                        Require(v.head.transform.position==home,"Cabin entry moved the tracked physical head");
                        checks.Add("Overview index-pinch click directly enters S08 on release; physical head pose is preserved");
                        CheckCabinAppearance();Capture("12-white-S08-cabin.png");
                        beforeJumpState=State();
                        CheckJumpCancellation();CheckClickCancellation();start=EditorApplication.timeSinceStartup;
                        if(InteriorRepair){Jump(r.NodeForRegion(5));phase=6;return;}
                        phase++;return;
                    case 5:
                        if(jumpIndex>=0)
                        {
                            Require(r.inside&&r.selectedNode==jumpIndex,"Overview click entered incorrect cabin: "+jumpIndex);
                            Require(v.head.transform.position==home,"Jump changed tracked head pose");
                            Require(State()==beforeJumpState,"Jump changed deformation parameters");
                            Vector3 floor=r.transform.TransformPoint(r.NodeFloor(jumpIndex));
                            Near(floor.x,home.x,"Landing floor x");Near(floor.z,home.z,"Landing floor z");Near(floor.y,v.trackingOrigin.position.y,"Landing floor height");
                            CheckCabinAppearance();
                            Require(v.PreviousInteriorNode==previousRoomBeforeClick,"Previous room history was overwritten");
                            Near(Vector3.Distance(v.linkedViews.RoomCamera.transform.position,r.transform.TransformPoint(r.NodeFloor(previousRoomBeforeClick)+previousEyeOffset)),0,"Previous interior eye position");
                            Require(Quaternion.Angle(v.linkedViews.RoomCamera.transform.rotation,r.transform.rotation*previousEyeRotation)<.1f,"Previous interior eye direction changed");
                            CheckEnteredRoomDeformation();
                            if(r.data.nodes[jumpIndex].id=="S05"||r.data.nodes[jumpIndex].id=="O13")
                                Capture("13-white-"+r.data.nodes[jumpIndex].id+".png");
                        }
                        jumpIndex++;
                        if(jumpIndex<r.data.nodes.Length){ClickCabin(jumpIndex);return;}
                        checks.Add("Overview index-pinch clicks enter all 28 cabins, with the off-hand either visible or untracked; previous interior eye pose, destination, floor height, parameters and three views remain correct");
                        checks.Add("All 28 destinations accept inflation/deflation through the existing regional deformation rules; S08/O13 remain independent; previous-room live geometry updates when its region is linked; undo restores each edit");
                        Require(errors.Count==0,string.Join("\n",errors));Finish(true);return;
                    case 6:
                        Require(r.inside&&r.selectedNode==r.NodeForRegion(5),"Right middle pinch did not enter O13");
                        Require(v.head.transform.position==home&&State()==beforeJumpState,"Jump changed the physical head or parameters");
                        CheckCabinAppearance();Capture("14-O13-interior-repair.png");
                        checks.Add("Interior repair scope: S08 and O13 entry, actual occupied-cabin rendering, bottom status and target feedback; exhaustive 28-cabin sweep not repeated");
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
            f.rightMiddlePinch=true;f.leftFist=(node%2)==0;
            f.leftTracked=(node%2)==0; // The off-hand may be closed or outside the tracking field during navigation.
            for(int i=0;i<5;i++)Send(f);
            Require(!v.IsTransitioning,"Jump triggered before hold completed");
            // Jitter off target after onset must keep the initially confirmed destination.
            f.pointer=new Ray(home,Vector3.up);
            for(int i=0;i<10;i++)Send(f);
            Require(v.IsTransitioning,"Deliberate middle hold did not start jump");
        }
        private static void ClickCabin(int node)
        {
            Open();previousRoomBeforeClick=r.selectedNode;
            previousEyeOffset=r.transform.InverseTransformPoint(v.head.transform.position)-r.NodeFloor(r.selectedNode);
            previousEyeRotation=Quaternion.Inverse(r.transform.rotation)*v.head.transform.rotation;
            var f=Frame();f.hasPointer=true;f.pointer=OverviewPointer(node);f.leftTracked=node%2==0;
            Require(v.PickSpatialNode(f.pointer)==node,"Overview cannot point at "+r.data.nodes[node].id);
            Send(f);f.primary=true;for(int i=0;i<8;i++)Send(f);
            Require(!v.IsTransitioning&&State()==beforeJumpState,"Holding an overview click edited or entered a room");
            f.pointer=new Ray(home,Vector3.up);f.primary=false;Send(f);
            Require(v.IsTransitioning,"Releasing overview click did not enter its original target");
        }
        private static void CheckClickCancellation()
        {
            int current=r.selectedNode,previous=v.PreviousInteriorNode;string state=State();int undo=v.SpatialUndoCount;
            Open();var f=Frame();f.hasPointer=true;f.pointer=OverviewPointer(r.NodeForRegion(5));f.primary=true;Send(f);
            f.rightTracked=false;Send(f);f.rightTracked=true;Send(f);f.primary=false;Send(f);Open();
            Require(!v.IsTransitioning&&r.selectedNode==current&&v.PreviousInteriorNode==previous&&State()==state,"Tracking loss did not cancel the pending click");
            f=Frame();f.hasPointer=true;f.pointer=OverviewPointer(current);f.primary=true;Send(f);f.primary=false;Send(f);Open();
            Require(!v.IsTransitioning&&v.PreviousInteriorNode==previous&&v.SpatialUndoCount==undo,"Clicking the occupied room changed history");
            f=Frame();f.hasPointer=true;f.pointer=OverviewPointer(r.NodeForRegion(5));f.primary=true;Send(f);Send(f);
            f.secondary=true;Send(f);f.rightPosition.x+=v.shapeHandTravel*.1f+CollectiveExperience.OneHandDeadZone;HoldShape(f);Open();
            Require(!v.IsTransitioning&&r.selectedNode==current&&v.PreviousInteriorNode==previous,"Second-hand pinch was misread as navigation");
            Require(State()!=state,"Asynchronous two-hand pinch failed to edit the current cabin");Undo();Require(State()==state,"Undo did not restore asynchronous edit");
            checks.Add("Tracking loss cancels overview click; same-room click preserves previous room; asynchronous second pinch edits current cabin without navigating");
        }
        private static void CheckEnteredRoomDeformation()
        {
            Open();int region=r.RegionForNode(r.selectedNode),previous=v.PreviousInteriorNode;
            Require(v.selectedRegion==region,"Entered room and gesture region differ");
            var saved=v.stateController.Capture();
            for(int i=0;i<6;i++)r.SetBaseline(i,.5f);r.Rebuild();v.stateController.Commit();
            string neutral=State();var geometry=Vertices();
            float[] actual=(float[])r.Actual.Clone();float p=r.GetBaseline(region);
            float delta=p>.8f?-.1f:.1f;Span(.4f,.4f+v.shapeHandTravel*delta);r.Rebuild();
            Near(r.GetBaseline(region),p+delta,"Entered room inflation parameter");
            for(int i=0;i<6;i++)
            {
                float expected=region==0&&i>0&&i<4?Mathf.Clamp01(r.GetBaseline(i)+(r.coupled?r.GetCoupling(i-1)*(r.GetBaseline(0)-.5f):0)):
                    i==region?Mathf.Clamp01(r.GetBaseline(i)+(i>0&&i<4&&r.coupled?r.GetCoupling(i-1)*(r.GetBaseline(0)-.5f):0)):actual[i];
                Near(r.Actual[i],expected,"Original GH regional response "+i);
            }
            Require(v.PreviousInteriorNode==previous&&v.linkedViews.RoomId==r.data.nodes[previous].id,"Deformation replaced the previous interior");
            Require(NodeDisplacement(r.selectedNode,geometry)>.001f,"Current room geometry did not deform");
            int priorRegion=r.RegionForNode(previous);
            if(Mathf.Abs(r.Actual[priorRegion]-actual[priorRegion])>.001f)
                Require(NodeDisplacement(previous,geometry)>.001f,"Linked previous-room geometry did not update");
            Undo();Require(State()==neutral,"Room deformation undo changed prior state");
            v.stateController.Restore(saved);r.Rebuild();v.stateController.Commit();Require(State()==beforeJumpState,"Room test did not restore parameters");
        }
        private static float NodeDisplacement(int node,Vector3[][] before)
        {
            float maximum=0;
            for(int b=0;b<r.filters.Length;b++)
            {
                if(!r.renderers[b].enabled)continue;
                var vertices=r.filters[b].sharedMesh.vertices;var batch=r.data.batches[b];
                for(int i=0;i<vertices.Length;i++)if(r.data.drivers[batch.vertexDrivers[i]].node==node)
                    maximum=Mathf.Max(maximum,Vector3.Distance(vertices[i],before[b][i]));
            }
            return maximum;
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
            CheckStatusPosition();
            v.linkedViews.UpdateViewState();
            int otherRemote=v.PreviousInteriorNode>=0?v.PreviousInteriorNode:r.NodeForRegion(r.selectedNode==r.NodeForRegion(4)?5:4);
            Require(v.linkedViews.RoomId==r.data.nodes[otherRemote].id,"Detail view repeats the occupied cabin");
            Require(v.linkedViews.RoomCamera.GetComponent<CollectiveViewFilter>().focusNode==otherRemote,"Detail camera and caption target differ");
            Require(!v.linkedViews.RoomCamera.orthographic&&v.linkedViews.DetailTitle.EndsWith("INTERIOR"),"Remote preview is not an interior view");
            Near(v.linkedViews.RoomCamera.transform.position.y-r.transform.TransformPoint(r.NodeFloor(otherRemote)).y,home.y-v.trackingOrigin.position.y,"Remote interior eye height");
            int remoteRegion=r.RegionForNode(otherRemote);
            Require(v.linkedViews.DetailNote==$"SHAPE {r.Actual[remoteRegion]:0.00}"+(remoteRegion>=4?$"    TRAVEL {r.GetTravel(remoteRegion-4):0.00}":""),"Detail values refer to a different cabin");
            var filter=v.head.GetComponent<CollectiveViewFilter>();filter.Apply();
            Near(Shader.GetGlobalFloat("_SCCurrentNode"),r.selectedNode,"Main overlay lost the authored white cabin color");
            Near(Shader.GetGlobalFloat("_SCOverlayView"),1,"Main overlay did not suppress floating virtual floors");
            Near(Shader.GetGlobalFloat("_SCInteriorView"),0,"Main overlay lost its authored transparency");
            for(int b=0;b<r.filters.Length;b++)Require(r.filters[b].sharedMesh.uv2.All(uv=>uv.y==(r.data.batches[b].kind=="floor"?1:0)),"Floor visibility tags do not match imported batches");
            int focus=r.selectedNode;
            int[] context=r.data.nodes[focus].detached
                ?Enumerable.Range(0,r.data.nodes.Length).Where(n=>n!=focus).OrderBy(n=>(r.NodeCenter(n)-r.NodeCenter(focus)).sqrMagnitude).Take(2).ToArray()
                :r.data.nodes[focus].neighbors;
            for(int n=0;n<r.data.nodes.Length;n++)
                Near(filter.Weight(n),n==focus?1:context.Contains(n)?.65f:0,"Main nearby-cabin context");
            Require(filter.VisibleCount==context.Length+1,"Main context contains unrelated cabins");
            var detail=v.linkedViews.RoomCamera.GetComponent<CollectiveViewFilter>();
            for(int n=0;n<r.data.nodes.Length;n++)Near(detail.Weight(n),n==otherRemote?1:0,"Previous interior remains isolated");
            var overview=v.linkedViews.OverviewCamera.GetComponent<CollectiveViewFilter>();overview.Apply();
            Near(Shader.GetGlobalFloat("_SCOverlayView"),0,"Main-only floor filter leaked into the whole preview");
            for(int n=0;n<r.data.nodes.Length;n++)Near(overview.Weight(n),1,"Whole context visibility");
            Near(Shader.GetGlobalFloat("_SCCurrentNode"),r.selectedNode,"Overview white cabin ID");
            for(int b=0;b<r.renderers.Length;b++)
            {
                var kind=r.data.batches[b].kind;var material=r.renderers[b].sharedMaterial;
                if(kind=="glass")Require(material.GetFloat("_SCCurrentOpacity")==0&&material.color.a<=.12f,"Glass painted white or became opaque");
                if(kind=="shell"||kind=="floor")Require(material.GetFloat("_SCCurrentOpacity")>=.9f,"Current cabin solid surface too transparent");
            }
            Require(v.head.clearFlags==CameraClearFlags.Skybox&&v.head.GetComponent<Skybox>().material==v.cabinSky,"Physical cabin backdrop is missing");
            v.Execute(CollectiveCommand.ToggleBackground,0);Require(RenderSettings.skybox==v.marsSky,"Legacy background key replaced Mars");
            Require(v.dashboard.ParticipantViewsVisible,"Jump hid participant views");
        }
        private static void CheckStatusPosition()
        {
            var root=v.Menu.Find("Participant three-view workspace");
            var card=root.Find("Read-only parameter glass");
            Require(root.Find("Location glass")==null,"Separate central status panel remains");
            Near(card.localPosition.x,v.dashboard.roomScreen.transform.localPosition.x,"Readout column alignment");
            Require(card.localPosition.y+card.localScale.y*.5f<v.dashboard.roomScreen.transform.localPosition.y-v.dashboard.roomScreen.transform.localScale.y*.5f,"Readout overlaps the interior view");
            foreach(string name in new[]{"Current space","Current values","Detail parameters","Gesture navigation status"})
            {
                Vector3 point=card.InverseTransformPoint(root.Find(name).position);
                Require(Mathf.Abs(point.x)<.5f&&Mathf.Abs(point.y)<.5f,"Parameter label outside shared card: "+name);
            }
        }
        private static CollectiveInputFrame Frame()=>new CollectiveInputFrame{source=PointerSource.Hand,leftTracked=true,rightTracked=true,leftPosition=home+new Vector3(-.2f,-.3f,.6f),rightPosition=home+new Vector3(.2f,-.3f,.6f)};
        private static void Send(CollectiveInputFrame f,float dt=.05f){v.ProcessSpatialInput(f,f.pointer,dt);r.EvaluatePose();v.SendMessage("LateUpdate");v.SendMessage("RefreshStatus");v.linkedViews.UpdateViewState();}
        private static void Open(){Send(Frame());Send(Frame());}
        private static void Whole(){Open();var f=Frame();f.recall=true;Send(f);Open();}
        private static void Span(float from,float to)
        {
            Open();var f=Frame();f.primary=f.secondary=true;f.leftPosition=home+new Vector3(-from/2,-.3f,.6f);f.rightPosition=home+new Vector3(from/2,-.3f,.6f);Send(f);
            to+=Mathf.Sign(to-from)*CollectiveExperience.OneHandDeadZone;
            f.leftPosition.x=home.x-to/2;f.rightPosition.x=home.x+to/2;HoldShape(f);
            f.secondary=false;Send(f);string held=State();f.rightPosition+=Vector3.up*.2f;Send(f);Require(State()==held,"Partial release continued editing");Open();
        }
        private static void HoldShape(CollectiveInputFrame f)
        {for(int i=0;i<100;i++)Send(f);}
        private static void CheckShapeResponse()
        {
            Whole();string before=State();float baseline=r.GetBaseline(0);
            var f=Frame();f.primary=f.secondary=true;Send(f);
            f.rightPosition.x+=.006f;HoldShape(f);Near(r.GetBaseline(0),baseline,"6 mm hand jitter");Open();
            Require(State()==before,"Small hand jitter created a change");
            f=Frame();f.primary=f.secondary=true;Send(f);f.rightPosition.x+=.11f;HoldShape(f);Open();
            float increase=r.GetBaseline(0)-baseline;
            Require(increase>.15f&&increase<.19f,"11 cm hand motion is still too sensitive");Undo();
            float[] results=new float[3];float[] steps={1f/30,1f/72,1f/90};
            for(int rate=0;rate<steps.Length;rate++)
            {
                Open();f=Frame();f.primary=f.secondary=true;Send(f);f.rightPosition.x+=.6f;
                for(int i=0;i<Mathf.RoundToInt(.5f/steps[rate]);i++)
                {
                    float previous=r.GetBaseline(0);Send(f,steps[rate]);
                    Require(r.GetBaseline(0)-previous<=v.shapeSpeed*steps[rate]+.00001f,"Shape exceeded rate limit");
                }
                results[rate]=r.GetBaseline(0);Open();string released=State();
                for(int i=0;i<30;i++)Open();Require(State()==released,"Shape continued after release");Undo();
            }
            Require(results.Max()-results.Min()<.002f,"Shape speed depends on frame rate");
            Require(State()==before,"Comfort checks changed stored parameters");
            checks.Add("6 mm jitter ignored; 11 cm span changes shape by about 0.17; rate bounded at 0.35/s at 30/72/90 Hz; release stops immediately");
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
                ray=new Ray(center-d*3,d);found=!v.dashboard.TryOverviewRay(ray,out _,out _)&&v.PickSpatialNode(ray)==node;
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
            Span(.8f,.8f-v.shapeHandTravel);Near(r.GetBaseline(0),0,"Whole lower endpoint");
            var folded=Vertices();Capture("10-whole-minimum.png");
            Span(.4f,.4f+v.shapeHandTravel);Near(r.GetBaseline(0),1,"Whole upper endpoint");
            var displacement=RegionDisplacements(folded);
            Require(displacement.All(d=>d>.01f),"Full-range gesture did not affect every connected region");
            Capture("11-whole-maximum.png");
            Span(.4f,.4f+v.shapeHandTravel);Near(r.GetBaseline(0),1,"Whole upper bound remains clamped");
            Require(v.SpatialHint.Contains("PINCH: ENTER"),"Released edit returns to navigation guidance");
            Undo();Undo();Require(State()==before,"Full-range check did not restore original state");
            checks.Add("60 cm deliberate bilateral separation covers p=0 to p=1 after settling; per-region maximum vertex displacement (source metres): "+string.Join(", ",displacement.Select(d=>d.ToString("0.000"))));
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
            Near(v.roomScale.HorizontalDisplacement,1.05f,"Walking displacement");
            bool closeup=v.linkedViews.RoomCamera.orthographic;
            Near(Vector3.Distance(remote,v.linkedViews.RoomCamera.transform.position),closeup?0:1.05f,"Linked view walking response");
            Require(r.transform.position==root&&State()==saved,"Walking moved habitat or changed parameters");
            v.head.transform.position=home;v.roomScale.Refresh();
            checks.Add(closeup?"1.05 m physical walk preserves remote close-up framing, habitat pose and parameters":"1.05 m physical walk gives 1.05 m linked viewpoint motion at 1.4x habitat scale; root and parameters stay fixed");
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
            CheckStatusPosition();
            var card=v.Menu.Find("Participant three-view workspace/Read-only parameter glass").GetComponent<Renderer>();
            foreach(var screen in new[]{v.dashboard.overviewScreen,v.dashboard.roomScreen,card})
            foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f})
            {
                Vector3 p=v.head.WorldToViewportPoint(screen.transform.TransformPoint(new Vector3(x,y,0)));
                Require(p.z>0&&p.x>.005f&&p.x<.995f&&p.y>.005f&&p.y<.995f,"Live viewport clipped: "+screen.name+" "+p);
            }
            v.linkedViews.UpdateViewState();
            for(int b=0;b<r.filters.Length;b++)
            {
                if(!r.renderers[b].enabled)continue;
                foreach(Vector3 vertex in r.filters[b].sharedMesh.vertices)
                {
                    Vector3 p=v.linkedViews.OverviewCamera.WorldToViewportPoint(r.filters[b].transform.TransformPoint(vertex));
                    Require(p.z>0&&p.x>.001f&&p.x<.999f&&p.y>.001f&&p.y<.999f,"Enlarged overview clipped a model vertex");
                }
            }
        }
        private static void Capture(string name,int width=960,int height=540)
        {
            var rt=new RenderTexture(width,height,24);var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
            var old=v.head.targetTexture;float aspect=v.head.aspect,fov=v.head.fieldOfView;var active=RenderTexture.active;
            // Desktop cameras have a narrower field of view than the simulated headset.
            if(v.dashboard.IsHeadsetPresentation)v.head.fieldOfView=90;
            v.head.targetTexture=rt;v.head.aspect=width/(float)height;v.dashboard.RecenterWorkspace();FrameBounds();v.linkedViews.RenderNow();v.head.Render();
            RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(Output,name),tex.EncodeToPNG());
            v.head.targetTexture=old;v.head.aspect=aspect;v.head.fieldOfView=fov;v.dashboard.RecenterWorkspace();RenderTexture.active=active;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
        }
        private static void Finish(bool passed)
        {
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
            var report=new Report{passed=passed,scope=InteriorRepair?"InteriorRepair":"Full",utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,inhabitedScale=v!=null?v.inhabitedScale:0,mainStudyDiameter=2.4f,compactStudyDiameter=2,checks=checks.ToArray(),errors=errors.ToArray()};
            File.WriteAllText(Path.Combine(Output,"WorkspaceVerification.json"),JsonUtility.ToJson(report,true));
            Debug.Log("WORKSPACE_VERIFICATION "+passed);EditorApplication.Exit(passed?0:1);
        }
    }
}

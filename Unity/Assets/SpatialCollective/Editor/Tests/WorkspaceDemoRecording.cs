using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpatialCollective;
[InitializeOnLoad]
public static class WorkspaceDemoRecording
{
    private const string Running="SpatialCollective.WorkspaceDemoRecording";
    private const int Fps=12,Duration=100;
    private static int captureStart,captureEnd=Fps*Duration;
    private static double nextTick;
    private static string Output=>Path.GetFullPath("../Recordings/v0.8");
    private static bool proof,ready,leftPinched,rightPinched,leftMiddle,rightMiddle,recallSent;
    private static int frame,nextEvent,pickNode=-1;
    private static double started;
    private static float span=.46f,handShift,palmSeconds,middleAmount,pickStart=-10,middleStart=-10,palmStart=-10;
    private static CollectiveExperience view;
    private static CollectiveRig rig;
    private static Vector3 originalHead,walkRoot,walkRemote;
    private static Quaternion originalRotation,viewRotation;
    private static WorkspaceDemoHandPose left,right;
    
    private static RenderTexture mainTexture;
    private static Texture2D pixels;
    
    private static string chapter="01 / DIRECT SPATIAL GESTURES",caption="The v0.8 participant view contains the three persistent views and a read-only parameter card.\nAll recorded edits use the production C# gesture state machine.",walkingState;
    private static Segment active;
    private static readonly List<Timed> events=new List<Timed>();
    private static readonly List<string> errors=new List<string>();
    private static Evidence evidence;
    private static StreamWriter telemetry;
    private static readonly int[] ProofSeconds={0,3,7,11,14,18,22,26,28,30,34,38,40,44,48,50,52,54,58,62,64,68,72,74,78,80,84,88,92,96,99};
    private sealed class Timed{public float time;public Action run;}
    private sealed class Segment{public float begin,end,from,to,startSpan;public int region,flight;public bool travel;public string label;public Vector3[][] vertices;public Vector3 head;public Quaternion rotation;public float independent;}
    [Serializable] private sealed class MotionProof{public string label;public bool bimanual;public float startValue,target,actual,maximumVertexChangeMetres,headMovementMetres;public bool independentParameterUnchanged;}
    [Serializable] private sealed class Evidence
    {
        public string utc,unityVersion,scope="Actual Unity Play Mode participant camera and built-in three-view UI; synthetic hand poses through production gesture input. No recording-only camera or compositing. Not Quest hardware footage or a frame-rate benchmark.";
        public bool passed,hardwareCapture=false,buttonActionsUsed=false,productionSpatialStateMachine=true,readOnlyCard=true,productionThreeViewUI=true,recordingCameraComposition=false;
        public int fps=Fps,durationSeconds=Duration,frames;
        public float walkMaximumMetres,linkedViewWalkMetres,inhabitedScale=1.4f,studyDiameterMetres=2.4f;
        public bool walkPreservesParameters,walkPreservesWorldAnchor,releaseCommits,gestureViewRoundTrip;
        public List<MotionProof> motions=new List<MotionProof>();public List<string> events=new List<string>();public string[] errors;
    }
    static WorkspaceDemoRecording(){if(SessionState.GetBool(Running,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}}
    public static void Run()
    {
        Directory.CreateDirectory(Path.Combine(Output,"Frames"));Directory.CreateDirectory(Path.Combine(Output,"ProofFrames"));
        EditorSceneManager.OpenScene("Assets/SpatialCollective/Scenes/Spatial_Collective_Quest3.unity");
        SessionState.SetBool(Running,true);EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    private static void Log(string m,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);}
    private static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        if(started==0)started=EditorApplication.timeSinceStartup;
        try
        {
            Require(EditorApplication.timeSinceStartup-started<2400,"Capture timeout");
            view=UnityEngine.Object.FindFirstObjectByType<CollectiveExperience>();
            if(view==null||view.Menu==null||view.IsTransitioning||!view.rig.Ready||view.roomScale==null||view.linkedViews.RoomCamera==null){EditorApplication.QueuePlayerLoopUpdate();return;}
            if(!ready){Initialize();ready=true;}
            if(EditorApplication.timeSinceStartup<nextTick)return;
            nextTick=EditorApplication.timeSinceStartup+(frame<captureStart?.025:.10);
            if(frame>=captureEnd){Finish();return;}
            float t=frame/(float)Fps;
            while(nextEvent<events.Count&&events[nextEvent].time<=t+.0001f)events[nextEvent++].run();
            Drive(t,proof?3f/Fps:1f/Fps);
            if(frame>=captureStart&&(!proof||ProofSeconds.Any(s=>frame==s*Fps)))Capture(t);
            WriteTelemetry(t);
            if(frame%(Fps*10)==0)Debug.Log("SPATIAL_FILM_PROGRESS "+frame+" / "+Duration*Fps);
            File.WriteAllText(Path.Combine(Output,"Progress.txt"),DateTime.UtcNow.ToString("O")+" frame="+frame);
            frame+=proof?3:1;EditorApplication.QueuePlayerLoopUpdate();
        }
        catch(Exception e){errors.Add(e.ToString());Finish();}
    }
    private static void Initialize()
    {
        var args=Environment.GetCommandLineArgs();proof=args.Contains("-proofOnly");rig=view.rig;
        int index=Array.IndexOf(args,"-captureStart");if(index>=0)captureStart=int.Parse(args[index+1]);
        index=Array.IndexOf(args,"-captureEnd");if(index>=0)captureEnd=int.Parse(args[index+1]);
        Require(captureStart>=0&&captureEnd<=Duration*Fps&&captureStart<captureEnd,"Invalid capture segment");
        Application.targetFrameRate=12;QualitySettings.antiAliasing=0;view.linkedViews.enabled=false;
        view.ProcessHardwareInput=false;view.SuspendInteraction();view.study.Pause();rig.animateDetached=false;
        Require(view.HeadsetLayout&&view.directSpatialInteraction,"Wrong participant workflow");
        Require(view.Menu.GetComponentsInChildren<Collider>().All(c=>!c.enabled),"Interactive panel hit target still active");
        originalHead=view.head.transform.position;originalRotation=viewRotation=view.head.transform.rotation;
        view.head.cullingMask|=1<<12;view.head.enabled=false;
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/SpatialCollective/Materials/TrackedHand.mat");
        left=new WorkspaceDemoHandPose(true,material);right=new WorkspaceDemoHandPose(false,material);
        mainTexture=Texture(960,540);pixels=new Texture2D(960,540,TextureFormat.RGB24,false);
        view.dashboard.presentation=CollectivePresentation.Desktop;view.head.aspect=1920f/1080;
        view.dashboard.RecenterWorkspace();BuildEvents();
        evidence=new Evidence{utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion};
        telemetry=new StreamWriter(Path.Combine(Output,"telemetry-"+captureStart.ToString("D5")+".csv"));
        telemetry.WriteLine("frame,seconds,chapter,leftPinch,rightPinch,leftMiddle,rightMiddle,span,leftTip,rightTip,target,operation,core,long,shortA,shortB,s08Shape,o13Shape,s08Travel,o13Travel,headX,headY,headZ,inside,applied");
    }
    private static RenderTexture Texture(int w,int h){var t=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);t.Create();return t;}
    private static void Add(float time,Action action)=>events.Add(new Timed{time=time,run=action});
    private static void Chapter(string title,string text){chapter=title;caption=text;}
    private static void BuildEvents()
    {
        Add(1,()=>{palmStart=1;recallSent=false;});
        Add(3,()=>Begin(11,0,.95f,false,"02 / TWO HANDS CHANGE THE WHOLE","Spread two index pinches: the core and three linked branches change together.\nThe head and overview camera remain fixed during the edit."));Add(11,End);
        Add(13,()=>Begin(21,0,.15f,false,"03 / CLOSE BOTH HANDS","Close the hands to reverse the whole-habitat parameter.\nSigned coupling gives different, coordinated responses in the three branches."));Add(21,End);
        Add(24,()=>{middleStart=24;Chapter("04 / ENTER THE SPATIAL OVERVIEW","Hold two thumb-middle pinches to switch views.\nSelect actual 3D modules; there is no control panel.");});
        Add(27,()=>{Require(!rig.inside,"View gesture failed");SelectRemote(4);});
        Add(29,()=>Begin(37,4,.95f,false,"05 / S08 SHAPE","Two index pinches change the detached cabin's shape.\nIts travel parameter stays fixed; the lower-right view enlarges the same live mesh."));Add(37,End);
        Add(39,()=>Begin(49,4,1,true,"06 / S08 DEPARTURE","Hold only the right index pinch and push away.\nS08 follows the existing departure route; its shape stays fixed."));Add(49,End);
        Add(51,()=>SelectRemote(5));
        Add(53,()=>Begin(61,5,.1f,false,"07 / O13 SHAPE","Close two index pinches to change O13's shape.\nThe lower-right camera stays fixed so the actual deformation is visible."));Add(61,End);
        Add(63,()=>Begin(73,5,1,true,"08 / O13 APPROACH","Hold the right index pinch and pull toward the body.\nO13 approaches along its configured route without changing its shape."));Add(73,End);
        Add(75,()=>{viewRotation=originalRotation;palmStart=75;recallSent=false;pickNode=-1;Chapter("09 / RETURN TO THE INHABITED SPACE","An open left palm selects the whole habitat.\nThe parameter card is read-only and has no input controls.");});
        Add(77,()=>middleStart=77);
        Add(80,()=>{
            Require(rig.inside&&view.selectedRegion==0,"Did not return to the reference room");evidence.gestureViewRoundTrip=true;
            walkingState=JsonUtility.ToJson(view.stateController.Capture());walkRoot=rig.transform.position;view.linkedViews.Calibrate();view.linkedViews.RenderNow();walkRemote=view.linkedViews.RoomCamera.transform.position;
            Chapter("10 / WALK IN THE ENLARGED ROOM","Move 0.95 m sideways and 0.45 m forward, then return.\nHabitat scale: 1.4x. Study circle: 2.4 m diameter. Real head movement remains 1:1.");
        });
        Add(89,()=>Chapter("11 / RETURN WITH HANDS OPEN","Walking changes the viewpoint, not the shape or travel values.\nThe local scene and linked room respond to the same physical displacement."));
        Add(96,()=>Chapter("12 / COMPLETE","Direct whole-body / remote-space editing with a small read-only value card.\nActual Unity v0.8 runtime; simulated hand input. Quest testing remains separate."));
    }
    private static void SelectRemote(int region)
    {
        pickNode=rig.NodeForRegion(region);pickStart=frame/(float)Fps;
        Vector3 target=rig.transform.TransformPoint(rig.NodeCenter(pickNode));
        viewRotation=Quaternion.LookRotation(target-originalHead,Vector3.up);

        Chapter(region==4?"SELECT S08 IN SPACE":"SELECT O13 IN SPACE","Point at the actual 3D cabin and briefly pinch the right hand.\nThe small card names the target and displays its current parameters.");
        evidence.events.Add(pickStart.ToString("F2")+" Select actual node "+rig.data.nodes[pickNode].id);
    }
    private static void Begin(float end,int region,float goal,bool travel,string label,string text)
    {
        Require(view.selectedRegion==region,"Target selection failed: "+view.selectedRegion+" vs "+region);
        float current=travel?rig.GetTravel(region-4):rig.GetBaseline(region);span=goal>=current?.38f:.72f;handShift=0;
        active=new Segment{begin=frame/(float)Fps,end=end,from=current,to=goal,startSpan=span,region=region,flight=region-4,travel=travel,label=label,vertices=rig.filters.Select(f=>f.sharedMesh.vertices).ToArray(),head=view.head.transform.position,rotation=view.head.transform.rotation,independent=region<4?0:travel?rig.GetBaseline(region):rig.GetTravel(region-4)};

        Chapter(label,text);
    }
    private static void End()
    {
        Require(active!=null&&view.SpatialEditing,"No active gesture at segment end");
        float actual=active.travel?rig.GetTravel(active.flight):rig.GetBaseline(active.region);
        Require(Mathf.Abs(actual-active.to)<.004f,"Gesture target missed "+active.label+": "+actual);
        float movement=0;
        for(int b=0;b<rig.filters.Length;b++)
        {
            var current=rig.filters[b].sharedMesh.vertices;
            for(int j=0;j<current.Length;j++)movement=Mathf.Max(movement,Vector3.Distance(current[j],active.vertices[b][j]));
        }
        Require(movement>.1f,"Only numeric values changed");
        bool independent=active.region<4||Mathf.Abs((active.travel?rig.GetBaseline(active.region):rig.GetTravel(active.flight))-active.independent)<.0001f;
        Require(independent,"Shape and travel were coupled unexpectedly");
        float headMove=Vector3.Distance(active.head,view.head.transform.position);Require(headMove<.0001f&&Quaternion.Angle(active.rotation,view.head.transform.rotation)<.001f,"Head moved during a geometry edit");
        evidence.motions.Add(new MotionProof{label=active.label,bimanual=!active.travel,startValue=active.from,target=active.to,actual=actual,maximumVertexChangeMetres=movement,headMovementMetres=headMove,independentParameterUnchanged=independent});
        active=null;caption="Release the pinch to retain the result. No APPLY button is used.\nThe geometry stays in its revised state while the hands reopen.";
    }
    private static void Drive(float t,float dt)
    {
        Vector3 step=Vector3.zero;
        if(t>=81&&t<85)step=Vector3.right*.95f*Mathf.SmoothStep(0,1,(t-81)/4);
        else if(t>=85&&t<89)step=new Vector3(.95f,0,.45f*Mathf.SmoothStep(0,1,(t-85)/4));
        else if(t>=89&&t<94)step=new Vector3(.95f,0,.45f)*(1-Mathf.SmoothStep(0,1,(t-89)/5));
        view.head.transform.SetPositionAndRotation(originalHead+step,viewRotation);
        bool pick=t>=pickStart+.20f&&t<pickStart+.42f;
        bool middle=t>=middleStart&&t<middleStart+1.4f;
        bool recalling=t>=palmStart&&t<palmStart+1.5f;
        bool wantedRight=active!=null||pick,wantedLeft=active!=null&&!active.travel;
        if(active!=null)
        {
            float progress=Mathf.SmoothStep(0,1,Mathf.InverseLerp(active.begin+.55f,active.end-.5f,t));
            if(active.travel)handShift=(active.to-active.from)*view.handTravel*(active.region==5?-1:1)*progress;
            else span=active.startSpan+((active.to-active.from)*view.shapeHandTravel+Mathf.Sign(active.to-active.from)*CollectiveExperience.OneHandDeadZone)*progress;
        }
        else if(!leftPinched&&!rightPinched){span=Mathf.MoveTowards(span,.46f,dt*.15f);handShift=Mathf.MoveTowards(handShift,0,dt*.25f);}
        Quaternion q=view.head.transform.rotation;Vector3 forward=Vector3.ProjectOnPlane(q*Vector3.forward,Vector3.up).normalized;
        Vector3 centre=view.head.transform.position+q*new Vector3(0,-.20f,.65f);
        Vector3 lp=centre-q*Vector3.right*span*.5f,rp=centre+q*Vector3.right*span*.5f+forward*handShift;
        Quaternion lq=q*(recalling?Quaternion.Euler(-110,0,180):Quaternion.Euler(-28,12,-12)),rq=q*Quaternion.Euler(-28,-12,12);
        middleAmount=Mathf.MoveTowards(middleAmount,middle?1:0,dt*12);
        if(middleAmount>0){left.SetMiddle(lp,lq,middleAmount);right.SetMiddle(rp,rq,middleAmount);}
        else{left.Set(lp,lq,wantedLeft,dt,recalling);right.Set(rp,rq,wantedRight,dt,false);}
        leftPinched=CollectiveInputReader.EvaluatePinch(left.TipDistance,leftPinched,.025f,.04f);
        rightPinched=CollectiveInputReader.EvaluatePinch(right.TipDistance,rightPinched,.025f,.04f);
        leftMiddle=middleAmount>0&&CollectiveInputReader.EvaluatePinch(left.MiddleDistance,leftMiddle,.025f,.04f);
        rightMiddle=middleAmount>0&&CollectiveInputReader.EvaluatePinch(right.MiddleDistance,rightMiddle,.025f,.04f);
        bool faces=Vector3.Dot(left.palm.rotation*Vector3.down,(view.head.transform.position-left.palm.position).normalized)>.65f;
        if(recalling&&!leftPinched&&!rightPinched&&faces&&Vector3.Distance(left.indexTip.position,left.palm.position)>.09f)palmSeconds+=dt;else palmSeconds=0;
        bool recall=palmSeconds>.65f&&!recallSent;if(recall){recallSent=true;evidence.events.Add(t.ToString("F2")+" Open-palm whole selection");}
        bool hasPointer=pickNode>=0&&t>=pickStart&&t<pickStart+1.2f;
        Ray ray=new Ray(right.PinchPosition,hasPointer?(rig.transform.TransformPoint(rig.NodeCenter(pickNode))-right.PinchPosition).normalized:q*Vector3.forward);
        if(hasPointer&&pick)Require(view.PickSpatialNode(ray)==pickNode,"Actual hand ray is occluded for "+rig.data.nodes[pickNode].id);
        view.ProcessSpatialInput(new CollectiveInputFrame{source=PointerSource.Hand,leftTracked=true,rightTracked=true,primary=rightPinched,secondary=leftPinched,leftMiddlePinch=leftMiddle,rightMiddlePinch=rightMiddle,leftPosition=left.PinchPosition,rightPosition=right.PinchPosition,hasPointer=hasPointer,pointer=ray,recall=recall,height=right.PinchPosition.y},ray,dt);
        rig.Rebuild();view.SendMessage("LateUpdate");view.roomScale.Refresh();view.SendMessage("RefreshStatus");view.linkedViews.UpdateViewState();
        if(active==null&&!leftPinched&&!rightPinched&&!leftMiddle&&!rightMiddle&&!view.SpatialEditing)evidence.releaseCommits=!view.stateController.HasChanges;
        if(t>=80)
        {
            Require(JsonUtility.ToJson(view.stateController.Capture())==walkingState,"Walking changed parameters");
            Require(Vector3.Distance(walkRoot,rig.transform.position)<.0001f,"Walking moved habitat anchor");
            evidence.walkPreservesParameters=evidence.walkPreservesWorldAnchor=true;evidence.walkMaximumMetres=Mathf.Max(evidence.walkMaximumMetres,step.magnitude);
            if(t>=89&&t<89+dt){view.linkedViews.RenderNow();evidence.linkedViewWalkMetres=Vector3.Distance(walkRemote,view.linkedViews.RoomCamera.transform.position);}
        }
    }
    private static void Capture(float t)
    {
        // The production dashboard already contains both RenderTextures; render exactly the head camera.
        Require(view.dashboard.ParticipantViewsVisible,"Production three-view workspace hidden");
        view.dashboard.RecenterWorkspace();view.linkedViews.RenderNow();
        var previous=view.head.targetTexture;var old=RenderTexture.active;
        view.head.targetTexture=mainTexture;view.head.Render();RenderTexture.active=mainTexture;
        pixels.ReadPixels(new Rect(0,0,960,540),0,0);pixels.Apply();
        File.WriteAllBytes(Path.Combine(Output,proof?"ProofFrames":"Frames",frame.ToString("D5")+".jpg"),pixels.EncodeToJPG(94));
        view.head.targetTexture=previous;RenderTexture.active=old;
    }
    private static void WriteTelemetry(float t)
    {
        Vector3 p=view.head.transform.position;
        var values=new List<string>{frame.ToString(),t.ToString("F4"),chapter.Replace(","," "),leftPinched.ToString(),rightPinched.ToString(),leftMiddle.ToString(),rightMiddle.ToString(),Vector3.Distance(left.PinchPosition,right.PinchPosition).ToString("F6"),left.TipDistance.ToString("F6"),right.TipDistance.ToString("F6"),view.selectedRegion.ToString(),view.SpatialOperation};
        values.AddRange(rig.Actual.Select(x=>x.ToString("F6")));values.Add(rig.GetTravel(0).ToString("F6"));values.Add(rig.GetTravel(1).ToString("F6"));values.AddRange(new[]{p.x.ToString("F6"),p.y.ToString("F6"),p.z.ToString("F6"),rig.inside.ToString(),(!view.stateController.HasChanges).ToString()});telemetry.WriteLine(string.Join(",",values));
    }
    private static void Finish()
    {
        SessionState.SetBool(Running,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        if(evidence==null)evidence=new Evidence{utc=DateTime.UtcNow.ToString("O")};
        if(frame>=Duration*Fps)
        {
            if(evidence.motions.Count!=6)errors.Add("Missing shape/travel segment");
            if(Mathf.Abs(evidence.walkMaximumMetres-evidence.linkedViewWalkMetres)>.004f)errors.Add("Physical and remote walk distances differ");
            if(!evidence.gestureViewRoundTrip||!evidence.releaseCommits)errors.Add("View round trip or release confirmation failed");
        }
        evidence.frames=frame;evidence.errors=errors.ToArray();evidence.passed=errors.Count==0&&frame>=captureEnd;telemetry?.Dispose();
        File.WriteAllText(Path.Combine(Output,"Segment-"+captureStart.ToString("D5")+"-"+captureEnd.ToString("D5")+".json"),JsonUtility.ToJson(evidence,true));
        if(captureEnd==Duration*Fps)File.WriteAllText(Path.Combine(Output,"CaptureEvidence.json"),JsonUtility.ToJson(evidence,true));
        EditorApplication.Exit(evidence.passed?0:1);
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}

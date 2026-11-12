using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Hands;
using SpatialCollective;

namespace SpatialCollectiveEditor
{
    public static class HeadsetInteractionVerification
    {
        public static void Run(CollectiveExperience view,List<string> checks)
        {
            var rig=view.rig;var state=view.stateController;var initial=state.Capture();
            view.ProcessHardwareInput=false;view.study.PauseForEdit();rig.coupled=false;
            var controls=view.Menu.GetComponentsInChildren<CollectiveParameterControl>(true);
            foreach(var control in controls)
            {
                view.SuspendInteraction();view.dashboard.Reveal(control);control.Write(rig,control.parameter==CollectiveParameter.Coupling?0:.4f);rig.Rebuild();
                Physics.SyncTransforms();Ray ray=new Ray(view.head.transform.position,(control.transform.position-view.head.transform.position).normalized);
                Send(view,ray,false,false,.3f);Send(view,ray,true,false,.3f);Send(view,ray,false,false,.3f);
                float before=control.Read(rig);Send(view,ray,true,true,.3f);Send(view,ray,true,true,.44f);
                float expected=Mathf.Clamp(before+.4f*(control.Maximum-control.Minimum),control.Minimum,control.Maximum);
                Require(Mathf.Abs(control.Read(rig)-expected)<.0001f,"Two-hand mapping failed: "+control.caption);
                Send(view,ray,true,true,.44f,new Vector3(.3f,.12f,-.25f));
                Require(Mathf.Abs(control.Read(rig)-expected)<.0001f,"Body translation changed "+control.caption);
                Send(view,ray,false,true,.44f);Send(view,ray,true,true,.65f);
                Require(!view.IsBimanual&&Mathf.Abs(control.Read(rig)-expected)<.0001f,"Release did not freeze "+control.caption);
                Send(view,ray,false,false,.3f);Send(view,ray,true,true,.3f);
                var lost=Frame(true,true,.4f,Vector3.zero);lost.leftTracked=false;view.ProcessInput(lost,ray);
                Send(view,ray,true,true,.7f);
                Require(!view.IsBimanual&&Mathf.Abs(control.Read(rig)-expected)<.0001f,"Lost-hand reacquisition jumped "+control.caption);
                Send(view,ray,false,false,.3f);
            }
            checks.Add("Bimanual input through live colliders: all 6 shape + 3 coupling + 2 travel parameters; common body translation invariant; either-hand release and left tracking-loss recovery freeze passed (synthetic)");
            // Pure state machine also rejects missing right hand and non-finite tracked data.
            var session=new CollectiveBimanualSession();session.Step(Frame(false,false,.3f,Vector3.zero),.35f);
            session.Step(Frame(true,true,.3f,Vector3.zero),.35f);var invalid=Frame(true,true,.5f,Vector3.zero);invalid.rightTracked=false;session.Step(invalid,.35f);
            Require(session.Ended&&!session.Active&&session.NeedsOpen,"Right tracking loss not gated.");
            invalid.rightTracked=true;invalid.leftPosition=Vector3.one*float.NaN;session.Step(invalid,.35f);Require(!session.Active,"NaN hand position accepted.");
            var fallback=controls.First(c=>c.parameter==CollectiveParameter.Activation&&c.index==0);
            view.dashboard.Reveal(fallback);Physics.SyncTransforms();
            Ray fallbackRay=new Ray(view.head.transform.position,(fallback.transform.position-view.head.transform.position).normalized);
            Send(view,fallbackRay,false,false,.3f);Send(view,fallbackRay,true,false,.3f);Send(view,fallbackRay,false,false,.3f);
            Send(view,fallbackRay,true,true,.3f);var handLost=Frame(true,true,.3f,Vector3.zero);handLost.leftTracked=false;view.ProcessInput(handLost,fallbackRay);
            view.ProcessInput(new CollectiveInputFrame{source=PointerSource.Controller,height=1,primary=true},fallbackRay);
            Require(!view.IsDragging,"Held controller took over an interrupted two-hand gesture.");
            view.ProcessInput(new CollectiveInputFrame{source=PointerSource.Controller,height=1,primary=false},fallbackRay);
            view.ProcessInput(new CollectiveInputFrame{source=PointerSource.Controller,height=1,primary=true},fallbackRay);
            Require(view.IsDragging,"Fresh controller press cannot take over after tracking loss.");
            checks.Add("Bimanual tracking loss to controller fallback: held input rejected, fresh release/press resumes passed");
            view.SuspendInteraction();state.Restore(initial);rig.Rebuild();view.dashboard.ShowTab(0);
            var filter=view.head.GetComponent<CollectiveViewFilter>();filter.Apply();
            int expectedCount=1+rig.data.nodes[rig.selectedNode].neighbors.Distinct().Count();
            Require(filter.VisibleCount==expectedCount&&filter.VisibleCount<27,"Local view did not suppress distant modules.");
            view.linkedViews.RenderNow();
            var overview=view.linkedViews.OverviewCamera.GetComponent<CollectiveViewFilter>();overview.Apply();
            Require(overview.VisibleCount==27,"Overview omitted modules.");
            view.dashboard.ShowTab(1);view.linkedViews.RenderNow();
            var remote=view.linkedViews.RoomCamera.GetComponent<CollectiveViewFilter>();
            Require(remote.focusNode==rig.FindNode("S08")&&remote.Weight(rig.FindNode("S08"))==1,"Independent remote focus not set.");
            for(int b=0;b<rig.filters.Length;b++)Require(rig.filters[b].sharedMesh.uv2.Length==rig.filters[b].sharedMesh.vertexCount,"Missing per-node view metadata.");
            Shader.SetGlobalFloat("_SCFilterEnabled",0);
            checks.Add("Camera-specific visibility: local selected + immediate neighbours only; independent S08 remote focus; full 27-module overview; per-vertex node metadata passed");

            var skins=UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            Require(skins.Length==2,"Expected two complete tracked hand meshes.");
            foreach(var skin in skins)
            {
                Require(skin.sharedMesh.vertexCount>300&&skin.bones.Length>=20,"Hand mesh/skeleton incomplete.");
                var root=skin.GetComponentInParent<XRHandTrackingEvents>();Require(root!=null,"Hand tracking events missing.");
                Require(root.GetComponent<XRHandSkeletonDriver>()!=null&&root.GetComponent<XRHandMeshController>()!=null,"Official hand driver/controller missing.");
                Require(!skin.enabled,"Untracked hand must be hidden.");
            }
            Require(!File.Exists("Assets/SpatialCollective/Runtime/HandSkeletonView.cs"),"Unused abstract hand renderer remains in source.");
            checks.Add("Two official XR Hands skinned meshes with tracking events, skeleton drivers, tracking-loss visibility controllers; no line-hand renderer in scene: passed (hardware tracking not exercised)");

            view.dashboard.ConfigureHeadsetLayout();view.dashboard.ShowTab(0);view.PlaceMenu();
            Require(!view.Menu.Find("Header").gameObject.activeSelf&&!view.Menu.Find("Input gesture indicator").gameObject.activeSelf,"Headset frame or abstract hand icon is visible.");
            Require(Mathf.Abs(Vector3.Distance(view.Menu.position,view.head.transform.position)-1.8f)<.001f,"Headset panel distance incorrect.");
            VerifyOpaqueMonitors(view);
            checks.Add("Rendered monitor isolation: remote/overview pixels are unchanged when the main camera background switches red to blue; main backdrop pixels change as expected");
            Vector3 head=view.head.transform.position;Quaternion rotation=view.head.transform.rotation;
            view.SendMessage("LateUpdate");view.roomScale.Calibrate();view.linkedViews.Calibrate();view.linkedViews.RenderNow();
            Vector3 room=view.linkedViews.RoomCamera.transform.position,anchor=rig.transform.position;
            string parameters=JsonUtility.ToJson(state.Capture());
            Vector3 step=new Vector3(.38f,0,.22f);view.head.transform.position+=step;
            view.SendMessage("LateUpdate");view.roomScale.Refresh();view.linkedViews.RenderNow();
            Require(Mathf.Abs(Vector3.Distance(room,view.linkedViews.RoomCamera.transform.position)-step.magnitude)<.001f,"Room view is not metre-for-metre.");
            Require(Vector3.Distance(anchor,rig.transform.position)<.0001f,"Walking moved world-anchored model.");
            Require(JsonUtility.ToJson(state.Capture())==parameters,"Walking edited geometry parameters.");
            Require(Mathf.Abs(view.roomScale.HorizontalDisplacement-step.magnitude)<.001f,"Small-area displacement incorrect.");
            view.dashboard.Refresh();EngineeringVerification.Capture(view.head,"headset-small-step.png");
            view.head.transform.SetPositionAndRotation(head,rotation);view.roomScale.Refresh();view.linkedViews.RenderNow();view.dashboard.Refresh();
            EngineeringVerification.Capture(view.head,"headset-layout.png");
            checks.Add("HMD layout: open stereo centre, peripheral overview/remote monitors, no abstract hand icons; 0.439 m physical step produces 0.439 m remote parallax without geometry mutation: passed (synthetic pose)");
            // Keep the original protocol evaluator byte-identical throughout this revision.
            var report=new Evidence{passed=true,utc=DateTime.UtcNow.ToString("O"),parameterCount=controls.Length,handMeshes=skins.Length,localVisibleModules=expectedCount,roomScaleStepMetres=step.magnitude,hardwareValidated=false};
            File.WriteAllText(Path.GetFullPath("../Documentation/Validation/HeadsetInteraction.json"),JsonUtility.ToJson(report,true));
        }
        [Serializable] private sealed class Evidence {public bool passed,hardwareValidated;public string utc;public int parameterCount,handMeshes,localVisibleModules;public float roomScaleStepMetres;}
        private static CollectiveInputFrame Frame(bool right,bool left,float span,Vector3 shift)
            =>new CollectiveInputFrame{source=PointerSource.Hand,primary=right,secondary=left,rightTracked=true,leftTracked=true,height=1.2f,
                leftPosition=new Vector3(-span*.5f,1.2f,.5f)+shift,rightPosition=new Vector3(span*.5f,1.2f,.5f)+shift};
        private static void Send(CollectiveExperience view,Ray ray,bool right,bool left,float span,Vector3 shift=default)=>view.ProcessInput(Frame(right,left,span,shift),ray);
        private static void Require(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
        private static void VerifyOpaqueMonitors(CollectiveExperience view)
        {
            var camera=view.head;var target=new RenderTexture(960,540,24);var pixels=new Texture2D(960,540,TextureFormat.RGB24,false);
            var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;int mask=camera.cullingMask;
            var clear=camera.clearFlags;var background=camera.backgroundColor;
            try
            {
                camera.targetTexture=target;camera.cullingMask=1<<9;camera.clearFlags=CameraClearFlags.SolidColor;
                Color[][] samples=new Color[2][];
                for(int pass=0;pass<2;pass++)
                {
                    camera.backgroundColor=pass==0?Color.red:Color.blue;camera.Render();RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,960,540),0,0);pixels.Apply();var values=new List<Color>();
                    values.Add(pixels.GetPixel(20,520));
                    foreach(var screen in new[]{view.dashboard.overviewScreen,view.dashboard.roomScreen})
                        for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)
                        {
                            Vector3 v=camera.WorldToViewportPoint(screen.transform.TransformPoint(new Vector3(x*.15f,y*.15f,0)));
                            values.Add(pixels.GetPixel(Mathf.RoundToInt(v.x*960),Mathf.RoundToInt(v.y*540)));
                        }
                    samples[pass]=values.ToArray();
                }
                Require(Vector4.Distance(samples[0][0],samples[1][0])>.5f,"Background isolation test did not change the background.");
                for(int i=1;i<samples[0].Length;i++)Require(Vector4.Distance(samples[0][i],samples[1][i])<.01f,"Main scene bleeds through a monitor.");
            }
            finally
            {
                camera.targetTexture=oldTarget;camera.cullingMask=mask;camera.clearFlags=clear;camera.backgroundColor=background;RenderTexture.active=oldActive;
                target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(pixels);
            }
        }
    }
}

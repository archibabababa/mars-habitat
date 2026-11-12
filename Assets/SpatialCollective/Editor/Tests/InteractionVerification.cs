using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SpatialCollective;

namespace SpatialCollectiveEditor
{
    public static class InteractionVerification
    {
        public static void Run(CollectiveExperience view,CollectiveRig rig,CollectiveStateController state,List<string> checks)
        {
            view.ProcessHardwareInput=false;
            var initial=state.Capture();
            rig.coupled=false;rig.animateDetached=false;
            for(int i=0;i<6;i++)rig.SetBaseline(i,.5f);
            for(int i=0;i<2;i++)rig.SetTravel(i,0);
            rig.Rebuild();
            int s08=rig.FindNode("S08"),o13=rig.FindNode("O13");
            Vector3 center=rig.NodeCenter(s08);
            var connected=rig.Matrices.Take(rig.data.nodes.Length).ToArray();
            var before=rig.filters.Select(f=>f.sharedMesh.vertices).ToArray();
            rig.SetBaseline(4,1);rig.Rebuild();
            Require(Vector3.Distance(center,rig.NodeCenter(s08))<.0001f,"S08 shape unexpectedly launches the unit.");
            for(int n=0;n<rig.data.nodes.Length;n++)if(!rig.data.nodes[n].detached)
                Require(connected[n]==rig.Matrices[n],"Detached shape changed a connected node.");
            bool changed=false;
            for(int b=0;b<rig.data.batches.Length;b++)
            {
                var current=rig.filters[b].sharedMesh.vertices;
                for(int v=0;v<current.Length;v++)
                    if(rig.data.drivers[rig.data.batches[b].vertexDrivers[v]].node==s08 && Vector3.Distance(before[b][v],current[v])>.01f)changed=true;
            }
            Require(changed,"S08 shape did not deform actual vertices.");
            float[] shapes=Enumerable.Range(0,6).Select(rig.GetBaseline).ToArray();
            Vector3 approachBefore=rig.NodeCenter(o13);
            rig.SetTravel(0,1);rig.Rebuild();
            Require(Vector3.Distance(center,rig.NodeCenter(s08))>20,"S08 departure too small.");
            Require(Vector3.Distance(approachBefore,rig.NodeCenter(o13))<.001f,"S08 travel changed O13.");
            for(int i=0;i<6;i++)Require(rig.GetBaseline(i)==shapes[i],"Travel changed a shape parameter.");
            int anchor=rig.FindNode("S03");float distance=Vector3.Distance(rig.NodeCenter(o13),rig.NodeCenter(anchor));
            rig.SetTravel(1,1);rig.Rebuild();
            Require(Vector3.Distance(rig.NodeCenter(o13),rig.NodeCenter(anchor))<distance,"O13 did not approach the collective.");
            checks.Add("Independent S08 shape deforms actual vertices; six-zone isolation and independent departure/approach: passed");

            for(int i=0;i<6;i++)rig.SetBaseline(i,.5f);
            for(int i=0;i<2;i++)rig.SetTravel(i,0);
            rig.coupled=false;state.Commit();rig.Rebuild();Physics.SyncTransforms();
            var controls=view.Menu.GetComponentsInChildren<CollectiveParameterControl>(true);
            var shape=controls.First(c=>c.parameter==CollectiveParameter.Activation&&c.index==4);
            Ray ray=Toward(view,shape.transform);
            Send(view,ray,false,1.2f);Send(view,ray,true,1.2f);Send(view,ray,true,1.375f);Send(view,ray,false,1.375f);rig.Rebuild();
            Require(Mathf.Abs(rig.GetBaseline(4)-1)<.0001f,"Pinch/drag did not reach the S08 shape control.");
            Require(!view.IsDragging&&state.HasChanges,"Release must freeze an unapplied preview.");
            view.Execute(CollectiveCommand.ToggleCompare,0);rig.Rebuild();
            Require(Mathf.Abs(rig.GetBaseline(4)-.5f)<.0001f&&state.IsComparing,"Compare did not show applied state.");
            view.Execute(CollectiveCommand.ToggleCompare,0);rig.Rebuild();
            Require(Mathf.Abs(rig.GetBaseline(4)-1)<.0001f&&!state.IsComparing,"Compare lost the proposal.");
            Click(view,CollectiveCommand.Apply);
            Require(!state.HasChanges,"Apply button did not commit.");
            Send(view,ray,false,1.2f);Send(view,ray,true,1.2f);Send(view,ray,true,.9f);Send(view,ray,false,.9f);
            Click(view,CollectiveCommand.Undo);
            Require(Mathf.Abs(rig.GetBaseline(4)-1)<.0001f,"Undo button did not restore last applied state.");
            var empty=new Ray(new Vector3(10000,10000,10000),Vector3.up);
            float value=rig.GetBaseline(view.selectedRegion);
            Send(view,empty,false,1);Send(view,empty,true,1);Send(view,empty,true,2);Send(view,empty,false,2);
            Require(rig.GetBaseline(view.selectedRegion)==value,"An empty-space pinch changed geometry.");
            checks.Add("Real scene colliders -> synthetic hand frames -> C# commands -> mesh: pinch, release-preview, Apply, Undo, Compare and empty-space rejection passed");

            shape=controls.First(c=>c.parameter==CollectiveParameter.Activation&&c.index==0);ray=Toward(view,shape.transform);
            rig.SetBaseline(0,.4f);rig.coupled=true;
            Send(view,ray,false,1);Send(view,ray,true,1);Send(view,ray,true,1.07f);
            float held=rig.GetBaseline(0);
            view.ProcessInput(new CollectiveInputFrame{source=PointerSource.None},ray);
            Send(view,ray,true,20);Send(view,ray,true,21);
            Require(!view.IsDragging && Mathf.Abs(rig.GetBaseline(0)-held)<.0001f,"Tracking recovery resumed a held pinch or jumped.");
            Send(view,ray,false,20);Send(view,ray,true,20);Send(view,ray,true,20.035f);
            Require(rig.GetBaseline(0)>held,"A fresh pinch after tracking recovery did not work.");
            held=rig.GetBaseline(0);view.SuspendInteraction();Send(view,ray,true,40);
            Require(!view.IsDragging&&rig.GetBaseline(0)==held,"Focus recovery resumed an old drag.");
            Send(view,ray,false,40);
            view.Menu.gameObject.SetActive(false);
            view.ProcessInput(new CollectiveInputFrame{source=PointerSource.None,secondary=false},ray);
            view.ProcessInput(new CollectiveInputFrame{source=PointerSource.None,secondary=true},ray);
            Require(view.Menu.gameObject.activeSelf,"Left-hand recall requires an unavailable right hand.");
            view.ProcessInput(new CollectiveInputFrame{source=PointerSource.None,secondary=false},ray);
            Physics.SyncTransforms();
            var coupling=controls.First(c=>c.parameter==CollectiveParameter.Coupling&&c.index==0);
            ray=Toward(view,coupling.transform);
            Send(view,ray,false,1);Send(view,ray,true,1);Send(view,ray,true,1.35f);Send(view,ray,false,1.35f);
            Require(rig.GetCoupling(0)>.99f,"Signed response control cannot reach +1.");
            rig.SetBaseline(0,1);rig.SetBaseline(1,.5f);rig.coupled=true;rig.Rebuild();
            Require(rig.Actual[1]==1,"Positive coupling did not follow the core.");
            rig.SetCoupling(0,-1);rig.Rebuild();Require(rig.Actual[1]==0,"Negative coupling did not oppose the core.");
            rig.coupled=false;rig.Rebuild();Require(rig.Actual[1]==.5f,"Free mode did not isolate regions.");
            checks.Add("Lost tracking, reacquired held pinch, focus interruption, deliberate resume, left-only menu recall and signed coupling through live controls: passed");

            state.Commit();var applied=state.Capture();
            string path=Path.Combine(Application.temporaryCachePath,"SpatialCollective-Verification-"+Guid.NewGuid()+".json");
            try
            {
                state.SaveApplied(path);rig.SetBaseline(0,.1f);state.LoadPreview(path);
                Require(JsonUtility.ToJson(state.Capture())==JsonUtility.ToJson(applied),"Saved applied state did not round-trip.");
                File.WriteAllText(path,"{\"schemaVersion\":99}");
                bool rejected=false;try{state.LoadPreview(path);}catch(InvalidDataException){rejected=true;}
                Require(rejected,"Malformed saved state was accepted.");
                Require(JsonUtility.ToJson(state.Capture())==JsonUtility.ToJson(applied),"Malformed load changed live state.");
            }
            finally {if(File.Exists(path))File.Delete(path);}
            checks.Add("Applied-state JSON save/load round-trip and invalid-load atomic rejection: passed");

            float detached=rig.GetBaseline(4),travel=rig.GetTravel(0);
            view.Execute(CollectiveCommand.ImportAuthoredState,0);rig.Rebuild();
            Require(!rig.coupled && Mathf.Abs(rig.Actual[0]-.8f)<.0001f && Mathf.Abs(rig.Actual[1]-.2f)<.0001f,"Native GH JSON was double-coupled or not loaded.");
            Require(rig.GetBaseline(4)==detached && rig.GetTravel(0)==travel,"GH import changed separate detached controls.");
            string unchanged=JsonUtility.ToJson(state.Capture());bool invalidGh=false;
            try{state.PreviewAuthoredJson(@"{""schema"":""spatial_collective.v19"",""units"":""mm""}");}catch(InvalidDataException){invalidGh=true;}
            Require(invalidGh && JsonUtility.ToJson(state.Capture())==unchanged,"Invalid GH JSON changed live state.");
            checks.Add("Native Grasshopper state JSON import, resolved-value semantics, detached-control preservation and invalid-unit rejection: passed");

            var study=view.study;study.Pause();
            study.Seek(0);rig.Rebuild();Require(rig.GetBaseline(0)==0,"Study does not start gathered.");
            study.Seek(10);rig.Rebuild();Require(rig.GetBaseline(0)==1&&rig.GetBaseline(1)==1,"Study does not unfurl.");
            study.Seek(18);rig.Rebuild();Require(Mathf.Abs(rig.GetBaseline(1)-.2f)<.0001f,"Study does not differentiate.");
            study.Seek(26);rig.Rebuild();Require(rig.GetBaseline(0)==0,"Study does not retreat.");
            study.Seek(32);rig.Rebuild();Require(rig.GetBaseline(1)==1&&rig.departureProgress==1,"Study does not reach/end travel.");
            foreach(float t in new[]{0f,10f,18f,26f,32f})
            {
                study.Seek(t);rig.Rebuild();view.linkedViews.RenderNow();
                for(int n=0;n<rig.data.nodes.Length;n++)
                {
                    Vector3 screen=view.linkedViews.OverviewCamera.WorldToViewportPoint(rig.transform.TransformPoint(rig.NodeCenter(n)));
                    Require(screen.z>0&&screen.x>.02f&&screen.x<.98f&&screen.y>.02f&&screen.y<.98f,"A unit leaves the collective overview at t="+t);
                }
            }
            checks.Add("Overview framing retains all 27 unit centres at five timeline checkpoints, including both detached endpoints: passed");
            state.Restore(initial);rig.Rebuild();
            Vector3 position=view.head.transform.position;
            Quaternion rotation=view.head.transform.rotation;
            Vector3 modelPosition=rig.transform.position;
            var baseline=state.Capture();view.linkedViews.RenderNow();
            Vector3 beforeCamera=view.linkedViews.RoomCamera.transform.position;
            view.head.transform.position=position+Vector3.right*.2f;view.head.transform.rotation=rotation*Quaternion.Euler(0,12,0);
            view.linkedViews.RenderNow();
            Require(Vector3.Distance(beforeCamera,view.linkedViews.RoomCamera.transform.position)>.1f*rig.transform.lossyScale.x,"Body displacement did not map into remote view.");
            Require(rig.transform.position==modelPosition,"Body observation moved the world-anchored model.");
            Require(JsonUtility.ToJson(state.Capture())==JsonUtility.ToJson(baseline),"Body observation changed model parameters.");
            Require(view.linkedViews.OverviewCamera.targetTexture==view.linkedViews.OverviewTexture&&view.linkedViews.RoomCamera.targetTexture==view.linkedViews.RoomTexture,"Views do not have independent render targets.");
            Require(view.linkedViews.OverviewCamera.cullingMask==((1<<8)|(1<<10))&&view.linkedViews.RoomCamera.cullingMask==(1<<8),"Views could recurse through their own panels.");
            view.head.transform.SetPositionAndRotation(position,rotation);view.linkedViews.Calibrate();
            checks.Add("32-second phase checkpoints, two live model cameras, metre-scale body displacement and observation-only invariants: passed");
            view.Execute(CollectiveCommand.SelectReferenceRoom,0);
        }
        private static Ray Toward(CollectiveExperience view,Transform target)
        {
            var c=target.GetComponent<CollectiveParameterControl>();if(c!=null)view.dashboard.Reveal(c);
            return new Ray(view.head.transform.position,(target.position-view.head.transform.position).normalized);
        }
        private static void Send(CollectiveExperience view,Ray ray,bool down,float height)
            =>view.ProcessInput(new CollectiveInputFrame{source=PointerSource.Hand,primary=down,height=height},ray);
        private static void Click(CollectiveExperience view,CollectiveCommand command)
        {
            var button=view.Menu.GetComponentsInChildren<CollectiveButton>().First(b=>b.action==command);
            var ray=Toward(view,button.transform);
            Send(view,ray,false,1);Send(view,ray,true,1);Send(view,ray,false,1);
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}

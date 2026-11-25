using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SpatialCollective;

namespace SpatialCollectiveEditor
{
    public static class EngineeringAuditVerification
    {
        [Serializable] private sealed class Evidence
        {
            public bool passed,hardwareValidated = false;
            public string utc;
            public float minimumTargetAngleDegrees,minimumTargetGapMetres,panelDistanceMetres,studyRadiusMetres;
            public float[] bimanualVertexDisplacementByConnectedRegion;
            public int verifiedParameterTargets;
            public List<string> checks=new List<string>();
        }
        public static void Run(CollectiveExperience view,List<string> checks)
        {
            var rig=view.rig;var state=view.stateController;var saved=state.Capture();
            var report=new Evidence{utc=DateTime.UtcNow.ToString("O"),minimumTargetAngleDegrees=180,minimumTargetGapMetres=100};
            var controls=view.Menu.GetComponentsInChildren<CollectiveParameterControl>(true);
            int occupied=rig.selectedNode;
            view.SuspendInteraction();view.ProcessHardwareInput=false;rig.coupled=false;
            foreach(var control in controls)
            {
                Select(view,control);
                int region=control.parameter==CollectiveParameter.Activation?control.index:control.parameter==CollectiveParameter.Travel?control.index+4:control.index+1;
                view.linkedViews.RenderNow();
                Require(view.linkedViews.RoomId==rig.data.nodes[rig.NodeForRegion(region)].id,"Remote window has stale target: "+control.caption);
                Require(rig.selectedNode==occupied,"Parameter selection teleported the occupied room.");
                report.verifiedParameterTargets++;
            }
            // Advanced LINK parameters can be selected while an independent-unit tab is still open.
            view.dashboard.ShowTab(2);
            Select(view,controls.First(c=>c.parameter==CollectiveParameter.Coupling&&c.index==0));
            view.linkedViews.RenderNow();
            Require(view.linkedViews.RoomId==rig.data.nodes[rig.NodeForRegion(1)].id,"O13 tab overrides the selected LINK target.");
            report.checks.Add("All 11 parameter selections and an O13-to-LINK selection update the remote focus without moving the occupied room.");

            var travel=controls.First(c=>c.parameter==CollectiveParameter.Travel&&c.index==0);
            Select(view,travel);rig.SetTravel(0,.2f);rig.SetBaseline(0,.2f);rig.Rebuild();
            var link=view.Menu.GetComponentsInChildren<CollectiveDashboardAction>(true).First(a=>a.action==4);
            Click(view,link.transform.position);
            Require(rig.coupled&&view.selectedRegion==0,"LINK ALL did not select CORE.");
            var before=rig.filters.Select(f=>f.sharedMesh.vertices).ToArray();
            Ray ray=new Ray(view.head.transform.position,view.head.transform.forward);
            Send(view,ray,false,false,.3f);Send(view,ray,true,true,.3f);Send(view,ray,true,true,.44f);rig.Rebuild();
            Require(rig.Actual.Take(4).All(v=>Mathf.Abs(v-.6f)<.001f),"LINK ALL kept the old two-hand target.");
            Require(Mathf.Abs(rig.GetTravel(0)-.2f)<.001f,"Collective edit changed S08 travel.");
            report.bimanualVertexDisplacementByConnectedRegion=new float[4];
            for(int b=0;b<rig.filters.Length;b++)
            {
                var after=rig.filters[b].sharedMesh.vertices;
                for(int v=0;v<after.Length;v++)
                {
                    var node=rig.data.nodes[rig.data.drivers[rig.data.batches[b].vertexDrivers[v]].node];
                    if(!node.detached)report.bimanualVertexDisplacementByConnectedRegion[node.region]=Mathf.Max(report.bimanualVertexDisplacementByConnectedRegion[node.region],Vector3.Distance(before[b][v],after[v]));
                }
            }
            Require(report.bimanualVertexDisplacementByConnectedRegion.All(v=>v>.1f),"Bimanual input did not move geometry in every connected region.");
            Send(view,ray,false,false,.44f);Send(view,ray,false,false,.44f);
            report.checks.Add("Select S08 TRAVEL -> LINK ALL -> two-hand spread: actual meshes move in all four connected regions; S08 travel stays unchanged.");

            view.dashboard.ShowTab(0);view.SuspendInteraction();
            string parameters=JsonUtility.ToJson(state.Capture());int target=view.InspectionNode;
            foreach(Vector3 point in new[]{view.Menu.TransformPoint(new Vector3(0,-.87f,-.004f)),view.dashboard.roomScreen.transform.position})
            {
                Ray blank=new Ray(view.head.transform.position,(point-view.head.transform.position).normalized);
                Send(view,blank,false,false,.3f);Send(view,blank,true,false,.3f);
                Require(!view.IsDragging&&view.InspectionNode==target,"Non-interactive UI selected geometry behind it.");
                Send(view,blank,false,false,.3f);
            }
            Require(JsonUtility.ToJson(state.Capture())==parameters,"Blank panel changed model state.");
            view.Execute(CollectiveCommand.SelectRegion,1);view.dashboard.Refresh();
            Require(view.Menu.GetComponentsInChildren<TextMesh>().Any(t=>t.text.Contains("LONG BRANCH selected.")),"Action feedback is not visible in headset layout.");
            report.checks.Add("Blank main-panel area and remote monitor block geometry selection; command feedback is visible.");

            for(int tab=0;tab<3;tab++)
            {
                view.dashboard.HandleAction(tab);MeasureTargets(view,report);
                view.dashboard.HandleAction(3);MeasureTargets(view,report);view.dashboard.HandleAction(3);
            }
            Require(report.minimumTargetAngleDegrees>=2.5f,"A headset target is smaller than 2.5 degrees at the calibrated pose.");
            Require(report.minimumTargetGapMetres>=.0119f,"Headset targets overlap or have less than 12 mm spacing.");
            report.panelDistanceMetres=Vector3.Distance(view.Menu.position,view.head.transform.position);
            report.studyRadiusMetres=view.roomScale.radiusMetres;
            int beforeRoom=rig.selectedNode;rig.selectedNode=rig.FindNode("T01");view.roomScale.Refresh();
            Require(Mathf.Abs(view.roomScale.EffectiveRadius-.3f)<.001f&&view.roomScale.Status.Contains("LOW HEADROOM"),"Small T room must use the limited-lean study cue.");
            rig.selectedNode=beforeRoom;view.roomScale.Refresh();
            report.checks.Add("T-type room uses a configurable 0.30 m lean-area cue and a low-headroom label; no headset position clamping or boundary claim.");
            report.checks.Add("All three tabs, with MORE open and closed: target angles >= 2.5 degrees at the calibrated pose, edge gaps >= 12 mm, every target centre ray reaches its own collider, and monitors do not overlap buttons or parameter bars.");
            state.Restore(saved);rig.Rebuild();view.Execute(CollectiveCommand.SelectRegion,0);view.dashboard.ShowTab(0);view.linkedViews.RenderNow();view.dashboard.Refresh();
            EngineeringVerification.Capture(view.head,"headset-audited.png");
            view.dashboard.HandleAction(3);EngineeringVerification.Capture(view.head,"headset-more-audited.png");view.dashboard.HandleAction(3);
            report.passed=true;File.WriteAllText(Path.GetFullPath("../Documentation/Validation/EngineeringAudit.json"),JsonUtility.ToJson(report,true));
            checks.AddRange(report.checks);
        }
        private static void MeasureTargets(CollectiveExperience view,Evidence report)
        {
            Physics.SyncTransforms();
            var targets=view.Menu.GetComponentsInChildren<BoxCollider>().Where(c=>c.GetComponent<CollectiveParameterControl>()!=null||c.GetComponent<CollectiveButton>()!=null||c.GetComponent<CollectiveDashboardAction>()!=null).ToArray();
            var rectangles=new List<Rect>();
            foreach(var c in targets)
            {
                Vector3 eye=view.head.transform.position,center=c.transform.TransformPoint(c.center);
                Ray ray=new Ray(eye,(center-eye).normalized);
                Require(Physics.Raycast(ray,out RaycastHit hit,12,1<<9)&&hit.collider==c,"Target is blocked by another collider: "+c.name);
                float a=Vector3.Angle(c.transform.TransformPoint(c.center+Vector3.up*c.size.y*.5f)-eye,c.transform.TransformPoint(c.center-Vector3.up*c.size.y*.5f)-eye);
                float b=Vector3.Angle(c.transform.TransformPoint(c.center+Vector3.right*c.size.x*.5f)-eye,c.transform.TransformPoint(c.center-Vector3.right*c.size.x*.5f)-eye);
                report.minimumTargetAngleDegrees=Mathf.Min(report.minimumTargetAngleDegrees,a,b);
                Vector3 p=view.Menu.InverseTransformPoint(center);rectangles.Add(new Rect(p.x-c.size.x*.5f,p.y-c.size.y*.5f,c.size.x,c.size.y));
            }
            for(int i=0;i<rectangles.Count;i++)for(int j=0;j<i;j++)
            {
                Rect a=rectangles[i],b=rectangles[j];
                float dx=Mathf.Max(0,Mathf.Max(a.xMin-b.xMax,b.xMin-a.xMax));
                float dy=Mathf.Max(0,Mathf.Max(a.yMin-b.yMax,b.yMin-a.yMax));
                report.minimumTargetGapMetres=Mathf.Min(report.minimumTargetGapMetres,Mathf.Sqrt(dx*dx+dy*dy));
            }
            foreach(var screen in new[]{view.dashboard.overviewScreen,view.dashboard.roomScreen})
            {
                Vector3 p=view.Menu.InverseTransformPoint(screen.transform.position),s=screen.transform.localScale;
                Rect monitor=new Rect(p.x-s.x*.5f,p.y-s.y*.5f,s.x,s.y);
                Require(rectangles.All(r=>!r.Overlaps(monitor)),"Monitor overlaps an interaction target.");
            }
        }
        private static void Select(CollectiveExperience view,CollectiveParameterControl control)
        {view.SuspendInteraction();view.dashboard.Reveal(control);Click(view,control.transform.position);}
        private static void Click(CollectiveExperience view,Vector3 point)
        {
            Physics.SyncTransforms();Ray ray=new Ray(view.head.transform.position,(point-view.head.transform.position).normalized);
            Send(view,ray,false,false,.3f);Send(view,ray,true,false,.3f);Send(view,ray,false,false,.3f);
        }
        private static void Send(CollectiveExperience view,Ray ray,bool right,bool left,float span)
            =>view.ProcessInput(new CollectiveInputFrame{source=PointerSource.Hand,primary=right,secondary=left,rightTracked=true,leftTracked=true,height=1.2f,leftPosition=new Vector3(-span*.5f,1.2f,.5f),rightPosition=new Vector3(span*.5f,1.2f,.5f)},ray);
        private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    }
}

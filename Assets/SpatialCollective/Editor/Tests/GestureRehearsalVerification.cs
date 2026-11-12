using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SpatialCollective;

namespace SpatialCollectiveEditor
{
    public static class GestureRehearsalVerification
    {
        [Serializable] private sealed class Evidence
        {
            public bool passed,headPoseFixed,actualInputPipeline;
            public string utc;
            public float collectiveVertexDisplacementMetres,s08ShapeDisplacementMetres,o13ShapeDisplacementMetres,s08TravelMetres,o13TravelMetres;
            public List<Checkpoint> checkpoints=new List<Checkpoint>();
        }
        [Serializable] private sealed class Checkpoint
        {
            public float seconds;
            public string chapter,remoteRoom;
            public float[] shape,travel;
            public bool proposal,comparing;
        }
        public static void Run(CollectiveExperience view,List<string> checks)
        {
            var rig=view.rig;var state=view.stateController;var demo=view.rehearsal;
            Vector3 head=view.head.transform.position;Quaternion rotation=view.head.transform.rotation;
            var result=new Evidence{utc=DateTime.UtcNow.ToString("O"),headPoseFixed=true,actualInputPipeline=true};
            string priorProposal=JsonUtility.ToJson(state.Capture()),priorApplied=JsonUtility.ToJson(state.CopyApplied());
            demo.Begin(false);var rest=Vertices(rig);Vector3 departure=Vector3.zero,approach=Vector3.zero;
            Vector3[][] shapeBefore=null;
            float frame=view.linkedViews.OverviewCamera.orthographicSize;
            foreach(float time in new[]{0,4,11,13,22,24,29,30,37,39,44,45,51,52,54,56,59})
            {
                demo.Step(time);
                Require(Vector3.Distance(head,view.head.transform.position)<1e-6f&&Quaternion.Angle(rotation,view.head.transform.rotation)<.001f,"Gesture proof must not move the head.");
                Require(Mathf.Abs(frame-view.linkedViews.OverviewCamera.orthographicSize)<1e-5f,"Overview auto-zoom hides deformation.");
                if(time==4)Require(view.IsDragging,"Core pinch did not begin.");
                if(time==11)
                {
                    Require(rig.Actual.Take(4).All(x=>x>.999f)&&state.HasChanges&&!view.IsDragging,"Whole collective did not unfurl as a held preview.");
                    result.collectiveVertexDisplacementMetres=Displacement(rig,rest,n=>!rig.data.nodes[n].detached);
                    Require(result.collectiveVertexDisplacementMetres>5,"Whole-model vertex movement is too small.");
                }
                if(time==13)Require(!state.HasChanges,"Apply did not commit the unfolded state.");
                if(time==22)Require(Mathf.Abs(rig.Actual[1]-.15f)<.001f&&rig.Actual[0]>.999f,"Regional gesture did not isolate the long branch.");
                if(time==24||time==39)shapeBefore=Vertices(rig);
                if(time==29)
                {
                    Require(rig.Actual[4]>.999f&&rig.departureProgress==0&&view.linkedViews.RoomId=="S08","S08 shape is not isolated or its room view did not switch.");
                    result.s08ShapeDisplacementMetres=Displacement(rig,shapeBefore,n=>rig.data.nodes[n].id=="S08");
                    Require(result.s08ShapeDisplacementMetres>.5f,"S08 mesh did not deform visibly.");
                }
                if(time==30)departure=rig.NodeCenter(rig.FindNode("S08"));
                if(time==37){result.s08TravelMetres=Vector3.Distance(departure,rig.NodeCenter(rig.FindNode("S08")));Require(result.s08TravelMetres>20&&rig.Actual[4]>.999f,"S08 travel failed or changed its shape.");}
                if(time==44)
                {
                    Require(Mathf.Abs(rig.Actual[5]-.15f)<.001f&&rig.approachProgress==0&&view.linkedViews.RoomId=="O13","O13 shape is not isolated or its room view did not switch.");
                    result.o13ShapeDisplacementMetres=Displacement(rig,shapeBefore,n=>rig.data.nodes[n].id=="O13");
                    Require(result.o13ShapeDisplacementMetres>.5f,"O13 mesh did not deform visibly.");
                }
                if(time==45)approach=rig.NodeCenter(rig.FindNode("O13"));
                if(time==51){result.o13TravelMetres=Vector3.Distance(approach,rig.NodeCenter(rig.FindNode("O13")));Require(result.o13TravelMetres>10&&Mathf.Abs(rig.Actual[5]-.15f)<.001f,"O13 travel failed or changed shape.");}
                if(time==52)Require(state.IsComparing&&rig.GetTravel(0)==0,"Compare did not show the previously applied state.");
                if(time==54)Require(!state.IsComparing&&state.HasChanges&&rig.GetTravel(0)==1,"Compare lost the proposed travel.");
                if(time==56)Require(!state.HasChanges,"Final Apply failed.");
                result.checkpoints.Add(new Checkpoint{seconds=time,chapter=view.dashboard.DemonstrationChapter,remoteRoom=view.linkedViews.RoomId,shape=(float[])rig.Actual.Clone(),travel=new[]{rig.departureProgress,rig.approachProgress},proposal=state.HasChanges,comparing=state.IsComparing});
                if(time==0||time==11||time==22||time==29||time==37||time==44||time==51||time==56)
                    EngineeringVerification.Capture(view.head,"gesture-"+time.ToString("00")+"s.png");
            }
            result.passed=true;File.WriteAllText(Path.GetFullPath("../Documentation/Validation/GestureRehearsal.json"),JsonUtility.ToJson(result,true));
            checks.Add("Visible gesture rehearsal through actual UI colliders and ProcessInput: collective unfurl, regional fold, S08/O13 independent shape and travel, release-preview, comparison and Apply passed; head pose and overview zoom fixed");
            demo.Stop();view.ProcessHardwareInput=false;
            Require(JsonUtility.ToJson(state.Capture())==priorProposal&&JsonUtility.ToJson(state.CopyApplied())==priorApplied,"Stopping a rehearsal must restore the user's proposal and applied state.");
            checks.Add("Stopping the synthetic gesture rehearsal restores the user's applied state and proposal: passed");
        }
        private static Vector3[][] Vertices(CollectiveRig rig)=>rig.filters.Select(f=>f.sharedMesh.vertices).ToArray();
        private static float Displacement(CollectiveRig rig,Vector3[][] before,Func<int,bool> include)
        {
            float max=0;for(int b=0;b<rig.filters.Length;b++)
            {
                var now=rig.filters[b].sharedMesh.vertices;
                for(int i=0;i<now.Length;i++)if(include(rig.data.drivers[rig.data.batches[b].vertexDrivers[i]].node))max=Mathf.Max(max,Vector3.Distance(before[b][i],now[i]));
            }
            return max;
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SpatialCollective
{
    public sealed partial class CollectiveExperience
    {
        [Tooltip("Direct spatial gestures and read-only values. Disable only for the legacy engineering console.")]
        public bool directSpatialInteraction=true; 
        [Range(1,2)] public float inhabitedScale=1.4f;
        private enum SpatialMode 
        { 
            Idle,
            PendingRight, 
            PendingLeft, 
            Span, 
            Shape,
            Travel, 
            Coupling 
        }
        private SpatialMode spatialMode;
        private readonly List<CollectiveSnapshot> spatialHistory=new List<CollectiveSnapshot>();
        private CollectiveSnapshot spatialStart;
        private Vector3 spatialRightStart,spatialLeftStart,spatialTravelAxis;
        private float spatialClock,spatialSpan,spatialValue,viewHold,undoHold,jumpHold;
        private int jumpNode=-1;
        public const float CabinJumpHoldSeconds=.65f;
        private int pendingSpatialNode=-1;
        private bool spatialTargetResolved;
        public const float OneHandDeadZone=.008f;
        public bool SpatialPointerOnOverview {get;private set;}
        public string SpatialHint
        {
            get
            {
                if(spatialTrackingInterrupted)return "SHOW BOTH HANDS / OPEN TO RESUME";
                if(IsTransitioning)return "CHANGING SPACE";
                if(jumpHold>0&&jumpNode>=0)return "JUMP "+rig.data.nodes[jumpNode].id+" / HOLD "+Mathf.RoundToInt(Mathf.Clamp01(jumpHold/CabinJumpHoldSeconds)*100)+"%";
                if(spatialMode==SpatialMode.Span||spatialMode==SpatialMode.Shape)
                {
                    float p=rig.GetBaseline(selectedRegion);
                    return p<=.001f||p>=.999f?"SHAPE LIMIT / REVERSE HAND MOTION":"RESHAPING / RELEASE TO KEEP";
                }
                if(spatialMode==SpatialMode.Travel)return "MOVING CABIN / RELEASE TO KEEP";
                if(spatialMode==SpatialMode.Coupling)return "LINK RESPONSE / RELEASE TO KEEP";
                int target=InspectionNode>=0?InspectionNode:rig.selectedNode;
                string action=!rig.inside||target!=rig.selectedNode?"ENTER "+rig.data.nodes[target].id:"OVERVIEW";
                return viewHold>0?action+" / HOLD "+Mathf.RoundToInt(Mathf.Clamp01(viewHold/.75f)*100)+"%":
                    "R MIDDLE: "+rig.data.nodes[target].id+" / BOTH: "+action;
            }
        }
        private static float DeliberateMotion(float delta)=>Mathf.Sign(delta)*Mathf.Max(0,Mathf.Abs(delta)-OneHandDeadZone);
        private bool spatialNeedsRelease=true,spatialActionLatched,spatialTrackingInterrupted;
        public bool SpatialReady=>!spatialNeedsRelease&&!IsTransitioning;
        public bool SpatialEditing=>spatialStart!=null;
        public string SpatialOperation=>spatialMode.ToString();
        public int SpatialUndoCount=>spatialHistory.Count;
        public void ProcessSpatialInput(CollectiveInputFrame frame,Ray ray,float seconds)
        {
            //Process direct spatial interaction only when enabled.
            if (!directSpatialInteraction)return;  
            float dt=Mathf.Clamp(seconds,0,.1f);
            bool tracked=frame.leftTracked&&frame.rightTracked;
            bool open=tracked&&!frame.primary&&!frame.secondary&&!frame.leftMiddlePinch&&!frame.rightMiddlePinch&&!frame.leftFist;
            if(IsTransitioning||!tracked)
            {
                CancelSpatialEdit();
                SetSpatialPointer(false,ray,-1);
                if(!tracked){spatialTrackingInterrupted=true;feedback="Tracking interrupted. Unfinished edit cancelled; open both hands to resume.";}
                return;
            }
            if(spatialNeedsRelease)
            {
                if(open)
                {
                    spatialNeedsRelease=false;spatialActionLatched=false;
                    if(spatialTrackingInterrupted){spatialTrackingInterrupted=false;feedback="Tracking restored. Ready for a new gesture.";}
                }
                else return;
            }
            if(open)spatialActionLatched=false;
            if(frame.recall && open && !SpatialEditing)
            {
                SelectSpatialNode(rig.FindNode(study.Profile.referenceRoom),true);
                PlaceMenu();
                feedback="WHOLE HABITAT selected. Spread / close two index pinches.";
            }
            if(!SpatialEditing)  
            {
                // A deliberate one-handed middle pinch jumps to a selected/model-pointed cabin.
                // Lock the target at gesture onset so late hand-ray jitter cannot change the destination.
                bool jumpGesture=frame.rightMiddlePinch&&!frame.leftMiddlePinch&&!frame.primary&&!frame.secondary;
                if(jumpGesture&&!spatialActionLatched)
                {
                    if(jumpHold==0)
                    {
                        int pointed=frame.hasPointer?PickSpatialNode(ray):-1;
                        jumpNode=pointed>=0?pointed:InspectionNode;
                        if(jumpNode>=0)SelectSpatialNode(jumpNode,false);
                    }
                    jumpHold+=dt;viewHold=undoHold=0;
                    SetSpatialPointer(frame.hasPointer,ray,jumpNode);
                    if(jumpHold>=CabinJumpHoldSeconds)
                    {
                        int destination=jumpNode;jumpHold=0;jumpNode=-1;
                        spatialActionLatched=true;spatialNeedsRelease=true;
                        JumpToCabin(destination);
                    }
                    return;
                }
                jumpHold=0;jumpNode=-1;
                bool viewGesture=frame.leftMiddlePinch&&frame.rightMiddlePinch&&!frame.primary&&!frame.secondary;
                viewHold=viewGesture?viewHold+dt:0;
                bool undoGesture=frame.leftFist&&!frame.primary&&!frame.secondary&&!frame.rightMiddlePinch;
                undoHold=undoGesture?undoHold+dt:0;
                if(!spatialActionLatched && viewHold>=.75f)
                {
                    spatialActionLatched=true;
                    spatialNeedsRelease=true;
                    viewHold=0;
                    Execute(CollectiveCommand.ToggleView,0);
                    return;
                }
                if(!spatialActionLatched && undoHold>=.75f)
                {
                    spatialActionLatched=true;
                    spatialNeedsRelease=true;
                    undoHold=0;
                    if(spatialHistory.Count>0)
                    {
                        var previous=spatialHistory[spatialHistory.Count-1];spatialHistory.RemoveAt(spatialHistory.Count-1);
                        stateController.Restore(previous);
                        rig.Rebuild();
                        stateController.Commit();
                        feedback="Previous gesture restored.";
                    }
                    else feedback="No earlier gesture in this session.";
                    return;
                }
                if(viewGesture||undoGesture||spatialActionLatched)
                {
                    SetSpatialPointer(false,ray,-1);
                    return;
                }
            }
            if(spatialMode==SpatialMode.Idle)
            {
                int hover=frame.hasPointer?PickSpatialNode(ray):-1;
                SetSpatialPointer(frame.hasPointer,ray,hover);
                if(frame.primary&&frame.secondary)   //Both hands pinching enters Span.
                {
                    BeginSpatial(SpatialMode.Span,frame);
                    return;
                }
                //Right-hand pinch enters PendingRight, then Shape or Travel.
                if (frame.primary && !frame.leftMiddlePinch && !frame.rightMiddlePinch)
                {
                    BeginSpatial(SpatialMode.PendingRight,frame);
                    pendingSpatialNode=hover;
                    return;
                }
                //Left middle-finger pinch enters PendingLeft, then Coupling.
                if (frame.leftMiddlePinch && !frame.rightMiddlePinch && !frame.primary && !frame.secondary && selectedRegion<4)
                {
                    BeginSpatial(SpatialMode.PendingLeft,frame);
                    return;
                }
                return;
            }
            // Wait briefly for an asynchronous second hand before moving a remote unit.
            if(spatialMode==SpatialMode.PendingRight && frame.secondary && frame.primary)
            {
                spatialMode=SpatialMode.Span;
                spatialSpan=Vector3.Distance(frame.leftPosition,frame.rightPosition);
                spatialValue=rig.GetBaseline(selectedRegion);
                return;
            }
            if(spatialMode==SpatialMode.PendingLeft && frame.leftMiddlePinch && frame.rightMiddlePinch && !frame.primary && !frame.secondary)
            {
                CancelSpatialEdit();
                spatialNeedsRelease=false;
                viewHold=dt;
                return;
            }
            bool held=spatialMode==SpatialMode.Span?frame.primary&&frame.secondary:
                spatialMode==SpatialMode.Coupling||spatialMode==SpatialMode.PendingLeft?frame.leftMiddlePinch&&!frame.primary&&!frame.secondary:frame.primary;
            if(!held)
            {
                if(spatialMode==SpatialMode.PendingRight && pendingSpatialNode>=0)SelectSpatialNode(pendingSpatialNode,false);
                FinishSpatialEdit();    // Compare with the starting snapshot to detect changes.
                return;
            }
            spatialClock+=dt;
            if(spatialMode==SpatialMode.PendingRight)
            {
                if(spatialClock<.22f)return;
                if(!spatialTargetResolved)
                {
                    if(pendingSpatialNode>=0)SelectSpatialNode(pendingSpatialNode,false);
                    pendingSpatialNode=-1;spatialTargetResolved=true;
                    spatialValue=selectedRegion>=4?rig.GetTravel(selectedRegion-4):rig.GetBaseline(selectedRegion);
                    // Pinch closure/selection motion does not deform or move the newly selected cabin.
                    spatialRightStart=frame.rightPosition;
                }
                float motion=selectedRegion>=4?Vector3.Dot(frame.rightPosition-spatialRightStart,spatialTravelAxis):frame.rightPosition.y-spatialRightStart.y;
                if(Mathf.Abs(motion)<=OneHandDeadZone)return;
                spatialMode=selectedRegion>=4?SpatialMode.Travel:SpatialMode.Shape;
            }
            if(spatialMode==SpatialMode.PendingLeft)
            {
                if(spatialClock<.22f)return;
                spatialMode=SpatialMode.Coupling;
                spatialValue=selectedRegion==0?(rig.GetCoupling(0)+rig.GetCoupling(1)+rig.GetCoupling(2))/3:rig.GetCoupling(selectedRegion-1);
            }
            float value=spatialValue;
            switch(spatialMode)
            {
                case SpatialMode.Span:
                    value+=(Vector3.Distance(frame.leftPosition,frame.rightPosition)-spatialSpan)/handTravel;
                    rig.SetBaseline(selectedRegion,value);
                    break;
                case SpatialMode.Shape:
                    value+=DeliberateMotion(frame.rightPosition.y-spatialRightStart.y)/handTravel;
                    rig.SetBaseline(selectedRegion,value);
                    break;
                case SpatialMode.Travel:
                    value+=DeliberateMotion(Vector3.Dot(frame.rightPosition-spatialRightStart,spatialTravelAxis))*(selectedRegion==5?-1:1)/handTravel;
                    rig.SetTravel(selectedRegion-4,value);
                    break;
                case SpatialMode.Coupling:
                    value+=(frame.leftPosition.y-spatialLeftStart.y)*2/handTravel;
                    rig.coupled=true;
                    if(selectedRegion==0)
                        for(int i=0;i<3;i++)
                            rig.SetCoupling(i,value);
                    else 
                        rig.SetCoupling(selectedRegion-1,value);
                    break;
            }
            rig.MarkDirty();feedback="Direct "+spatialMode+" gesture. Release to keep the result.";
        }
        private void BeginSpatial(SpatialMode mode,CollectiveInputFrame frame)
        {
            StopMotion();
            stateController.EndCompare();
            spatialStart=stateController.Capture();
            spatialMode=mode;spatialClock=0;spatialTargetResolved=false;viewHold=undoHold=jumpHold=0;jumpNode=-1;pendingSpatialNode=-1;
            spatialRightStart=frame.rightPosition;spatialLeftStart=frame.leftPosition;
            spatialSpan=Vector3.Distance(frame.leftPosition,frame.rightPosition);spatialValue=rig.GetBaseline(selectedRegion);
            spatialTravelAxis=Vector3.ProjectOnPlane(head.transform.forward,Vector3.up).normalized;
            if(spatialTravelAxis.sqrMagnitude<.01f)spatialTravelAxis=Vector3.forward;
            SetSpatialPointer(false,new Ray(),-1);
        }
        private void FinishSpatialEdit()
        {
            if(spatialStart!=null && JsonUtility.ToJson(spatialStart)!=JsonUtility.ToJson(stateController.Capture()))
            {
                spatialHistory.Add(spatialStart);
                if(spatialHistory.Count>20)
                    spatialHistory.RemoveAt(0);
                rig.Rebuild();
                stateController.Commit();
                feedback="Gesture result retained.";
                Debug.Log("[SpatialCollective] Gesture committed: region="+selectedRegion+", shape="+rig.GetBaseline(selectedRegion)+", S08 travel="+rig.GetTravel(0)+", O13 travel="+rig.GetTravel(1));
            }
            spatialStart=null;
            spatialMode=SpatialMode.Idle;
            spatialNeedsRelease=true;
        }
        private void CancelSpatialEdit()
        {
            if(spatialStart!=null && stateController!=null)
            {
                stateController.Restore(spatialStart);
                rig.Rebuild();
            }
            spatialStart=null;
            spatialMode=SpatialMode.Idle;
            spatialNeedsRelease=true;
            viewHold=undoHold=jumpHold=0;
            jumpNode=-1;
        }
        public bool JumpToCabin(int node)
        {
            if(IsTransitioning||SpatialEditing||node<0||node>=rig.data.nodes.Length)return false;
            if(rig.inside&&rig.selectedNode==node){feedback="Already inside "+rig.data.nodes[node].id+". Point to another cabin.";return false;}
            bimanual.Suspend();
            if(stateController.IsComparing)stateController.EndCompare();
            if(rig.inside)roomHistory.Push(rig.selectedNode);
            StartCoroutine(ChangeView(true,node));return true;
        }
        private void SelectSpatialNode(int node,bool whole)
        {
            selectedRegion=whole?0:rig.RegionForNode(node);InspectionNode=node;
            selectedControl=null;bimanualSelected=true;
            if(!rig.inside){rig.selectedNode=node;rig.MarkDirty();}
            rig.Highlight(selectedRegion);
            feedback=(whole?"WHOLE HABITAT":rig.data.nodes[node].id)+" selected. "+SpatialHint;
        }
        public int PickSpatialNode(Ray ray)
        {
            // The overview renders the shared geometry.
            // A ray inside its bounds must not fall through and select the room behind it.
            Ray modelRay=default;
            bool overview=dashboard!=null&&dashboard.TryOverviewRay(ray,out modelRay,out _);
            if(overview)ray=modelRay;
            else if(dashboard!=null&&dashboard.IsReadOnlySurface(ray))return -1;
            return PickSpatialVolume(ray,overview);
        }
        private int PickSpatialVolume(Ray ray,bool overview)
        {
            int result=-1;float best=float.PositiveInfinity,s=rig.transform.lossyScale.x;
            var filter=head.GetComponent<CollectiveViewFilter>();
            for(int n=0;n<rig.data.nodes.Length;n++)
            {
                if(!overview&&rig.inside&&filter!=null&&filter.Weight(n)<=0)continue;
                Vector3 center=rig.transform.TransformPoint(rig.NodeCenter(n)),oc=center-ray.origin;
                float radius=(rig.data.nodes[n].kind=="T"?1.64f:2.79f)*s;
                float projection=Vector3.Dot(oc,ray.direction);
                if(projection<=0)continue;
                float distance=(oc-ray.direction*projection).magnitude;
                if(distance>radius)continue;
                float entry=projection-Mathf.Sqrt(radius*radius-distance*distance);
                if(entry<0)continue;
                // In the translucent overview, aim at the visible cabin centre instead of
                // letting a large foreground pick sphere swallow a smaller cabin behind it.
                float score=overview?distance/radius:entry;
                if(score<best){best=score;result=n;}
            }
            return result;
        }
        private void SetSpatialPointer(bool visible,Ray ray,int node)
        {
            SpatialPointerOnOverview=visible&&dashboard!=null&&dashboard.TryOverviewRay(ray,out _,out _);
            if(pointerLine!=null)pointerLine.enabled=false;
            if(reticle==null)return;
            reticle.gameObject.SetActive(visible&&node>=0);
            if(visible&&node>=0)
            {
                reticle.position=dashboard!=null&&dashboard.TryOverviewRay(ray,out _,out Vector3 hit)
                    ?hit-ray.direction*.012f:rig.transform.TransformPoint(rig.NodeCenter(node));
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace SpatialCollective
{
    public enum PointerSource {None,Hand,Controller,Mouse,HandGaze}
    public struct CollectiveInputFrame
    {
        public bool primary;
        public bool secondary;
        public bool apply;
        public bool undo;
        public bool toggleView;
        public bool compare;
        public bool hasPointer;
        public float height;
        public Ray pointer;
        public PointerSource source;
        public bool leftTracked;
        public bool rightTracked;
        public bool recall;
        public bool leftMiddlePinch;
        public bool rightMiddlePinch;
        public bool leftFist;
        public Vector3 leftPosition;
        public Vector3 rightPosition;
    }
    public sealed class CollectiveInputReader : MonoBehaviour
    {
        public Transform trackingOrigin;
        public TextAsset interactionJson;
        private InteractionProfile profile;
        private XRHandSubsystem hands;
        private readonly List<XRHandSubsystem> candidates=new List<XRHandSubsystem>();
        private float nextSearch;
        private bool rightPinch,leftPinch,leftMiddle,rightMiddle;
        private float palmTime;
        private bool palmRecalled;
        private Ray pointingRay;
        private bool hasPointingRay;
        private void Awake(){profile=InteractionProfile.Load(interactionJson);}
        public CollectiveInputFrame Read()
        {
            // 1) Subsystem fallback: hand tracking may become available asynchronously after scene startup.
            //    Probe once per second to avoid the overhead of querying every frame.
            if((hands==null||!hands.running)&&Time.unscaledTime>=nextSearch)
            {
                nextSearch=Time.unscaledTime+1;
                candidates.Clear();
                SubsystemManager.GetSubsystems(candidates);
                hands=null;
                foreach(var candidate in candidates)
                {
                    if (candidate.running)
                    {
                        hands = candidate; break;
                    }
                }          
            }
            var frame=new CollectiveInputFrame();

            // 2) Read basic tracking and index-finger pinches for both hands, obtaining their world positions.
            bool rightTracked=ReadHand(false,ref rightPinch,out Vector3 rightPosition);
            bool leftTracked=ReadHand(true,ref leftPinch,out Vector3 leftPosition);
            if(!rightTracked)hasPointingRay=false;
            frame.rightTracked=rightTracked;frame.leftTracked=leftTracked;
            frame.rightPosition=rightPosition;frame.leftPosition=leftPosition;

            // 3) Add secondary gestures such as middle-finger pinches and the left fist.
            ReadSecondaryGestures(leftTracked,rightTracked,ref frame);
            // Open left palm selects the whole habitat and recalls only the read-only values.
            bool openPalm=false;
            if(leftTracked&&!leftPinch&&!rightPinch&&!frame.leftMiddlePinch&&!frame.rightMiddlePinch&&Camera.main!=null&&
                hands.leftHand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm)&&
                hands.leftHand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose tip))
            {
                Vector3 normal=trackingOrigin.rotation*palm.rotation*Vector3.down;
                Vector3 toward=Camera.main.transform.position-trackingOrigin.TransformPoint(palm.position);
                openPalm=Vector3.Dot(normal,toward.normalized)>.65f&&Vector3.Distance(tip.position,palm.position)>.09f;
            }
            if(openPalm){palmTime+=Time.unscaledDeltaTime;if(palmTime>.65f&&!palmRecalled){frame.recall=true;palmRecalled=true;}}
            else {palmTime=0;palmRecalled=false;}

            // 4) Right-hand input priority:
            //    Hand (with ray) > HandGaze (tracked without ray) > Controller > Mouse.
            if(rightTracked)
            {
                frame.source=PointerSource.HandGaze;frame.primary=rightPinch;frame.height=rightPosition.y;
                var aim=MetaAimHand.right;
                if(aim!=null)
                {
                    var flags=(MetaAimFlags)aim.aimFlags.ReadValue();
                    if((flags&MetaAimFlags.SystemGesture)!=0)return frame; // Do not consume the OS menu gesture.
                    if(aim.isTracked.isPressed && (flags&MetaAimFlags.Valid)!=0)
                    {
                        frame.pointer=new Ray(trackingOrigin.TransformPoint(aim.devicePosition.ReadValue()),trackingOrigin.rotation*aim.deviceRotation.ReadValue()*Vector3.forward);
                        frame.hasPointer=true;frame.source=PointerSource.Hand;
                    }
                }
                // EXT_hand_interaction can remain valid when Meta aim is unavailable.
                // Keep remote selection tied to the tracked hand rather than a stale ray.
                // Fall back to the OpenXR HandInteraction pointer pose when Meta Aim is unavailable.
                if(!frame.hasPointer)
                {
                    var device=InputSystem.GetDevice<HandInteractionProfile.HandInteraction>(UnityEngine.InputSystem.CommonUsages.RightHand);
                    if(device!=null)
                    {
                        var pose=device.pointer.ReadValue();
                        var required=InputTrackingState.Position|InputTrackingState.Rotation;
                        if(pose.isTracked&&(pose.trackingState&required)==required)
                        {
                            frame.pointer=new Ray(trackingOrigin.TransformPoint(pose.position),trackingOrigin.rotation*pose.rotation*Vector3.forward);
                            frame.hasPointer=true;frame.source=PointerSource.Hand;
                        }
                    }
                }
            }
            else
            {
                // Try the right controller when the right hand is not tracked.
                var controller=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if((controller.characteristics & InputDeviceCharacteristics.Controller)!=0 && controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool tracked)&&tracked&&
                    controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition,out Vector3 position))
                {
                    frame.source=PointerSource.Controller;frame.height=trackingOrigin.TransformPoint(position).y;
                    controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton,out frame.primary);
                    controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton,out frame.apply);
                    controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton,out frame.undo);
                    if(controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation,out Quaternion rotation))
                    {
                        frame.pointer=new Ray(trackingOrigin.TransformPoint(position),trackingOrigin.rotation*rotation*Vector3.forward);frame.hasPointer=true;
                    }
                }
            }
            if(rightTracked)
            {
                // Pinching changes finger geometry and may move the runtime aim ray.
                // Freeze the last open-hand ray for target acquisition only; deformation
                // continues to use the current tracked positions above.
                if(!rightPinch&&!frame.rightMiddlePinch){hasPointingRay=frame.hasPointer;if(hasPointingRay)pointingRay=frame.pointer;}
                else if(hasPointingRay){frame.pointer=pointingRay;frame.hasPointer=true;frame.source=PointerSource.Hand;}
            }
            frame.secondary=leftTracked&&leftPinch;
            if(!leftTracked)
            {
                // Use the left controller for secondary/toggleView/compare when the left hand is not tracked.
                var controller=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                if((controller.characteristics & InputDeviceCharacteristics.Controller)!=0 && controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool tracked)&&tracked)
                {
                    controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton,out frame.secondary);
                    controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton,out frame.toggleView);
                    controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton,out frame.compare);
                }
            }
            if(frame.source==PointerSource.None && !XRSettings.isDeviceActive && Mouse.current!=null)
            {
                // Final desktop fallback: left mouse button provides primary input; mouse Y pixels map to height.
                frame.source=PointerSource.Mouse;frame.primary=Mouse.current.leftButton.isPressed;
                frame.height=Mouse.current.position.ReadValue().y/profile.mousePixelsPerMetre;
            }

            // 5) Return a unified frame so callers need not handle individual hardware sources.
            return frame;
        }
        private void ReadSecondaryGestures(bool leftTracked,bool rightTracked,ref CollectiveInputFrame frame)
        {
            frame.leftMiddlePinch=MiddlePinch(leftTracked,true,ref leftMiddle);
            frame.rightMiddlePinch=MiddlePinch(rightTracked,false,ref rightMiddle);
            if(frame.leftMiddlePinch)leftPinch=false;
            if(frame.rightMiddlePinch)rightPinch=false;
            if(frame.leftMiddlePinch&&!leftPinch && hands.leftHand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose leftPalm))
                frame.leftPosition=trackingOrigin.TransformPoint(leftPalm.position);
            if(frame.rightMiddlePinch&&!rightPinch && hands.rightHand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose rightPalm))
                frame.rightPosition=trackingOrigin.TransformPoint(rightPalm.position);
            if(leftTracked && !leftPinch && !leftMiddle && hands.leftHand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm))
            {
                frame.leftFist=true;
                foreach(var joint in new[]{XRHandJointID.IndexTip,XRHandJointID.MiddleTip,XRHandJointID.RingTip,XRHandJointID.LittleTip})
                    if(!hands.leftHand.GetJoint(joint).TryGetPose(out Pose tip)||Vector3.Distance(tip.position,palm.position)>.065f)frame.leftFist=false;
            }
        }
        private bool MiddlePinch(bool tracked,bool left,ref bool held)
        {
            if(!tracked){held=false;return false;}
            XRHand hand=left?hands.leftHand:hands.rightHand;
            if(!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb)||!hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out Pose middle)||
                !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index))
                {held=false;return false;}
            held=EvaluateMiddlePinch(Vector3.Distance(thumb.position,middle.position),Vector3.Distance(thumb.position,index.position),held,profile.pinchPressMetres,profile.pinchReleaseMetres);
            return held;
        }
        public static bool EvaluateMiddlePinch(float middleDistance,float indexDistance,bool held,float press,float release)
            =>EvaluatePinch(middleDistance,held,press,release)&&middleDistance+.006f<indexDistance;
        private bool ReadHand(bool left,ref bool pressed,out Vector3 position)
        {
            position=Vector3.zero;
            if(hands==null||!hands.running){pressed=false;return false;}
            XRHand hand=left?hands.leftHand:hands.rightHand;
            if(!hand.isTracked||!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb)||
                !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index)){pressed=false;return false;}
            var aim=left?MetaAimHand.left:MetaAimHand.right;
            if(aim!=null && (((MetaAimFlags)aim.aimFlags.ReadValue())&MetaAimFlags.SystemGesture)!=0){pressed=false;return false;}
            position=trackingOrigin.TransformPoint((thumb.position+index.position)*.5f);
            pressed=EvaluatePinch(Vector3.Distance(thumb.position,index.position),pressed,profile.pinchPressMetres,profile.pinchReleaseMetres);
            return true;
        }
        public static bool EvaluatePinch(float distance,bool wasPressed,float press,float release)
            =>!float.IsNaN(distance)&&distance>=0&&distance<(wasPressed?release:press);
        private void OnDisable(){rightPinch=leftPinch=leftMiddle=rightMiddle=false;palmTime=0;palmRecalled=false;hasPointingRay=false;}
    }
}

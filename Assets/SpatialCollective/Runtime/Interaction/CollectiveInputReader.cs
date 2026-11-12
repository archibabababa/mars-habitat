using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace SpatialCollective
{
    /// <summary>Pointing source; HandGaze means a hand is tracked but no valid hand pointing ray is available.</summary>
    public enum PointerSource {None,Hand,Controller,Mouse,HandGaze}
    /// <summary>Unified input frame containing buttons, ray, tracking, gestures, and world positions for both hands.</summary>
    public struct CollectiveInputFrame
    {
        /// <summary>Whether the primary input is pressed: right pinch, right trigger, or left mouse button; used for selection and parameter dragging.</summary>
        public bool primary;

        /// <summary>Whether the secondary input is pressed, usually left pinch or left trigger; commonly combined with primary for two-hand interaction.</summary>
        public bool secondary;

        /// <summary>Whether Apply is triggered, typically by a controller primary button or keyboard binding.</summary>
        public bool apply;

        /// <summary>Whether Undo to the applied state is triggered.</summary>
        public bool undo;

        /// <summary>Whether the overview/interior view-switch command is triggered.</summary>
        public bool toggleView;

        /// <summary>Whether comparison with the applied state is held; press to compare and release to return to the preview.</summary>
        public bool compare;

        /// <summary>Whether this frame has a valid pointer ray; callers generally fall back to head-forward pointing when false.</summary>
        public bool hasPointer;

        /// <summary>Input height, from world Y or mapped mouse pixels, used to convert vertical hand displacement into parameter changes.</summary>
        public float height;

        /// <summary>World-space pointing ray from hand, controller, or mouse, used to hit UI or model nodes.</summary>
        public Ray pointer;

        /// <summary>Input source for this frame: None, Hand, Controller, Mouse, or HandGaze.</summary>
        public PointerSource source;

        /// <summary>Whether the left hand is tracked successfully.</summary>
        public bool leftTracked;

        /// <summary>Whether the right hand is tracked successfully.</summary>
        public bool rightTracked;

        /// <summary>Whether a gesture requests menu recall or return to the reference interaction pose, such as holding the left palm open.</summary>
        public bool recall;

        /// <summary>Whether the left middle-finger pinch is active, for special spatial gestures.</summary>
        public bool leftMiddlePinch;

        /// <summary>Whether the right middle-finger pinch is active, for cabin jumping, confirmation, and other spatial gestures.</summary>
        public bool rightMiddlePinch;

        /// <summary>Whether the left fist is closed, for the spatial undo hold gesture.</summary>
        public bool leftFist;

        /// <summary>Left-hand world position, usually the thumb/index fingertip midpoint; some gestures use the palm instead.</summary>
        public Vector3 leftPosition;

        /// <summary>Right-hand world position, usually the thumb/index fingertip midpoint; some gestures use the palm instead.</summary>
        public Vector3 rightPosition;
    }
    /// <summary>Recognize gestures from XR Hands joints, then try Meta pointing, OpenXR hand pointing, controllers, and mouse; never modify the model here.</summary>
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
        /// <summary>Load pinch thresholds and mouse sensitivity settings.</summary>
        private void Awake(){profile=InteractionProfile.Load(interactionJson);}
        /// <summary>Read hands, controllers, or mouse into a unified frame; periodically rediscover the hand subsystem after tracking loss.</summary>
        /// <returns>Frame containing the input source, gesture states, and an optional pointing ray.</returns>
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
                if(!rightPinch){hasPointingRay=frame.hasPointer;if(hasPointingRay)pointingRay=frame.pointer;}
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
        /// <summary>Detect middle-finger pinches and the left fist; use palm position during middle-finger pinches.</summary>
        /// <param name="leftTracked">Whether the left hand is tracked successfully.</param>
        /// <param name="rightTracked">Whether the right hand is tracked successfully.</param>
        /// <param name="frame">Input frame to receive secondary gesture states.</param>
        private void ReadSecondaryGestures(bool leftTracked,bool rightTracked,ref CollectiveInputFrame frame)
        {
            frame.leftMiddlePinch=MiddlePinch(leftTracked,true,ref leftMiddle);
            frame.rightMiddlePinch=MiddlePinch(rightTracked,false,ref rightMiddle);
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
        /// <summary>Detect middle-finger pinching with hysteresis from thumb-to-middle fingertip distance.</summary>
        /// <param name="tracked">Whether this hand is tracked.</param>
        /// <param name="left">True reads the left hand; false reads the right hand.</param>
        /// <param name="held">Previous pinch state, updated by this method.</param>
        private bool MiddlePinch(bool tracked,bool left,ref bool held)
        {
            if(!tracked){held=false;return false;}
            XRHand hand=left?hands.leftHand:hands.rightHand;
            if(!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb)||!hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out Pose middle))
                {held=false;return false;}
            held=EvaluatePinch(Vector3.Distance(thumb.position,middle.position),held,profile.pinchPressMetres,profile.pinchReleaseMetres);
            return held;
        }
        /// <summary>Read thumb/index pinch and fingertip midpoint, excluding system gestures.</summary>
        /// <param name="left">True reads the left hand; false reads the right hand.</param>
        /// <param name="pressed">Previous pinch state, updated to the current state.</param>
        /// <param name="position">Output world-space fingertip midpoint; zero when not tracked.</param>
        /// <returns>Whether all required joints are tracked successfully.</returns>
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
        /// <summary>Use separate press and release distance thresholds for hysteresis to prevent boundary jitter.</summary>
        /// <param name="distance">Distance between fingertips in meters.</param>
        /// <param name="wasPressed">Whether the pinch was active in the previous frame.</param>
        /// <param name="press">Distance threshold for starting a pinch.</param>
        /// <param name="release">Distance threshold for releasing an active pinch.</param>
        /// <returns>Whether the pinch is currently considered active.</returns>
        public static bool EvaluatePinch(float distance,bool wasPressed,float press,float release)
            =>!float.IsNaN(distance)&&distance>=0&&distance<(wasPressed?release:press);
        /// <summary>Clear cached gestures and rays on disable to avoid reusing stale state on resume.</summary>
        private void OnDisable(){rightPinch=leftPinch=leftMiddle=rightMiddle=false;palmTime=0;palmRecalled=false;hasPointingRay=false;}
    }
}

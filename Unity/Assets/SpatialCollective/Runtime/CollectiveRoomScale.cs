using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace SpatialCollective
{
    public sealed class CollectiveRoomScale : MonoBehaviour
    {
        [Min(.1f)] public float radiusMetres=1.2f;
        [Min(.1f)] public float compactRoomRadiusMetres=1f;
        private bool CompactRoom=>experience!=null&&experience.rig.inside&&experience.rig.data.nodes[experience.rig.selectedNode].kind=="T";
        public float EffectiveRadius=>Mathf.Max(.1f,CompactRoom?Mathf.Min(radiusMetres,compactRoomRadiusMetres):radiusMetres);
        public Vector3 Center {get;private set;}
        public float HorizontalDisplacement {get;private set;}
        public string Status {get;private set;}="ROOM SCALE / 1:1";
        private CollectiveExperience experience;
        private LineRenderer circle;
        private Material material;
        private readonly List<XRInputSubsystem> inputs=new List<XRInputSubsystem>();
        public void Initialize(CollectiveExperience view)
        {
            experience=view;Calibrate();SubsystemManager.GetSubsystems(inputs);
            foreach(var input in inputs)input.trackingOriginUpdated+=OriginChanged;
            var go=new GameObject("Study-area cue (not physical boundary)");go.layer=11;
            circle=go.AddComponent<LineRenderer>();circle.useWorldSpace=true;circle.positionCount=64;circle.loop=true;circle.widthMultiplier=.008f;
            material=new Material(Shader.Find("SpatialCollective/SpatialTint"));material.color=new Color(.4f,.8f,.85f,.2f);circle.sharedMaterial=material;
        }
        public void Calibrate()
        {
            if(experience==null)return;
            Center=new Vector3(experience.head.transform.position.x,experience.trackingOrigin.position.y,experience.head.transform.position.z);
        }
        private void OriginChanged(XRInputSubsystem subsystem)
        {
            experience.SuspendInteraction();Calibrate();experience.linkedViews.Calibrate();experience.PlaceMenu();
        }
        public void Refresh()
        {
            if(experience==null)return;
            HorizontalDisplacement=Vector3.ProjectOnPlane(experience.head.transform.position-Center,Vector3.up).magnitude;
            float radius=EffectiveRadius;
            bool edge=HorizontalDisplacement>radius*.8f;
            Status=edge?"NEAR STUDY EDGE / STEP BACK":CompactRoom?"COMPACT ROOM / 2.0 m STUDY DIAMETER":$"ROOM SCALE  {HorizontalDisplacement:0.00} m / 1:1";
            if(circle==null)return;circle.enabled=experience.HeadsetLayout;
            material.color=edge?new Color(1,.65f,.24f,.8f):new Color(.4f,.8f,.85f,.16f);
            for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;circle.SetPosition(i,Center+new Vector3(Mathf.Cos(a)*radius,.015f,Mathf.Sin(a)*radius));}
        }
        private void LateUpdate()=>Refresh();
        private void OnDestroy()
        {
            foreach(var input in inputs)input.trackingOriginUpdated-=OriginChanged;
            if(circle!=null)Destroy(circle.gameObject);if(material!=null)Destroy(material);
        }
    }
}

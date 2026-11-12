using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Recording-only posed copy of the shipped Unity hand mesh. No live XR claim.</summary>
public sealed class WorkspaceDemoHandPose
{
    public readonly Transform wrist,indexTip,thumbTip,palm;
    private readonly Transform[] bones;
    private readonly Dictionary<Transform,Quaternion> rest=new Dictionary<Transform,Quaternion>();
    private readonly string prefix;
    private float closure;
    public Vector3 PinchPosition=>(indexTip.position+thumbTip.position)*.5f;
    public float TipDistance=>Vector3.Distance(indexTip.position,thumbTip.position);
    public float MiddleDistance=>Vector3.Distance(Bone("MiddleTip").position,thumbTip.position);
    public WorkspaceDemoHandPose(bool left,Material material)
    {
        prefix=left?"L_":"R_";
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SpatialCollective/OfficialHands/"+(left?"Left":"Right")+" Hand Tracking.prefab"));
        root.name=(left?"Left":"Right")+" hand - SYNTHETIC RECORDING POSE";
        foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
        bones=root.GetComponentsInChildren<Transform>(true);foreach(var b in bones){b.gameObject.layer=12;rest[b]=b.localRotation;}
        wrist=Bone("Wrist");indexTip=Bone("IndexTip");thumbTip=Bone("ThumbTip");palm=Bone("Palm");
        var skin=root.GetComponentInChildren<SkinnedMeshRenderer>(true);skin.sharedMaterial=material;skin.enabled=true;skin.updateWhenOffscreen=true;
    }
    private Transform Bone(string name)=>bones.First(b=>b.name==prefix+name);
    public void Set(Vector3 position,Quaternion rotation,bool pinch,float dt,bool recall)
    {
        closure=Mathf.MoveTowards(closure,pinch?1:0,dt*12);
        foreach(var b in bones)b.localRotation=rest[b];
        wrist.SetPositionAndRotation(Vector3.zero,rotation);
        foreach(string digit in new[]{"Middle","Ring","Little"})
            foreach(string joint in new[]{"Proximal","Intermediate","Distal"})Bone(digit+joint).localRotation*=Quaternion.Euler(closure*25,0,0);
        if(closure>0)
        {
            float sign=prefix=="L_"?1:-1;
            Vector3 centre=wrist.TransformPoint(new Vector3(sign*.024f,-.036f,.105f));
            Vector3 indexGoal=Vector3.Lerp(indexTip.position,centre+wrist.up*.004f,closure);
            Vector3 thumbGoal=Vector3.Lerp(thumbTip.position,centre-wrist.up*.004f,closure);
            Solve(new[]{Bone("IndexProximal"),Bone("IndexIntermediate"),Bone("IndexDistal")},indexTip,indexGoal);
            Solve(new[]{Bone("ThumbMetacarpal"),Bone("ThumbProximal"),Bone("ThumbDistal")},thumbTip,thumbGoal);
        }
        wrist.position+=position-PinchPosition;
    }
    private static void Solve(Transform[] chain,Transform tip,Vector3 goal)
    {
        for(int iteration=0;iteration<12;iteration++)for(int i=chain.Length-1;i>=0;i--)
            chain[i].rotation=Quaternion.FromToRotation(tip.position-chain[i].position,goal-chain[i].position)*chain[i].rotation;
    }
    public void SetMiddle(Vector3 position,Quaternion rotation,float amount)
    {
        Set(position,rotation,false,1,false);
        float sign=prefix=="L_"?1:-1;
        var middle=Bone("MiddleTip");
        Vector3 centre=wrist.TransformPoint(new Vector3(sign*.01f,-.036f,.105f));
        Solve(new[]{Bone("MiddleProximal"),Bone("MiddleIntermediate"),Bone("MiddleDistal")},middle,Vector3.Lerp(middle.position,centre+wrist.up*.004f,amount));
        Solve(new[]{Bone("ThumbMetacarpal"),Bone("ThumbProximal"),Bone("ThumbDistal")},thumbTip,Vector3.Lerp(thumbTip.position,centre-wrist.up*.004f,amount));
        wrist.position+=position-PinchPosition;
    }
}

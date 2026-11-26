using UnityEngine;

namespace SpatialCollective
{
    [RequireComponent(typeof(Camera))]
    public sealed class CollectiveViewFilter : MonoBehaviour
    {
        public CollectiveRig rig;
        public bool overview,followSelected=true;
        public int focusNode;
        [Range(0,1)] public float neighborVisibility=.65f;
        private readonly float[] weights=new float[32];
        public int VisibleCount {get;private set;}
        public float Weight(int node)
        {
            if(rig==null||overview||(!rig.inside&&followSelected))return 1;
            int focus=followSelected?rig.selectedNode:focusNode;
            if(node==focus)return 1;
            if(focus>=0&&focus<rig.data.nodes.Length)
            {
                foreach(int n in rig.data.nodes[focus].neighbors)if(n==node)return neighborVisibility;
                // Detached cabins have no GH links. Show only their two closest cabins as context;
                // visibility does not create a connection or change deformation parameters.
                if(followSelected&&rig.data.nodes[focus].detached&&node>=0&&node<rig.data.nodes.Length)
                {
                    Vector3 center=rig.NodeCenter(focus);
                    float distance=(rig.NodeCenter(node)-center).sqrMagnitude;int closer=0;
                    for(int n=0;n<rig.data.nodes.Length;n++)
                        if(n!=focus&&n!=node&&(rig.NodeCenter(n)-center).sqrMagnitude<distance&&++closer>=2)return 0;
                    return neighborVisibility;
                }
            }
            return 0;
        }
        public void Apply()
        {
            VisibleCount=0;
            for(int n=0;n<32;n++){weights[n]=Weight(n);if(rig!=null&&n<rig.data.nodes.Length&&weights[n]>0)VisibleCount++;}
            Shader.SetGlobalFloatArray("_SCNodeVisibility",weights);
            Shader.SetGlobalFloat("_SCFilterEnabled",1);
            bool overlay=rig!=null&&rig.inside&&followSelected&&!overview;
            Shader.SetGlobalFloat("_SCOverlayView",overlay?1:0);
            Shader.SetGlobalFloat("_SCCurrentNode",rig!=null?rig.selectedNode:-1);
            Shader.SetGlobalFloat("_SCInteriorView",rig!=null&&!overview&&!GetComponent<Camera>().orthographic&&!followSelected?1:0);
        }
        private void OnPreRender()=>Apply();
        private void OnPostRender(){Shader.SetGlobalFloat("_SCFilterEnabled",0);Shader.SetGlobalFloat("_SCCurrentNode",-1);Shader.SetGlobalFloat("_SCOverlayView",0);}
        private void OnDisable()=>Shader.SetGlobalFloat("_SCFilterEnabled",0);
    }
}

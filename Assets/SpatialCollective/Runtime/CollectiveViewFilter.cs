using UnityEngine;

namespace SpatialCollective
{
    /// <summary>Set node visibility weights per camera; overview shows the whole model, while interior views can filter other cabins.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CollectiveViewFilter : MonoBehaviour
    {
        public CollectiveRig rig;
        public bool overview,followSelected=true;
        public int focusNode;
        [Range(0,1)] public float neighborVisibility=0;
        private readonly float[] weights=new float[32];
        public int VisibleCount {get;private set;}
        /// <summary>Calculate a node's visibility weight relative to the current focus.</summary>
        /// <param name="node">Model node index to query.</param>
        /// <returns>1 for the focused node, the configured weight for neighbors, and usually 0 for other nodes.</returns>
        public float Weight(int node)
        {
            if(rig==null||overview||(!rig.inside&&followSelected))return 1;
            int focus=followSelected?rig.selectedNode:focusNode;
            if(node==focus)return 1;
            if(focus>=0&&focus<rig.data.nodes.Length)
                foreach(int n in rig.data.nodes[focus].neighbors)if(n==node)return neighborVisibility;
            return 0;
        }
        /// <summary>Write node weights and current view state to global shader variables.</summary>
        public void Apply()
        {
            VisibleCount=0;
            for(int n=0;n<32;n++){weights[n]=Weight(n);if(n<27&&weights[n]>0)VisibleCount++;}
            Shader.SetGlobalFloatArray("_SCNodeVisibility",weights);
            Shader.SetGlobalFloat("_SCFilterEnabled",1);
            Shader.SetGlobalFloat("_SCCurrentNode",rig!=null?rig.selectedNode:-1);
            Shader.SetGlobalFloat("_SCInteriorView",rig!=null&&!overview&&!GetComponent<Camera>().orthographic&&(rig.inside||!followSelected)?1:0);
        }
        /// <summary>Apply this camera's filtering parameters before rendering.</summary>
        private void OnPreRender()=>Apply();
        /// <summary>Clear global filtering after rendering to avoid affecting other cameras.</summary>
        private void OnPostRender(){Shader.SetGlobalFloat("_SCFilterEnabled",0);Shader.SetGlobalFloat("_SCCurrentNode",-1);}
        /// <summary>Disable global filtering when the component is disabled.</summary>
        private void OnDisable()=>Shader.SetGlobalFloat("_SCFilterEnabled",0);
    }
}

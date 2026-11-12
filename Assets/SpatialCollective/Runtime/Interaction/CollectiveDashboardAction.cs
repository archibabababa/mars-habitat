using UnityEngine;
namespace SpatialCollective
{
    /// <summary>Marker for an internal engineering-panel action; the panel interprets its action value.</summary>
    public sealed class CollectiveDashboardAction : MonoBehaviour
    {
        // 0/1/2: whole model, S08, O13; 3: more; 4: link all; 5: free regions; 6: gesture demonstration.
        public int action;
    }
}

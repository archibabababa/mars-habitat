using UnityEngine;
namespace SpatialCollective
{
    public sealed class CollectiveButton : MonoBehaviour
    {
        public CollectiveCommand action;
        public int argument;
        public int bindingIndex;
        public TextMesh label;
    }
}

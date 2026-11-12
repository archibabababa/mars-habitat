using UnityEngine;
namespace SpatialCollective
{
    /// <summary>Menu button command, argument, binding index and label; does not execute commands itself.</summary>
    public sealed class CollectiveButton : MonoBehaviour
    {
        /// <summary>Command type triggered by this button.</summary>
        public CollectiveCommand action;
        /// <summary>Command argument; its valid range depends on command type.</summary>
        public int argument;
        /// <summary>Index in the interaction menu configuration, used for rebinding validation.</summary>
        public int bindingIndex;
        /// <summary>Button label component.</summary>
        public TextMesh label;
    }
}

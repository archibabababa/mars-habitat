using UnityEngine;
namespace SpatialCollective
{
    /// <summary>Convert a hit on the engineering panel's overview screen into a model ray from its source camera.</summary>
    public sealed class CollectiveViewportTarget : MonoBehaviour
    {
        public Camera sourceCamera;
        /// <summary>Calculate the source camera's viewport ray from screen-local coordinates.</summary>
        /// <param name="worldPoint">World-space hit position on the screen plane.</param>
        /// <returns>Ray pointing into the model in the source camera's view.</returns>
        public Ray ModelRay(Vector3 worldPoint)
        {
            Vector3 p=transform.InverseTransformPoint(worldPoint);
            return sourceCamera.ViewportPointToRay(new Vector3(p.x+.5f,p.y+.5f,0));
        }
    }
}

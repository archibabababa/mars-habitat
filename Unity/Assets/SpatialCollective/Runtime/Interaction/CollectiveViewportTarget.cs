using UnityEngine;
namespace SpatialCollective
{
    public sealed class CollectiveViewportTarget : MonoBehaviour
    {
        public Camera sourceCamera;
        public Ray ModelRay(Vector3 worldPoint)
        {
            Vector3 p=transform.InverseTransformPoint(worldPoint);
            return sourceCamera.ViewportPointToRay(new Vector3(p.x+.5f,p.y+.5f,0));
        }
    }
}

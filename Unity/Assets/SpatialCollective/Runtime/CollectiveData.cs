using System;
using UnityEngine;

namespace SpatialCollective
{
    [Serializable] public sealed class HabitatNode
    {
        public string id, kind;
        public int region;
        public bool detached;
        public Vector3 center, floor, delta;
        public int[] neighbors;
    }
    [Serializable] public sealed class HabitatRegion
    {
        public string label;
        public Vector3 anchor, axis;
        public float angle;
    }
    [Serializable] public sealed class GeometryDriver
    {
        public int node, parent;
        public Vector3 origin, axis, offset;
        public float angle;
    }
    [Serializable] public sealed class GeometryBatch
    {
        public string kind;
        public int region, node;
        public Mesh mesh;
        public int[] vertexDrivers;
    }
    public sealed class CollectiveData : ScriptableObject
    {
        public string modelId, meshSha256;
        public HabitatNode[] nodes;
        public HabitatRegion[] regions;
        public GeometryDriver[] drivers;
        public GeometryBatch[] batches;
        public Vector3 overviewCenter = new Vector3(9,6,-4);
    }
    public static class CollectiveKinematics
    {
        public static Matrix4x4 Around(Vector3 origin, Vector3 axis, float degrees)
        {
            return Matrix4x4.Translate(origin) * Matrix4x4.Rotate(Quaternion.AngleAxis(degrees,axis.normalized)) * Matrix4x4.Translate(-origin);
        }
        public static void Evaluate(CollectiveData data, float[] activation, Matrix4x4[] result, float[] nodeActivation = null)
        {
            for (int i=0;i<data.drivers.Length;i++)
            {
                GeometryDriver d=data.drivers[i]; HabitatNode n=data.nodes[d.node];
                HabitatRegion r=data.regions[n.region];
                float p=nodeActivation != null && nodeActivation[d.node]>=0 ? nodeActivation[d.node] : activation[n.region];
                if(d.parent<0)
                    result[i]=nodeActivation != null && n.detached ? Matrix4x4.identity : Around(r.anchor,r.axis,r.angle*p)*Matrix4x4.Translate(n.delta*p);
                else
                    result[i]=result[d.parent]*Matrix4x4.Translate(d.offset*p)*Around(d.origin,d.axis,d.angle*p);
            }
        }
    }
}

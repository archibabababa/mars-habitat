using System;
using UnityEngine;

namespace SpatialCollective
{
    /// <summary>Habitat node: ID, type, region, independent-motion flag, centre/floor/displacement and neighbour indices.</summary>
    [Serializable] public sealed class HabitatNode
    {
        public string id, kind;
        public int region;
        public bool detached;
        public Vector3 center, floor, delta;
        public int[] neighbors;
    }
    /// <summary>Region name and whole-region rotation anchor, axis and maximum angle.</summary>
    [Serializable] public sealed class HabitatRegion
    {
        public string label;
        public Vector3 anchor, axis;
        public float angle;
    }
    /// <summary>Vertex deformation driver; a negative parent denotes a root, otherwise inherit the parent matrix first.</summary>
    [Serializable] public sealed class GeometryDriver
    {
        public int node, parent;
        public Vector3 origin, axis, offset;
        public float angle;
    }
    /// <summary>Mesh batch sharing one material category; vertexDrivers assigns a driver index per vertex.</summary>
    [Serializable] public sealed class GeometryBatch
    {
        public string kind;
        public int region, node;
        public Mesh mesh;
        public int[] vertexDrivers;
    }
    /// <summary>Imported model asset containing topology, regions, drivers and mesh batches.</summary>
    public sealed class CollectiveData : ScriptableObject
    {
        public HabitatNode[] nodes;
        public HabitatRegion[] regions;
        public GeometryDriver[] drivers;
        public GeometryBatch[] batches;
        public Vector3 overviewCenter = new Vector3(9,6,-4);
    }

    /// <summary>Compute driver matrices from region activations without modifying source meshes.</summary>
    public static class CollectiveKinematics
    {
        /// <summary>Construct a rotation matrix about a point and axis.</summary>
        /// <param name="origin">Rotation centre in model-local coordinates.</param>
        /// <param name="axis">Rotation axis, normalized internally.</param>
        /// <param name="degrees">Rotation angle in degrees.</param>
        /// <returns>Matrix translating to the origin, rotating and translating back.</returns>
        public static Matrix4x4 Around(Vector3 origin, Vector3 axis, float degrees)
        {
            return Matrix4x4.Translate(origin) * Matrix4x4.Rotate(Quaternion.AngleAxis(degrees,axis.normalized)) * Matrix4x4.Translate(-origin);
        }
        /// <summary>Compute hierarchical transforms in driver order; the caller supplies a sufficiently large result array.</summary>
        /// <param name="data">Model data with nodes, regions and parent-child drivers.</param>
        /// <param name="activation">Activation values ordered by region index.</param>
        /// <param name="result">Output matrix array indexed by driver.</param>
        /// <param name="nodeActivation">Optional per-node overrides; nonnegative values replace regional activation.</param>
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

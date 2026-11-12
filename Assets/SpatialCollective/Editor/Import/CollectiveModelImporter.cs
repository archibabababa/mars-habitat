using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using SpatialCollective;

namespace SpatialCollectiveEditor
{
    [Serializable] public sealed class ModelSource
    {
        public int schemaVersion;
        public string modelFile, protocolFile;
    }

    [Serializable] public sealed class ModelProtocol
    {
        public int schemaVersion;
        public string modelId, units, coordinateSystem, meshSha256;
        public Vector3 overviewCenter;
        public HabitatNode[] nodes;
        public HabitatRegion[] regions;
        public GeometryDriver[] drivers;
        public BatchProtocol[] batches;
    }

    [Serializable] public sealed class BatchProtocol
    {
        public string id, kind, topology;
        public int region, node, vertexCount, indexCount;
        public int[] vertexDrivers;
    }

    /// <summary>
    /// Imports the original OBJ vertex stream plus its versioned JSON bindings.
    /// Unity's normal ModelImporter may weld/reorder vertices; its mesh is deliberately
    /// not used for these per-vertex bindings. The OBJ remains a standard inspectable file.
    /// Deterministic sub-asset IDs keep scene/prefab references stable on reimport.
    /// </summary>
    [ScriptedImporter(1, "scmodel")]
    public sealed class CollectiveModelImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            ModelSource source = JsonUtility.FromJson<ModelSource>(File.ReadAllText(context.assetPath));
            Require(source != null && source.schemaVersion == 1, "Unsupported source schemaVersion.");
            string modelPath = Resolve(context.assetPath, source.modelFile);
            string protocolPath = Resolve(context.assetPath, source.protocolFile);
            context.DependsOnSourceAsset(modelPath);
            context.DependsOnSourceAsset(protocolPath);
            ModelProtocol protocol = JsonUtility.FromJson<ModelProtocol>(File.ReadAllText(protocolPath));
            ValidateProtocol(protocol);
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(modelPath))
            {
                string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                Require(hash == protocol.meshSha256, "OBJ checksum mismatch. Export model and JSON as one matching pair.");
            }
            Dictionary<string, ObjPart> parts = ReadObj(modelPath);
            Require(parts.Count == protocol.batches.Length, "OBJ object count differs from JSON batches.");
            CollectiveData data = ScriptableObject.CreateInstance<CollectiveData>();
            data.name = protocol.modelId;
            data.nodes = protocol.nodes; data.regions = protocol.regions; data.drivers = protocol.drivers;
            data.overviewCenter = protocol.overviewCenter;
            data.batches = new GeometryBatch[protocol.batches.Length];
            Matrix4x4[] neutralMatrices = new Matrix4x4[data.drivers.Length];
            CollectiveKinematics.Evaluate(data, new[] { .5f, .5f, .5f, .5f }, neutralMatrices);
            context.AddObjectToAsset("model", data);
            context.SetMainObject(data);
            for (int i = 0; i < protocol.batches.Length; i++)
            {
                BatchProtocol binding = protocol.batches[i];
                Require(parts.TryGetValue(binding.id, out ObjPart part), "Missing OBJ object: " + binding.id);
                Require(part.vertices.Count == binding.vertexCount && part.indices.Count == binding.indexCount,
                    "OBJ/JSON counts differ: " + binding.id);
                Require(part.lines == (binding.topology == "lines"), "Topology differs: " + binding.id);
                Mesh mesh = new Mesh { name = binding.id,
                    indexFormat = binding.vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(part.vertices);
                mesh.SetIndices(part.indices, part.lines ? MeshTopology.Lines : MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();
                data.batches[i] = new GeometryBatch { kind = binding.kind, region = binding.region, node = binding.node,
                    mesh = mesh, vertexDrivers = binding.vertexDrivers };
                context.AddObjectToAsset("rest_" + binding.id, mesh);
                Mesh neutral = Instantiate(mesh); neutral.name = binding.id + "_Neutral";
                Vector3[] positions = mesh.vertices;
                for (int v = 0; v < positions.Length; v++)
                    positions[v] = neutralMatrices[binding.vertexDrivers[v]].MultiplyPoint3x4(positions[v]);
                neutral.vertices = positions; neutral.RecalculateBounds();
                context.AddObjectToAsset("neutral_" + binding.id, neutral);
            }
        }

        private static string Resolve(string sourcePath, string file)
        {
            Require(!string.IsNullOrWhiteSpace(file) && Path.GetFileName(file) == file && !Path.IsPathRooted(file),
                "Source references must be sibling filenames.");
            string path = Path.GetDirectoryName(sourcePath).Replace('\\', '/') + "/" + file;
            Require(File.Exists(path), "Source file missing: " + path);
            return path;
        }

        public static void ValidateProtocol(ModelProtocol p)
        {
            Require(p != null && p.schemaVersion == 1, "Unsupported protocol schemaVersion.");
            Require(p.units == "metres" && p.coordinateSystem == "Unity-Y-up", "Expected metres and Unity-Y-up.");
            Require(!string.IsNullOrWhiteSpace(p.modelId), "modelId is required.");
            Require(p.nodes != null && p.nodes.Length > 0 && p.nodes.Length <= 500, "Invalid nodes.");
            Require(p.regions != null && p.regions.Length == 4, "This runtime requires exactly four regions.");
            Require(p.drivers != null && p.drivers.Length >= p.nodes.Length && p.drivers.Length <= 5000, "Invalid drivers.");
            Require(p.batches != null && p.batches.Length > 0 && p.batches.Length <= 1000, "Invalid batches.");
            Finite(p.overviewCenter);
            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < p.nodes.Length; i++)
            {
                HabitatNode n = p.nodes[i];
                Require(n != null && !string.IsNullOrWhiteSpace(n.id) && ids.Add(n.id), "Duplicate/missing node ID.");
                Require(n.region >= 0 && n.region < 4, "Invalid node region: " + n.id);
                Require(n.neighbors != null, "Missing neighbors: " + n.id);
                Require(n.neighbors.Distinct().Count() == n.neighbors.Length, "Duplicate neighbor: " + n.id);
                Finite(n.center); Finite(n.floor); Finite(n.delta);
                foreach (int other in n.neighbors)
                    Require(other >= 0 && other < p.nodes.Length && other != i, "Invalid neighbor: " + n.id);
            }
            for (int i = 0; i < p.nodes.Length; i++)
                foreach (int other in p.nodes[i].neighbors)
                    Require(p.nodes[other].neighbors.Contains(i), "Connections must be reciprocal: " + p.nodes[i].id);
            foreach (HabitatRegion r in p.regions)
            {
                Require(r != null && !string.IsNullOrWhiteSpace(r.label), "Invalid region.");
                Finite(r.anchor); Finite(r.axis); Finite(r.angle);
                Require(r.axis.sqrMagnitude > .000001f, "Region axis is zero.");
            }
            for (int i = 0; i < p.drivers.Length; i++)
            {
                GeometryDriver d = p.drivers[i];
                Require(d != null && d.node >= 0 && d.node < p.nodes.Length, "Invalid driver node.");
                Require(d.parent >= -1 && d.parent < i, "Driver parents must precede children (no cycles).");
                if (i < p.nodes.Length) Require(d.node == i && d.parent == -1, "First drivers must match node order.");
                else Require(d.parent >= 0 && p.drivers[d.parent].node == d.node, "Child driver must share its parent's node.");
                Finite(d.origin); Finite(d.axis); Finite(d.offset); Finite(d.angle);
                Require(d.axis.sqrMagnitude > .000001f, "Driver axis is zero.");
            }
            ids.Clear();
            HashSet<string> kinds = new HashSet<string> { "shell", "glass", "soft", "wire", "inner", "floor" };
            foreach (BatchProtocol b in p.batches)
            {
                Require(b != null && !string.IsNullOrWhiteSpace(b.id) && ids.Add(b.id), "Duplicate/missing batch ID.");
                Require(kinds.Contains(b.kind) && b.region >= 0 && b.region < 4, "Invalid batch kind/region.");
                Require(b.node >= -1 && b.node < p.nodes.Length, "Invalid batch node.");
                if (b.kind == "inner" || b.kind == "floor") Require(b.node >= 0, "Interior batch requires a node.");
                Require(b.topology == "lines" || b.topology == "triangles", "Unsupported topology.");
                Require(b.vertexCount > 0 && b.vertexCount <= 1000000 && b.vertexDrivers != null &&
                    b.vertexDrivers.Length == b.vertexCount, "Vertex binding length mismatch.");
                Require(b.indexCount > 0 && b.indexCount <= 5000000 && b.indexCount % (b.topology == "lines" ? 2 : 3) == 0,
                    "Invalid index count.");
                foreach (int d in b.vertexDrivers) Require(d >= 0 && d < p.drivers.Length, "Invalid vertex driver.");
            }
        }

        private sealed class ObjPart
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<int> indices = new List<int>();
            public int firstVertex;
            public bool lines, hasTopology;
        }

        private static Dictionary<string, ObjPart> ReadObj(string path)
        {
            var parts = new Dictionary<string, ObjPart>();
            ObjPart current = null; int total = 0, lineNumber = 0;
            foreach (string raw in File.ReadLines(path))
            {
                lineNumber++;
                string line = raw.Split('#')[0].Trim(); if (line.Length == 0) continue;
                string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string context = "OBJ line " + lineNumber + ": ";
                switch (fields[0])
                {
                    case "o":
                        Require(fields.Length == 2 && !parts.ContainsKey(fields[1]), context + "invalid object ID.");
                        current = new ObjPart { firstVertex = total + 1 }; parts.Add(fields[1], current); break;
                    case "v":
                        Require(current != null && fields.Length == 4 && !current.hasTopology, context + "vertices must precede topology.");
                        Vector3 v = new Vector3(Parse(fields[1]), Parse(fields[2]), Parse(fields[3])); Finite(v);
                        current.vertices.Add(v); total++; break;
                    case "f": case "l":
                        bool isLine = fields[0] == "l";
                        Require(current != null && fields.Length == (isLine ? 3 : 4), context + "triangles or two-point lines only.");
                        Require(!current.hasTopology || current.lines == isLine, context + "mixed topology.");
                        current.lines = isLine; current.hasTopology = true;
                        for (int j = 1; j < fields.Length; j++)
                        {
                            Require(int.TryParse(fields[j], NumberStyles.Integer, CultureInfo.InvariantCulture, out int global), context + "plain indices required.");
                            int local = global - current.firstVertex;
                            Require(local >= 0 && local < current.vertices.Count, context + "index outside object.");
                            current.indices.Add(local);
                        }
                        break;
                    default: throw new InvalidDataException(context + "unsupported OBJ record: " + fields[0]);
                }
            }
            return parts;
        }
        private static float Parse(string text) => float.Parse(text, CultureInfo.InvariantCulture);
        private static void Finite(Vector3 v) { Finite(v.x); Finite(v.y); Finite(v.z); }
        private static void Finite(float v) => Require(!float.IsNaN(v) && !float.IsInfinity(v), "Non-finite numeric value.");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}

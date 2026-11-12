using System;
using System.IO;
using UnityEngine;

namespace SpatialCollective
{
    /// <summary>Native state exported from Grasshopper; p contains resolved values for four regions, and follow_weights contains three coupling weights.</summary>
    [Serializable]
    public sealed class AuthoredState
    {
        public string schema, units;
        public float[] p, follow_weights;
    }
    /// <summary>Persistent model snapshot: six deformation values, three coupling coefficients, two paths, and display switches.</summary>
    [Serializable]
    public sealed class CollectiveSnapshot
    {
        public int schemaVersion = 2;
        public string model = "SpatialCollective-27";
        public float[] activation = new float[6], coupling = new float[3], travel = new float[2];
        public bool linked, rooms;
    }
    /// <summary>Manage the applied state, editing preview, comparison, undo, presets, and device-local persistence.</summary>
    public sealed class CollectiveStateController : MonoBehaviour
    {
        public CollectiveRig rig;
        public TextAsset interactionJson;
        public TextAsset authoredStateJson;
        public InteractionProfile Profile { get; private set; }
        public bool IsComparing { get; private set; }
        private CollectiveSnapshot committed, proposal;
        public string SavedPath => Path.Combine(Application.persistentDataPath, "SpatialCollective-Applied-v2.json");
        /// <summary>Load interaction settings and default coupling weights, then record the initial applied state.</summary>
        private void Awake()
        {
            Profile = InteractionProfile.Load(interactionJson);
            for (int i = 0; i < 3; i++) rig.SetCoupling(i, Profile.couplingResponses[i]);
            Commit();
        }
        /// <summary>Create an independent snapshot from the model's baseline values rather than effective coupled values.</summary>
        public CollectiveSnapshot Capture()
        {
            var s = new CollectiveSnapshot { linked = rig.coupled, rooms = rig.showAllChambers };
            for (int i = 0; i < 6; i++) s.activation[i] = rig.GetBaseline(i);
            for (int i = 0; i < 3; i++) s.coupling[i] = rig.GetCoupling(i);
            for (int i = 0; i < 2; i++) s.travel[i] = rig.GetTravel(i);
            return s;
        }
        /// <summary>Validate and restore a snapshot, stopping automatic detached-cabin movement.</summary>
        /// <param name="s">Snapshot compatible with the current model and version.</param>
        public void Restore(CollectiveSnapshot s)
        {
            Validate(s);
            for (int i = 0; i < 6; i++) rig.SetBaseline(i, s.activation[i]);
            for (int i = 0; i < 3; i++) rig.SetCoupling(i, s.coupling[i]);
            for (int i = 0; i < 2; i++) rig.SetTravel(i, s.travel[i]);
            rig.coupled = s.linked; rig.showAllChambers = s.rooms; rig.animateDetached = false; rig.MarkDirty();
        }
        /// <summary>Commit the current preview as the applied state; leave compare mode first if necessary.</summary>
        public void Commit()
        {
            // Final Apply operation.
            // When comparing, restore proposal first, then record the current preview as committed.
            if (IsComparing)
                EndCompare();
            rig.animateDetached = false;
            committed = Capture();
        }
        /// <summary>Deep-copy the applied snapshot so callers cannot modify internal state.</summary>
        public CollectiveSnapshot CopyApplied() => JsonUtility.FromJson<CollectiveSnapshot>(JsonUtility.ToJson(committed));
        /// <summary>Discard the preview and restore the last applied state.</summary>
        public void Undo()
        {
            // Undo to the last applied state.
            IsComparing = false; 
            proposal = null; 
            Restore(committed);
        }
        public bool HasChanges => JsonUtility.ToJson(IsComparing ? proposal : Capture()) != JsonUtility.ToJson(committed);
        /// <summary>Save the current preview and temporarily display the applied state.</summary>
        public void BeginCompare()
        {
            // Enter Compare: save the preview as proposal, then display committed for comparison.
            if (IsComparing) return; proposal = Capture(); Restore(committed); IsComparing = true;
        }
        /// <summary>Leave compare mode and restore the preview saved on entry.</summary>
        public void EndCompare()
        {
            // Leave Compare: restore the proposal preview saved before comparison.
            if (!IsComparing) return; 
            IsComparing = false; 
            Restore(proposal); 
            proposal = null;
        }
        /// <summary>Load six preset deformation values into the preview without changing the two cabin paths.</summary>
        /// <param name="index">Preset index in the configuration.</param>
        public void ApplyPreset(int index)
        {
            if (index < 0 || index >= Profile.presets.Length) throw new ArgumentOutOfRangeException(nameof(index));
            EndCompare(); rig.coupled = false; rig.animateDetached = false;
            // Presets change shape only. They never implicitly launch or recall a detached unit.
            for (int i = 0; i < 6; i++) rig.SetBaseline(i, Profile.presets[index].values[i]);
        }
        /// <summary>Write the applied state through a temporary file; do not save unapplied preview changes.</summary>
        /// <param name="path">Destination path; use the device's persistent directory when empty.</param>
        public void SaveApplied(string path = null)
        {
            // Save only committed state, not the unapplied preview.
            string destination = path ?? SavedPath;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string pending = destination + ".tmp";
            File.WriteAllText(pending, JsonUtility.ToJson(committed, true));
            if (File.Exists(destination)) File.Replace(pending, destination, null);
            else File.Move(pending, destination);
        }
        /// <summary>Load and validate a JSON snapshot, restoring it as a preview without applying it automatically.</summary>
        /// <param name="path">Source path; use the default save path when empty.</param>
        public void LoadPreview(string path = null)
        {
            // Restore loaded data as a preview; the caller decides whether to Apply it.
            var s = JsonUtility.FromJson<CollectiveSnapshot>(File.ReadAllText(path ?? SavedPath)); Validate(s);
            EndCompare(); Restore(s);
        }
        /// <summary>Import native GH state in meters; preview resolved values in free mode to avoid applying coupling twice.</summary>
        /// <param name="json">JSON text in spatial_collective.v19 format.</param>
        public void PreviewAuthoredJson(string json)
        {
            AuthoredState authored = JsonUtility.FromJson<AuthoredState>(json);
            if (authored == null || authored.schema != "spatial_collective.v19" || authored.units != "m")
                throw new InvalidDataException("Expected the native GH spatial_collective.v19 schema in metres.");
            Check(authored.p, 4, 0); Check(authored.follow_weights, 3, -1);
            EndCompare();
            // The native GH output already contains resolved/clamped p values. Do not couple twice.
            for (int i = 0; i < 4; i++) rig.SetBaseline(i, authored.p[i]);
            for (int i = 0; i < 3; i++) rig.SetCoupling(i, authored.follow_weights[i]);
            rig.coupled = false; rig.animateDetached = false; rig.MarkDirty();
        }
        /// <summary>Validate snapshot version, model identifier, array lengths, and value ranges.</summary>
        /// <param name="s">Snapshot to validate.</param>
        public static void Validate(CollectiveSnapshot s)
        {
            if (s == null || s.schemaVersion != 2 || s.model != "SpatialCollective-27") throw new InvalidDataException("State belongs to a different model or version.");
            Check(s.activation, 6, 0); Check(s.coupling, 3, -1); Check(s.travel, 2, 0);
        }
        /// <summary>Check a numeric array's length and finite value range.</summary>
        /// <param name="values">Array to validate.</param>
        /// <param name="length">Required element count.</param>
        /// <param name="minimum">Allowed lower bound; the upper bound is fixed at 1.</param>
        private static void Check(float[] values, int length, float minimum)
        {
            if (values == null || values.Length != length) throw new InvalidDataException("State parameter count differs.");
            foreach (float v in values) if (float.IsNaN(v) || float.IsInfinity(v) || v < minimum || v > 1) throw new InvalidDataException("Invalid state parameter.");
        }
    }
}

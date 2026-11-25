using System;
using System.IO;
using UnityEngine;

namespace SpatialCollective
{
    [Serializable]
    public sealed class CollectiveSnapshot
    {
        public int schemaVersion = 2;
        public string model, meshSha256;
        public float[] activation = new float[6], coupling = new float[3], travel = new float[2];
        public bool linked, rooms;
    }
    public sealed class CollectiveStateController : MonoBehaviour
    {
        public CollectiveRig rig;
        public TextAsset interactionJson;
        public TextAsset authoredStateJson;
        public InteractionProfile Profile { get; private set; }
        public bool IsComparing { get; private set; }
        private CollectiveSnapshot committed, proposal;
        public string SavedPath => Path.Combine(Application.persistentDataPath, "SpatialCollective-Applied-v2.json");
        private void Awake()
        {
            Profile = InteractionProfile.Load(interactionJson);
            for (int i = 0; i < 3; i++) rig.SetCoupling(i, Profile.couplingResponses[i]);
            Commit();
        }
        public CollectiveSnapshot Capture()
        {
            var s = new CollectiveSnapshot { model = rig.data.modelId, meshSha256 = rig.data.meshSha256, linked = rig.coupled, rooms = rig.showAllChambers };
            for (int i = 0; i < 6; i++) s.activation[i] = rig.GetBaseline(i);
            for (int i = 0; i < 3; i++) s.coupling[i] = rig.GetCoupling(i);
            for (int i = 0; i < 2; i++) s.travel[i] = rig.GetTravel(i);
            return s;
        }
        public void Restore(CollectiveSnapshot s)
        {
            Validate(s);
            if (s.model != rig.data.modelId || s.meshSha256 != rig.data.meshSha256) throw new InvalidDataException("Snapshot model differs from the loaded GH model.");
            for (int i = 0; i < 6; i++) rig.SetBaseline(i, s.activation[i]);
            for (int i = 0; i < 3; i++) rig.SetCoupling(i, s.coupling[i]);
            for (int i = 0; i < 2; i++) rig.SetTravel(i, s.travel[i]);
            rig.coupled = s.linked; rig.showAllChambers = s.rooms; rig.animateDetached = false; rig.MarkDirty();
        }
        public void Commit()
        {
            // Final Apply operation.
            // When comparing, restore proposal first, then record the current preview as committed.
            if (IsComparing)
                EndCompare();
            rig.animateDetached = false;
            committed = Capture();
        }
        public CollectiveSnapshot CopyApplied() => JsonUtility.FromJson<CollectiveSnapshot>(JsonUtility.ToJson(committed));
        public void Undo()
        {
            // Undo to the last applied state.
            IsComparing = false; 
            proposal = null; 
            Restore(committed);
        }
        public bool HasChanges => JsonUtility.ToJson(IsComparing ? proposal : Capture()) != JsonUtility.ToJson(committed);
        public void BeginCompare()
        {
            // Enter Compare: save the preview as proposal, then display committed for comparison.
            if (IsComparing) return; proposal = Capture(); Restore(committed); IsComparing = true;
        }
        public void EndCompare()
        {
            // Leave Compare: restore the proposal preview saved before comparison.
            if (!IsComparing) return; 
            IsComparing = false; 
            Restore(proposal); 
            proposal = null;
        }
        public void ApplyPreset(int index)
        {
            if (index < 0 || index >= Profile.presets.Length) throw new ArgumentOutOfRangeException(nameof(index));
            EndCompare(); rig.coupled = false; rig.animateDetached = false;
            // Presets affect shape; travel retains its current value.
            for (int i = 0; i < 6; i++) rig.SetBaseline(i, Profile.presets[index].values[i]);
        }
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
        public void LoadPreview(string path = null)
        {
            // Restore loaded data as a preview; the caller decides whether to Apply it.
            var s = JsonUtility.FromJson<CollectiveSnapshot>(File.ReadAllText(path ?? SavedPath)); Validate(s);
            EndCompare(); Restore(s);
        }
        public void PreviewAuthoredJson(string json)
        {
            var authored = JsonUtility.FromJson<CollectiveSnapshot>(json);
            Validate(authored); EndCompare(); Restore(authored);

        }
        public static void Validate(CollectiveSnapshot s)
        {
            if (s == null || s.schemaVersion != 2 || string.IsNullOrEmpty(s.model)) throw new InvalidDataException("State belongs to a different model or version.");
            Check(s.activation, 6, 0); Check(s.coupling, 3, -1); Check(s.travel, 2, 0);
        }
        private static void Check(float[] values, int length, float minimum)
        {
            if (values == null || values.Length != length) throw new InvalidDataException("State parameter count differs.");
            foreach (float v in values) if (float.IsNaN(v) || float.IsInfinity(v) || v < minimum || v > 1) throw new InvalidDataException("Invalid state parameter.");
        }
    }
}

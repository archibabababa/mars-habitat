using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpatialCollective
{
    public enum CollectiveCommand
    {
        SelectRegion,
        Preset,
        ToggleView,
        CycleNode,
        ToggleCoupling,
        ToggleRooms,
        Apply,
        Undo,
        ToggleDemo,
        ToggleDrift,
        Reposition,
        ToggleBackground,
        HideMenu,
        ToggleCompare,
        SaveState,
        LoadState,
        RestartDemo,
        SelectReferenceRoom,
        ImportAuthoredState
    }
    [Serializable] public sealed class MenuBinding
    {
        public string label, command;
        public int argument;
        public CollectiveCommand ParsedCommand => (CollectiveCommand)Enum.Parse(typeof(CollectiveCommand), command);
    }
    [Serializable] public sealed class KeyBinding
    {
        public string key, command;
        public int argument;
    }
    [Serializable] public sealed class ModelPreset
    {
        public string name;
        public float[] values;
    }
    [Serializable] public sealed class InteractionProfile
    {
        public int schemaVersion;
        public float pinchPressMetres;
        public float pinchReleaseMetres;
        public float handTravelMetres;
        public float mousePixelsPerMetre;
        public float overviewScale;
        public float[] couplingResponses;
        public MenuBinding[] menu;
        public KeyBinding[] keyboard;
        public ModelPreset[] presets;
        public static InteractionProfile Load(TextAsset source)
        {
            InteractionProfile p = JsonUtility.FromJson<InteractionProfile>(source.text);
            if (p == null || p.schemaVersion != 2) throw new InvalidDataException("Unsupported interaction schema.");
            Positive(p.pinchPressMetres); Positive(p.pinchReleaseMetres); Positive(p.handTravelMetres);
            Positive(p.mousePixelsPerMetre); Positive(p.overviewScale);
            if (p.pinchReleaseMetres <= p.pinchPressMetres) throw new InvalidDataException("Pinch release must exceed press distance.");
            if (p.couplingResponses == null || p.couplingResponses.Length != 3) throw new InvalidDataException("Three coupling responses required.");
            foreach (float value in p.couplingResponses)
                if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 1) throw new InvalidDataException("Invalid coupling response.");
            if (p.menu == null || p.menu.Length == 0 || p.menu.Length > 32) throw new InvalidDataException("Expected 1..32 menu buttons.");
            if (p.presets == null || p.presets.Length != 3) throw new InvalidDataException("Expected three presets.");
            foreach (ModelPreset preset in p.presets)
            {
                if (preset == null || preset.values == null || preset.values.Length != 6) throw new InvalidDataException("Preset requires six values.");
                foreach (float value in preset.values)
                    if (float.IsNaN(value) || value < 0 || value > 1) throw new InvalidDataException("Preset value outside 0..1.");
            }
            foreach (MenuBinding binding in p.menu)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.label)) throw new InvalidDataException("Menu label missing.");
                ValidateCommand(binding.command, binding.argument);
            }
            if (p.keyboard == null) throw new InvalidDataException("Keyboard bindings missing.");
            foreach (KeyBinding binding in p.keyboard)
            {
                if (binding == null || !Enum.TryParse(binding.key, out Key key) || !Enum.IsDefined(typeof(Key), key) || key == Key.None)
                    throw new InvalidDataException("Unknown keyboard key.");
                ValidateCommand(binding.command, binding.argument);
            }
            return p;
        }
        public static void ValidateCommand(string command, int argument)
        {
            if (!Enum.TryParse(command, out CollectiveCommand type) || !Enum.IsDefined(typeof(CollectiveCommand), type))
                throw new InvalidDataException("Unknown command: " + command);
            if (type == CollectiveCommand.SelectRegion && (argument < 0 || argument > 5) ||
                type == CollectiveCommand.Preset && (argument < 0 || argument > 2) ||
                type == CollectiveCommand.CycleNode && argument != -1 && argument != 1)
                throw new InvalidDataException("Invalid argument for " + command);
        }
        private static void Positive(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0) throw new InvalidDataException("Interaction values must be finite and positive.");
        }
    }
}

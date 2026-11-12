using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpatialCollective
{
    /// <summary>Commands triggered by interaction settings and buttons; some require region, preset, or direction arguments.</summary>
    public enum CollectiveCommand
    {
        /// <summary>Select a region parameter (0-5): core, long branch, short branch A, short branch B, S08, O13.</summary>
        SelectRegion,
        /// <summary>Apply a preset (0-2), writing six regional deformation values without changing path progress.</summary>
        Preset,
        /// <summary>Toggle overview/interior views; enter the target cabin from overview or return to overview from indoors.</summary>
        ToggleView,
        /// <summary>Navigate adjacent nodes; the argument must be -1 for backward or 1 for forward.</summary>
        CycleNode,
        /// <summary>Toggle LINKED/FREE mode to control whether branches follow core changes.</summary>
        ToggleCoupling,
        /// <summary>Toggle cabin visibility between current/neighboring cabins and all rooms.</summary>
        ToggleRooms,
        /// <summary>Apply the current preview as the committed baseline.</summary>
        Apply,
        /// <summary>Restore the last committed state and discard current preview changes.</summary>
        Undo,
        /// <summary>Play or pause the Motion Study timeline.</summary>
        ToggleDemo,
        /// <summary>Toggle automatic detached-cabin path motion (Drift/Travel), independently of deformation editing.</summary>
        ToggleDrift,
        /// <summary>Reposition the interaction panel and calibrate the linked views' reference pose.</summary>
        Reposition,
        /// <summary>Toggle the skybox; engineering mode can switch between interior and exterior backgrounds.</summary>
        ToggleBackground,
        /// <summary>Hide the interaction panel object.</summary>
        HideMenu,
        /// <summary>Toggle Compare between the applied state and the current preview.</summary>
        ToggleCompare,
        /// <summary>Save the applied state locally, excluding unapplied preview changes.</summary>
        SaveState,
        /// <summary>Load a local state as a preview; Apply is required to commit it.</summary>
        LoadState,
        /// <summary>Reset Motion Study to zero seconds and immediately start playback.</summary>
        RestartDemo,
        /// <summary>Enter the configured referenceRoom.</summary>
        SelectReferenceRoom,
        /// <summary>Import external AuthoredState from GH as a preview using free-mode parameters.</summary>
        ImportAuthoredState
    }

    /// <summary>Menu configuration record containing a label, command name, and integer argument.</summary>
    [Serializable] public sealed class MenuBinding
    {
        public string label, command;
        public int argument;
        public CollectiveCommand ParsedCommand => (CollectiveCommand)Enum.Parse(typeof(CollectiveCommand), command);
    }
    /// <summary>Keyboard mapping to a command and argument.</summary>
    [Serializable] public sealed class KeyBinding
    {
        public string key, command;
        public int argument;
    }
    /// <summary>Named six-region deformation preset without path progress.</summary>
    [Serializable] public sealed class ModelPreset
    {
        public string name;
        public float[] values;
    }
    /// <summary>Configuration model for interaction thresholds, menu, keyboard bindings, coupling defaults, and presets.</summary>
    [Serializable] public sealed class InteractionProfile
    {
        /// <summary>Interaction configuration version; the current implementation requires 2.</summary>
        public int schemaVersion;
        /// <summary>Index-finger pinch press threshold in meters.</summary>
        public float pinchPressMetres;
        /// <summary>Index-finger pinch release threshold in meters; greater than the press threshold to provide hysteresis.</summary>
        public float pinchReleaseMetres;
        /// <summary>Reference hand travel in meters for gesture-based parameter adjustment.</summary>
        public float handTravelMetres;
        /// <summary>Desktop conversion factor from mouse pixels to meters.</summary>
        public float mousePixelsPerMetre;
        /// <summary>Overview model scale relative to world dimensions.</summary>
        public float overviewScale;
        /// <summary>Default coupling response coefficients for the three branch regions, from -1 to 1.</summary>
        public float[] couplingResponses;
        /// <summary>World-panel button bindings: label, command, and argument.</summary>
        public MenuBinding[] menu;
        /// <summary>Keyboard shortcuts: key, command, and argument.</summary>
        public KeyBinding[] keyboard;
        /// <summary>Deformation presets, each containing six regional values.</summary>
        public ModelPreset[] presets;

        /// <summary>Parse version 2 configuration and validate thresholds, presets, commands, and keys.</summary>
        /// <param name="source">Unity text asset containing interaction settings.</param>
        /// <returns>Validated configuration; invalid data throws an exception.</returns>
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

        /// <summary>Validate a command name and its constrained arguments.</summary>
        /// <param name="command">CollectiveCommand enumeration name.</param>
        /// <param name="argument">Command argument: region 0-5, preset 0-2, or navigation direction -1 or 1.</param>
        public static void ValidateCommand(string command, int argument)
        {
            if (!Enum.TryParse(command, out CollectiveCommand type) || !Enum.IsDefined(typeof(CollectiveCommand), type))
                throw new InvalidDataException("Unknown command: " + command);
            if (type == CollectiveCommand.SelectRegion && (argument < 0 || argument > 5) ||
                type == CollectiveCommand.Preset && (argument < 0 || argument > 2) ||
                type == CollectiveCommand.CycleNode && argument != -1 && argument != 1)
                throw new InvalidDataException("Invalid argument for " + command);
        }
        /// <summary>Require a finite positive value.</summary>
        /// <param name="v">Configuration value to validate.</param>
        private static void Positive(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0) throw new InvalidDataException("Interaction values must be finite and positive.");
        }
    }
}

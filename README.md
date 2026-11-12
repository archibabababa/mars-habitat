# Mars Habitat

An architectural XR prototype led by Fan Huang, connecting a parametric habitat concept with an interactive Unity environment.

## Project and contribution

Fan Huang developed the spatial rules and parametric models and led the Unity engineering and testing, with technical assistance from Jiujing Interactive. This is the first XR project he has led towards potential commercial application. The repository documents a prototype, not a deployed commercial product.

## Current version

This repository contains the working Unity source snapshot dated 12 November 2026, including exported model and configuration data. Further manual refinement and final release checks are in progress. It is not a tagged final release. Native Grasshopper authoring files are not included in this initial Unity snapshot.

## Opening the project

1. Use Unity 6000.3.23f1, the version recorded in `ProjectSettings/ProjectVersion.txt`.
2. Add this repository folder as a project in Unity Hub. Let Package Manager resolve the versions pinned in `Packages/manifest.json` and `Packages/packages-lock.json`.
3. Restore the Unity XR Hands sample visuals as described below.
4. Open `Assets/SpatialCollective/Scenes/Spatial_Collective_Quest3.unity`. Inspect import messages and Play mode before a headset build. Device-specific build support and OpenXR configuration must match your equipment.

## Unity sample visuals

Unity XR Hands sample hand prefabs and FBX files are dependencies, not original project work. Their source files are not republished here. Their existing `.meta` files, paths and checksums are retained so that the matching assets can be restored from a locally installed, appropriately licensed XR Hands package.

Use `python tools/restore_hand_samples.py PATH_TO_INSTALLED_XR_HANDS_PACKAGE` after installing the matching package/samples. The script copies only files with exact matching checksums and does not overwrite files with unverified substitutes. If matching files are unavailable, the hand visuals require manual setup in Unity before the project is run. The Unity sample license remains in its original folder.

## Reading the source

`Assets` contains project scripts, models and scene assets; `Packages` pins dependencies; `ProjectSettings` records the editor and build configuration. Local caches, personal editor settings and build outputs are excluded. This repository contains project material rather than participant records.

Project-specific code and design materials remain under their existing copyright; public availability does not add a general reuse license. Third-party materials retain their own notices and terms.

## Related project

[Yantai VR spatial review](https://github.com/archibabababa/yantai-vr-spatial-review)

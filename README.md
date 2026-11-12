# Meddle
<a href="https://ko-fi.com/ramen_au"><img alt="Sponsor Badge" src="https://img.shields.io/badge/Meddle-Sponsor-pink?style=flat"></a>
<a href="https://github.com/PassiveModding/Meddle/"><img alt="Meddle" src="https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FPassiveModding%2FMeddle%2Frefs%2Fheads%2Fmain%2Frepo.json&query=%24.%5B0%5D.AssemblyVersion&label=Meddle"></a>
<a href="https://github.com/PassiveModding/MeddleTools/releases"><img alt="MeddleTools" src="https://img.shields.io/badge/dynamic/toml?url=https%3A%2F%2Fraw.githubusercontent.com%2FPassiveModding%2FMeddleTools%2Frefs%2Fheads%2Fmain%2FMeddleTools%2Fblender_manifest.toml&query=%24.version&label=MeddleTools"></a>

This project is a Dalamud plugin which provides a GUI for exporting player models from FFXIV.

This fork also writes editable UE 5.8.3 StaticMesh, Skeleton, SkeletalMesh, AnimSequence,
Texture2D and Material assets directly from Meddle. Character exports include NPCs,
monsters and attachments; Layout exports include furniture, background models,
shared groups and terrain plates, with three mesh grouping options. Rigid geometry
uses StaticMesh; skinned instances use SkeletalMesh. It also supports captured final-pose
animation and PAP/SKLB clip baking. See [UE export and validation](docs/UE_EXPORT_SCHEMA.md)
for usage, supported material features and verification status. Initialize the
`third_party/UAssetAPI` submodule before building.

### Building with Visual Studio

The projects require .NET 10 and C# 14 (Dalamud SDK 15). Visual Studio 2026
is the officially supported IDE. For Visual Studio 2022, use 17.14 with a
separately installed .NET 10.0.1xx SDK; newer SDK feature bands can require
newer MSBuild versions. VS2022 builds still report `NETSDK1233`, and its editor
does not fully support this target.

Open `Meddle.sln` and select `Debug | x64` or `Release | x64`. Set `DALAMUD_HOME`
to your Dalamud development library directory. The plugin maps to x64 while
managed library projects use Any CPU. Package pruning is disabled so VS2022
and the dotnet CLI can share the dependency lock files without a
`System.Text.Json` restore failure.

- [Installation](#installation)
- [Releases](https://github.com/PassiveModding/Meddle/releases)
- [MeddleTools Blender Addon](https://github.com/PassiveModding/MeddleTools)
- [Attributions](#attributions)

> NOTE: For single mesh export and mod creation, use Penumbra. 
> This plugin is intended for exporting multiple meshes at once, such as for a full character model or an environment.

## Installation
Meddle is written as a Dalamud plugin and as such, requires that you use FFXIVQuickLauncher to start your game.
This will enable you to install community-created plugins.

1. Type the `/xlsettings` command into your chatbox. This will open your Dalamud Settings.
2. In the window that opens, navigate to the "Experimental" tab. Scroll down to "Custom Plugin Repositories".
3. Copy and paste the repo URL (seen below) into the input box, making sure to press the "+" button to add it.
4. Press the "Save and Close" button. This will add Meddle to Dalamud's list of available plugins.
5. Open the plugin installer by typing the `/xlplugins` command, search for Meddle, and click install.

Repo URL

```
https://raw.githubusercontent.com/PassiveModding/Meddle/main/repo.json
```

## Territory export

Open the independent **Territory** tab. Search by level name, code (such as `f1f1`), ID or resource path, then choose its scene version and festival phase and click **导出 Territory**. **直接导出当前区域…** selects the loaded region and its current branches and opens the export destination dialog. Unloaded regions are read directly from the game's LVB/LGB/SGB and terrain files. The menu only lists festival phases present in that scene version. **无节庆** retains common layers and removes festival decorations.

For the currently loaded Territory, **使用当前游戏分支** captures the active scene filter and complete festival combination. Static instances are matched with runtime layer/instance identifiers; loaded models use captured transforms and material changes, while unloaded geometry uses the static layout. This export ignores the overlay distance and camera culling. Refresh the region state after changing zones. Selecting another scene version disables incompatible runtime overrides; selecting another festival keeps only matching layers.

Export supports the existing glTF/GLB and UE asset output. UE output is geometry assets, not a generated `.umap`. Static export includes terrain, background models, nested shared groups and event-object SGB references; NPCs, VFX, lighting and timeline animation are not reconstructed by this path. Missing resources, unsupported entry types and unmatched runtime children are recorded in `territory-report.json`. Unloaded dynamic objects retain their file defaults.

Territory grouping is independent of character mesh grouping:

| Rule | Group boundary |
| --- | --- |
| 全部合并 | The entire selected scene |
| 按重复资源合并 (default) | Full model path + replacement material path/slot + stain ID; every occurrence is retained |
| 按场景层分组 | Source layout file + group/layer ID; nested SGB geometry inherits its containing scene layer, terrain has its own group |
| 逐实例导出 | Each placed model instance; shared groups are expanded and terrain plates are separate |

UE bakes each group into one mesh at its world positions; repeated-resource grouping is not ISM/HISM instancing and does not remove repeated placements or weld vertices. glTF/GLB writes one scene file per group and retains its internal nodes. Terrain plates participate with their individual model paths and positions. `territory-groups.json` records the selected rule, output group names and occurrence counts. Material-only and filename-only grouping are deliberately not used: different model paths, material replacements and stains remain distinguishable in repeated-resource mode.

Developer validation: `dotnet test Meddle/Meddle.Utils.Tests --filter FullyQualifiedName~TerritorySceneTests`. To inspect installed scene data without launching the game, run `dotnet run --project tools/TerritoryProbe -- <game directory> [bg/.../level/scene.lvb]`. Without a scene path, the probe follows all TerritoryType scene references and reports missing files separately from parse failures.

## Attributions
Much of this code is from or based on the following projects and wouldn't have been possible without them:
- [Xande](https://github.com/xivdev/Xande) [[GNU AGPL v3](https://github.com/xivdev/Xande/blob/main/LICENSE)]
  - PBD file structure
  - MeshBuilder
  - RaceDeformer
  - Havok Skeletons
- [Penumbra](https://github.com/xivdev/Penumbra) 
  - shader comp logic
  - vertex type info
  - SpanBinaryReader
- [Lumina](https://github.com/NotAdam/Lumina/)
  - file structures
- [SaintCoinach](https://github.com/xivapi/SaintCoinach)
  - terrain and level file structures
- [Alpha](https://github.com/NotNite/Alpha) [[MIT](https://github.com/NotNite/Alpha/blob/main/LICENSE)]
  - initial reference for Meddle.UI project setup and draw logic 
- [PathFinder](https://github.com/chirpxiv/ffxiv-pathfinder) [[MIT](https://github.com/chirpxiv/ffxiv-pathfinder/blob/main/LICENSE)]
  - 🐇
  - reference for world overlay logic
- [Ktisis](https://github.com/ktisis-tools/Ktisis) [[GNU GPL v3](https://github.com/ktisis-tools/Ktisis/blob/main/LICENSE)]
  - lighting structs
  - hkQsTransformf matrix handling

Important contributors:
- [WorkingRobot](https://github.com/WorkingRobot) 
  - GPU texture exports 
  - Character-tree structure for exports
  - Shape and attribute data
  - Attach structs
  - Skeleton Parsing
- Members of the Penumbra discord
  - Testing and feedback


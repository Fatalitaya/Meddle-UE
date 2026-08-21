# Meddle-UE Phase 2 Export Schema (ABI v4)

## Architecture

The UE exporter branches after the existing Meddle runtime parser and before
`CharacterComposer`. The data flow is:

```text
FFXIV Character / CharacterBase / DrawObject
  -> ResolverService / ParseMaterialUtil / PbdHooks
  -> ParsedCharacterInfo
  -> UeSnapshotBuilder
  -> character.json + manifest.json + resources/
  -> FFXIVImporter
```

The UE path never creates a `SceneBuilder`, `CharacterComposer`, glTF, GLB, or
OBJ artifact. The exporter stores observed runtime state, not reconstructed
lookup rules.

## Bundle Layout

```text
Character-<name>-<timestamp>/
  character.json
  manifest.json
  bundle.complete
  resources/
    c0101e0001_top_91ac82ef.mdl
    mt_c0101e0001_top_a_a81bc921.mtrl
    diffuse_82fa91be.tex
    character_opaque_7ca91d2e.shpk
    sklb_3f9ac512.sklb
    runtime_decal_2c6f18aa.png
```

`resources/` is always flat. Physical filenames are human-readable aliases
containing a sanitized source basename and a hash prefix. The SHA-256 remains
the authoritative resource identity.

## Schema Version

`UeExportSchema.CurrentVersion` is the single authoritative schema version:

```csharp
public const int CurrentVersion = 4;
```

Both `character.json` and `manifest.json` use this integer at the root as
`schemaVersion`. There is no `header.schemaVersion`.

## character.json

Document metadata exists once at the root:

```json
{
  "schemaVersion": 4,
  "generator": {
    "name": "Meddle-UE",
    "version": "0.0.1",
    "commit": null,
    "dirty": true,
    "buildId": "<module version id>"
  },
  "knownCaptureGaps": [
    "SkinMaterialConstantBuffer",
    "HairRenderMaterialFlags",
    "DeformerObservationDependsOnHumanCreateDeformer"
  ],
  "root": {
    "nodeId": "root",
    "capture": {
      "capturedAtUtc": "...",
      "characterName": "...",
      "objectKind": "Pc",
      "modelType": "Human",
      "genderRace": 801
    },
    "customize": {},
    "models": [],
    "skeleton": {
      "transform": {},
      "partialSkeletons": []
    },
    "attach": {},
    "attaches": []
  }
}
```

Each runtime character/draw-object node recurses under `root.attaches` and
does not duplicate `schemaVersion`, `generator`, or `knownCaptureGaps`.
Node ids are deterministic:

```text
root
root/attach-0
root/attach-1
root/attach-0/attach-0
```

Model ids are globally scoped:

```text
root/model-0
root/attach-0/model-0
```

Every manifest observation event carries `nodeId`; model-scoped observations
also carry the global `modelId`.

## Deformer Observation Semantics

Deformer state is captured by `PbdHooks` from `Human_CreateDeformer`. The UE
path never parses a race code from an MDL filename, never reads `human.pbd`
to infer a deformer, and never emits `StaticResolved`.

The observation cache is keyed by human pointer and slot, but also stores the
current model pointer, model resource handle pointer, and model resource path.
At parse time, a cached observation is returned only when all of those match
the current model. A mismatch is treated as `NotObserved`, never as stale
evidence. Destroyed humans are pruned from the cache using the existing
`IFramework.Update` lifecycle tick and the game object manager; entries without
a complete model identity are never stored.

States:

| State | Meaning |
| --- | --- |
| `Observed` | `Human_CreateDeformer` returned a usable deformer/PBD relationship. |
| `ObservedNone` | `Human_CreateDeformer` was observed and returned no deformer. |
| `ObservedIncomplete` | The call returned a structure, but PBD handle/path data was unavailable. |
| `NotObserved` | No valid, identity-matched runtime observation exists. |
| `NotApplicable` | The model is not a Human model. |

An observed deformer uses the raw runtime fields:

```json
{
  "status": "Observed",
  "pbdResourcePath": "chara/xls/boneDeformer/human.pbd",
  "pbdResource": {
    "resourceId": "sha256:...:pbd",
    "availability": "Available"
  },
  "raceSexId": 1401,
  "deformerId": 13
}
```

PBD resource availability is a separate axis from runtime observation
validity. If the observed PBD path cannot be read, `status` remains
`Observed`; the manifest records the PBD as `Unavailable` or `Unresolved` and
adds a resource observation. The deformer's `pbdResource.availability` then
carries the same `Unavailable` or `Unresolved` value; resource export failure
never downgrades runtime observation.

PBD resources are registered only when an observed PBD relationship provides
`pbdResourcePath`. A bundle containing only `NotObserved`, `ObservedNone`, or
`NotApplicable` deformer states may contain no PBD resource.

## ColorTable ABI

`colorTable` is an explicit DTO, not a serialized `JsonElement` blob.

Modern runtime tables use:

```json
{
  "kind": "Modern",
  "colorTable": [
    {
      "diffuse": [0.08, 0.05, 0.02],
      "glossStrength": 0.5,
      "scalar3": 0.5,
      "specular": [1.0, 1.0, 1.0],
      "specularStrength": 1.0,
      "scalar7": 1.0,
      "emissive": [0.0, 0.0, 0.0],
      "scalar11": 0.0,
      "sheenRate": 0.0,
      "sheenTint": 0.0,
      "sheenAptitude": 0.0,
      "scalar15": 0.0,
      "roughness": 0.5,
      "scalar17": 0.0,
      "metalness": 0.0,
      "anisotropy": 0.0,
      "scalar20": 0.0,
      "sphereMask": 0.0,
      "scalar22": 0.0,
      "scalar23": 0.0,
      "shaderId": 0,
      "tileIndex": 0,
      "tileAlpha": 1.0,
      "sphereIndex": 0,
      "tileMatrix": {
        "uu": 150.0,
        "uv": 0.0,
        "vu": 0.0,
        "vv": 225.0
      }
    }
  ],
  "colorDyeTable": []
}
```

Vectors are always numeric arrays. `tileMatrix` always has named numeric
fields. No vector or matrix may serialize as `{}`.

Legacy runtime tables use:

```json
{
  "kind": "Legacy",
  "colorTable": [
    {
      "diffuse": [0.08, 0.05, 0.02],
      "specular": [1.0, 1.0, 1.0],
      "specularStrength": 0.0,
      "emissive": [0.0, 0.0, 0.0],
      "glossStrength": 0.0,
      "materialRepeat": [1.0, 1.0],
      "materialSkew": [0.0, 0.0],
      "tileIndex": 0
    }
  ],
  "colorDyeTable": []
}
```

Modern dye rows preserve template, channel, and all exposed flags. Legacy dye
rows preserve template, diffuse/specular/emissive flags, gloss, and specular
strength. Row order is preserved.

## manifest.json

The manifest keeps the same content-addressed resource model:

```json
{
  "schemaVersion": 4,
  "resources": [],
  "observations": []
}
```

Observation events now identify the snapshot node:

```json
{
  "code": "ResourceUnavailable",
  "message": "PBD bytes were unavailable.",
  "resourceType": "pbd",
  "logicalPath": "chara/xls/boneDeformer/human.pbd",
  "resolvedPath": "chara/xls/boneDeformer/human.pbd",
  "resourceId": "observation:<sha256>",
  "nodeId": "root",
  "modelId": "root/model-0"
}
```

Resource behavior is unchanged:

- content SHA-256 identity
- collision-safe flat filenames
- logicalPath + resolvedPath
- aliases and deduplication
- Game / Modded / RuntimeGenerated origins
- `Available`, `Unavailable`, and `Unresolved` availability
- atomic completion and `bundle.complete`

## Excluded From ABI

- `ParsedModelInfo.ModelAddress`
- any `nint`, `IntPtr`, or process address
- `header.schemaVersion`
- `sourceRace` / `targetRace` deformer fields
- `StaticResolved`, `CaptureFailed`, `Unsupported` deformer statuses
- CharacterComposer lookup/fallback rules
- filename-derived race or deformer state
- automatic `chara/xls/boneDeformer/human.pbd` export

## Known Remaining Gaps

- Skin material constant buffers are not captured.
- Hair render material flags are not captured.
- Deformer state may legitimately be `NotObserved` because capture is
  observation-driven and depends on `Human_CreateDeformer`.
- Skeleton composition semantics are not declared fully solved.

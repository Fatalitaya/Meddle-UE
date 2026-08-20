# Meddle-UE Phase 1 Export Schema

## Architecture

The UE exporter branches after the existing Meddle runtime parser and before
`CharacterComposer`. The data flow is:

```text
FFXIV Character / CharacterBase / DrawObject
  -> ResolverService / ParseMaterialUtil
  -> ParsedCharacterInfo
  -> UeSnapshotBuilder
  -> character.json + manifest.json + resources/
  -> FFXIVImporter
```

The UE path never creates a `SceneBuilder`, `CharacterComposer`, glTF, GLB, or
OBJ artifact.

Validation is split into three layers:

- Observation data: `character.json` and `manifest.json` record what Meddle
  observed. They do not assign importance, criticality, or workflow fitness to
  individual resources.
- Validation policy: consumers evaluate the same observations against their own
  requirements. A resource that is irrelevant to a preview consumer may be
  essential to a character-reconstruction consumer; the exporter does not make
  that decision for them.
- Structural validation: `UeExportValidator` checks only that the bundle is
  internally consistent and that declared bytes are present and unmodified.
  Structural validity does not imply that any consumer-specific requirement was
  satisfied.

Structural validation checks that each `Available` manifest file exists, its
bytes match `sha256`, its `resourceId` resolves, and no physical file is shared
by resources with different SHA-256 values. The filename itself is not an
identity key and is not matched against the hash.

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
    pbd_8b17c21a.pbd
    runtime_decal_2c6f18aa.png
```

`resources/` is always flat. Physical filenames are human-readable aliases
containing a sanitized source basename and a hash prefix. The SHA-256 remains
the authoritative resource identity.

## Resource Identity

Resource identity is immutable and content-addressed:

- `id` and `sha256` are derived from the actual exported bytes.
- `file` is a physical representation, not an identity key.
- Consumers must continue to resolve and reference resources by `resourceId`.
- The physical filename may be a short `hash8`, a longer `hash16`, or the full
  SHA-256 when hash-prefix collisions require it.

The filename is never parsed to recover source identity or resource content.

## Physical Filename Generation

`UeResourceFileNameBuilder` derives a physical filename from the observed source
path:

1. Use the `resolvedPath` basename when available.
2. Fall back to the `logicalPath` basename.
3. Fall back to the resource type name.

Runtime handle decorations are stripped before basename extraction. Invalid
Windows filename characters are replaced with `_`, leading/trailing whitespace
is removed, empty basenames use the resource type, and excessively long
basenames are truncated.

Runtime-generated resources use `runtime_{kind}_{hash8}{extension}`. When no
runtime kind is known, the builder falls back to `runtime_texture`. Runtime
source metadata is normalized to one of `decal`, `texture`, or `resource`, so
transient Penumbra/runtime source labels never leak into the physical name.

The first attempt is `{basename}_{hash8}{extension}`. If that physical filename
is already used by a different SHA-256, the builder falls back to `hash16`,
then to the full SHA-256. Same-content resources keep sharing the same physical
file even when their source basenames differ.

## character.json

The root object contains:

```json
{
  "schemaVersion": 3,
  "generator": {
    "name": "Meddle-UE",
    "version": "0.0.1",
    "commit": null,
    "dirty": true,
    "buildId": "<module version id>"
  },
  "capture": {
    "capturedAtUtc": "...",
    "characterName": "...",
    "objectKind": "Pc",
    "modelType": "Human",
    "genderRace": 801
  },
  "customize": {
    "data": {},
    "parameters": {},
    "equipmentModelIds": []
  },
  "models": [],
  "skeleton": {
    "transform": {},
    "partialSkeletons": []
  },
  "attach": {},
  "attaches": [],
  "knownCaptureGaps": [
    "SkinMaterialConstantBuffer",
    "HairRenderMaterialFlags"
  ]
}
```

Each `models[]` entry stores the observed runtime state:

- stable `id`, `slotIndex`, `modelType`, `enabled`
- `logicalPath` from `ParsedModelInfo.Path.GamePath`
- `resolvedPath` from `ParsedModelInfo.Path.FullPath`
- `resourceId` for the captured MDL or unavailable/unresolved observation
- effective `stain0`, `stain1`, `shapeAttributes`
- material bindings
- deformer observation status

`schemaVersion` is only a structural generation marker. `version`, `commit`,
`dirty`, and `buildId` are provenance only:

- `schemaVersion` identifies the bundle structure.
- `version` identifies the source package/assembly version, not a compatibility
  contract.
- `commit` identifies the Git HEAD commit from which the plugin was built when
  Git was available at build time.
- `dirty` indicates whether tracked/untracked working-tree modifications existed
  when the plugin was built.
- `buildId` identifies the compiled plugin binary through the plugin assembly
  `ModuleVersionId`.

Meddle-UE does not promise compatibility between different builds or commits.
No compatibility negotiation, version range, migration, or minimum-supported
build logic is implied by these fields.

Git provenance is build-time only. The build runs `git rev-parse HEAD` and
`git status --porcelain --untracked-files=normal`, uses a successful exit code
plus a 40- or 64-character hex object ID before recording a commit, and stores
dirty state as `true`, `false`, or `null`. Plugin/export runtime never invokes
Git or reads `.git` state.

Each material stores both the authored resource and the runtime observation:

- MTRL `resourceId`
- canonical SHPK path and `shaderPackageResourceId`
- `colorTable` copied from the effective runtime `ColorTableBlob`
- texture bindings with logical/resolved paths and resource ids
- normal texture `runtimeMetadata` copied from `TextureResource` (format,
  dimensions, mips, array size, dimension, misc flags; never pixel data)
- `renderOverrides` with decal and skin material texture references

`skeleton.partialSkeletons[]` stores the raw SKLB `resourceId`, runtime
`partialSkeletonFlagsRaw`, `connectedBoneIndex`, `boneCount`, runtime bone
names/parents, and runtime poses. The raw SKLB is the authoritative skeleton
asset; the runtime skeleton data is diagnostic/composition evidence.

Partial skeleton slots are classified before resource registration:

- An unused runtime slot with no `HandlePath`, no `HkSkeleton`, `boneCount == 0`,
  no runtime poses, `partialSkeletonFlagsRaw == 0`, and
  `connectedBoneIndex == -1` is preserved in `partialSkeletons[]` with null
  `logicalPath`, `resolvedPath`, and `resourceId`. Its `index`,
  `partialSkeletonFlagsRaw`, `connectedBoneIndex`, and `boneCount` remain
  intact because the array position is runtime composition evidence.
- Runtime evidence without a `HandlePath` does not prove independent SKLB
  ownership. Raw flags, connection indices, runtime bones, and poses are
  preserved without synthesizing an SKLB path. `resourceId` remains null when
  no source path was observed; a source path that was observed but could not
  be captured becomes an unavailable observation.

`attaches[]` recursively contains child character snapshots, preserving mount,
ornament, and weapon attach state. Each attached node retains its own
`capturedAtUtc`, `modelType`, `genderRace`, customize data/parameters, and
equipment model ids when available. Character names and object kinds remain
null for child nodes unless the caller knows them.

The attach object preserves full `ownerSkeleton` and `targetSkeleton` runtime
skeleton snapshots, including SKLB resource ids, transforms, partial skeleton
flags, bone counts, runtime bones, and runtime poses.

`EquipmentModelId` snapshots preserve the raw packed `value` observed by
FFXIVClientStructs, without reconstructing it from id/variant/stain fields.

Deformer snapshots store only the raw observed fields:

```json
{
  "status": "Observed",
  "pbdResourceId": "observation:...",
  "raceSexId": 801,
  "deformerId": 801
}
```

`pbdResourceId` may reference an unavailable observation when the deformer was
observed but PBD bytes could not be captured. No source/target race inference
is performed in the capture ABI.

## manifest.json

```json
{
  "schemaVersion": 3,
  "resources": [
    {
      "id": "sha256:<sha256>:mdl",
      "type": "mdl",
      "availability": "Available",
      "logicalPath": "chara/...",
      "resolvedPath": "chara/...",
      "file": "resources/c0101e0001_top_91ac82ef.mdl",
      "sha256": "<sha256>",
      "size": 12345,
      "origin": "game",
      "aliases": []
    },
    {
      "id": "observation:<sha256>",
      "type": "sklb",
      "availability": "Unavailable",
      "logicalPath": "chara/...",
      "resolvedPath": "chara/...",
      "file": null,
      "sha256": null,
      "size": null,
      "origin": "game",
      "reason": "ResourceUnavailable",
      "aliases": []
    }
  ],
  "observations": []
}
```

Entries are sorted by `type`, `availability`, `logicalPath`, `resolvedPath`,
then `sha256`.
Same-byte resources are deduplicated into one physical file. Later path
references are preserved in `aliases`, and every `aliases[]` list is sorted by
`logicalPath`, `resolvedPath`, then `origin`.

`availability` is one of:

- `Available`: actual bytes were captured and the entry records `file`,
  `sha256`, and `size`.
- `Unavailable`: a source reference was understood and attempted, but bytes
  were not available.
- `Unresolved`: the observed source reference could not be safely resolved.

`Unavailable` and `Unresolved` entries must have `file`, `sha256`, and `size`
set to null. They are observations, not content identities.

`origin` describes the actual byte source: SqPack reads are `game`, rooted
disk reads are `modded`, runtime PNGs are `runtimeGenerated`, and unresolved
resources are `unknown`. A decorated non-rooted handle path is trimmed only
for lookup; when it resolves from SqPack its origin is `game`, while the
manifest still preserves the original decorated `resolvedPath`. A rooted path
never falls back to SqPack.

Content IDs use `sha256:<hash>:<type>` and identify actual captured bytes.
Observation IDs use `observation:<hash>` and identify a resource observation
for which no bytes were captured. They are stable across identical
logical/resolved path and type metadata, but they are not content hashes.

Source identity is explicit:

- source observed but bytes unavailable -> `observation:<id>`
- source itself was not observed -> `ResourceId = null` plus a
  `ResourceSourceNotObserved` observation event

Available entries must have canonical identity: `Id` derives from the actual
content SHA, type, and runtime encoding. `File` is a human-readable alias that
uses a sanitized source basename plus the shortest collision-free SHA prefix,
and it is validated by its bytes rather than by filename shape.
Unavailable/unresolved observations must have canonical observation IDs derived
from their source path metadata.

## Runtime-Generated Textures

When `OnRenderMaterialOutput.DecalTexture` exists without a stable source path,
Meddle currently has decoded RGBA data in `SkTexture`. The exporter encodes
those pixels as lossless PNG bytes, hashes the PNG, writes
`resources/runtime_decal_<hash8>.png`, and registers:

```json
{
  "id": "sha256:<sha256>:tex:png",
  "type": "tex",
  "availability": "Available",
  "origin": "runtimeGenerated",
  "runtimeTexture": {
    "encoding": "png",
    "width": 128,
    "height": 128,
    "source": "decal"
  }
}
```

The material `renderOverrides.decalTextureResourceId` points to this manifest
entry. If a path-backed decal cannot be read but runtime decal pixels exist,
the runtime PNG is used as the fallback resource. The attempted
`logicalPath`, `resolvedPath`, and `fallbackReason` are preserved in both
`renderOverrides.decalTextureLogicalPath`, `decalTextureResolvedPath`,
`decalTextureFallbackReason`, and the runtime texture metadata.

PNG encoding is checked before the resource is registered. An empty or failed
encoding aborts the export; a partial PNG is never treated as a successful
runtime-generated resource.

## Observations

`manifest.json` stores observation events without severity. They describe what
the exporter saw and what happened during capture:

- `ResourceUnavailable`: a source reference was understood and attempted, but
  bytes were not available.
- `ResourceReadFailed`: a controlled source read failed.
- `ResourceUnresolved`: the observed source reference could not be safely
  resolved.
- `SkeletonSourceNotObserved`: runtime skeleton evidence exists without a
  `HandlePath`; the exporter does not infer SKLB ownership from flags, bones,
  or poses.
- `PathSourceReplacedByRuntimeCapture`: path-backed decal bytes were unavailable
  and runtime pixels were encoded instead.

Observation events are facts, not workflow verdicts. A missing MDL is not
automatically a warning or an error; each consumer decides whether that missing
byte matters for its own goal.

## Validation Policy

Consumers evaluate the same observations with `UeValidationPolicy`. They can
name resource ids, name resource types, or provide their own predicate:

```csharp
var policy = UeValidationPolicy.ForResourceIds("sha256:<hash>:mdl");
var result = policy.Evaluate(snapshot, manifest, outputDirectory);
```

The structural validator runs first in this combined path. `result.IsSatisfied`
is false only when the consumer policy requires bytes that the manifest
reports as unavailable, unresolved, or missing. The policy does not mutate the
bundle and does not control `bundle.complete`.

## Resource Policy

MDL, MTRL, TEX, SHPK, SKLB, and PBD acquisition failures follow the same
Phase 1 rule: when a source reference was observed and read failed, the
exporter records an observation and completes the bundle. No resource type is
globally required for bundle completion, and no type is globally optional
either. Consumers pass their own `UeValidationPolicy` requirement when they
need a specific resource or resource type.

An absent SHPK identity (`Shpk == null`) produces no resource observation and
no observation event. An observed SHPK path that cannot be read produces an
unavailable observation and a `ResourceUnavailable` event.

An observed deformer retains `Status = Observed`, `RaceSexId`, and
`DeformerId` even when PBD bytes are unavailable. `NotObserved` and
`NotApplicable` remain explicit observation states, not failures.

## Path Semantics

`HandleString.GamePath` is preserved as `logicalPath`.

`HandleString.FullPath` is preserved as `resolvedPath`.

The exporter classifies each effective resolved path before reading:

- a plain non-rooted game path is read from SqPack with `origin = game`;
- a decorated non-rooted path such as `|...|chara/...` has the handle
  decoration removed only for lookup. If the trimmed path is a valid
  game-relative path, SqPack lookup is allowed and `origin = game`; the
  original decorated path remains in `resolvedPath`. Handle decoration is
  metadata, not a mod classification or an unresolved classification;
- a plain or decorated rooted disk path is read directly from disk as
  `modded` and never falls back to SqPack;
- malformed decorated paths such as `|broken`, `||`, or `|metadata|` are
  `ResourceUnresolved` observations;
- a path that was never observed has no resource observation and `resourceId`
  remains null; it is not treated as a capture failure.

If the exporter cannot prove that a decorated/modded runtime path resolves to
the actual runtime resource bytes, it must not silently substitute vanilla
SqPack bytes. Rooted alternate/mod paths must fail if disk reads fail; they are
never stripped to a basename or game path for a fallback. It never tries to
reconstruct lookup rules, slots, or deformation state from filenames.

## Mapping

| Parsed field | UE snapshot field | Source authority | Notes |
| --- | --- | --- | --- |
| `ParsedCharacterInfo` | root `UeCharacterSnapshot` | runtime parse | explicit DTO, not raw blob |
| `ParsedCharacterInfo.Attaches` | `attaches[]` | runtime parse | recursive, cycle-safe |
| `ParsedCharacterInfo.ModelType` | `capture.modelType` | `CharacterBase.GetModelType` | directly observed |
| `ParsedModelInfo.Path.GamePath` | `models[].logicalPath` | `CharacterBase.ResolveMdlPath` | logical request |
| `ParsedModelInfo.Path.FullPath` | `models[].resolvedPath` | `ModelResourceHandle.FileName` | observed resource |
| `ParsedModelInfo.ModelType` | `models[].modelType` | `CharacterBase.GetModelType` | newly preserved |
| `ParsedModelInfo.SlotIndex` | `models[].slotIndex` | `Model.SlotIndex` | newly preserved |
| `ParsedModelInfo.Enabled` | `models[].enabled` | `MeddleModel.BodyVisible` | visibility is data |
| `ParsedModelInfo.Stain0/1` | `models[].stain0/1` | equipment data | effective runtime stain |
| `ParsedModelInfo.ShapeAttributeGroup` | `models[].shapeAttributes` | runtime model masks | observed masks |
| `ParsedModelInfo.Deformer` | `models[].deformation` | PbdHooks cache | raw `raceSexId` / `deformerId` only |
| `EquipmentModelId.Value` | `customize.equipmentModelIds[].value` | FFXIVClientStructs | raw `ulong`, never reconstructed |
| `ParsedMaterialInfo.Path` | material `logicalPath` / `resolvedPath` | material handle | both semantics kept |
| `ParsedMaterialInfo.Shpk` | `shaderPackageLogicalPath` | `shader/sm5/shpk/<name>` | canonical resolution; null when absent |
| `ParsedMaterialInfo.ColorTableBlob` | `colorTable` | runtime color table | raw MTRL also exported |
| `ParsedMaterialInfo.Textures` | `textures[]` | material handle | resolved and logical paths |
| `ParsedTextureInfo.Resource` | `textures[].runtimeMetadata` | `DxHelper.ExportTextureResource` | runtime metadata, not pixel data |
| `OnRenderMaterialOutput.DecalTexturePath` | `renderOverrides.decalTexturePath` | render material output | path-backed decal |
| `OnRenderMaterialOutput.DecalTexture` | `renderOverrides.decalTextureResourceId` | runtime SkTexture | encoded as runtime PNG |
| `OnRenderMaterialOutput.SkinMaterialTextures` | `renderOverrides.skinMaterialTextures` | skin material output | texture refs preserved |
| `ParsedPartialSkeleton.HandlePath` | `partialSkeletons[].resourceId` | skeleton handle | captured SKLB or unavailable observation; no path inference |
| `ParsedPartialSkeleton.PartialSkeletonFlagsRaw` | `partialSkeletons[].partialSkeletonFlagsRaw` | `PartialSkeletonEx` | newly preserved raw flags |
| `ParsedPartialSkeleton.ConnectedBoneIndex` | `partialSkeletons[].connectedBoneIndex` | runtime partial skeleton | preserved and used in empty-slot classification |
| `ParsedPartialSkeleton.HkSkeleton` | `runtimeSkeleton` | runtime HKA | diagnostic only |
| `ParsedPartialSkeleton.Poses` | `runtimePoses` | runtime HKA pose | diagnostic only |
| `ParsedAttach` | `attach` | runtime attach | full owner/target skeleton snapshots preserved |

## Excluded From ABI

- `ParsedModelInfo.ModelAddress`
- any `nint`, `IntPtr`, or process address
- semantic compatibility handling for `generator.version`
- `ParsedHkaSkeleton.ReferencePose`
- `MaterialComposer.Extras`
- CharacterComposer lookup/fallback rules
- filename-derived slot or deformer state

`ModelAddress` remains internal for existing UI model selection. The UE DTO
does not expose it.

`ReferencePose` is intentionally not dumped into the UE ABI because the raw
SKLB is authoritative and the current blob did not serialize it. Runtime poses
are kept as diagnostic evidence.

## Completion and Failure Semantics

The final bundle directory must not already exist. The service writes to a
temporary sibling directory:

1. write `resources/` for every `Available` manifest entry
2. write `manifest.json`
3. write `character.json`
4. validate ABI invariants, manifest state, referenced IDs, and all
   `Available` physical resources by SHA-256
5. write `bundle.complete`
6. atomically `Directory.Move` the temporary directory to the final name

An existing file or directory destination is an explicit failure. There is no
overwrite, merge, backup, or stale-marker preservation path.

`bundle.complete` means only that bundle writing completed, the JSON documents
are valid, every declared `Available` resource is present and its SHA-256
matches the file bytes, references resolve, ABI invariants passed, and no
temporary filesystem transaction remains. It does not mean every possible MDL,
MTRL, TEX, SHPK, SKLB, or PBD was captured, and it does not mean
reconstruction is complete.

Structural errors stop export. Consumer policy results do not alter the bundle;
consumers evaluate those after structural validation. Cancellation is checked
between traversal steps, before resource reads, and before file writes.
Cancelled or failed exports never produce a valid final bundle.

Missing or semantically unresolved reconstruction evidence does not by itself
invalidate the bundle. Meddle-UE records runtime evidence; downstream
reconstruction decides which evidence is required.

## Unknown Semantics

Phase 1 does not assume semantic ownership or reconstruction necessity for
runtime fields whose role is not established. Raw values such as
`PartialSkeletonFlagsRaw`, `ConnectedBoneIndex`, deformer/PBD relationships,
and SHPK reconstruction necessity are preserved without turning uncertain
interpretation into fatal validation rules.

## Manual Dalamud Verification

Test at least:

- normal player character
- visible weapon
- mount/ornament attach
- dyed equipment
- hair/face customization
- modded resource when available

For each:

- UE Bundle export succeeds
- no glTF/GLB/OBJ is produced by the UE action
- `character.json`, `manifest.json`, `bundle.complete`, and flat `resources/`
  exist
- Available MDL, MTRL, TEX, SKLB, SHPK, and PBD files exist; unavailable
  observations remain in the manifest without physical files
- every snapshot resource id resolves to an Available or unavailable
  observation in the manifest
- missing/unresolved resource observations remain in the manifest without
  removing other captured evidence
- `character.json` does not contain `ModelAddress`
- enabled flags, stains, customize parameters, color tables, skeleton partial
  count, and attach hierarchy match the existing debug blob

## Manual Modded-Byte Verification

Use one intentionally modified MTRL or TEX whose bytes are easy to distinguish.

1. Record `sha256` of the vanilla game file.
2. Record `sha256` of the Penumbra modded disk file.
3. Export a UE bundle while the mod is active.
4. Locate the corresponding resource in `manifest.json` by `logicalPath` and
   `resolvedPath`.
5. Record `sha256` of the exported bundle resource.

Expected result:

```text
exported SHA == modded SHA
exported SHA != vanilla SHA
```

If Meddle exposes a rooted decorated path, the exporter must read disk bytes
or fail; it must not substitute SqPack bytes. If a decorated relative path is
resolved through the SqPack lookup Meddle exposes, the exported origin is
`game` and the bytes are the SqPack bytes. The exporter does not label that
case `modded` without evidence that a mod supplied the bytes.

## Known Phase 1 Gaps

- deformer state may be `NotObserved`; this does not mean deformation is absent
- per-slot skin material constant buffers are not captured
- hair runtime material flags are not captured
- skeleton composition semantics are not considered fully solved merely
  because `ParsedSkeleton` exists

# Runtime Audit and Character Snapshot

## Meddle Runtime Character Snapshot Contract

`RuntimeCharacterSnapshot.json` is an experimental observation-only projection.
It is projected from the canonical runtime observations so the diagnostic
report and projection share one capture path, but it is not the formal bundle
ABI. The numeric v4 `character.json` plus `manifest.json` bundle remains the
only formal Meddle-to-Importer contract.

```text
FFXIV Runtime
    -> Meddle-UE Capture
    -> Character Runtime Snapshot
    -> UE Consumer Plugin
    -> Geometry + Material Assembly
```

The experimental version 1 payload contains `actor`, `customize`, `appearance`, `resources`,
and `skeleton`. Appearance contains equipment state, normal appearance
components (including `MainHand` and `OffHand`), visor state, and an attachment
node/edge graph. Resource bindings record logical and resolved paths,
`overrideDetected`, and generic `original`/`resolved`/`unknown` provenance.

Unknown and null values are valid. Bone names are included only when observed or
present in the conservative semantic map; socket names are never invented.

The contract does not capture geometry, vertices, indices, animation data,
converted meshes, Unreal Engine assets, glTF, or Blender pipeline state. Asset
parsing, Havok handling, geometry/material assembly, and UE asset generation
remain the consumer's responsibility.

`FFXIVCharacterRuntimeAudit.json` is a diagnostic view and may contain warnings,
missing-field explanations, debug evidence, and legacy audit projections. Those
diagnostics are not part of the snapshot contract.

**Status: experimental — not a snapshot ABI.**

This document describes the `audit` branch: a runtime information auditing
instrument for Meddle. Its only goal is to discover what the FFXIV runtime
actually exposes for character reconstruction by FFXIVImporter.

The audit system does NOT:

- redesign or replace existing exporters;
- add UE-specific output;
- define final snapshot schemas;
- parse MDL/MTRL/SKLB/PAP files;
- generate Unreal assets;
- reconstruct FFXIV character logic.

FFXIVImporter stays responsible for MDL/MTRL/SKLB/PAP parsing, Unreal
reconstruction and final asset generation. Meddle only observes the runtime.

## Architecture

All audit code lives under `Meddle/Meddle.Plugin/Audit` (runtime collectors)
and `Meddle/Meddle.Utils/Audit` (pure data model, serializer, diff, runner).
The runtime side is optional and **disabled by default**:

```text
Meddle/Meddle.Plugin/Audit/
  RuntimeAuditService.cs        capture lifecycle, persistence, diffing
  RuntimeAuditCapture.cs        runs collectors against one actor
  RuntimeAuditCommands.cs       /paudit chat commands
  AuditCollectorUtil.cs         fail-closed runtime helpers
  Collectors/
    CharacterCompositionCollector.cs
    SkeletonCompositionCollector.cs
    MaterialCollector.cs
    RuntimeTextureCollector.cs
    DeformerCollector.cs
    CustomizeCollector.cs

Meddle/Meddle.Utils/Audit/
  RuntimeAuditSession.cs        one capture (actor + observations)
  RuntimeAuditObservation.cs    one observed runtime fact
  RuntimeAuditCollector.cs      collector contract + context
  RuntimeAuditRunner.cs         fail-closed collector loop
  RuntimeAuditSerializer.cs     temporary dump format
  RuntimeAuditDiff.cs           capture comparison
  RuntimeAuditFormat.cs         format marker / constants
  RuntimeAuditReport.cs         consolidated FFXIVCharacterRuntimeAudit.json
```

Every collector must explicitly report one of:

- `Available` — the runtime exposed the data and it was captured;
- `Unavailable` — the runtime did not expose it (or it is not applicable);
- `Unknown` — the audit cannot tell whether the data exists.

## What is observed

| Category | Source types | Captured facts |
| --- | --- | --- |
| `CharacterComposition` | `CharacterBase`, `DrawObject`, `Model` | model type, draw object visibility, per-slot model resource identity, logical path (if runtime resolves one), enabled state, equipment variant (humans only), attach execute type / attachment count / bone attachment |
| `CharacterComposition` | `GameObject`, `ModelResource` | object id (`EntityId`), data id (`LayoutId`), model id (`ModelContainer.ModelCharaId`), per-model runtime header counts (mesh/submesh/material/bone/shape) |
| `SkeletonEvidence` | `Skeleton`, `PartialSkeleton`, `hkaSkeleton` | skeleton handle path, partial skeleton count, bone count, connected bone index, partial skeleton flags, pose count, hierarchy availability, parent indices, bone names, owner/target skeleton handles on attaches |
| `Material` | `Material`, `MaterialResourceHandle` | material path, shader package identity, shader flags, ref count, texture binding count, color table presence, parameter buffer size/float count |
| `RuntimeTexture` | `TextureResourceHandle`, `Texture` | binding id, sampler flags, runtime handle, source path, resource path, generated/runtime status, dimensions, mip count, array size, format |
| `Deformer` | `Deformer` (via existing `PbdHooks` cache) | per-slot existence: `exists` / `none` / `incomplete` / `unknown`, deformer id, race/sex id, PBD path, hook cache size |
| `Customize` | `Human` | raw race/sex/height/tribe/face/hairstyle/highlights/skin/eye/hair colors/face paint/lip fields, `RaceSexId`, customize constant buffer parameters, decal color, equipment model ids + stains, face decal presence |

Values are kept in FFXIV-native representation. Nothing is converted or
reinterpreted beyond what the runtime itself exposes.

## What is known to be unavailable / unknown today

These are observations made by reading the current code, not assumptions:

- Semantic model slots (Head/Body/Hair/Weapon) are **not** reported. The
  runtime exposes slot indexes; mapping slot indexes to semantics is
  reconstruction, which belongs to FFXIVImporter.
- Model `variant` remains unknown. A model `SlotIndex` is not cast into the
  separate Human equipment array without an independently verified mapping.
- Customize data is only available for `Human` targets. For demihuman,
  monster and weapon targets the collector reports `Unavailable`.
- Deformer evidence depends on the existing `Human_CreateDeformer` runtime
  hook having fired for the observed character. Before the hook fires for a
  slot, the collector reports `unknown`.
- Skeleton hierarchy is available only when a partial skeleton has a
  `SkeletonResourceHandle` with a loaded Havok skeleton. Otherwise
  `hierarchyAvailable` is false and `parentRelationship` is `unknown`.
- Texture `generatedStatus` is `unknown` when neither the material binding
  path nor the resource handle file name are populated.
- Material "exposed parameters" are reported as buffer size/count evidence.
  Parameter *names* are stored in the shader package file and are not read
  by the audit tool (that would be file parsing, not runtime observation).
- Per-mesh vertex/index counts and vertex-buffer availability are **not**
  reported from the runtime. The runtime model header exposes counts
  (mesh/submesh/material/bone/shape), but per-mesh vertex/index data lives in
  the MDL file and would require file parsing, which the audit does not do.
- Weapon child draw objects are captured as separate `Weapon` observations and
  are traversed by the material/texture collectors. Their raw `weaponIndex` is
  preserved, but MainHand/OffHand semantics remain unknown because array order
  alone is not sufficient evidence.

## How to run a capture

The audit is disabled by default. Enable it from the plugin configuration
(`AuditConfig.Enabled`) or with:

```text
/paudit enable
```

Capture the local player character once:

```text
/paudit capture A
```

Continuous observation (keeps appending observations while the character is
visible in the world):

```text
/paudit start A
... change character (equipment, dye, hair, height, face) ...
/paudit stop
```

Other commands:

```text
/paudit list
/paudit status
/paudit diff A B
/paudit disable
```

Captures are written to the audit output directory
(default: `<MyDocuments>/Meddle/AuditDump`), one folder per capture id.

## How to compare captures

```text
/paudit capture A
change character: equipment, dye, hair, height, face
/paudit capture B
/paudit diff A B
```

The diff reports:

- `added` — resources/skeletons/textures/parameters present in B but not A;
- `changed` — parameters, attachments, material state whose value differs;
- `removed` — resources/skeletons/textures present in A but not B.

Diffing is deterministic: identical inputs always produce identical output,
and entries are sorted by identity. A diff JSON is also written next to the
captures (`diff-<a>-<b>.json`).

## Output format

Temporary audit dump layout (marked `"Experimental audit format"` in every
file; **not** the final snapshot ABI):

```text
AuditDump/
  <captureId>/
    runtime.json       canonical session (metadata + all observations)
    resources.json     projection: CharacterComposition + RuntimeTexture
    materials.json     projection: Material
    skeletons.json     projection: SkeletonEvidence
    deformers.json     projection: Deformer
    FFXIVCharacterRuntimeAudit.json
                       consolidated character audit report
```

`FFXIVCharacterRuntimeAudit.json` is the primary report for the audit branch.
It reorganizes the canonical observations into the requested schema:

```json
{
  "format": "Experimental audit format",
  "schemaVersion": 1,
  "captureId": "player",
  "actor": {
    "address": "0x1A2B3C4D",
    "objectKind": "Pc",
    "name": "Test Character",
    "objectId": 268435456,
    "dataId": 131073,
    "modelId": 257,
    "race": 1,
    "gender": 0,
    "tribe": 1,
    "modelType": "Human"
  },
  "customize": {
    "available": true,
    "raceSexId": 101,
    "race": 1,
    "sex": 0,
    "face": 1,
    "hairstyle": 5,
    "skinColor": 100,
    "hairColor": 200,
    "parameterBuffer": { "available": true },
    "faceDecal": { "present": false }
  },
  "drawObjects": [
    {
      "slotIndex": 1,
	  "slotName": null,
      "resource": "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
      "logicalPath": "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
      "enabled": true,
      "variant": 1,
      "relation": "unknown",
      "meshEvidence": { "meshCount": 4, "submeshCount": 6 }
    }
  ],
  "meshes": [
    {
      "slotIndex": 1,
      "resource": "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
      "meshDataAvailable": true,
      "meshCount": 4,
      "submeshCount": 6,
      "vertexDeclarationCount": 1,
      "materialCount": 2,
      "boneCount": 24,
      "shapeCount": 3,
      "vertexCount": null,
      "indexCount": null,
      "vertexBufferAvailable": null,
      "normalTangentAvailable": null,
      "uvAvailable": null,
      "reason": "per-mesh vertex/index data requires MDL parsing, which is outside the runtime audit scope"
    }
  ],
  "materials": [
    {
      "materialIndex": 0,
      "modelSlot": 1,
      "path": "chara/human/c0101e0001/obj/body/b0001/vmat/c0101e0001_top_b0001.mtrl",
      "shaderPackage": "character.shpk",
      "hasColorTable": true,
      "textureCount": 3,
      "parameterBufferFloatCount": 16
    }
  ],
  "textures": [
    {
      "modelSlot": 1,
      "materialIndex": 0,
      "index": 0,
      "bindingId": 0,
      "sourcePath": "chara/human/c0101e0001/obj/body/b0001/tex/c0101e0001_top_b0001_d.tex",
      "resourcePath": "chara/human/c0101e0001/obj/body/b0001/tex/c0101e0001_top_b0001_d.tex",
      "generatedStatus": "file",
      "width": 512,
      "height": 512,
      "format": "BC7"
    }
  ],
  "equipment": [
    {
      "slot": 1,
      "slotName": "Body",
      "modelId": 101,
      "variant": 1,
      "finalModel": "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
      "finalMesh": { "meshEvidence": "..." },
      "finalMaterial": [ "..." ],
      "stain0": 2,
      "stain1": 0,
      "resolved": true
    }
  ],
  "attachments": [
    {
      "source": "CharacterBase.Attach",
      "attachmentData": { "executeType": 1, "attachmentCount": 2, "partialSkeletonIdx": 3, "boneIdx": 4 }
    },
    {
      "source": "Skeleton.AttachRelationships",
      "attachmentData": { "attachmentCount": 2, "executeType": 1 }
    }
  ],
  "skeleton": {
    "available": true,
    "partialSkeletonCount": 1,
    "skeletonAddress": "0x...",
    "partialSkeletons": [
      {
        "partialSkeletonIndex": 0,
        "handle": "chara/human/c0101e0001/skeleton/base/b0001/skl_c0101e0001_b0001.sklb",
        "boneCount": 100,
        "boneNames": ["n_root", "n_hips", "n_waist"],
        "parentIndices": [-1, 0, 1],
        "hierarchyAvailable": true
      }
    ],
    "completeness": "COMPLETE"
  },
  "penumbraResolution": {
    "observed": true,
    "overrideObserved": false,
    "resources": [
      {
        "slotIndex": 1,
        "original": "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
        "resolved": "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
        "overrideObserved": false,
		"overridden": null,
		"provenance": "unknown",
		"note": "equal or missing paths do not independently prove original-file provenance"
      }
    ],
    "limitation": "The audit only observes final runtime paths. It cannot enumerate Penumbra's mod state; a resource replaced at load time is indistinguishable from a game file with the same name."
  },
  "missingData": [
    {
      "category": "Mesh",
      "sourceType": "ModelResource",
      "availability": "Unknown",
      "reason": "per-mesh vertex/index/buffer data requires MDL parsing, which is outside the runtime audit scope"
    },
    {
	  "category": "Attachment",
	  "sourceType": "Mount",
      "availability": "Unknown",
	  "reason": "mount draw state is not independently captured by the current experimental audit"
    }
  ],
  "verdict": {
	"reconstructionReady": false,
    "missingCoreCategories": [],
	"gaps": ["Attachment:Mount", "Attachment:Ornament", "Mesh:ModelResource"],
    "note": "A reconstruction-ready verdict only means the runtime exposed the audited evidence. FFXIVImporter still needs MDL/MTRL/SKLB/PAP parsing and its own reconstruction logic."
  }
}
```

Notes on the report:

- `actor` carries the runtime identity: `objectId` = `EntityId`,
  `dataId` = `LayoutId`, `modelId` = `ModelContainer.ModelCharaId`. These are
  the final runtime values Meddle sees; they are not resolved against Excel.
- `meshes` reports per-model header evidence the runtime exposes without file
  parsing. Per-mesh `vertexCount`/`indexCount`/buffer availability are
  explicitly `null` with a reason: obtaining them requires MDL parsing, which
  is out of scope for the runtime audit.
- `equipment` uses the existing `HumanEquipmentSlotIndex` names
  (Head/Body/Hands/Legs/Feet/Ears/Neck/Wrists/RFinger/LFinger/Glasses/Extra)
  and matches each entry to the final runtime model, mesh evidence, materials
  and stain/dye values by slot.
- `attachments` reports `CharacterBase.Attach` and the skeleton attach
  relationships (owner/target skeleton handles) the runtime exposes.
- `penumbraResolution` compares the runtime resource-handle path with the
  logical resolved path. A difference is reported as an override. The audit
  cannot see Penumbra's mod list, so an unmodded resource is reported as
  "runtime resource handle path equals the logical path".
- `missingData` aggregates every `Unavailable`/`Unknown` observation with its
  recorded reason, plus known structural gaps of the instrument.
- `verdict` summarizes which core categories were collected and whether any
  are missing; it is an observation-level verdict, not a guarantee that
  FFXIVImporter can rebuild the character.

Example observation:

```json
{
  "time": "2026-08-21T12:00:00.0000000Z",
  "actorId": "0x1A2B3C4D",
  "category": "CharacterComposition",
  "sourceType": "DrawObject",
  "value": {
    "models": [
      {
        "slotIndex": 1,
        "resource": "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
        "logicalPath": "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
        "enabled": true,
        "variant": 1,
        "relation": "unknown"
      }
    ]
  },
  "availability": "Available"
}
```

## Safety / fail-closed behavior

The audit system must never crash the game. Collectors run inside a guarded
loop (`RuntimeAuditRunner`): if an object is unavailable, a pointer is
invalid, a resource is missing, or runtime state changes mid-read, the
failing collector records an `Unavailable` observation and the remaining
collectors still run. Loading a corrupt or partial dump returns `null` or a
partial session instead of throwing.

## Limitations

- The audit observes one character (local player, falling back to the first
  valid object-table character). It walks explicitly linked weapon child draw
  objects, including their materials/textures and attachment records. Mount and
  ornament draw objects are not independently captured and remain known gaps.
- Continuous captures are capped at 10,000 observations per capture.
- Collectors read runtime pointers on the game thread; a long capture with a
  very short interval can add overhead.
- The dump format is temporary and will change as research progresses. Do
  not build consumers against it.
- This document records what the current implementation observes. Anything
  not listed here is not claimed to be observed.

## Recommended next investigation targets

1. Mount / ornament draw objects: capture them as separate composition
   observations to verify whether the runtime exposes their resource
   identities independently of the character base.
2. Skin material override chain (`SlotSkinMaterials`) and the shader package
   texture id -> sampler mapping, to see which texture bindings the runtime
   resolves that are not present in the base material.
3. Dynamic/instanced texture bindings (decal, dye, company crest) to confirm
   `generatedStatus` classification against real in-game cases.
4. Skeleton pose evidence: capture pose count and per-pose bone counts to
   determine whether runtime poses are usable without SKLB parsing.

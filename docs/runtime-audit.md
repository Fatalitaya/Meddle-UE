# Runtime Audit (investigation instrument)

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
```

Every collector must explicitly report one of:

- `Available` — the runtime exposed the data and it was captured;
- `Unavailable` — the runtime did not expose it (or it is not applicable);
- `Unknown` — the audit cannot tell whether the data exists.

## What is observed

| Category | Source types | Captured facts |
| --- | --- | --- |
| `CharacterComposition` | `CharacterBase`, `DrawObject`, `Model` | model type, draw object visibility, per-slot model resource identity, logical path (if runtime resolves one), enabled state, equipment variant (humans only), attach execute type / attachment count / bone attachment |
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
- Model `variant` is only available for `Human` model type (from the
  equipment model id). For all other model types it is `null`/unknown.
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
```

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
  valid object-table character). It does not yet walk weapon/mount/ornament
  draw objects as separate captures; attach relationships are captured from
  the character base attach struct only.
- Continuous captures are capped at 10,000 observations per capture.
- Collectors read runtime pointers on the game thread; a long capture with a
  very short interval can add overhead.
- The dump format is temporary and will change as research progresses. Do
  not build consumers against it.
- This document records what the current implementation observes. Anything
  not listed here is not claimed to be observed.

## Recommended next investigation targets

1. Weapon / mount / ornament draw objects: capture them as separate
   composition observations to verify whether the runtime exposes their
   resource identities independently of the character base.
2. Skin material override chain (`SlotSkinMaterials`) and the shader package
   texture id -> sampler mapping, to see which texture bindings the runtime
   resolves that are not present in the base material.
3. Dynamic/instanced texture bindings (decal, dye, company crest) to confirm
   `generatedStatus` classification against real in-game cases.
4. Skeleton pose evidence: capture pose count and per-pose bone counts to
   determine whether runtime poses are usable without SKLB parsing.

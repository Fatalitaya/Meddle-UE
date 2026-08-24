using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Formats.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Export.Ue;
using Meddle.SqPack;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Models.Composer;
public class CharacterComposer
{
    public record SkinningContext(List<BoneNodeBuilder> Bones, BoneNodeBuilder? RootBone, Matrix4x4 Transform);

    private sealed record ComposeContext(SceneBuilder Scene, NodeBuilder Root, Matrix4x4 InstanceWorldTransform);

    /// <summary>
    /// Attachment context for a child character (weapon/ornament). The owner's
    /// ACTUAL skeleton-root world (<see cref="OwnerRootWorld"/>, including the
    /// owner root's own local transform) is propagated explicitly so the child
    /// never re-derives it by walking scene parents through foreign skeletons.
    /// <c>OwnerRootWorld</c> is the owner skeleton root node's actual world
    /// matrix (RootWorld from the owner compose result), never an attach-point
    /// world: descendants compose as
    /// <c>childRootLocal * attachBoneToRoot * OwnerRootWorld</c>.
    /// </summary>
    private readonly record struct AttachContext(
        ParsedCharacterInfo Owner,
        IReadOnlyList<BoneNodeBuilder> OwnerBones,
        Matrix4x4 OwnerRootWorld,
        ParsedAttach Attach);

    /// <summary>
    /// Result of composing one character (or a root attach / weapon) with full
    /// world provenance. <see cref="RootWorld"/> is the skeleton root node's
    /// ACTUAL world matrix (mesh pose matrix): it includes the root's own local
    /// transform (current pose or child-root local/attach offset), the parent
    /// attachment placement, and the instance/world layer — each exactly once.
    /// It is the ONLY value handed to descendants as their owner root world.
    /// </summary>
    private sealed record CharacterComposeResult(
        IReadOnlyList<BoneNodeBuilder> Bones,
        BoneNodeBuilder RootBone,
        Matrix4x4 RootWorld);

    private readonly ComposerCache composerCache;
    private readonly Configuration.ExportConfiguration exportConfig;
    private readonly CancellationToken cancellationToken;
    private readonly Dictionary<ParsedCharacterInfo, Dictionary<string, MaterialBuilder>> materialCache = new();

    public CharacterComposer(ComposerCache composerCache, Configuration.ExportConfiguration exportConfig, CancellationToken cancellationToken)
    {
        this.composerCache = composerCache;
        this.exportConfig = exportConfig;
        this.cancellationToken = cancellationToken;
    }
    
    public CharacterComposer(SqPack.SqPack pack, Configuration.ExportConfiguration exportConfig, string outDir, CancellationToken cancellationToken)
    {
        this.exportConfig = exportConfig;
        this.cancellationToken = cancellationToken;
        Directory.CreateDirectory(outDir);
        var cacheDir = Path.Combine(outDir, "cache");
        Directory.CreateDirectory(cacheDir);
        composerCache = new ComposerCache(pack, cacheDir, exportConfig);
    }
    
    private void HandleModel(ParsedCharacterInfo characterInfo, ParsedModelInfo m, SceneBuilder scene, SkinningContext skinningContext)
    {
        if (m.Path.GamePath.Contains("b0003_top"))
        {
            Plugin.Logger.LogDebug("Skipping model {ModelPath}", m.Path.GamePath);
            return;
        }
        
        if (!m.Enabled && exportConfig.ApplyVisibilityFlags)
        {
            Plugin.Logger.LogDebug("Skipping disabled model {ModelPath}", m.Path.GamePath);
            return;
        }
        
        var mdlFile = composerCache.GetMdlFile(m.Path.FullPath);
        Plugin.Logger.LogInformation("Loaded model {modelPath}", m.Path.FullPath);
        var materialBuilders = new MaterialBuilder[m.Materials.Length];
        for (int i = 0; i < m.Materials.Length; i++)
        {
            var materialInfo = m.Materials[i];
            if (materialInfo == null)
            {
                materialBuilders[i] = new MaterialBuilder("null");
                continue;
            }
            
            try
            {
                if (!materialCache.TryGetValue(characterInfo, out var characterCache))
                {
                    characterCache = new Dictionary<string, MaterialBuilder>();
                    materialCache[characterInfo] = characterCache;
                }
                
                if (characterCache.TryGetValue(materialInfo.GetHash(), out var cachedBuilder))
                {
                    materialBuilders[i] = cachedBuilder;
                }
                else
                {
                    materialBuilders[i] = composerCache.ComposeMaterial(materialInfo.Path.FullPath,
                                                                        materialInfo: materialInfo,
                                                                        characterInfo: characterInfo);
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e, "Failed to load material\n{Message}\n{MaterialInfo}", e.Message, 
                                        JsonSerializer.Serialize(m.Materials[i], 
                                            MaterialComposer.JsonOptions));
                materialBuilders[i] = new MaterialBuilder("error");
            }
        }

        var model = new Model(m.Path.GamePath, mdlFile, m.ShapeAttributeGroup);
        EnsureBonesExist(model, skinningContext.Bones, skinningContext.RootBone);
        (GenderRace from, GenderRace to, RaceDeformer deformer)? deform = null;
        if (exportConfig.UseDeformer)
        {
            if (m.Deformer is { State: DeformerObservationState.Observed } deformerObservation)
            {
                if (deformerObservation.DeformerId is not { } deformerId ||
                    deformerObservation.RaceSexId is not { } raceSexId ||
                    string.IsNullOrWhiteSpace(deformerObservation.PbdPath))
                {
                    throw new InvalidOperationException("Observed deformer is missing required runtime fields.");
                }

                var pbdFile = composerCache.GetPbdFile(deformerObservation.PbdPath);
                if (pbdFile == null)
                {
                    throw new InvalidOperationException($"Failed to get deformer pbd {deformerObservation.PbdPath} returned null");
                }

                deform = ((GenderRace)deformerId,
                             (GenderRace)raceSexId,
                             new RaceDeformer(pbdFile, skinningContext.Bones));
                Plugin.Logger.LogDebug("Using deformer pbd {Path}", deformerObservation.PbdPath);
            }
            else
            {
                var parsed = RaceDeformer.ParseRaceCode(m.Path.GamePath);
                if (Enum.IsDefined(parsed))
                {
                    deform = (parsed, characterInfo.GenderRace, new RaceDeformer(composerCache.GetDefaultPbdFile(), skinningContext.Bones));
                }
                else
                {
                    deform = null;
                }
            }
        }

        var enabledAttributes = Model.GetEnabledValues(model.EnabledAttributeMask, model.AttributeMasks).ToArray();
        var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, skinningContext.Bones, deform, exportConfig.CreateMeshBuilderOptions());
        foreach (var meshExport in meshes)
        {
            var extrasDict = new Dictionary<string, string>
            {
                {"modelGamePath", m.Path.GamePath},
                {"modelFullPath", m.Path.FullPath},
                {"modelEnabled", m.Enabled.ToString()},
                {"meshShapes", meshExport.Shapes != null ? string.Join(",", meshExport.Shapes) : ""},
                {"modelEnabledAttributes", string.Join(",", enabledAttributes)},
                {"modelAttributes", string.Join(",", model.AttributeMasks.Select(x => x.name))},
                {"nodeType", "CharacterMesh"}
            };
            
            if (deform != null)
            {
                extrasDict.Add("modelDeformFromName", deform.Value.from.ToString());
                extrasDict.Add("modelDeformToName", deform.Value.to.ToString());
                extrasDict.Add("modelDeformFromId", ((int)deform.Value.from).ToString());
                extrasDict.Add("modelDeformToId", ((int)deform.Value.to).ToString());
            }
            else
            {
                extrasDict.Add("modelDeformFromName", "");
                extrasDict.Add("modelDeformToName", "");
                extrasDict.Add("modelDeformFromId", "");
                extrasDict.Add("modelDeformToId", "");
            }

            // for shape keys to work, mesh.Mesh.Extras already has an object with a field 'targetNames' which names all the shape keys. need to preserve that.
            var extras = meshExport.Mesh.Extras?.AsObject();
            if (extras != null)
            {
                foreach (var kvp in extrasDict)
                {
                    if (!extras.ContainsKey(kvp.Key))
                    {
                        extras.Add(kvp.Key, kvp.Value);
                    }
                }
                
                meshExport.Mesh.Extras = extras;
            }
            else
            {
                meshExport.Mesh.Extras = JsonNode.Parse(JsonSerializer.Serialize(extrasDict, MaterialComposer.JsonOptions));
            }
            
            InstanceBuilder instance;
            if (skinningContext.Bones.Count > 0)
            {
                instance = scene.AddSkinnedMesh(meshExport.Mesh, skinningContext.Transform, skinningContext.Bones.Cast<NodeBuilder>().ToArray());
            }
            else
            {
                instance = scene.AddRigidMesh(meshExport.Mesh, skinningContext.Transform);
            }

            if (model.Shapes.Count != 0 && meshExport.Shapes != null)
            {
                // This will set the morphing value to 1 if the shape is enabled, 0 if not
                var enabledShapes = Model.GetEnabledValues(model.EnabledShapeMask, model.ShapeMasks)
                                         .ToArray();
                var shapes = model.Shapes
                                  .Where(x => meshExport.Shapes.Contains(x.Name))
                                  .Select(x => (x, enabledShapes.Contains(x.Name)));
                instance.Content.UseMorphing().SetValue(shapes.Select(x => x.Item2 ? 1f : 0).ToArray());
            }
            
            if (meshExport.Submesh != null && exportConfig.ApplyVisibilityFlags)
            {
                // Remove subMeshes that are not enabled
                if (!meshExport.Submesh.Attributes.All(x => enabledAttributes.Contains(x)))
                {
                    instance.Remove();
                }
            }
        }
    }
    
    private int attachSuffix;
    private readonly object attachLock = new();

    /// <summary>
    /// Parents a child skeleton root at the owner attach point, applies the
    /// attach offset exactly once (into <c>rootBone.LocalMatrix</c>), and
    /// sets <paramref name="transform"/> to the child's ACTUAL RootWorld:
    /// <c>childRootLocal * attachBoneToRoot * ownerRootWorld</c>. That value is
    /// the child's mesh pose matrix AND the only value handed to the child's
    /// own descendants as their owner root world.
    /// </summary>
    private void HandleAttach(ComposeContext ctx, AttachContext attachData, BoneNodeBuilder rootBone, ref Matrix4x4 transform)
    {
        var attach = attachData.Attach;
        if (rootBone == null) throw new InvalidOperationException("Root bone not found");

        if (!SkeletonUtils.TryResolveAttachPoint(
                attachData.Owner.Skeleton,
                attachData.OwnerBones,
                attach,
                out var attachPointBone,
                out var attachName,
                out var resolveReason))
        {
            Plugin.Logger.LogWarning(
                "Attach bone could not be resolved for owner {Owner}: {Reason}; attaching child at the scene root as a documented fallback.",
                attachData.Owner,
                resolveReason);
            ctx.Root.AddNode(rootBone);
            transform = AttachmentTransformPolicy.ComposeAttachedRootWorld(
                rootBone.LocalMatrix, Matrix4x4.Identity, ctx.Root.LocalMatrix);
            return;
        }

        Plugin.Logger.LogInformation("Attaching {AttachName} to {RootBone}", attachName, rootBone.BoneName);
        lock (attachLock)
        {
            Interlocked.Increment(ref attachSuffix);
            rootBone.SetSuffixRecursively(attachSuffix);
        }

        if (attach.OffsetTransform is { } ct)
        {
            rootBone.WithLocalScale(ct.Scale);
            rootBone.WithLocalRotation(ct.Rotation);
            rootBone.WithLocalTranslation(ct.Translation);
            if (rootBone.AnimationTracksNames.Contains("pose"))
            {
                rootBone.UseScale().UseTrackBuilder("pose").WithPoint(0, ct.Scale);
                rootBone.UseRotation().UseTrackBuilder("pose").WithPoint(0, ct.Rotation);
                rootBone.UseTranslation().UseTrackBuilder("pose").WithPoint(0, ct.Translation);
            }
        }

        attachPointBone!.AddNode(rootBone);

        // Owner-skeleton-only chain from the attach point to the owner root
        // local space, using the owner's CURRENT pose and EXCLUDING the owner
        // root's own local transform (which already lives in
        // attachData.OwnerRootWorld). The owner's actual RootWorld was
        // resolved when the owner composed and is propagated explicitly; no
        // scene-parent rescan through foreign skeletons happens here.
        var ownerBoneSet = new HashSet<BoneNodeBuilder>(attachData.OwnerBones);
        var boneToRoot = SkeletonUtils.ComputeBoneToRoot(
            attachPointBone, attachData.Owner.Skeleton, ownerBoneSet);

        // Attach.OffsetTransform is applied exactly once, into
        // rootBone.LocalMatrix above. ChildRootWorld (this child's RootWorld)
        // = rootBone.LocalMatrix * boneToRoot * OwnerRootWorld (row-vector).
        transform = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            rootBone.LocalMatrix, boneToRoot, attachData.OwnerRootWorld);
    }

    /// <summary>
    /// Handles a root-attached (mounted) character: composes the mount as a
    /// top-level character, attaches this character's skeleton root at the
    /// mount's attach point using the mount's CURRENT pose, and sets
    /// <paramref name="transform"/> to this character's ACTUAL RootWorld
    /// (<c>childRootLocal * mountAttachPointWorld</c>), which descendants
    /// inherit directly. Returns <c>false</c> when no root attach exists or the
    /// mount failed to compose (the caller keeps the documented scene-root
    /// fallback).
    /// </summary>
    private bool HandleRootAttach(
        ComposeContext ctx,
        ParsedCharacterInfo characterInfo,
        ref BoneNodeBuilder rootBone,
        ref Matrix4x4 transform,
        ExportProgress rootProgress)
    {
        var rootAttach = characterInfo.Attaches.FirstOrDefault(x => x.Attach.ExecuteType == 0);
        if (rootAttach == null)
        {
            Plugin.Logger.LogWarning("Root attach not found");
            return false;
        }

        Plugin.Logger.LogWarning("Root attach found");
        // handle root first, then attach this to the root
        var rootAttachProgress = new ExportProgress(rootAttach.Models.Count, "Root attach");
        rootProgress.Children.Add(rootAttachProgress);
        try
        {
            var rootAttachData = ComposeCharacterInfo(ctx, rootAttach, null, rootAttachProgress);
            if (rootAttachData == null)
            {
                return false;
            }

            if (rootBone == null) throw new InvalidOperationException("Root bone not found");
            var attachRoot = rootAttachData.RootBone;
            lock (attachLock)
            {
                Interlocked.Increment(ref attachSuffix);
                attachRoot.SetSuffixRecursively(attachSuffix);
            }

            if (rootAttach.Attach.OffsetTransform is { } ct)
            {
                attachRoot.WithLocalScale(ct.Scale);
                attachRoot.WithLocalRotation(ct.Rotation);
                attachRoot.WithLocalTranslation(ct.Translation);
                if (attachRoot.AnimationTracksNames.Contains("pose"))
                {
                    attachRoot.UseScale().UseTrackBuilder("pose").WithPoint(0, ct.Scale);
                    attachRoot.UseRotation().UseTrackBuilder("pose").WithPoint(0, ct.Rotation);
                    attachRoot.UseTranslation().UseTrackBuilder("pose").WithPoint(0, ct.Translation);
                }
            }

            // This character is attached at the mount's attach point. The
            // mount's actual RootWorld was already computed when the mount
            // composed; the mount-bone chain uses the mount's CURRENT pose
            // strictly inside the mount skeleton (never foreign LocalMatrix)
            // and excludes the mount root's own local transform.
            var mountBoneSet = new HashSet<BoneNodeBuilder>(rootAttachData.Bones);
            Matrix4x4 attachPointWorld;
            if (!SkeletonUtils.TryResolveAttachPoint(
                    rootAttach.Skeleton,
                    rootAttachData.Bones,
                    characterInfo.Attach,
                    out var attachPointBone,
                    out _,
                    out var resolveReason))
            {
                Plugin.Logger.LogWarning(
                    "Root attach bone could not be resolved: {Reason}; attaching at the scene root as a documented fallback.",
                    resolveReason);
                ctx.Root.AddNode(rootBone);
                attachPointWorld = ctx.Root.LocalMatrix;
            }
            else
            {
                attachPointBone!.AddNode(rootBone);
                var boneToRoot = SkeletonUtils.ComputeBoneToRoot(
                    attachPointBone, rootAttach.Skeleton, mountBoneSet);
                attachPointWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
                    Matrix4x4.Identity, boneToRoot, rootAttachData.RootWorld);
            }

            // PlayerRootWorld = PlayerRootLocal (incl. any root placement)
            // * mount attach point world.
            transform = AttachmentTransformPolicy.ComposeAttachedRootWorld(
                rootBone.LocalMatrix, Matrix4x4.Identity, attachPointWorld);
            rootBone = attachRoot;
            return true;
        }
        finally
        {
            rootAttachProgress.IsComplete = true;
        }
    }
    
    public (List<BoneNodeBuilder> bones, BoneNodeBuilder root)? Compose(
        ParsedCharacterInfo characterInfo,
        SceneBuilder scene,
        NodeBuilder root,
        ExportProgress progress,
        Matrix4x4? instanceWorldTransform = null)
    {
        var worldTransform = instanceWorldTransform ?? Matrix4x4.Identity;
        composerCache.SaveArrayTextures();
        // Contract: the instance/world layer is represented exactly once — as
        // the scene root's local matrix AND inside the top-level character's
        // RootWorld (rootLocal * instance). Both carry the same matrix, so the
        // mesh pose and the armature share one coordinate space and the layer
        // can never be applied a second time inside RootWorld or descendant
        // attachments.
        root.LocalMatrix = worldTransform;
        root.Extras = JsonNode.Parse(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            {"raceCode", ((int)characterInfo.GenderRace).ToString()},
            {"raceCodeName", characterInfo.GenderRace.ToString() },
            { "nodeType", "CharacterRoot" }
        }));
        var ctx = new ComposeContext(scene, root, worldTransform);
        var result = ComposeCharacterInfo(ctx, characterInfo, null, progress);
        return result == null ? null : (result.Bones.ToList(), result.RootBone);
    }
    
    private CharacterComposeResult? ComposeCharacterInfo(
        ComposeContext ctx,
        ParsedCharacterInfo characterInfo,
        AttachContext? attachData,
        ExportProgress rootProgress)
    {
        // Cache the runtime-observed partial skeleton identities (.sklb
        // HandlePaths) as optional raw resource evidence. This is a cache-only
        // side channel: it never parses the SKLB and never alters skeleton or
        // transform semantics. "Observed path / cached resource" — not
        // "verified runtime skeleton".
        foreach (var partial in characterInfo.Skeleton.PartialSkeletons)
        {
            if (!string.IsNullOrWhiteSpace(partial.HandlePath))
            {
                composerCache.CacheSklb(partial.HandlePath);
            }
        }

        List<BoneNodeBuilder> bones;
        BoneNodeBuilder? rootBone;
        try
        {
            bones = SkeletonUtils.GetBoneMap(characterInfo.Skeleton, exportConfig.PoseMode, out rootBone);
            if (rootBone == null)
            {
                Plugin.Logger.LogWarning("Root bone not found");
                return null;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e, "Failed to get bone map");
            return null;
        }

        var ownerBoneSet = new HashSet<BoneNodeBuilder>(bones);
        // RootWorld: the skeleton root node's ACTUAL world matrix, including
        // the root's own local transform (current pose / child-root local with
        // attach offset) and the root-parent world (instance layer, or the
        // attach point world computed by HandleAttach / HandleRootAttach) —
        // each layer exactly once. This same value is the mesh pose matrix and
        // is handed directly to descendants as their owner root world.
        Matrix4x4 transform = SkeletonUtils.ComputeRootWorld(
            rootBone,
            characterInfo.Skeleton,
            ownerBoneSet,
            ctx.InstanceWorldTransform);
        bool rootParented = false;
        if (attachData != null)
        {
            try
            {
                HandleAttach(ctx, attachData.Value, rootBone, ref transform);
                rootParented = true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e, "Failed to handle attach {AttachData}", JsonSerializer.Serialize(new
                {
                    AttachData = attachData.Value.Attach,
                    Owner = attachData.Value.Owner
                }, MaterialComposer.JsonOptions));
            }
        }
        else if (characterInfo.Attach.ExecuteType != 0)
        {
            try
            {
                if (HandleRootAttach(ctx, characterInfo, ref rootBone, ref transform, rootProgress))
                {
                    rootParented = true;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e, "Failed to handle root attach {CharacterInfo}", JsonSerializer.Serialize(characterInfo, MaterialComposer.JsonOptions));
            }
        }

        if (!rootParented)
        {
            ctx.Root.AddNode(rootBone);
        }
        
        foreach (var t in characterInfo.Models)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Plugin.Logger.LogInformation("Export cancelled, stopping model processing");
                break;
            }
            
            try
            {
                HandleModel(characterInfo, t, 
                            ctx.Scene,
                            new SkinningContext(bones, rootBone, transform));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e, "Failed to handle model\n{Message}\n{ModelInfo}", e.Message, JsonSerializer.Serialize(t, MaterialComposer.JsonOptions));
            }
            
            rootProgress.IncrementProgress();
        }
        
        foreach (var t in characterInfo.Attaches)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Plugin.Logger.LogInformation("Export cancelled, stopping attach processing");
                break;
            }
            
            ExportProgress? attachProgress = null;
            try
            {
                if (t.Attach.ExecuteType == 0) continue;
                attachProgress = new ExportProgress(t.Models.Count, "Attach Meshes");
                rootProgress.Children.Add(attachProgress);
                // Descendants inherit this character's ACTUAL RootWorld, not an
                // attach-point world, so nested children keep this root's own
                // local transform / Attach.OffsetTransform.
                ComposeCharacterInfo(ctx, t, new AttachContext(characterInfo, bones, transform, t.Attach), attachProgress);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e, "Failed to handle attach {Attach}", JsonSerializer.Serialize(t, MaterialComposer.JsonOptions));
            }
            finally
            {
                if (attachProgress != null)
                {
                    attachProgress.IsComplete = true;
                }
            }
        }

        return new CharacterComposeResult(bones, rootBone, transform);
    }
    
    public static void EnsureBonesExist(Model model, List<BoneNodeBuilder> bones, BoneNodeBuilder? root)
    {
        foreach (var mesh in model.Meshes)
        {
            if (mesh.BoneTable == null) continue;
    
            foreach (var boneName in mesh.BoneTable)
            {
                if (bones.All(b => !b.BoneName.Equals(boneName, StringComparison.Ordinal)))
                {
                    if (root == null)
                    {
                        throw new InvalidOperationException($"Root bone not found when generating missing bone {boneName} for {model.HandlePath}");
                    }
                    
                    var bone = new BoneNodeBuilder(boneName)
                    {
                        IsGenerated = true
                    };
                    
                    var parent = FindLogicalParent(model, bone, bones) ?? root;
                    parent.AddNode(bone);
                    
                    Plugin.Logger.LogWarning("Added bone {BoneName} to {ParentBone}\n" +
                                              "NOTE: This may break posing in some cases, you may find better results " +
                                              "deleting the bone after importing into editing software", boneName, parent.BoneName);
                    bones.Add(bone);
                }
            }
        }
    }

    /// <summary>
    /// Find a logical parent bone for a bone in a model
    /// </summary>
    /// <returns></returns>
    public static BoneNodeBuilder? FindLogicalParent(Model model, BoneNodeBuilder node, List<BoneNodeBuilder> bones)
    {
        // for each vertex in meshes which contain the bone
        // check if the bone is included in the blend indices for the vertex
        // if it is, check other bones weighted to the same vertex to find a parent bone
        foreach (var mesh in model.Meshes)
        {
            if (mesh.BoneTable != null && mesh.BoneTable.Contains(node.BoneName))
            {
                foreach (var vertex in mesh.Vertices)
                {
                    if (vertex.BlendIndices == null) continue;
                    
                    var vertexBones = vertex.BlendIndices.Select(x => mesh.BoneTable[x]).ToArray();
                    if (vertexBones.Contains(node.BoneName))
                    {
                        var parentBone = vertexBones.FirstOrDefault(x => bones.Any(b => b.BoneName.Equals(x, StringComparison.Ordinal)));
                        if (parentBone != null)
                        {
                            return bones.FirstOrDefault(x => x.BoneName.Equals(parentBone, StringComparison.Ordinal));
                        }
                    }
                }
            }
        }
        
        return null;
    }
}

using System.Numerics;
using System.Text.RegularExpressions;
using Meddle.Unreal;
using SharpGLTF.Schema2;
using SkiaSharp;

namespace Meddle.Utils.Export.Ue;

/// <summary>Converts the composer's in-memory scene directly into editable UE packages.</summary>
public static class NativeAssetExporter
{
    public static string SafeName(string name) => AssetNames.Sanitize(name);

    public static void Export(ModelRoot scene, string destination, string name, string? textureCache = null,
        int fps = 30, bool meshes = true, CancellationToken token = default, bool? inPlace = null, bool relativeMotion = true,
        UeMeshMergeMode mergeMode = UeMeshMergeMode.All, bool writeStaticMeshes = true,
        List<string>? staticMaterialSlots = null)
    {
        if (fps is not (30 or 60)) throw new ArgumentOutOfRangeException(nameof(fps));
        if (meshes && !scene.LogicalNodes.Any(n => n.Mesh != null || n.PunctualLight != null))
            throw new InvalidDataException("Instance contains no mesh geometry or lights.");
        if (!meshes && scene.LogicalSkins.Count == 0) throw new InvalidDataException("Animation scene contains no skeletons.");
        name = SafeName(name);
        var package = "/Game/Meddle/" + name;
        var folder = Path.Combine(destination, "Content", "Meddle", name);
        Directory.CreateDirectory(folder);
        if (meshes)
        {
            var lights = UeLightExport.Read(scene);
            if (lights.Length != 0)
            {
                token.ThrowIfCancellationRequested();
                var mapPath = Path.Combine(folder, name + "_Lights.umap");
                LevelWriter.Write(mapPath, package + "/" + name + "_Lights", [], lights);
                UeLightExport.WriteMetadata(mapPath, lights);
            }
        }
        string FileFor(string category, string asset) => Path.Combine(folder, category, asset + ".uasset");
        string PackageFor(string category, string asset) => package + "/" + category + "/" + asset;
        var materials = new Dictionary<Material, string>();
        if (meshes)
        foreach (var material in scene.LogicalMaterials)
        {
            token.ThrowIfCancellationRequested();
            var matName = $"M_{material.LogicalIndex}_{SafeName(material.Name ?? "Material")}";
            string? diffuse = null, normal = null;
            var metadata = material.Extras as System.Text.Json.Nodes.JsonObject;
            metadata ??= new System.Text.Json.Nodes.JsonObject();
            var shader = CharacterMaterial.Classify(metadata["ShaderPackage"]?.GetValue<string>() ?? "");
            var tintKey = shader switch { CharacterShader.Skin => "SkinColor", CharacterShader.Hair => "MainColor", _ => "g_DiffuseColor" };
            var tint = Vector4.One;
            if (metadata[tintKey] is System.Text.Json.Nodes.JsonArray components && components.Count >= 3)
                tint = new(components[0]!.GetValue<float>(), components[1]!.GetValue<float>(), components[2]!.GetValue<float>(), 1);
            var textures = new Dictionary<string, MaterialTexture>();
            if (textureCache != null)
            {
                var sources = metadata.Where(p => p.Key.EndsWith("_PngCachePath") && p.Value != null)
                    .ToDictionary(p => p.Key.Replace("_PngCachePath", ""), p => p.Value!.GetValue<string>());
                if (shader is CharacterShader.Skin or CharacterShader.Equipment or CharacterShader.Legacy)
                foreach (var usage in new[] { "chara_tile_norm_array", "chara_tile_orb_array" })
                {
                    var directory = Path.Combine(textureCache, "array_textures/chara/common/texture", usage.Replace("chara_", ""));
                    if (!Directory.Exists(directory)) continue;
                    var pattern = Regex.Escape(usage.Replace("chara_", "")) + @"\..+\.vertical\.png$";
                    var candidates = Directory.GetFiles(directory, "*.vertical.png")
                        .Where(path => Regex.IsMatch(Path.GetFileName(path), pattern)).ToArray();
                    if (candidates.Length == 1) sources[usage] = Path.GetRelativePath(textureCache, candidates[0]);
                }
                foreach (var (usage, cachePath) in sources)
                {
                    var source = Path.GetFullPath(Path.Combine(textureCache, cachePath));
                    var cacheRoot = Path.GetFullPath(textureCache).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!source.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Texture escapes composition cache.");
                    if (!File.Exists(source)) continue; // Build reports missing formula dependencies.
                    using var codec = SKCodec.Create(source) ?? throw new InvalidDataException($"Cannot decode {source}");
                    using var bitmap = SKBitmap.Decode(codec, new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul))
                        ?? throw new InvalidDataException($"Cannot decode {source}");
                    var texName = $"T_{material.LogicalIndex}_{SafeName(usage)}";
                    var texturePath = PackageFor("Textures", texName);
                    var srgb = CharacterMaterial.IsColor(usage) && shader is not (CharacterShader.Legacy or CharacterShader.Skin or CharacterShader.Background or CharacterShader.Lightshaft);
                    var nearest = usage is "g_SamplerIndex" or "chara_tile_norm_array" or "chara_tile_orb_array";
                    var clamp = usage == "Decal" || (usage == "g_SamplerDiffuse" && shader == CharacterShader.Skin && metadata["GetMaterialValue"]?.GetValue<string>() == "GetMaterialValueFace");
                    TextureWriter.Write(FileFor("Textures", texName), texturePath, bitmap.Width, bitmap.Height, bitmap.Bytes, srgb,
                        nearest: nearest, clamp: clamp, linearData: !srgb);
                    textures[usage] = new(usage, texturePath, srgb, nearest, clamp, bitmap.Width, bitmap.Height);
                    if (usage == "g_SamplerDiffuse") diffuse = texturePath;
                    if (usage == "g_SamplerNormal") normal = texturePath;
                }
            }
            var table = MaterialColorTable.Read(metadata["ColorTable"]?["ColorTable"]?["Rows"]);
            if (table != null)
            {
                var tableName = $"T_{material.LogicalIndex}_ColorTableFloat";
                TextureWriter.WriteFloat(FileFor("Textures", tableName), PackageFor("Textures", tableName), 4, 32, table.ToFloatPixels());
            }
            var formula = CharacterMaterial.Build(metadata, textures);
            MaterialWriter.Write(FileFor("Materials", matName), PackageFor("Materials", matName),
                new AssetMaterial(tint, 0, 0.6f, diffuse, normal,
                    TwoSided: IsTwoSided(material), AlphaMode: formula.AlphaMode, Graph: formula.Graph, PackedNormal: true, Unlit: formula.Unlit, DitherOpacityMask:formula.DitherOpacityMask));
            var reportPath = Path.ChangeExtension(FileFor("Materials", matName), ".formula.json");
            System.IO.File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                Reference = shader == CharacterShader.Lightshaft ? LightshaftMaterial.Reference : shader == CharacterShader.Background ? BackgroundMaterial.Reference : shader is CharacterShader.Legacy or CharacterShader.Tattoo or CharacterShader.Occlusion ? ShpkMaterial.Reference : shader == CharacterShader.Skin ? SkinMaterial.Reference : shader == CharacterShader.Hair ? CharacterMaterial.HairReference : CharacterMaterial.ReferenceCommit,
                Metadata = shader is CharacterShader.Skin or CharacterShader.Background or CharacterShader.Lightshaft ? metadata : null,
                Shader = metadata["ShaderPackage"]?.GetValue<string>(),
                FormulaEnabled = formula.Graph != null,
                formula.Gap,
                formula.AlphaMode,
                formula.Unlit,
                formula.DitherOpacityMask,
                formula.Adaptation,
                Textures = textures.Values,
                Outputs = formula.Graph?.Outputs.Keys.Where(x => x != "Opacity" || formula.AlphaMode != "OPAQUE").ToArray()
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            materials[material] = PackageFor("Materials", matName);
        }

        var groups = meshes ? UeMeshGrouping.Build(scene, mergeMode) : scene.LogicalSkins
            .Select(s => new UeMeshGroup(s.LogicalIndex.ToString(),scene.LogicalNodes.Where(n=>n.Skin==s && n.Mesh!=null).ToArray())).ToArray();
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            token.ThrowIfCancellationRequested();
            var group = groups[groupIndex];
            var nodes = group.Nodes;
            if (nodes.Length == 0) continue;
            var joints = nodes.Where(n=>n.Skin != null).SelectMany(n=>n.Skin.Joints).Distinct().ToArray();
            var ordered = joints.OrderBy(Depth).ThenBy(j => j.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var maps = nodes.ToDictionary(n=>n,n=>n.Skin == null ? Array.Empty<int>() : n.Skin.Joints.Select(j=>Array.IndexOf(ordered,j)+1).ToArray());
            // SceneBuilder stores the mesh bind placement in inverse binds while
            // emitting an identity mesh node. Recover it before combining skins.
            var transforms = nodes.ToDictionary(n=>n,n=>n.Skin == null ? n.WorldMatrix : n.Skin.InverseBindMatrices[0]*n.Skin.Joints[0].WorldMatrix*n.WorldMatrix);
            var globals = new Dictionary<Node, Matrix4x4>();
            foreach (var node in nodes)
            {
                if (node.Skin == null) continue;
                var nodeJoints=node.Skin.Joints.ToArray();
                var inverseBind=node.Skin.InverseBindMatrices.ToArray();
                for(var i=0;i<nodeJoints.Length;i++)
                {
                    if (!Matrix4x4.Invert(inverseBind[i], out var bind)) throw new InvalidDataException("Singular inverse bind matrix.");
                    var worldBind=bind*transforms[node];
                    if(globals.TryGetValue(nodeJoints[i],out var existing) && !BindMatches(existing,worldBind))
                        throw new InvalidDataException($"Cannot merge meshes with different bind poses for bone {nodeJoints[i].Name}.");
                    globals.TryAdd(nodeJoints[i],worldBind);
                }
            }
            var bones = new List<AssetBone> { new("Meddle_Root", -1, BoneTransform.Identity) };
            var boneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Meddle_Root" };
            foreach (var joint in ordered)
            {
                var parent = joint.VisualParent;
                while (parent != null && !globals.ContainsKey(parent)) parent = parent.VisualParent;
                var local = globals[joint];
                if (parent != null)
                {
                    if (!Matrix4x4.Invert(globals[parent], out var inv)) throw new InvalidDataException("Singular bind transform.");
                    local *= inv;
                }
                var boneName=joint.Name ?? $"Bone_{joint.LogicalIndex}";
                if(!boneNames.Add(boneName))
                {
                    var baseName=boneName; var suffix=joint.LogicalIndex;
                    do { boneName=$"{baseName}_{suffix++}"; } while(!boneNames.Add(boneName));
                }
                bones.Add(new(boneName, parent == null ? 0 : Array.IndexOf(ordered, parent) + 1, Coordinates.Transform(local)));
            }
            var skinned = nodes.Any(n => n.Skin != null);
            var (skeletonPath, guid) = skinned ? SkeletonIdentity.Write(destination, bones, name) : ("", Guid.Empty);
            if (meshes)
            {
                var sections = new List<AssetSection>();
                var morphs = new Dictionary<string, (List<MorphDelta> Deltas, List<int> Sections)>();
                uint vertexOffset = 0;
                foreach (var node in nodes)
                foreach (var primitive in node.Mesh.Primitives)
                {
                    var map=maps[node];
                    var meshTransform=transforms[node];
                    var positions = primitive.GetVertexAccessor("POSITION").AsVector3Array();
                    var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                    var tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array();
                    var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
                    var uv = Enumerable.Range(0, 4).Select(i => primitive.GetVertexAccessor("TEXCOORD_" + i)?.AsVector2Array()).ToArray();
                    var js = new[] { primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array(), primitive.GetVertexAccessor("JOINTS_1")?.AsVector4Array() };
                    var ws = new[] { primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array(), primitive.GetVertexAccessor("WEIGHTS_1")?.AsVector4Array() };
                    Matrix4x4.Invert(meshTransform, out var inverseWorld);
                    var normalMatrix = Matrix4x4.Transpose(inverseWorld);
                    var mirrored = meshTransform.GetDeterminant() < 0;
                    var vertices = new AssetVertex[positions.Count];
                    for (var v = 0; v < vertices.Length; v++)
                    {
                        var influences = new List<int>(); var weights = new List<float>();
                        for (var set = 0; node.Skin != null && set < 2; set++)
                        if (js[set] != null && ws[set] != null)
                        for (var c = 0; c < 4; c++)
                        {
                            var weight = ws[set]![v][c]; if (weight <= 0) continue;
                            influences.Add(map[checked((int)js[set]![v][c])]); weights.Add(weight);
                        }
                        if (influences.Count == 0) { influences.Add(0); weights.Add(1); }
                        var normalValue = Vector3.Normalize(Coordinates.Direction(Vector3.TransformNormal(normals?[v] ?? Vector3.UnitY, normalMatrix)));
                        var tangentValue = tangents?[v] ?? new Vector4(1, 0, 0, 1);
                        var tangentDirection = Vector3.Normalize(Coordinates.Direction(Vector3.TransformNormal(new(tangentValue.X, tangentValue.Y, tangentValue.Z), meshTransform)));
                        var color = colors?[v] ?? Vector4.One;
                        var vertexShader = CharacterMaterial.Classify(primitive.Material?.Extras?["ShaderPackage"]?.GetValue<string>() ?? "");
                        if (vertexShader is CharacterShader.Legacy or CharacterShader.Tattoo or CharacterShader.Occlusion or CharacterShader.Skin)
                            color = ShpkMaterial.VertexColor(color);
                        byte C(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
                        uint packed = (uint)(C(color.Z) | C(color.Y) << 8 | C(color.X) << 16 | C(color.W) << 24);
                        vertices[v] = new(Coordinates.Position(Vector3.Transform(positions[v], meshTransform)), normalValue,
                            new(tangentDirection, ConvertTangentSign(tangentValue.W, mirrored)), uv.TakeWhile(a => a != null).Select(a => a![v]).ToArray(), packed, influences.ToArray(), weights.ToArray());
                    }
                    for (var target = 0; target < primitive.MorphTargetsCount; target++)
                    {
                        var accessors = primitive.GetMorphTargetAccessors(target);
                        if (!accessors.TryGetValue("POSITION", out var positionAccessor)) continue;
                        var deltas = positionAccessor.AsVector3Array();
                        var normalDeltas = accessors.TryGetValue("NORMAL", out var normalAccessor) ? normalAccessor.AsVector3Array() : null;
                        var targetName = node.Mesh.Extras?["targetNames"]?[target]?.GetValue<string>() ?? $"Morph_{target}";
                        if (!morphs.TryGetValue(targetName, out var morph)) { morph = ([], []); morphs.Add(targetName, morph); }
                        morph.Sections.Add(sections.Count);
                        for (var v = 0; v < deltas.Count; v++)
                        {
                            var delta = Coordinates.Position(Vector3.TransformNormal(deltas[v], meshTransform));
                            var normalDelta = Coordinates.Direction(Vector3.TransformNormal(normalDeltas?[v] ?? Vector3.Zero, normalMatrix));
                            if (delta.LengthSquared() > 0 || normalDelta.LengthSquared() > 0)
                                morph.Deltas.Add(new(vertexOffset + (uint)v, delta, normalDelta));
                        }
                    }
                    var indices = TriangleIndices(primitive, mirrored);
                    sections.Add(new(primitive.Material?.Name ?? "Material", primitive.Material == null ? null : materials[primitive.Material], vertices, indices));
                    vertexOffset += (uint)vertices.Length;
                }
                var meshName = (skinned ? "SK_" : "SM_") + (mergeMode == UeMeshMergeMode.All ? name : group.Name);
                if (skinned)
                    SkeletalMeshWriter.Write(FileFor("Meshes", meshName), PackageFor("Meshes", meshName), skeletonPath, new(bones.ToArray(), sections.ToArray(),
                        morphs.Select(m => new AssetMorph(m.Key, m.Value.Deltas.ToArray(), m.Value.Sections.ToArray())).ToArray()));
                else
                {
                    staticMaterialSlots?.AddRange(sections.Select(s => s.MaterialPath ?? "/Engine/EngineMaterials/DefaultMaterial"));
                    if (writeStaticMeshes)
                        StaticMeshWriter.Write(FileFor("Meshes", meshName), PackageFor("Meshes", meshName), sections.ToArray());
                }
            }
            if (!skinned) continue;
            foreach (var animation in scene.LogicalAnimations)
            {
                var count = Math.Max(1, (int)Math.Ceiling(animation.Duration * fps));
                var tracks = new BoneTrack[bones.Count];
                tracks[0] = new(0, Enumerable.Repeat(BoneTransform.Identity, count + 1).ToArray());
                for (var j = 0; j < ordered.Length; j++)
                {
                    var keys = new BoneTransform[count + 1];
                    for (var frame = 0; frame <= count; frame++)
                    {
                        token.ThrowIfCancellationRequested();
                        var t = Math.Min((float)frame / fps, animation.Duration);
                        var joint = ordered[j];
                        var local = joint.GetLocalTransform(animation, t).Matrix;
                        var parent = joint.VisualParent;
                        while (parent != null && !globals.ContainsKey(parent))
                        { local *= parent.GetLocalTransform(animation, t).Matrix; parent = parent.VisualParent; }
                        keys[frame] = Coordinates.Transform(local);
                    }
                    tracks[j + 1] = new(j + 1, keys);
                }
                var animName = $"A_{name}_{SafeName(animation.Name ?? "Animation")}_{groupIndex}_{animation.LogicalIndex}";
                var baked = new BakedAnimation(fps, count, tracks);
                if (inPlace.HasValue) RootMotion.Extract(bones, baked, inPlace.Value, relativeMotion);
                AnimationWriter.Write(FileFor("Animations", animName), PackageFor("Animations", animName), skeletonPath, guid, bones, baked, inPlace == false);
            }
        }
    }

    private static bool BindMatches(Matrix4x4 a, Matrix4x4 b)
    {
        var delta=a-b;
        return new[]{delta.M11,delta.M12,delta.M13,delta.M14,delta.M21,delta.M22,delta.M23,delta.M24,
            delta.M31,delta.M32,delta.M33,delta.M34,delta.M41,delta.M42,delta.M43,delta.M44}.All(x=>MathF.Abs(x)<0.0001f);
    }

    internal static bool IsTwoSided(Material material)
        => material.DoubleSided || material.Extras?["RenderBackfaces"]?.GetValue<bool>() == true;

    internal static float ConvertTangentSign(float sign, bool mirrored) => mirrored ? sign : -sign;

    internal static uint[] TriangleIndices(MeshPrimitive primitive, bool mirrored)
        // The reflected glTF-to-UE basis already converts CCW to UE's clockwise front faces.
        // Only an additional reflection in the baked node transform requires swapping indices.
        => primitive.GetTriangleIndices().SelectMany(t => mirrored
            ? new[] { (uint)t.Item1, (uint)t.Item3, (uint)t.Item2 }
            : new[] { (uint)t.Item1, (uint)t.Item2, (uint)t.Item3 }).ToArray();

    private static int Depth(Node n) { var depth = 0; while (n.VisualParent != null) { depth++; n = n.VisualParent; } return depth; }
}

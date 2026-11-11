using System.Numerics;
using System.Text.RegularExpressions;
using Meddle.Unreal;
using SharpGLTF.Schema2;
using SkiaSharp;

namespace Meddle.Utils.Export.Ue;

/// <summary>Converts the composer's in-memory scene directly into editable UE packages.</summary>
public static class NativeAssetExporter
{
    public static string SafeName(string name)
    {
        var value = Regex.Replace(name, "[^a-zA-Z0-9_]", "_");
        return string.IsNullOrWhiteSpace(value) ? "Character" : value;
    }

    public static void Export(ModelRoot scene, string destination, string name, string? textureCache = null,
        int fps = 30, bool meshes = true, CancellationToken token = default, bool? inPlace = null, bool relativeMotion = true)
    {
        if (fps is not (30 or 60)) throw new ArgumentOutOfRangeException(nameof(fps));
        if (scene.LogicalSkins.Count == 0) throw new InvalidDataException("Character scene contains no skeletons.");
        name = SafeName(name);
        var package = "/Game/Meddle/" + name;
        var folder = Path.Combine(destination, "Content", "Meddle", name);
        Directory.CreateDirectory(folder);
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
            var shader = metadata?["ShaderPackage"]?.GetValue<string>() ?? "";
            var tintKey = shader.Contains("skin", StringComparison.OrdinalIgnoreCase) ? "SkinColor" :
                shader.Contains("hair", StringComparison.OrdinalIgnoreCase) ? "MainColor" :
                shader.Contains("iris", StringComparison.OrdinalIgnoreCase) ? "LeftIrisColor" : "g_DiffuseColor";
            var tint = Vector4.One;
            if (metadata?[tintKey] is System.Text.Json.Nodes.JsonArray components && components.Count >= 3)
                tint = new(components[0]!.GetValue<float>(), components[1]!.GetValue<float>(), components[2]!.GetValue<float>(), 1);
            if (textureCache != null && material.Extras is System.Text.Json.Nodes.JsonObject extras)
            foreach (var property in extras)
            {
                if (!property.Key.EndsWith("_PngCachePath") || property.Value == null) continue;
                var source = Path.GetFullPath(Path.Combine(textureCache, property.Value.GetValue<string>()));
                var cacheRoot = Path.GetFullPath(textureCache).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!source.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Texture escapes composition cache.");
                using var decoded = SKBitmap.Decode(source) ?? throw new InvalidDataException($"Cannot decode {source}");
                using var bitmap = new SKBitmap(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                using (var canvas = new SKCanvas(bitmap)) canvas.DrawBitmap(decoded, 0, 0);
                var texName = $"T_{material.LogicalIndex}_{SafeName(property.Key.Replace("_PngCachePath", ""))}";
                var isNormal = property.Key.Contains("Normal", StringComparison.OrdinalIgnoreCase);
                var isDiffuse = property.Key.Contains("Diffuse", StringComparison.OrdinalIgnoreCase);
                var bytes = bitmap.Bytes;
                if (isDiffuse)
                for (var i = 0; i < bytes.Length; i += 4)
                {
                    // Multiply linear color factors in linear light, retaining source alpha.
                    for (var c = 0; c < 3; c++)
                    {
                        var encoded = bytes[i + c] / 255f;
                        var linear = encoded <= .04045f ? encoded / 12.92f : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
                        linear *= tint[2 - c];
                        var output = linear <= .0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - .055f;
                        bytes[i + c] = (byte)Math.Clamp((int)MathF.Round(output * 255), 0, 255);
                    }
                }
                if (isNormal) for (var i = 1; i < bytes.Length; i += 4) bytes[i] = (byte)(255 - bytes[i]);
                var texturePath = PackageFor("Textures", texName);
                TextureWriter.Write(FileFor("Textures", texName), texturePath, bitmap.Width, bitmap.Height, bytes, isDiffuse, isNormal);
                if (isDiffuse) diffuse ??= texturePath;
                if (isNormal) normal ??= texturePath;
            }
            MaterialWriter.Write(FileFor("Materials", matName), PackageFor("Materials", matName),
                new AssetMaterial(diffuse == null ? tint : Vector4.One, 0, 0.6f, diffuse, normal,
                    TwoSided: metadata?["RenderBackfaces"]?.GetValue<bool>() ?? material.DoubleSided,
                    AlphaMode: metadata?["IsTransparent"]?.GetValue<bool>() == true ? "BLEND" : "OPAQUE"));
            materials[material] = PackageFor("Materials", matName);
        }

        foreach (var skin in scene.LogicalSkins)
        {
            token.ThrowIfCancellationRequested();
            var nodes = scene.LogicalNodes.Where(n => n.Skin == skin && n.Mesh != null).ToArray();
            if (nodes.Length == 0) continue;
            var joints = skin.Joints.ToArray();
            var ordered = joints.OrderBy(Depth).ThenBy(j => j.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var map = joints.Select(j => Array.IndexOf(ordered, j) + 1).ToArray();
            var inverseBind = skin.InverseBindMatrices.ToArray();
            var globals = new Dictionary<Node, Matrix4x4>();
            for (var i = 0; i < joints.Length; i++)
            {
                if (!Matrix4x4.Invert(inverseBind[i], out var bind)) throw new InvalidDataException("Singular inverse bind matrix.");
                globals[joints[i]] = bind * nodes[0].WorldMatrix;
            }
            var bones = new List<AssetBone> { new("Meddle_Root", -1, BoneTransform.Identity) };
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
                bones.Add(new(joint.Name ?? $"Bone_{joint.LogicalIndex}", parent == null ? 0 : Array.IndexOf(ordered, parent) + 1, Coordinates.Transform(local)));
            }
            var (skeletonPath, guid) = SkeletonIdentity.Write(destination, bones, name);
            if (meshes)
            {
                var sections = new List<AssetSection>();
                var morphs = new Dictionary<string, (List<MorphDelta> Deltas, List<int> Sections)>();
                uint vertexOffset = 0;
                foreach (var node in nodes)
                foreach (var primitive in node.Mesh.Primitives)
                {
                    var positions = primitive.GetVertexAccessor("POSITION").AsVector3Array();
                    var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                    var tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array();
                    var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
                    var uv = Enumerable.Range(0, 4).Select(i => primitive.GetVertexAccessor("TEXCOORD_" + i)?.AsVector2Array()).ToArray();
                    var js = new[] { primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array(), primitive.GetVertexAccessor("JOINTS_1")?.AsVector4Array() };
                    var ws = new[] { primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array(), primitive.GetVertexAccessor("WEIGHTS_1")?.AsVector4Array() };
                    Matrix4x4.Invert(node.WorldMatrix, out var inverseWorld);
                    var normalMatrix = Matrix4x4.Transpose(inverseWorld);
                    var vertices = new AssetVertex[positions.Count];
                    for (var v = 0; v < vertices.Length; v++)
                    {
                        var influences = new List<int>(); var weights = new List<float>();
                        for (var set = 0; set < 2; set++)
                        if (js[set] != null && ws[set] != null)
                        for (var c = 0; c < 4; c++)
                        {
                            var weight = ws[set]![v][c]; if (weight <= 0) continue;
                            influences.Add(map[checked((int)js[set]![v][c])]); weights.Add(weight);
                        }
                        if (influences.Count == 0) { influences.Add(0); weights.Add(1); }
                        var normalValue = Vector3.Normalize(Coordinates.Direction(Vector3.TransformNormal(normals?[v] ?? Vector3.UnitY, normalMatrix)));
                        var tangentValue = tangents?[v] ?? new Vector4(1, 0, 0, 1);
                        var tangentDirection = Vector3.Normalize(Coordinates.Direction(Vector3.TransformNormal(new(tangentValue.X, tangentValue.Y, tangentValue.Z), node.WorldMatrix)));
                        var color = colors?[v] ?? Vector4.One;
                        byte C(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
                        uint packed = (uint)(C(color.Z) | C(color.Y) << 8 | C(color.X) << 16 | C(color.W) << 24);
                        vertices[v] = new(Coordinates.Position(Vector3.Transform(positions[v], node.WorldMatrix)), normalValue,
                            new(tangentDirection, -tangentValue.W), uv.TakeWhile(a => a != null).Select(a => a![v]).ToArray(), packed, influences.ToArray(), weights.ToArray());
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
                            var delta = Coordinates.Position(Vector3.TransformNormal(deltas[v], node.WorldMatrix));
                            var normalDelta = Coordinates.Direction(Vector3.TransformNormal(normalDeltas?[v] ?? Vector3.Zero, normalMatrix));
                            if (delta.LengthSquared() > 0 || normalDelta.LengthSquared() > 0)
                                morph.Deltas.Add(new(vertexOffset + (uint)v, delta, normalDelta));
                        }
                    }
                    var indices = primitive.GetTriangleIndices().SelectMany(t => new[] { (uint)t.Item1, (uint)t.Item3, (uint)t.Item2 }).ToArray();
                    sections.Add(new(primitive.Material?.Name ?? "Material", primitive.Material == null ? null : materials[primitive.Material], vertices, indices));
                    vertexOffset += (uint)vertices.Length;
                }
                var meshName = $"SK_{name}_{skin.LogicalIndex}";
                SkeletalMeshWriter.Write(FileFor("Meshes", meshName), PackageFor("Meshes", meshName), skeletonPath, new(bones.ToArray(), sections.ToArray(),
                    morphs.Select(m => new AssetMorph(m.Key, m.Value.Deltas.ToArray(), m.Value.Sections.ToArray())).ToArray()));
            }
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
                var animName = $"A_{name}_{SafeName(animation.Name ?? "Animation")}_{skin.LogicalIndex}_{animation.LogicalIndex}";
                var baked = new BakedAnimation(fps, count, tracks);
                if (inPlace.HasValue) RootMotion.Extract(bones, baked, inPlace.Value, relativeMotion);
                AnimationWriter.Write(FileFor("Animations", animName), PackageFor("Animations", animName), skeletonPath, guid, bones, baked, inPlace == false);
            }
        }
    }

    private static int Depth(Node n) { var depth = 0; while (n.VisualParent != null) { depth++; n = n.VisualParent; } return depth; }
}

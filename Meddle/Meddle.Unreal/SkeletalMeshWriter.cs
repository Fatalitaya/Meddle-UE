using System.Numerics;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace Meddle.Unreal;

public static class SkeletalMeshWriter
{
    public static void Write(string file, string packagePath, string skeletonPath, AssetMesh mesh)
    {
        AssetValidation.Bones(mesh.Bones);
        if (mesh.Bones.Length > ushort.MaxValue || mesh.Sections.Length == 0 || mesh.Sections.Length > short.MaxValue)
            throw new ArgumentException("Unsupported skeleton or section count.");
        foreach (var s in mesh.Sections)
        {
            if (s.Vertices.Length == 0 || s.Indices.Length == 0 || s.Indices.Length % 3 != 0 || s.Indices.Any(i => i >= s.Vertices.Length))
                throw new ArgumentException("Each section requires vertices and valid triangle indices.");
            foreach (var v in s.Vertices)
            {
                if (v.TexCoords.Length > 4 || v.Bones.Length != v.Weights.Length || v.Bones.Length == 0 || v.Bones.Length > 12 ||
                    v.Bones.Any(b => b < 0 || b >= mesh.Bones.Length) || v.Weights.Any(w => !float.IsFinite(w) || w < 0) || v.Weights.Sum() <= 0)
                    throw new ArgumentException("Invalid vertex UVs or skin influences.");
                AssetValidation.Transform(new(v.Position, Quaternion.Identity, v.Normal));
            }
        }
        var p = new AssetPackage(packagePath);
        var skeleton = p.Reference(skeletonPath, "Skeleton");
        var native = p.Bytes(w =>
        {
            w.Write((ushort)0);
            var positions = mesh.Sections.SelectMany(s => s.Vertices).Select(v => v.Position).ToArray();
            var min = positions.Aggregate(Vector3.Min); var max = positions.Aggregate(Vector3.Max);
            var center = (min + max) / 2; var extent = (max - min) / 2;
            foreach (double n in new[] { center.X, center.Y, center.Z, extent.X, extent.Y, extent.Z, positions.Max(v => Vector3.Distance(v, center)) }) w.Write(n);
            w.Write(mesh.Sections.Length);
            foreach (var section in mesh.Sections)
            {
                w.Write(section.MaterialPath == null ? 0 : p.Reference(section.MaterialPath, "Material"));
                w.Write(p.NameOf(section.MaterialSlot)); w.Write(1); w.Write(p.NameOf(section.MaterialSlot));
                w.Write(0); w.Write(0); // UVChannelData initialized / override
                for (var i = 0; i < 4; i++) w.Write(0f);
                w.Write(0); // overlay material
            }
            SkeletonWriter.ReferenceSkeleton(p, w, mesh.Bones);
            w.Write((ushort)0); // imported model strip flags
            w.Write(1); // LODModels
            Lod(p, w, mesh);
            w.Write(Guid.NewGuid()); w.Write(0); // model GUID / is hash
            w.Write(1); // inline reduction cache
            w.Write((uint)positions.Length); w.Write((uint)(mesh.Sections.Sum(s => s.Indices.Length) / 3));
            w.Write(0); // not cooked
            w.Write(0); // legacy object references
        });
        p.AddObject(p.Name, "SkeletalMesh", w =>
        {
            p.ObjectProperty(w, "Skeleton", skeleton);
            p.BoolProperty(w, "bHasVertexColors", mesh.Sections.Any(s => s.Vertices.Any(v => v.Color != uint.MaxValue)));
            if (mesh.Morphs is { Length: > 0 })
                p.Property(w, "MorphTargets", p.Bytes(b => { b.Write(mesh.Morphs.Length); for (var i = 0; i < mesh.Morphs.Length; i++) b.Write(i + 2); }), ("ArrayProperty", 1), ("ObjectProperty", 0));
            p.Property(w, "LODInfo", p.Bytes(b => { b.Write(1); p.End(b); }),
                ("ArrayProperty", 1), ("StructProperty", 1), ("SkeletalMeshLODInfo", 1), ("/Script/Engine", 0));
        }, native);
        foreach (var morph in mesh.Morphs ?? [])
        {
            p.AddObject(morph.Name, "MorphTarget", w => p.ObjectProperty(w, "BaseSkelMesh", 1), p.Bytes(w =>
            {
                w.Write((ushort)0); w.Write(0); w.Write(1);
                w.Write(morph.Deltas.Length);
                foreach (var delta in morph.Deltas.OrderBy(d => d.Vertex))
                { Vector(w, delta.Position); Vector(w, delta.Normal); w.Write(delta.Vertex); }
                w.Write(mesh.Sections.Sum(s => s.Vertices.Length));
                w.Write(morph.Sections.Length); foreach (var section in morph.Sections) w.Write(section);
                w.Write(0); w.Write(new FString(""));
            }), outer: 1);
        }
        p.Save(file);
    }

    private static void Lod(AssetPackage p, AssetBinaryWriter w, AssetMesh mesh)
    {
        w.Write((ushort)0);
        w.Write(mesh.Sections.Length);
        uint baseVertex = 0, baseIndex = 0;
        for (var i = 0; i < mesh.Sections.Length; i++)
        {
            var s = mesh.Sections[i];
            w.Write((ushort)0); w.Write((short)i); w.Write(baseIndex); w.Write(s.Indices.Length / 3);
            w.Write(0); w.Write((byte)3); // recompute tangents / mask
            w.Write(1); w.Write(1); // cast shadow / ray tracing
            w.Write(baseVertex);
            w.Write(s.Vertices.Length);
            foreach (var v in s.Vertices) Vertex(w, v);
            w.Write(mesh.Bones.Length > 256 ? 1 : 0);
            BoneIndices(w, mesh.Bones.Length);
            w.Write(s.Vertices.Length); w.Write(s.Vertices.Max(v => v.Bones.Length));
            w.Write(0); // cloth mappings
            w.Write((short)-1); w.Write(Guid.Empty); w.Write(-1); // clothing reference
            w.Write(0); // overlapping vertices
            w.Write(0); w.Write(-1); w.Write(i); w.Write(-1); // disabled, LOD, original and parent section
            baseVertex += (uint)s.Vertices.Length;
            baseIndex += (uint)s.Indices.Length;
        }
        w.Write(0); // UserSectionsData
        w.Write((int)baseIndex);
        uint offset = 0;
        foreach (var s in mesh.Sections)
        {
            foreach (var index in s.Indices) w.Write(index + offset);
            offset += (uint)s.Vertices.Length;
        }
        BoneIndices(w, mesh.Bones.Length); // active bones
        w.Write(0); // ImportedMeshInfos
        w.Write(0u); w.Write(baseVertex); // legacy size / NumVertices
        BoneIndices(w, mesh.Bones.Length); // required bones
        w.Write((int)baseVertex);
        for (var i = 0; i < baseVertex; i++) w.Write(i); // RawPointIndices2
        w.Write(new FString("")); w.Write(0); w.Write(1); // raw import data ID / available / empty
        w.Write((int)baseVertex);
        for (var i = 0; i < baseVertex; i++) w.Write(i);
        w.Write((int)baseVertex - 1); // maximum import vertex
        w.Write((uint)Math.Max(1, mesh.Sections.SelectMany(s => s.Vertices).Max(v => v.TexCoords.Length)));
        w.Write(0); // skin weight profiles
    }

    private static void BoneIndices(AssetBinaryWriter w, int count)
    {
        w.Write(count);
        for (var i = 0; i < count; i++) w.Write((ushort)i);
    }

    private static void Vector(AssetBinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    private static void Vertex(AssetBinaryWriter w, AssetVertex v)
    {
        Vector(w, v.Position);
        var tangent = new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z);
        Vector(w, tangent);
        Vector(w, Vector3.Cross(v.Normal, tangent) * v.Tangent.W);
        Vector(w, v.Normal); w.Write(v.Tangent.W);
        for (var i = 0; i < 4; i++) { var uv = i < v.TexCoords.Length ? v.TexCoords[i] : Vector2.Zero; w.Write(uv.X); w.Write(uv.Y); }
        w.Write(v.Color);
        for (var i = 0; i < 12; i++) w.Write((ushort)(i < v.Bones.Length ? v.Bones[i] : 0));
        var total = (double)v.Weights.Sum();
        var weights = v.Weights.Select(x => (int)Math.Floor(x / total * ushort.MaxValue)).ToArray();
        var largest = Array.IndexOf(v.Weights, v.Weights.Max());
        weights[largest] += ushort.MaxValue - weights.Sum();
        for (var i = 0; i < 12; i++) w.Write((ushort)(i < weights.Length ? weights[i] : 0));
    }
}

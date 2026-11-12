using System.Buffers.Binary;
using System.Collections;
using System.Numerics;
using UAssetAPI;
using UAssetAPI.ExportTypes;

namespace Meddle.Unreal;

/// <summary>Writes editable static mesh source geometry; UE builds render data on load.</summary>
public static class StaticMeshWriter
{
    public static void Write(string file, string packagePath, AssetSection[] sections)
    {
        if (sections.Length == 0 || sections.Any(s => s.Vertices.Length == 0 || s.Indices.Length == 0 ||
                s.Indices.Length % 3 != 0 || s.Indices.Any(i => i >= s.Vertices.Length)))
            throw new ArgumentException("Each section requires vertices and valid triangle indices.");
        var vertices = sections.SelectMany(s => s.Vertices).ToArray();
        foreach (var v in vertices)
        {
            if (v.TexCoords.Length > 4) throw new ArgumentException("At most four UV channels are supported.");
            AssetValidation.Transform(new(v.Position, Quaternion.Identity, v.Normal));
        }
        var p = new AssetPackage(packagePath);
        // Select UE's supported RawMesh source encoding. PostLoad converts it to
        // MeshDescription, which is saved in the current format on the next save.
        p.Asset.CustomVersionContainer.Single(v => v.FriendlyName == "FEditorObjectVersion").Version = 27;
        var raw = p.Bytes(w => WriteRawMesh(w, sections));
        long bulkOffset = 0;
        var native = p.Bytes(w =>
        {
            w.Write((ushort)0); w.Write(0); // strip flags / not cooked
            w.Write(0); w.Write(0); // body setup / nav collision
            w.Write(Guid.NewGuid()); w.Write(0); // lighting GUID / sockets
            w.Write((uint)((1 << 6) | (1 << 16))); // inline bulk, absolute offset
            w.Write(raw.Length); w.Write(raw.Length);
            bulkOffset = w.BaseStream.Position;
            w.Write(0L); w.Write(raw);
            w.Write(Guid.NewGuid()); w.Write(0); // source GUID / is hash
            w.Write(0); // no SpeedTree wind
            w.Write(sections.Length);
            for (var i = 0; i < sections.Length; i++)
            {
                var section = sections[i];
                w.Write(section.MaterialPath == null ? 0 : p.Reference(section.MaterialPath, "Material"));
                // Unique imported slots keep sections distinct even with repeated material names.
                var slot = p.NameOf($"{section.MaterialSlot}_{i}");
                w.Write(slot); w.Write(slot);
                w.Write(0); w.Write(0); // UV channel initialized / override
                for (var uv = 0; uv < 4; uv++) w.Write(0f);
                w.Write(0); // overlay material
            }
        });
        p.AddObject(p.Name, "StaticMesh", w =>
        {
            p.Property(w, "SourceModels", p.Bytes(b =>
            {
                b.Write(1);
                p.Property(b, "BuildSettings", p.Bytes(settings =>
                {
                    p.BoolProperty(settings, "bRecomputeNormals", false);
                    p.BoolProperty(settings, "bRecomputeTangents", false);
                    p.BoolProperty(settings, "bGenerateLightmapUVs", false);
                    p.BoolProperty(settings, "bUseFullPrecisionUVs", true);
                    p.End(settings);
                }), ("StructProperty", 1), ("MeshBuildSettings", 1), ("/Script/Engine", 0));
                p.End(b);
            }), ("ArrayProperty", 1), ("StructProperty", 1), ("StaticMeshSourceModel", 1), ("/Script/Engine", 0));
        }, native);
        p.Asset.ImportBits = new BitArray(p.Asset.Imports.Count, true);
        using (p.Asset.WriteData()) { }
        var export = (RawExport)p.Asset.Exports[0];
        var offset = checked((int)(export.ScriptSerializationEndOffset + 4 + bulkOffset));
        BinaryPrimitives.WriteInt64LittleEndian(export.Data.AsSpan(offset, 8), export.SerialOffset + offset + 8);
        p.Save(file);
    }

    private static void WriteRawMesh(AssetBinaryWriter w, AssetSection[] sections)
    {
        var vertices = sections.SelectMany(s => s.Vertices).ToArray();
        var wedges = sections.SelectMany(s => s.Indices.Select(i => s.Vertices[i])).ToArray();
        var faceCount = wedges.Length / 3;
        w.Write(1); w.Write(0); // RawMesh version / licensee version
        w.Write(faceCount);
        for (var i = 0; i < sections.Length; i++)
            for (var face = 0; face < sections[i].Indices.Length / 3; face++) w.Write(i);
        w.Write(faceCount);
        for (var face = 0; face < faceCount; face++) w.Write(1u);
        w.Write(vertices.Length);
        foreach (var v in vertices) Vector(w, v.Position);
        w.Write(wedges.Length);
        uint offset = 0;
        foreach (var section in sections)
        {
            foreach (var i in section.Indices) w.Write(i + offset);
            offset += (uint)section.Vertices.Length;
        }
        w.Write(wedges.Length);
        foreach (var v in wedges) Vector(w, new(v.Tangent.X, v.Tangent.Y, v.Tangent.Z));
        w.Write(wedges.Length);
        foreach (var v in wedges) Vector(w, Vector3.Cross(v.Normal, new(v.Tangent.X, v.Tangent.Y, v.Tangent.Z)) * v.Tangent.W);
        w.Write(wedges.Length);
        foreach (var v in wedges) Vector(w, v.Normal);
        var uvCount = Math.Max(1, vertices.Max(v => v.TexCoords.Length));
        for (var channel = 0; channel < 8; channel++)
        {
            w.Write(channel < uvCount ? wedges.Length : 0);
            if (channel >= uvCount) continue;
            foreach (var v in wedges)
            {
                var uv = channel < v.TexCoords.Length ? v.TexCoords[channel] : Vector2.Zero;
                w.Write(uv.X); w.Write(uv.Y);
            }
        }
        w.Write(wedges.Length);
        foreach (var v in wedges) w.Write(v.Color);
        w.Write(0); // no material index remapping
    }

    private static void Vector(AssetBinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
}

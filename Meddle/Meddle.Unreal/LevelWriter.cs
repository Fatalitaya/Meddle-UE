using System.Numerics;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace Meddle.Unreal;

public sealed record LevelMeshInstances(string Name, string MeshPath, Matrix4x4[] Transforms, string[] Materials);
public sealed record LevelActor(string Name, Vector3 Origin, LevelMeshInstances[] Components);

/// <summary>Editable non-partitioned UE map. All input transforms are in UE centimetres.</summary>
public static class LevelWriter
{
    public static void Write(string file, string packagePath, LevelActor[] actors)
    {
        var p = new AssetPackage(packagePath);
        p.Asset.PackageFlags |= EPackageFlags.PKG_ContainsMap;
        p.Asset.CustomVersionContainer.Add(new CustomVersion(UAPUtils.GUID(0xB02B49B5, 0xBB2044E9, 0xA30432B7, 0x52E40360), 1) { IsSerialized = true });
        var actorIndices = new List<int> { 3 };
        var next = 4;
        foreach (var actor in actors) { actorIndices.Add(next); next += 2 + actor.Components.Length; }
        p.AddObject(p.Name, "World", _ => { }, p.Bytes(w => { w.Write(2); w.Write(0); w.Write(0); }));
        p.AddObject("PersistentLevel", "Level", w =>
        {
            p.ObjectProperty(w, "WorldSettings", 3);
            p.BoolProperty(w, "bUseExternalActors", false);
            p.BoolProperty(w, "bUseActorFolders", false);
        }, p.Bytes(w =>
        {
            w.Write(actorIndices.Count); foreach (var index in actorIndices) w.Write(index);
            foreach (var value in new[] { "unreal", "", packagePath, "" }) w.Write(new FString(value));
            w.Write(0); w.Write(7777); w.Write(1); // URL options / port / valid
            w.Write(next); // empty BSP model required by the editor
            for (var i = 0; i < 5; i++) w.Write(0); // model components, scripts, nav list
            w.Write(0d); w.Write(0d); // visibility bucket origin
            w.Write(0f); w.Write(0f); w.Write(0); w.Write(0); w.Write(0);
            w.Write(0f); // volume max distance
            for (var i = 0; i < 6; i++) w.Write(0d); // volume box
            w.Write((byte)0);
            for (var i = 0; i < 4; i++) w.Write(0); // volume dimensions / data
        }), outer: 1);
        p.AddObject("WorldSettings", "WorldSettings", _ => { }, [], outer: 2);
        for (var i = 0; i < actors.Length; i++)
        {
            var actor = actors[i];
            var actorIndex = actorIndices[i + 1];
            var rootIndex = actorIndex + 1;
            var components = Enumerable.Range(rootIndex, actor.Components.Length + 1).ToArray();
            p.AddObject($"Group_{i}", "Actor", w =>
            {
                p.Property(w, "ActorLabel", p.Bytes(b => b.Write(new FString(actor.Name))), ("StrProperty", 0));
                p.ObjectProperty(w, "RootComponent", rootIndex);
                Objects(w, "InstanceComponents", components);
            }, [], outer: 2);
            p.AddObject("SceneRoot", "SceneComponent", w =>
            {
                Component(w);
                VectorProperty(w, "RelativeLocation", actor.Origin);
                Objects(w, "AttachChildren", components.Skip(1).ToArray());
            }, [], outer: actorIndex);
            for (var c = 0; c < actor.Components.Length; c++)
            {
                var component = actor.Components[c];
                p.AddObject($"ISM_{c}_{AssetNames.Sanitize(component.Name)}", "InstancedStaticMeshComponent", w =>
                {
                    Component(w);
                    p.ObjectProperty(w, "AttachParent", rootIndex);
                    p.ObjectProperty(w, "StaticMesh", p.Reference(component.MeshPath, "StaticMesh"));
                    Objects(w, "OverrideMaterials", component.Materials.Select(m => p.Reference(m, "Material")).ToArray());
                    // Negative determinant instances use a separate component for correct culling.
                    p.BoolProperty(w, "bReverseCulling", component.Transforms.FirstOrDefault().GetDeterminant() < 0);
                }, p.Bytes(w =>
                {
                    w.Write(0); // StaticMeshComponent LODData
                    w.Write(0); // mesh paint data is not cooked
                    w.Write(0); w.Write(1); // not cooked / per-instance data present
                    w.Write(128); w.Write(component.Transforms.Length); // bulk matrix size / count
                    foreach (var transform in component.Transforms) Matrix(w, transform);
                    w.Write(4); w.Write(0); // custom floats, empty
                }), outer: actorIndex);
            }
        }
        p.AddObject("Model", "Model", _ => { }, p.Bytes(w =>
        {
            w.Write((ushort)0);
            for (var i = 0; i < 7; i++) w.Write(0d); // bounds
            foreach (var size in new[] { 12, 12, 64 }) { w.Write(size); w.Write(0); } // vectors, points, nodes
            w.Write(0); w.Write(24); w.Write(0); // surfaces / vertices
            w.Write(0); w.Write(0); // shared sides / polys
            w.Write(4); w.Write(0); w.Write(4); w.Write(0); // leaf hulls / leaves
            w.Write(1); w.Write(0); w.Write(0); // root outside / linked / unique vertices
            w.Write(0); // vertex buffer
            w.Write(Guid.NewGuid()); w.Write(0); // lighting guid / lightmass settings
        }), outer: 2);
        p.Save(file);

        void Objects(AssetBinaryWriter w, string name, int[] values) => p.Property(w, name,
            p.Bytes(b => { b.Write(values.Length); foreach (var value in values) b.Write(value); }),
            ("ArrayProperty", 1), ("ObjectProperty", 0));
        void Component(AssetBinaryWriter w)
        {
            p.EnumProperty(w, "Mobility", "EComponentMobility", "Static");
            p.EnumProperty(w, "CreationMethod", "EComponentCreationMethod", "Instance");
        }
        void VectorProperty(AssetBinaryWriter w, string name, Vector3 value) => p.NativeProperty(w, name,
            p.Bytes(b => { b.Write((double)value.X); b.Write((double)value.Y); b.Write((double)value.Z); }),
            ("StructProperty", 1), ("Vector", 1), ("/Script/CoreUObject", 0));
    }

    private static void Matrix(AssetBinaryWriter w, Matrix4x4 m)
    {
        foreach (var v in new[] { m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
                     m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44 }) w.Write((double)v);
    }
}

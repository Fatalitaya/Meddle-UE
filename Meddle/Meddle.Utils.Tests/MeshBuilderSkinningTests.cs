using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meddle.Utils.Export;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class MeshBuilderSkinningTests
{
    [TestMethod]
    public void NullBoneMap_ProducesNonSkinnedVertexLayout()
    {
        var builder = BuildMeshBuilder(boneMap: null);

        Assert.IsFalse(MeshBuilder.HasSkinnedBoneMap(null));
        Assert.AreEqual(typeof(VertexEmpty), builder.SkinningT);
    }

    [TestMethod]
    public void EmptyBoneMap_ProducesNonSkinnedVertexLayout()
    {
        var empty = Array.Empty<BoneNodeBuilder>();
        var builder = BuildMeshBuilder(boneMap: empty);

        Assert.IsFalse(MeshBuilder.HasSkinnedBoneMap(empty));
        Assert.AreEqual(typeof(VertexEmpty), builder.SkinningT);
    }

    [TestMethod]
    public void NonEmptyBoneMap_ProducesSkinnedVertexLayout()
    {
        var boneMap = new[] { new BoneNodeBuilder("n_root") };
        var builder = BuildMeshBuilder(boneMap: boneMap);

        Assert.IsTrue(MeshBuilder.HasSkinnedBoneMap(boneMap));
        Assert.AreEqual(typeof(VertexJoints4), builder.SkinningT);
    }

    private static MeshBuilder BuildMeshBuilder(IReadOnlyList<BoneNodeBuilder>? boneMap)
    {
        var vertex = new Vertex
        {
            Position = new Vector3(0, 0, 0),
            BlendIndices = [0, 0, 0, 0],
            BlendWeights = [1, 0, 0, 0]
        };

        var mesh = CreateMesh(
            new[] { vertex },
            boneMap is { Count: > 0 } ? new[] { boneMap[0].BoneName } : null);

        return new MeshBuilder(mesh, boneMap, new MaterialBuilder("test"), raceDeformer: null);
    }

    private static Mesh CreateMesh(IReadOnlyList<Vertex> vertices, IReadOnlyList<string>? boneTable)
    {
        var mesh = (Mesh)RuntimeHelpers.GetUninitializedObject(typeof(Mesh));

        SetBackingField(mesh, "Vertices", vertices);
        SetBackingField(mesh, "BoneTable", boneTable);

        return mesh;
    }

    private static void SetBackingField<T>(object instance, string propertyName, T? value)
    {
        var field = instance.GetType().GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Missing backing field for {propertyName}");
        field!.SetValue(instance, value);
    }
}

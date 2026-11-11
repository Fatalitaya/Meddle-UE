using System.Numerics;
using System.Text.Json.Nodes;
using Meddle.Unreal;
using Meddle.Utils.Export.Ue;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class NativeMeshOrientationTests
{
    [TestMethod]
    [DataRow(1f)]
    [DataRow(-1f)]
    public void HairNormalInStoredBinormalFrameMatchesGameWorldNormal(float sign)
    {
        var rotation=Matrix4x4.CreateRotationY(.7f)*Matrix4x4.CreateRotationX(.3f);
        var normal=Vector3.TransformNormal(Vector3.UnitZ,rotation);
        var binormal=Vector3.TransformNormal(Vector3.UnitY,rotation);
        // hair VS2: tangent = cross(authored binormal, normal) * sign.
        var tangent=Vector3.Cross(binormal,normal)*sign;
        var sampled=Vector3.Normalize(new Vector3(.3f,-.2f,.8f));
        var expected=Coordinates.Direction(tangent*sampled.X+binormal*sampled.Y+normal*sampled.Z);
        var ueNormal=Coordinates.Direction(normal);
        var ueTangent=Coordinates.Direction(binormal);
        var ueBitangent=Vector3.Cross(ueNormal,ueTangent)*NativeAssetExporter.ConvertTangentSign(sign,false);
        var actual=ueTangent*sampled.Y-ueBitangent*sampled.X+ueNormal*sampled.Z;
        Assert.IsTrue(Vector3.Distance(expected,actual)<1e-5f);
    }

    [TestMethod]
    [DataRow(1f)]
    [DataRow(-1f)]
    public void ExportedWindingAndTangentFrameMatchNormals(float scaleX)
    {
        var mesh = new MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>();
        mesh.UsePrimitive(new MaterialBuilder("Test")).AddTriangle(
            new VertexPositionNormal(Vector3.Zero, Vector3.UnitZ),
            new VertexPositionNormal(Vector3.UnitX, Vector3.UnitZ),
            new VertexPositionNormal(Vector3.UnitY, Vector3.UnitZ));
        var scene = new SceneBuilder();
        scene.AddRigidMesh(mesh, Matrix4x4.Identity);
        var primitive = scene.ToGltf2().LogicalMeshes[0].Primitives[0];
        var world = Matrix4x4.CreateScale(scaleX, 2, 3) * Matrix4x4.CreateRotationY(.4f);
        var mirrored = world.GetDeterminant() < 0;
        var indices = NativeAssetExporter.TriangleIndices(primitive, mirrored);
        var positions = primitive.GetVertexAccessor("POSITION").AsVector3Array()
            .Select(p => Coordinates.Position(Vector3.Transform(p, world))).ToArray();
        Matrix4x4.Invert(world, out var inverse);
        var normal = Vector3.Normalize(Coordinates.Direction(Vector3.TransformNormal(Vector3.UnitZ, Matrix4x4.Transpose(inverse))));
        // UE VectorUtil::Normal uses (C-A) cross (B-A) for clockwise front faces.
        var face = Vector3.Normalize(Vector3.Cross(positions[indices[2]] - positions[indices[0]], positions[indices[1]] - positions[indices[0]]));
        Assert.IsTrue(Vector3.Dot(face, normal) > .9999f);

        var tangent = Vector3.Normalize(Coordinates.Direction(Vector3.TransformNormal(Vector3.UnitX, world)));
        var expectedBitangent = Vector3.Normalize(Coordinates.Direction(Vector3.TransformNormal(Vector3.UnitY, world)));
        var bitangent = Vector3.Cross(normal, tangent) * NativeAssetExporter.ConvertTangentSign(1, mirrored);
        Assert.IsTrue(Vector3.Dot(bitangent, expectedBitangent) > .9999f);
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public void TwoSidedPreservesEitherSourceRequest(bool gltf, bool backfaces, bool expected)
    {
        var material = ModelRoot.CreateModel().CreateMaterial("ThinSurface");
        material.DoubleSided = gltf;
        material.Extras = new JsonObject { ["RenderBackfaces"] = backfaces };
        Assert.AreEqual(expected, NativeAssetExporter.IsTwoSided(material));
        material.Extras = null;
        Assert.AreEqual(gltf, NativeAssetExporter.IsTwoSided(material));
    }
}

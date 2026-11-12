using System.Numerics;
using System.Text.Json.Nodes;
using Meddle.Unreal;
using Meddle.Utils.Export.Ue;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeMeshGroupingTests
{
    [TestMethod]
    public void NamesPreserveUnicodeAndRemovePathCharacters()
    {
        Assert.AreEqual("已屏蔽角色",NativeAssetExporter.SafeName("已屏蔽角色"));
        Assert.AreEqual("角色_头发",NativeAssetExporter.SafeName("角色/头发"));
        Assert.AreEqual("Character",NativeAssetExporter.SafeName(" /:*? "));
        Assert.AreEqual("é",NativeAssetExporter.SafeName("e\u0301"));
    }

    [TestMethod]
    public void RigidInstancesExportWithoutSkinsAndRetainEveryOccurrence()
    {
        var scene = new SceneBuilder();
        var mesh = new MeshBuilder<VertexPositionNormal,VertexTexture1,VertexEmpty>("家具部件");
        mesh.Extras = new JsonObject { ["modelPart"] = "座面" };
        mesh.UsePrimitive(new MaterialBuilder("Wood")).AddTriangle(
            (new VertexPositionNormal(Vector3.Zero,Vector3.UnitZ),new VertexTexture1(Vector2.Zero)),
            (new VertexPositionNormal(Vector3.UnitX,Vector3.UnitZ),new VertexTexture1(Vector2.UnitX)),
            (new VertexPositionNormal(Vector3.UnitY,Vector3.UnitZ),new VertexTexture1(Vector2.UnitY)));
        var root = new NodeBuilder("Furniture");
        for (var i=0;i<3;i++)
        {
            var child = root.CreateNode("Part"+i);
            child.LocalTransform = new SharpGLTF.Transforms.AffineTransform(Matrix4x4.CreateTranslation(i*2,0,0));
            scene.AddRigidMesh(mesh,child);
        }
        var model = scene.ToGltf2();
        Assert.AreEqual(0,model.LogicalSkins.Count);
        var dir = Path.Combine(Path.GetTempPath(),"MeddleRigid_"+Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var mode in Enum.GetValues<UeMeshMergeMode>())
            {
                var groups = UeMeshGrouping.Build(model,mode);
                Assert.AreEqual(3,groups.Sum(g=>g.Nodes.Length));
                var output = Path.Combine(dir,mode.ToString());
                NativeAssetExporter.Export(model,output,"家具",mergeMode:mode);
                var files = Directory.GetFiles(Path.Combine(output,"Content/Meddle/家具/Meshes"),"*.uasset");
                Assert.AreEqual(mode==UeMeshMergeMode.None ? 3 : 1,files.Length);
                Assert.AreEqual(0, Directory.GetFiles(output, "*Skeleton*.uasset", SearchOption.AllDirectories).Length);
                foreach (var file in files)
                {
                    var asset = new UAsset(file,EngineVersion.VER_UE5_8);
                    Assert.IsTrue(Path.GetFileName(file).StartsWith("SM_"));
                    Assert.IsTrue(asset.Imports.Any(i => i.ObjectName.ToString() == "StaticMesh"));
                    Assert.IsFalse(asset.Imports.Any(i => i.ObjectName.ToString() is "Skeleton" or "SkeletalMesh"));
                    Assert.AreEqual(Path.GetFileNameWithoutExtension(file),asset.Exports[0].ObjectName.ToString());
                }
            }
        }
        finally { if(Directory.Exists(dir))Directory.Delete(dir,true); }
    }

    [TestMethod]
    public void MixedSceneKeepsStaticAndSkinnedAssetsSeparateInEveryMergeMode()
    {
        var scene = new SceneBuilder();
        var mesh = new MeshBuilder<VertexPositionNormal,VertexTexture1,VertexJoints4>("SharedName");
        mesh.UsePrimitive(new MaterialBuilder("Material")).AddTriangle(
            (new VertexPositionNormal(Vector3.Zero,Vector3.UnitZ),new VertexTexture1(Vector2.Zero),new VertexJoints4(0)),
            (new VertexPositionNormal(Vector3.UnitX,Vector3.UnitZ),new VertexTexture1(Vector2.UnitX),new VertexJoints4(0)),
            (new VertexPositionNormal(Vector3.UnitY,Vector3.UnitZ),new VertexTexture1(Vector2.UnitY),new VertexJoints4(0)));
        var root = new NodeBuilder("root");
        scene.AddSkinnedMesh(mesh, Matrix4x4.Identity, root);
        var model = scene.ToGltf2();
        // Same source mesh and part name, but only the character occurrence has a skin.
        var rigid = model.DefaultScene.CreateNode("Scenery");
        rigid.Mesh = model.LogicalMeshes[0];
        rigid.LocalMatrix = Matrix4x4.CreateTranslation(10, 0, 0);
        var dir = Path.Combine(Path.GetTempPath(), "MeddleMixed_" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var mode in Enum.GetValues<UeMeshMergeMode>())
            {
                var groups = UeMeshGrouping.Build(model, mode);
                Assert.AreEqual(2, groups.Length);
                Assert.IsTrue(groups.All(g => g.Nodes.All(n => n.Skin != null) || g.Nodes.All(n => n.Skin == null)));
                var output = Path.Combine(dir, mode.ToString());
                NativeAssetExporter.Export(model, output, "Mixed", mergeMode: mode);
                var meshes = Directory.GetFiles(Path.Combine(output, "Content/Meddle/Mixed/Meshes"), "*.uasset");
                Assert.AreEqual(2, meshes.Length);
                Assert.AreEqual(1, meshes.Count(f => Path.GetFileName(f).StartsWith("SM_")));
                Assert.AreEqual(1, meshes.Count(f => Path.GetFileName(f).StartsWith("SK_")));
            }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void GranularityChangesMeshCountAndNamesWithoutLosingParts()
    {
        var scene=new SceneBuilder();
        var body=new NodeBuilder("root"); var weapon=new NodeBuilder("root");
        scene.AddNode(body);scene.AddNode(weapon);
        for(var i=0;i<4;i++)
        {
            var mesh=new MeshBuilder<VertexPositionNormal,VertexTexture1,VertexJoints4>(i==3 ? "武器" : "原始模型");
            mesh.Extras=new JsonObject { ["modelPart"]=i<2 ? "Top" : i==2 ? "Face" : "Weapon", ["modelOwner"]=i==3 ? "1" : "0" };
            var p=mesh.UsePrimitive(new MaterialBuilder("Material"+i));
            p.AddTriangle((new VertexPositionNormal(Vector3.Zero,Vector3.UnitZ),new VertexTexture1(Vector2.Zero),new VertexJoints4(0)),
                (new VertexPositionNormal(Vector3.UnitX,Vector3.UnitZ),new VertexTexture1(Vector2.UnitX),new VertexJoints4(0)),
                (new VertexPositionNormal(Vector3.UnitY,Vector3.UnitZ),new VertexTexture1(Vector2.UnitY),new VertexJoints4(0)));
            scene.AddSkinnedMesh(mesh,Matrix4x4.Identity,i==3 ? weapon : body);
        }
        var model=scene.ToGltf2();
        Assert.AreEqual(1,UeMeshGrouping.Build(model,UeMeshMergeMode.All).Length);
        var parts=UeMeshGrouping.Build(model,UeMeshMergeMode.ByPart);
        Assert.AreEqual(3,parts.Length);
        Assert.AreEqual(2,parts.Single(g=>g.Name=="Top").Nodes.Length);
        var separate=UeMeshGrouping.Build(model,UeMeshMergeMode.None);
        Assert.AreEqual(4,separate.Length);
        Assert.AreEqual(4,separate.Select(g=>g.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var dir=Path.Combine(Path.GetTempPath(),"MeddleGrouping_"+Guid.NewGuid().ToString("N"));
        try
        {
            foreach(var mode in Enum.GetValues<UeMeshMergeMode>())
            {
                var output=Path.Combine(dir,mode.ToString());
                NativeAssetExporter.Export(model,output,"测试角色",mergeMode:mode);
                var meshes=Directory.GetFiles(Path.Combine(output,"Content/Meddle/测试角色/Meshes"),"*.uasset");
                Assert.AreEqual(mode==UeMeshMergeMode.All ? 1 : mode==UeMeshMergeMode.ByPart ? 3 : 4,meshes.Length);
                Assert.AreEqual(mode==UeMeshMergeMode.All,meshes.Any(p=>Path.GetFileName(p)=="SK_测试角色.uasset"));
                foreach(var file in meshes)
                {
                    var asset=new UAsset(file,EngineVersion.VER_UE5_8);
                    Assert.AreEqual(Path.GetFileNameWithoutExtension(file),asset.Exports[0].ObjectName.ToString());
                    Assert.IsTrue(asset.Imports.Any(i=>i.ObjectName.ToString().Contains("/Game/Meddle/测试角色/Skeletons/")));
                }
            }
        }
        finally { if(Directory.Exists(dir))Directory.Delete(dir,true); }
    }
}

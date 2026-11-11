using System.Numerics;
using Meddle.Unreal;
using Meddle.Utils.Export.Ue;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

if (args.Length != 1) throw new ArgumentException("Pass an empty validation project directory.");
var project = Path.GetFullPath(args[0]);
Directory.CreateDirectory(project);
File.WriteAllText(Path.Combine(project, "Probe.uproject"), """
{"FileVersion":3,"Plugins":[{"Name":"PythonScriptPlugin","Enabled":true},{"Name":"EditorScriptingUtilities","Enabled":true}]}
""");
var folder = Path.Combine(project, "Content", "Meddle", "Probe"); Directory.CreateDirectory(folder);
const string package = "/Game/Meddle/Probe/";
string FileFor(string name) => Path.Combine(folder, name + ".uasset");
AssetBone[] bones = [new("root", -1, BoneTransform.Identity), new("child", 0, new(new(0, 0, 25), Quaternion.Identity, Vector3.One))];
var guid = SkeletonWriter.Write(FileFor("Skeleton"), package + "Skeleton", bones);
TextureWriter.Write(FileFor("Texture"), package + "Texture", 2, 2, [0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255], true);
MaterialWriter.Write(FileFor("Material"), package + "Material", new(Vector4.One, 0, .6f, package + "Texture"));
AssetVertex V(float x, float y) => new(new(x,y,0), Vector3.UnitZ, new(1,0,0,1), [new(x/100,y/100)], uint.MaxValue, [0,1], [.25f,.75f]);
SkeletalMeshWriter.Write(FileFor("Mesh"), package + "Mesh", package + "Skeleton",
    new(bones, [new("Body", package + "Material", [V(0,0),V(100,0),V(0,100)], [0,1,2])],
        [new("Raise", [new(2, new(0,0,10), Vector3.Zero)], [0])]));
foreach (var fps in new[] {30,60})
{
    var keys = Enumerable.Range(0, fps+1).Select(i => new BoneTransform(new(0,0,i*100f/fps), Quaternion.Identity, Vector3.One)).ToArray();
    AnimationWriter.Write(FileFor("Animation"+fps), package + "Animation"+fps, package + "Skeleton", guid, bones,
        new(fps,fps,[new(0,keys),new(1,Enumerable.Repeat(bones[1].ReferencePose,fps+1).ToArray())]));
}
var scene = new SceneBuilder();
var root = new NodeBuilder("root");
root.UseTranslation().UseTrackBuilder("Move").WithPoint(0, Vector3.Zero).WithPoint(1, Vector3.UnitY);
scene.AddNode(root);
var mesh = new MeshBuilder<VertexPositionNormal,VertexTexture1,VertexJoints4>("Body");
var primitive = mesh.UsePrimitive(new MaterialBuilder("Body"));
primitive.AddTriangle((new VertexPositionNormal(Vector3.Zero, Vector3.UnitZ),new VertexTexture1(Vector2.Zero),new VertexJoints4(0)),
    (new VertexPositionNormal(Vector3.UnitX, Vector3.UnitZ),new VertexTexture1(Vector2.UnitX),new VertexJoints4(0)),
    (new VertexPositionNormal(Vector3.UnitY, Vector3.UnitZ),new VertexTexture1(Vector2.UnitY),new VertexJoints4(0)));
scene.AddSkinnedMesh(mesh, Matrix4x4.Identity, root);
var model = scene.ToGltf2();
Console.WriteLine($"Adapter: {model.LogicalSkins.Count} skins, {model.LogicalNodes.Count} nodes, {model.LogicalAnimations.Count} animations");
NativeAssetExporter.Export(model, project, "Adapter");
NativeAssetExporter.Export(model, project, "RootMotion", inPlace: false);
Console.WriteLine(project);

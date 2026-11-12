using System.Numerics;
using Meddle.Unreal;
using Meddle.Utils.Export.Ue;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

if (args.Length is < 1 or > 2) throw new ArgumentException("Pass an empty validation project directory.");
var project = Path.GetFullPath(args[0]);
Directory.CreateDirectory(project);
File.WriteAllText(Path.Combine(project, "Probe.uproject"), """
{"FileVersion":3,"Plugins":[{"Name":"PythonScriptPlugin","Enabled":true},{"Name":"EditorScriptingUtilities","Enabled":true},{"Name":"GeometryScripting","Enabled":true}]}
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
    new(bones, [new("Body", package + "Material", [V(0,0),V(100,0),V(0,100)], [0,2,1])],
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
model.LogicalMaterials[0].DoubleSided = true;
model.LogicalMaterials[0].Extras = new System.Text.Json.Nodes.JsonObject { ["RenderBackfaces"] = false };
Console.WriteLine($"Adapter: {model.LogicalSkins.Count} skins, {model.LogicalNodes.Count} nodes, {model.LogicalAnimations.Count} animations");
NativeAssetExporter.Export(model, project, "Adapter");
NativeAssetExporter.Export(model, project, "RootMotion", inPlace: false);
var mirrorParent = model.DefaultScene.CreateNode("MirrorParent");
mirrorParent.LocalMatrix = Matrix4x4.CreateScale(-1, 2, 3);
foreach (var node in model.LogicalNodes.Where(n => n.Mesh != null).ToArray())
{
    var child = mirrorParent.CreateNode("MirroredMesh");
    child.Mesh = node.Mesh;
    child.Skin = node.Skin;
    node.Skin = null;
    node.Mesh = null;
}
NativeAssetExporter.Export(model, project, "Mirrored");
Console.WriteLine(project);

// Rigid instances must preserve placement even when the same mesh is instanced.
var rigidScene = new SceneBuilder();
var rigidRoot = new NodeBuilder("Furniture") { LocalTransform = new SharpGLTF.Transforms.AffineTransform(Matrix4x4.CreateTranslation(10,0,0)) };
var rigidMesh = new MeshBuilder<VertexPositionNormal,VertexTexture1,VertexEmpty>("Chair");
rigidMesh.Extras = new System.Text.Json.Nodes.JsonObject { ["modelPart"] = "Seat" };
rigidMesh.UsePrimitive(new MaterialBuilder("Wood")).AddTriangle(
    (new VertexPositionNormal(Vector3.Zero,Vector3.UnitZ),new VertexTexture1(Vector2.Zero)),
    (new VertexPositionNormal(Vector3.UnitX,Vector3.UnitZ),new VertexTexture1(Vector2.UnitX)),
    (new VertexPositionNormal(Vector3.UnitY,Vector3.UnitZ),new VertexTexture1(Vector2.UnitY)));
for (var i=0;i<3;i++)
{
    var rigidNode = rigidRoot.CreateNode("Part"+i);
    rigidNode.LocalTransform = new SharpGLTF.Transforms.AffineTransform(Matrix4x4.CreateScale(i==2 ? -1 : 1,2,1)*Matrix4x4.CreateTranslation(2+i*2,0,0));
    rigidScene.AddRigidMesh(rigidMesh,rigidNode);
}
foreach (var mode in Enum.GetValues<UeMeshMergeMode>())
    NativeAssetExporter.Export(rigidScene.ToGltf2(),project,"Furniture"+mode,mergeMode:mode);

// Multiple source skins, a separately positioned attachment with a duplicate bone
// name, and a shared named morph exercise all three UE mesh grouping modes.
var groupedScene = new SceneBuilder();
var bodyRoot = new NodeBuilder("root");
var attachmentRoot = new NodeBuilder("root") { LocalTransform = new SharpGLTF.Transforms.AffineTransform(Matrix4x4.CreateTranslation(6,0,0)) };
groupedScene.AddNode(bodyRoot); groupedScene.AddNode(attachmentRoot);
for(var i=0;i<4;i++)
{
    var source=new MeshBuilder<VertexPositionNormal,VertexTexture1,VertexJoints4>(i==3 ? "武器" : "原始模型");
    source.Extras=new System.Text.Json.Nodes.JsonObject {
        ["modelPart"]=i<2 ? "Top" : i==2 ? "Face" : "Weapon", ["modelOwner"]=i==3 ? "1" : "0",
        ["targetNames"]=new System.Text.Json.Nodes.JsonArray("形态") };
    var p=source.UsePrimitive(new MaterialBuilder("材质"+i));
    var origin=i==3 ? Vector3.Zero : new Vector3(i*2,0,0);
    p.AddTriangle((new VertexPositionNormal(origin,Vector3.UnitZ),new VertexTexture1(Vector2.Zero),new VertexJoints4(0)),
        (new VertexPositionNormal(origin+Vector3.UnitX,Vector3.UnitZ),new VertexTexture1(Vector2.UnitX),new VertexJoints4(0)),
        (new VertexPositionNormal(origin+Vector3.UnitY,Vector3.UnitZ),new VertexTexture1(Vector2.UnitY),new VertexJoints4(0)));
    source.UseMorphTarget(0).SetVertex(new VertexPositionNormal(origin,Vector3.UnitZ),new VertexPositionNormal(origin+Vector3.UnitZ*.1f,Vector3.UnitZ));
    groupedScene.AddSkinnedMesh(source,i==3 ? Matrix4x4.CreateTranslation(6,0,0) : Matrix4x4.Identity,i==3 ? attachmentRoot : bodyRoot);
}
var groupedModel=groupedScene.ToGltf2();
foreach(var mode in Enum.GetValues<UeMeshMergeMode>())
    NativeAssetExporter.Export(groupedModel,project,"角色"+mode,mergeMode:mode);
groupedScene.AddRigidMesh(rigidMesh,Matrix4x4.CreateTranslation(8,0,0));
NativeAssetExporter.Export(groupedScene.ToGltf2(),project,"MixedInstances");

// Formula assets use real textures and the same graph builder as production exports.
TextureWriter.Write(FileFor("Linear"), package + "Linear", 2, 2,
    Enumerable.Repeat(new byte[] { 191, 64, 128, 128 },4).SelectMany(x=>x).ToArray(), false, linearData:true);
TextureWriter.WriteFloat(FileFor("FloatTable"), package + "FloatTable", 1, 1, [4f, .1234567f, 65535f, 1f]);
var precisionGraph = new MaterialGraph();
precisionGraph.Outputs["EmissiveColor"] = precisionGraph.Sample(package+"FloatTable",false,precisionGraph.Uv(0)) with { Mask=7 };
MaterialWriter.Write(FileFor("FloatPrecision"),package+"FloatPrecision",new(Vector4.One,0,1,Graph:precisionGraph,Unlit:true));
foreach (var shader in new[] { "hair", "iris", "skin", "character" })
{
    var data = new System.Text.Json.Nodes.JsonObject {
        ["ShaderPackage"] = shader + ".shpk",
        ["g_DiffuseColor"] = new System.Text.Json.Nodes.JsonArray(2f,1f,4f),
        ["MainColor"] = new System.Text.Json.Nodes.JsonArray(2f,.2f,.1f),
        ["MeshColor"] = new System.Text.Json.Nodes.JsonArray(.1f,.3f,3f),
        ["LeftIrisColor"] = new System.Text.Json.Nodes.JsonArray(2f,.1f,.2f,1f),
        ["RightIrisColor"] = new System.Text.Json.Nodes.JsonArray(.1f,.2f,2f,.5f),
        ["g_AlphaThreshold"] = new System.Text.Json.Nodes.JsonArray(.5f)
    };
    var rows = new System.Text.Json.Nodes.JsonArray();
    for (var row=0;row<32;row++) rows.Add(System.Text.Json.Nodes.JsonNode.Parse(
        $$$"""{"Diffuse":{"X":{{{row+1}}}.0,"Y":0.2,"Z":0.3},"Emissive":{"X":0.1,"Y":0.2,"Z":0.3},"Metalness":0.25,"Roughness":0.8,"TileIndex":0.0,"TileAlpha":0.5,"TileMatrix":{"UU":1.0,"VV":1.0}}"""));
    data["ColorTable"] = new System.Text.Json.Nodes.JsonObject { ["ColorTable"] = new System.Text.Json.Nodes.JsonObject { ["Rows"] = rows } };
    var textures = new Dictionary<string,MaterialTexture>();
    foreach (var usage in new[] { "g_SamplerDiffuse", "g_SamplerNormal", "g_SamplerMask", "g_SamplerIndex", "chara_tile_norm_array", "chara_tile_orb_array" })
        textures[usage] = new(usage,package+(usage=="g_SamplerDiffuse" ? "Texture" : "Linear"),usage=="g_SamplerDiffuse");
    var formula=CharacterMaterial.Build(data,textures);
    if (formula.Graph == null) throw new Exception(formula.Gap);
    MaterialWriter.Write(FileFor(shader),package+shader,new(Vector4.One,0,.6f,Graph:formula.Graph,AlphaMode:formula.AlphaMode));
}

if (args.Length == 2)
{
    var cases = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(args[1]))!.AsArray();
    foreach (var item in cases)
    {
        var test = item!.AsObject(); var name = test["name"]!.GetValue<string>();
        var textures = new Dictionary<string, MaterialTexture>();
        foreach (var (usage, value) in test["textures"]!.AsObject())
        {
            var path = package + name + "_" + usage;
            TextureWriter.WriteFloat(FileFor(name+"_"+usage),path,value!["width"]!.GetValue<int>(),value["height"]!.GetValue<int>(),
                value["pixels"]!.AsArray().Select(x=>x!.GetValue<float>()).ToArray(), nearest:usage!="Decal", clamp:usage=="Decal");
            textures[usage] = new(usage,path,false,usage!="Decal",usage=="Decal",value["width"]!.GetValue<int>(),value["height"]!.GetValue<int>());
        }
        var formula = CharacterMaterial.Build(test["metadata"]!.AsObject(),textures);
        var graph = formula.Graph ?? throw new Exception(formula.Gap);
        if (name.StartsWith("lightshaft_shpk_",StringComparison.Ordinal) || name.StartsWith("bg_shpk_", StringComparison.Ordinal) || name.StartsWith("shpk_", StringComparison.Ordinal) || name.StartsWith("hair_shpk_",StringComparison.Ordinal) || name.StartsWith("skin_shpk_",StringComparison.Ordinal))
            MaterialWriter.Write(FileFor(name+"_Full"),package+name+"_Full",
                new(Vector4.One,0,1,Graph:graph,AlphaMode:formula.AlphaMode,Unlit:formula.Unlit,DitherOpacityMask:formula.DitherOpacityMask,TwoSided:name.StartsWith("hair_shpk_",StringComparison.Ordinal)));
        // Inject known UVs and vertex color while retaining production texture samples and HLSL.
        for (var i=0;i<graph.Nodes.Count;i++)
        {
            if (graph.Nodes[i] is MaterialVertexInterpolator interp && test["lightshaftAttenuation"] != null)
            {
                var source=(MaterialCustom)graph.Nodes[interp.Input.Expression-3];
                graph.Nodes[i]=source.Dimensions==2
                    ? new MaterialCustom("Test projected UV","return float2(0.2,0.3);",2,[])
                    : new MaterialCustom("Test VS attenuation",FormattableString.Invariant($"return {test["lightshaftAttenuation"]!.GetValue<float>():R};"),1,[]);
            }
            if (graph.Nodes[i] is MaterialUv uv)
            {
                var values=test[uv.Channel==0 ? "uv0" : "uv1"]!.AsArray().Select(x=>x!.GetValue<float>()).ToArray();
                graph.Nodes[i]=new MaterialCustom("Test UV",FormattableString.Invariant($"return float2({values[0]:R},{values[1]:R});"),2,[]);
            }
            if (graph.Nodes[i] is MaterialVertexColor)
            {
                var values=test["vertex"]!.AsArray().Select(x=>x!.GetValue<float>().ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                graph.Nodes[i]=new MaterialCustom("Test vertex color","return float4("+string.Join(",",values)+");",4,[]);
            }
        }
        foreach (var (attribute, link) in graph.Outputs)
        {
            var proxy=new MaterialGraph();proxy.Nodes.AddRange(graph.Nodes);
            // Signed normals must be encoded for the emissive render target path.
            proxy.Outputs["EmissiveColor"] = attribute=="Normal"
                ? proxy.Custom("Signed probe encoding","return x*0.5+0.5;",3,("x",link)) : link;
            MaterialWriter.Write(FileFor(name+"_"+attribute),package+name+"_"+attribute,
                new(Vector4.One,0,1,Graph:proxy,Unlit:true));
        }
    }
    File.Copy(args[1],Path.Combine(project,"formula_cases.json"),true);
}

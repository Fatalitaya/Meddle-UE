using System.Numerics;
using Meddle.Unreal;

var project = Path.GetFullPath(args[0]);
Directory.CreateDirectory(project);
File.WriteAllText(Path.Combine(project, "Probe.uproject"), """
{"FileVersion":3,"Plugins":[{"Name":"PythonScriptPlugin","Enabled":true},{"Name":"EditorScriptingUtilities","Enabled":true},{"Name":"GeometryScripting","Enabled":true}]}
""");
var folder = Path.Combine(project, "Content/Meddle/Probe");
const string package = "/Game/Meddle/Probe/";
AssetVertex V(float x, float y) => new(new(x,y,0), Vector3.UnitZ, new(1,0,0,1), [new(x/100,y/100)], uint.MaxValue, [], []);
StaticMeshWriter.Write(Path.Combine(folder, "LocalMesh.uasset"), package + "LocalMesh",
    [new("Body", null, [V(0,0), V(100,0), V(0,100)], [0,2,1])]);
MaterialWriter.Write(Path.Combine(folder, "Red.uasset"), package + "Red", new(new Vector4(1,0,0,1), 0, .5f));
MaterialWriter.Write(Path.Combine(folder, "Blue.uasset"), package + "Blue", new(new Vector4(0,0,1,1), 0, .5f));
LevelWriter.Write(Path.Combine(folder, "Territory.umap"), package + "Territory",
    [new("Test group", new(1000,2000,3000), [new("Instances", package + "LocalMesh",
        [Matrix4x4.CreateTranslation(10,20,30), Matrix4x4.CreateScale(2,3,4)*Matrix4x4.CreateRotationZ(.5f)*Matrix4x4.CreateTranslation(50,60,70)], [package + "Red"]),
        new("Mirrored", package + "LocalMesh", [Matrix4x4.CreateScale(-1,1,1)*Matrix4x4.CreateTranslation(-10,0,0)], [package + "Blue"])])],
    [new("Point test", LevelLightType.Point, new(100,200,300), Vector3.UnitX, new(.25f,.5f,1), 20, 1500, 0, 0, true, true),
     new("Spot test", LevelLightType.Spot, new(400,500,600), Vector3.UnitY, Vector3.One, 35, 2500, 20, 40, false, false),
     new("Directional test", LevelLightType.Directional, Vector3.Zero, -Vector3.UnitZ, Vector3.One, 5, 0, 0, 0, true, true)]);

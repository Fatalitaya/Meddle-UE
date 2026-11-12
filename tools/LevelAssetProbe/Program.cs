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
        new("Mirrored", package + "LocalMesh", [Matrix4x4.CreateScale(-1,1,1)*Matrix4x4.CreateTranslation(-10,0,0)], [package + "Blue"])])]);

using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Meddle.Formats.Files;
using Meddle.Utils.Helpers;
using Meddle.Formats.Constants;
using Meddle.Formats.Files.MtrlFile;
using Meddle.Formats.Helpers;
using Meddle.Utils.Export;
using Meddle.Utils.Export.Ue;
using System.Text.Json.Nodes;
using Meddle.Unreal;

if (args.Length == 3 && args[0] is "--hair-graphs" or "--graphs")
{
    var inputs=JsonNode.Parse(File.ReadAllText(args[1]))!.AsArray();
    var graphs=new JsonArray();
    foreach (var input in inputs)
    {
        var textures=input!["Textures"]!.Deserialize<MaterialTexture[]>()!.ToDictionary(t=>t.Usage);
        var formula=CharacterMaterial.Build(input["Metadata"]!.AsObject(),textures);
        var graph=formula.Graph ?? throw new InvalidDataException(formula.Gap);
        graphs.Add(JsonSerializer.SerializeToNode(new {
            Package=input["Package"]!.GetValue<string>(), Reference=formula.Shader==CharacterShader.Lightshaft ? LightshaftMaterial.Reference : CharacterMaterial.HairReference,
            formula.AlphaMode,formula.Unlit,formula.DitherOpacityMask,formula.Adaptation,
            Nodes=graph.Nodes.Select((n,i)=>new { Id=i+3,Kind=n.GetType().Name,Data=JsonSerializer.SerializeToNode(n,n.GetType(),new JsonSerializerOptions { IncludeFields=true }) }),
            graph.Outputs
        }));
    }
    File.WriteAllText(args[2],graphs.ToJsonString(new JsonSerializerOptions { WriteIndented=true }));
    return;
}

if (args.Length is < 2 or > 4) throw new ArgumentException("Usage: ShpkProbe <game directory> <output directory> [shader name | --materials <paths.json>]");
using var pack = new Meddle.SqPack.SqPack(Path.GetFullPath(args[0]));
if (args.Length==4 && args[2]=="--material-name")
{
    Directory.CreateDirectory(args[1]);
    var nameHash=Meddle.SqPack.SqPack.GetHash(args[3]);
    foreach(var category in pack.Repositories.SelectMany(r=>r.Categories.Values))
    foreach(var hash in category.UnifiedIndexEntries.Keys.Where(h=>h>uint.MaxValue && (uint)h==nameHash))
        if(category.TryGetFile(hash,out var file))
        {
            var output=Path.Combine(args[1],$"{hash:X16}_{args[3]}");
            File.WriteAllBytes(output,file.RawData.ToArray());
            Console.WriteLine(output);
        }
    return;
}
if (args.Length==4 && args[2]=="--materials")
{
    var results=new JsonArray();
    foreach(var path in JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[3]))!)
    {
        var m=new MtrlFile(Path.IsPathRooted(path) ? File.ReadAllBytes(path) : pack.GetFile(path)!.File.RawData);
        var shader=m.GetShaderPackageName();
        var package=new ShaderPackage(new ShpkFile(pack.GetFile("shader/sm5/shpk/"+shader)!.File.RawData),shader);
        string Name(uint id)=>Names.GetConstants().GetValueOrDefault(id)?.Value ?? $"0x{id:X8}";
        var metadata=new JsonObject { ["ShaderPackage"]=shader,["IsTransparent"]=(m.ShaderHeader.Flags&16)!=0 };
        foreach(var (id,value) in package.DefaultKeyValues)metadata[Name(id)]=Name(value);
        foreach(var key in m.ShaderKeys)metadata[Name(key.Category)]=Name(key.Value);
        foreach(var (id,values) in package.MaterialConstants)metadata[Name(id)]=JsonSerializer.SerializeToNode(values);
        foreach(var (id,values) in m.GetConstants())metadata[Name(id)]=JsonSerializer.SerializeToNode(values);
        results.Add(new JsonObject { ["Path"]=path,["Metadata"]=metadata });
    }
    File.WriteAllText(args[1],results.ToJsonString(new JsonSerializerOptions { WriteIndented=true }));
    return;
}
if (args.Length == 3 && args[2] == "--scan-lighting")
{
    Directory.CreateDirectory(args[1]);
    foreach (var category in pack.Repositories.SelectMany(r=>r.Categories.Values).Where(c=>c.Id==5))
    foreach (var hash in category.UnifiedIndexEntries.Keys)
    {
        if (hash <= uint.MaxValue) continue; // Index2 aliases of the same files.
        if (!category.TryGetFile(hash,out var file)) continue;
        var raw=file.RawData.ToArray();
        if (raw.Length<4 || Encoding.ASCII.GetString(raw,0,4)!="ShPk") continue;
        var strings=Encoding.ASCII.GetString(raw);
        if (!strings.Contains("GBuffer")) continue;
        var s=new ShpkFile(raw);
        var dir=Path.Combine(args[1],hash.ToString("X16"));Directory.CreateDirectory(dir);
        for(var i=0;i<s.PixelShaders.Length;i++) File.WriteAllText(Path.Combine(dir,$"ps_{i}.asm"),ShaderUtils.Disassemble(s.PixelShaders[i].Blob));
        Console.WriteLine($"{hash:X16}: {s.PixelShaders.Length} PS");
    }
    return;
}
foreach (var name in args.Length == 3 ? new[] { args[2] } : new[] { "character", "characterlegacy", "charactertattoo", "characterocclusion", "hair" })
{
    var path = "shader/sm5/shpk/" + name + ".shpk";
    var bytes = (pack.GetFile(path) ?? throw new FileNotFoundException(path)).File.RawData.ToArray();
    var shpk = new ShpkFile(bytes);
    var dir = Path.Combine(args[1], name);
    Directory.CreateDirectory(dir);
    string ResourceName(ShpkFile.Resource r)
    {
        var offset = checked((int)(shpk.FileHeader.StringsOffset + r.StringOffset));
        var end = Array.IndexOf(bytes, (byte)0, offset);
        return Encoding.UTF8.GetString(bytes, offset, end-offset);
    }
    object Resources(ShpkFile.Resource[] rs) => rs.Select(r => new { r.Id, Name = ResourceName(r), r.Slot, r.Size }).ToArray();
    File.WriteAllText(Path.Combine(dir,"manifest.json"), JsonSerializer.Serialize(new {
        Path = path, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), shpk.FileHeader,
        MaterialParams=shpk.MaterialParams.Select(p=>new {p.Id, Name=Names.TryResolveName(p.Id),p.ByteOffset,p.ByteSize}),
        shpk.MaterialParamDefaults, MaterialKeys=shpk.MaterialKeys.Select(k=>new {k.Id,Name=Names.TryResolveName(k.Id),k.DefaultValue,DefaultName=Names.TryResolveName(k.DefaultValue)}), shpk.SystemKeys, shpk.SceneKeys, shpk.SubViewKeys,
        shpk.Nodes, shpk.NodeAliases, shpk.NodeAliasClusters,
        StageCounts = new { Vertex = shpk.VertexShaders.Length, Pixel = shpk.PixelShaders.Length,
            Hull = shpk.HullShaders.Length, Domain = shpk.DomainShaders.Length, Geometry = shpk.GeometryShaders.Length },
        Pixels = shpk.PixelShaders.Select((s,i) => new { Index=i, Constants=Resources(s.Constants), Samplers=Resources(s.Samplers), Textures=Resources(s.Textures) })
    }, new JsonSerializerOptions { WriteIndented=true, IncludeFields=true }));
    for (var i=0;i<shpk.PixelShaders.Length;i++)
        File.WriteAllText(Path.Combine(dir,$"ps_{i}.asm"), ShaderUtils.Disassemble(shpk.PixelShaders[i].Blob));
    for (var i=0;i<shpk.VertexShaders.Length;i++)
        File.WriteAllText(Path.Combine(dir,$"vs_{i}.asm"), ShaderUtils.Disassemble(shpk.VertexShaders[i].Blob));
    foreach (var (prefix, shaders) in new[] { ("hs", shpk.HullShaders), ("ds", shpk.DomainShaders), ("gs", shpk.GeometryShaders) })
        for (var i = 0; i < shaders.Length; i++)
            File.WriteAllText(Path.Combine(dir, $"{prefix}_{i}.asm"), ShaderUtils.Disassemble(shaders[i].Blob));
    Console.WriteLine($"{name}: {shpk.PixelShaders.Length} pixel shaders, {shpk.Nodes.Length} nodes -> {dir}");
}
foreach (var (folder, material) in new[] {
    ("chara/equipment/e0903", "mt_c0101e0903_sho_a"),
    ("chara/equipment/e5048", "mt_c0101e5048_met_a"),
    ("chara/equipment/e0199", "mt_c0201e0199_top_a"),
    ("chara/equipment/e0199", "mt_c0101e0199_dwn_a"),
    ("chara/weapon/w1026/obj/body/b0001", "mt_w1026b0001_a"),
    ("chara/human/c1401/obj/face/f0003", "mt_c1401f0003_etc_b"),
    ("chara/human/c0201/obj/hair/h0156", "mt_c0201h0156_hir_a"),
    ("chara/human/c1401/obj/face/f0003", "mt_c1401f0003_etc_a"),
    ("chara/human/c1401/obj/face/f0003", "mt_c1401f0003_etc_c"),
    ("chara/human/c1401/obj/face/f0003", "mt_c1401f0003_fac_a"),
    ("chara/human/c1401/obj/face/f0003", "mt_c1401f0003_fac_b"),
    ("chara/human/c0201/obj/body/b0001", "mt_c0201b0001_a"),
    ("chara/human/c1401/obj/tail/t0003", "mt_c1401t0003_a") })
{
    for(var variant=0;variant<=5;variant++)
    {
        var path=variant==0 ? $"{folder}/material/{material}.mtrl" : $"{folder}/material/v{variant:0000}/{material}.mtrl";
        var file=pack.GetFile(path);
        if(file==null)continue;
        var m=new MtrlFile(file.File.RawData);
        var shaderName=m.GetShaderPackageName();
        var shpk=new ShpkFile(pack.GetFile("shader/sm5/shpk/"+shaderName)!.File.RawData);
        var package=new ShaderPackage(shpk,shaderName);
        string Name(uint id)=>Names.GetConstants().GetValueOrDefault(id)?.Value ?? $"0x{id:X8}";
        var metadata=new JsonObject { ["ShaderPackage"]=shaderName,["IsTransparent"]=(m.ShaderHeader.Flags&16)!=0 };
        foreach(var (id,value) in package.DefaultKeyValues) metadata[Name(id)]=Name(value);
        foreach(var key in m.ShaderKeys) metadata[Name(key.Category)]=Name(key.Value);
        foreach(var (id,values) in package.MaterialConstants) metadata[Name(id)]=JsonSerializer.SerializeToNode(values);
        foreach(var (id,values) in m.GetConstants()) metadata[Name(id)]=JsonSerializer.SerializeToNode(values);
        var options=new JsonSerializerOptions { IncludeFields=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        if(m.GetColorTable() is ColorTableSet table) metadata["ColorTable"]=JsonSerializer.SerializeToNode(table.ToObject(),options);
        var materialTextures=new Dictionary<string,MaterialTexture>();
        foreach(var sampler in m.Samplers)
            if(package.ResourceKeys.TryGetValue(sampler.SamplerId,out var usage)) materialTextures[usage]=new(usage,"/Game/Probe/"+usage,CharacterMaterial.IsColor(usage));
        foreach(var usage in new[]{"Decal","chara_tile_norm_array","chara_tile_orb_array"}) materialTextures[usage]=new(usage,"/Game/Probe/"+usage,false);
        var formula=CharacterMaterial.Build(metadata,materialTextures);
        Console.WriteLine($"{material} v{variant:0000}: {shaderName}, graph={formula.Graph!=null}, {formula.Gap ?? formula.AlphaMode}");
        string Str(int offset) {var end=Array.IndexOf(m.Strings,(byte)0,offset);return Encoding.UTF8.GetString(m.Strings,offset,end-offset);}
        File.WriteAllText(Path.Combine(args[1],$"{material}_v{variant:0000}.json"),JsonSerializer.Serialize(new {
            Path=path,Metadata=metadata,m.ShaderHeader, Keys=m.ShaderKeys.Select(k=>new{k.Category,Name=Names.TryResolveName(k.Category),k.Value,ValueName=Names.TryResolveName(k.Value)}),
            Textures=m.Samplers.Select(s=>new{s.SamplerId,Name=Names.TryResolveName(s.SamplerId),
                Path=s.TextureIndex==byte.MaxValue ? null : Str(m.TextureOffsets[s.TextureIndex].Offset),
                s.Flags, AddressModeU=s.AddressModeU.ToString(), AddressModeV=s.AddressModeV.ToString(),s.LodBias,s.MinLod,s.UnknownFlags}),
            Constants=m.Constants.Select(c=>new{c.ConstantId,Name=Names.TryResolveName(c.ConstantId),Values=m.ShaderValues.Skip(c.ValueOffset/4).Take(c.ValueSize/4).Select(x=>BitConverter.UInt32BitsToSingle(x)).ToArray()}),
            m.LargeColorTable,m.HasTable,FormulaEnabled=formula.Graph!=null,formula.Gap,formula.Adaptation
        },new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}

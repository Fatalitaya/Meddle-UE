using System.Collections;
using System.Text;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

namespace Meddle.Unreal;

/// <summary>UE 5.8 tagged properties and package tables, independent of an installed engine.</summary>
internal sealed class AssetPackage
{
    private static readonly string Shell = ReadShell();
    public UAsset Asset { get; }
    public string Name { get; }
    private readonly Dictionary<(string, string, string), int> imports = new();

    public AssetPackage(string packagePath)
    {
        if (!packagePath.StartsWith("/Game/", StringComparison.Ordinal) ||
            packagePath.Split('/').Skip(1).Any(x => !AssetNames.IsValid(x)))
            throw new ArgumentException("Use a /Game/ package path containing Unicode letters, digits and underscores.", nameof(packagePath));
        Asset = UAsset.DeserializeJson(Shell);
        Asset.FolderName = new FString(packagePath);
        Asset.PersistentGuid = Guid.NewGuid();
        Name = packagePath[(packagePath.LastIndexOf('/') + 1)..];
    }

    private static string ReadShell()
    {
        using var s = typeof(AssetPackage).Assembly.GetManifestResourceStream("Meddle.Unreal.PackageShell.json")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public FName NameOf(string value) => FName.FromString(Asset, value);

    public int Import(string package, string name, string className, string classPackage = "/Script/CoreUObject")
    {
        var key = (package, name, className);
        if (imports.TryGetValue(key, out var index)) return index;
        var outer = package.Length == 0 ? 0 : Import("", package, "Package");
        Asset.Imports.Add(new Import(classPackage, className, new FPackageIndex(outer), name, false, Asset)
        { PackageName = NameOf("None"), ObjectName = NameOf(name) });
        index = -Asset.Imports.Count;
        imports.Add(key, index);
        return index;
    }

    public int Reference(string packagePath, string type) =>
        Import(packagePath, packagePath[(packagePath.LastIndexOf('/') + 1)..], type, "/Script/Engine");

    public byte[] Bytes(Action<AssetBinaryWriter> write)
    {
        using var s = new MemoryStream();
        using var w = new AssetBinaryWriter(s, Asset);
        write(w);
        return s.ToArray();
    }

    public void Property(AssetBinaryWriter w, string name, byte[] value, params (string Name, int Children)[] type)
        => PropertyFlags(w, name, value, 0, type);

    public void NativeProperty(AssetBinaryWriter w, string name, byte[] value, params (string Name, int Children)[] type)
        => PropertyFlags(w, name, value, 8, type);

    private void PropertyFlags(AssetBinaryWriter w, string name, byte[] value, byte flags, (string Name, int Children)[] type)
    {
        w.Write(NameOf(name));
        foreach (var n in type) { w.Write(NameOf(n.Name)); w.Write(n.Children); }
        w.Write(value.Length);
        w.Write(flags);
        w.Write(value);
    }

    public void IntProperty(AssetBinaryWriter w, string name, int value) => Property(w, name, Bytes(b => b.Write(value)), ("IntProperty", 0));
    public void ObjectProperty(AssetBinaryWriter w, string name, int value) => Property(w, name, Bytes(b => b.Write(value)), ("ObjectProperty", 0));
    public void BoolProperty(AssetBinaryWriter w, string name, bool value) => PropertyFlags(w, name, [], value ? (byte)16 : (byte)0, [("BoolProperty", 0)]);
    public void EnumProperty(AssetBinaryWriter w, string name, string enumType, string value) =>
        Property(w, name, Bytes(b => b.Write(NameOf(value))), ("ByteProperty", 1), (enumType, 1), ("/Script/Engine", 0));
    public void End(AssetBinaryWriter w) => w.Write(NameOf("None"));

    public int AddObject(string name, string type, Action<AssetBinaryWriter> properties, byte[] nativeData, int outer = 0, string module = "/Script/Engine")
    {
        var classIndex = Import(module, type, "Class");
        long propertyEnd = 0;
        var data = Bytes(w =>
        {
            w.Write((byte)0); // EClassSerializationControlExtension
            properties(w);
            End(w);
            propertyEnd = w.BaseStream.Position;
            w.Write(0); // UObject has no ObjectGuid
            w.Write(nativeData);
        });
        Asset.Exports.Add(new RawExport(data, Asset, [])
        {
            ObjectName = NameOf(name), ClassIndex = new FPackageIndex(classIndex),
            OuterIndex = new FPackageIndex(outer), SuperIndex = new FPackageIndex(0), TemplateIndex = new FPackageIndex(0),
            ObjectFlags = outer == 0 ? EObjectFlags.RF_Public | EObjectFlags.RF_Standalone : EObjectFlags.RF_Transactional,
            bIsAsset = outer == 0, bNotAlwaysLoadedForEditorGame = true,
            ScriptSerializationStartOffset = 0, ScriptSerializationEndOffset = propertyEnd
        });
        if (outer == 0)
            Asset.AssetRegistryRecords.Add(new FAssetRegistryRecord
            { Path = name, ClassName = module + "." + type, TagMap = new Dictionary<string, string>() });
        return Asset.Exports.Count;
    }

    public void Save(string file)
    {
        Asset.ImportBits = new BitArray(Asset.Imports.Count, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        Asset.Write(file);
    }
}

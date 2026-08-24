using Meddle.SqPack;
using Meddle.Utils.Export.Ue;

namespace Meddle.Plugin.Export.Ue;

public interface IUeResourceReader
{
    UeResourceReadResult Read(UeResourceReadRequest request);
}

public sealed class SqPackUeResourceReader(SqPack.SqPack pack) : IUeResourceReader
{
    private readonly UeResourceSource source = new(
        path => pack.GetFile(path)?.File.RawData.ToArray(),
        File.ReadAllBytes);

    public UeResourceReadResult Read(UeResourceReadRequest request)
    {
        return source.Read(request);
    }
}

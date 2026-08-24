using System.Text.Json;
using Meddle.Plugin.Havok;
using Meddle.Formats.Files;
using Meddle.Formats.Helpers;
using Meddle.SqPack;

namespace Meddle.Plugin.Services;

/// <summary>
/// Manual/debug evidence exporter for raw SKLB/PAP resources and their embedded
/// Havok payloads. This is NOT a production exporter: it exists so the
/// runtime-observed resource identities can be cross-verified against the
/// offline SqPack data and the Havok XML oracle.
/// </summary>
public sealed class AnimationEvidenceExportService : IService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SqPack.SqPack pack;
    private readonly HavokConverter havokConverter;

    public AnimationEvidenceExportService(SqPack.SqPack pack, HavokConverter havokConverter)
    {
        this.pack = pack;
        this.havokConverter = havokConverter;
    }

    /// <summary>
    /// Reads an .sklb (game path, absolute disk path or resolved handle path),
    /// extracts its embedded Havok skeleton payload and writes:
    /// <c>&lt;name&gt;.sklb</c>, <c>&lt;name&gt;.skeleton.hkx</c>,
    /// <c>&lt;name&gt;.skeleton.xml</c>.
    /// </summary>
    public void ExportSklbEvidence(string path, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("SKLB path is required.", nameof(path));
        }

        var data = pack.GetFileOrReadFromDisk(path);
        if (data == null)
        {
            throw new InvalidDataException($"SKLB could not be read: {path}");
        }

        var sklb = new SklbFile(data);
        var havok = sklb.Skeleton.ToArray();
        var xml = havokConverter.HkxToXml(havok);

        Directory.CreateDirectory(outputDirectory);
        var name = Path.GetFileNameWithoutExtension(path.TrimHandlePath());
        File.WriteAllBytes(Path.Combine(outputDirectory, $"{name}.sklb"), data);
        File.WriteAllBytes(Path.Combine(outputDirectory, $"{name}.skeleton.hkx"), havok);
        File.WriteAllText(Path.Combine(outputDirectory, $"{name}.skeleton.xml"), xml);
    }

    /// <summary>
    /// Reads a .pap (game path, absolute disk path or resolved handle path),
    /// extracts its embedded Havok payload and writes:
    /// <c>&lt;name&gt;.pap</c>, <c>&lt;name&gt;.havok.hkx</c>,
    /// <c>&lt;name&gt;.havok.xml</c>, <c>&lt;name&gt;.animations.json</c>.
    /// </summary>
    public void ExportPapEvidence(string path, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("PAP path is required.", nameof(path));
        }

        var data = pack.GetFileOrReadFromDisk(path);
        if (data == null)
        {
            throw new InvalidDataException($"PAP could not be read: {path}");
        }

        var pap = new PapFile(data);
        var havok = pap.HavokData.ToArray();
        var xml = havokConverter.HkxToXml(havok);

        Directory.CreateDirectory(outputDirectory);
        var name = Path.GetFileNameWithoutExtension(path.TrimHandlePath());
        File.WriteAllBytes(Path.Combine(outputDirectory, $"{name}.pap"), data);
        File.WriteAllBytes(Path.Combine(outputDirectory, $"{name}.havok.hkx"), havok);
        File.WriteAllText(Path.Combine(outputDirectory, $"{name}.havok.xml"), xml);
        File.WriteAllText(
            Path.Combine(outputDirectory, $"{name}.animations.json"),
            JsonSerializer.Serialize(BuildAnimationsJson(pap), JsonOptions));
    }

    private static object BuildAnimationsJson(PapFile pap)
    {
        var header = pap.FileHeader;
        return new
        {
            fileHeader = new
            {
                version = header.Version,
                animationCount = header.AnimationCount,
                modelId = header.ModelId,
                modelType = header.ModelType.ToString(),
                variant = header.Variant,
                infoOffset = header.InfoOffset,
                havokOffset = header.HavokOffset,
                footerOffset = header.FooterOffset
            },
            animations = pap.Animations
                            .Select((animation, index) => new
                            {
                                tableIndex = index,
                                name = animation.GetName,
                                type = animation.Type,
                                havokIndex = animation.HavokIndex,
                                isFace = animation.IsFace
                            })
                            .ToArray(),
            note = "Verification artifact only. HavokIndex is raw evidence and is not a UE clip index."
        };
    }
}

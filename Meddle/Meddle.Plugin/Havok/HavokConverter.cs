using System.Text;
using FFXIVClientStructs.Havok.Common.Base.Reflection;
using FFXIVClientStructs.Havok.Common.Base.Reflection.Registry;
using FFXIVClientStructs.Havok.Common.Base.System.IO.OStream;
using FFXIVClientStructs.Havok.Common.Base.Types;
using FFXIVClientStructs.Havok.Common.Serialize.Resource;
using FFXIVClientStructs.Havok.Common.Serialize.Util;
using InteropGenerator.Runtime;
using Meddle.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Havok;

/// <summary>
/// Manual/debug verification oracle: converts an embedded Havok payload
/// (from a .sklb or .pap) into a human-readable XML document using the native
/// Havok serializer.
///
/// This service is intentionally NOT part of any production exporter or audit
/// collector dependency graph. It exists only so raw SKLB/PAP Havok payloads
/// can be independently inspected and cross-verified against runtime evidence.
///
/// Native lifetime contract:
///
/// - The input HKX buffer remains pinned for the complete
///   load -> inspect -> serialize -> release lifetime.
/// - Every successfully returned hkResource is released exactly once with
///   RemoveReference().
/// - hkOstream storage is stack-owned; there is no separately allocated native
///   stream buffer to leak on an early failure.
/// - hkOstream.Dtor() is called only after Ctor() completed successfully.
/// - The temporary XML file is removed on every managed success/failure path
///   after its path has been created.
/// </summary>
public unsafe sealed class HavokConverter : IService
{
    private const string RootLevelContainerClassName = "hkRootLevelContainer";

    private readonly ILogger<HavokConverter> logger;

    public HavokConverter(ILogger<HavokConverter> logger)
    {
        this.logger = logger;
    }

    public string HkxToXml(ReadOnlySpan<byte> hkx)
    {
        if (hkx.IsEmpty)
        {
            throw new InvalidDataException("HKX payload is empty.");
        }

        return HkxToXml(hkx.ToArray());
    }

    public string HkxToXml(byte[] hkx)
    {
        ArgumentNullException.ThrowIfNull(hkx);

        if (hkx.Length == 0)
        {
            throw new InvalidDataException("HKX payload is empty.");
        }

        // Validate the builtin/class/type registries BEFORE the load and pass
        // the same validated registries into serialization. LoadFromBuffer
        // receives explicit, initialized ErrorDetails and LoadOptions storage
        // (never null), per the pinned FFXIVClientStructs contract.
        var loadOptions = BuildLoadOptions(
            out var classNameRegistry,
            out var typeInfoRegistry);

        hkSerializeUtil.ErrorDetails errorDetails = default;

        // Keep the source bytes pinned for the entire lifetime of hkResource.
        //
        // FFXIVClientStructs documents that the source buffer may be released
        // immediately after LoadFromBuffer(), but deliberately retaining the
        // pin until RemoveReference() gives this debug/verification path the
        // strongest and simplest lifetime contract.
        fixed (byte* buffer = hkx)
        {
            var resource = hkSerializeUtil.LoadFromBuffer(
                buffer,
                hkx.Length,
                &errorDetails,
                &loadOptions);

            if (resource == null)
            {
                throw BuildHavokLoadException(errorDetails);
            }

            try
            {
                var xml = SerializeToXml(
                    resource,
                    classNameRegistry,
                    typeInfoRegistry);

                logger.LogDebug(
                    "Converted {Length} byte Havok payload to XML",
                    hkx.Length);

                return xml;
            }
            finally
            {
                // hkSerializeUtil returns a reference-counted hkResource.
                // The caller owns this reference and must release it.
                resource->RemoveReference();
            }
        }
    }

    /// <summary>
    /// Resolves and validates the Havok registries required for both loading
    /// and serializing HKX payloads. The same validated pointers are passed to
    /// LoadFromBuffer (via LoadOptions) and to SerializeToXml, so load and save
    /// never consult two different registry contexts.
    /// </summary>
    private static hkSerializeUtil.LoadOptions BuildLoadOptions(
        out hkClassNameRegistry* classNameRegistry,
        out hkTypeInfoRegistry* typeInfoRegistry)
    {
        var registry = hkBuiltinTypeRegistry.Instance();
        if (registry == null)
        {
            throw new InvalidOperationException(
                "hkBuiltinTypeRegistry.Instance() returned null.");
        }

        classNameRegistry = registry->GetClassNameRegistry();
        if (classNameRegistry == null)
        {
            throw new InvalidOperationException(
                "hkBuiltinTypeRegistry has no class name registry.");
        }

        typeInfoRegistry = registry->GetTypeInfoRegistry();
        if (typeInfoRegistry == null)
        {
            throw new InvalidOperationException(
                "hkBuiltinTypeRegistry has no type info registry.");
        }

        return new hkSerializeUtil.LoadOptions
        {
            Flags = new hkFlags<hkSerializeUtil.LoadOptionBits, int>
            {
                Storage = (int)hkSerializeUtil.LoadOptionBits.Default
            },
            ClassNameRegistry = classNameRegistry,
            TypeInfoRegistry = typeInfoRegistry
        };
    }

    /// <summary>
    /// Builds a deterministic load-failure exception from the ErrorDetails
    /// filled by LoadFromBuffer. Error rendering is defensive: a malformed or
    /// unexpected detail pointer must never cause a second exception that
    /// replaces the primary failure.
    /// </summary>
    private static InvalidOperationException BuildHavokLoadException(
        hkSerializeUtil.ErrorDetails errorDetails)
    {
        var message = "Havok failed to read HKX resource";

        var errorId = (int)errorDetails.Id.Storage;
        if (errorId != 0)
        {
            message += $" (ErrorId={errorId})";
        }

        try
        {
            if (errorDetails.DefaultMessage.StringAndFlag != null)
            {
                var text = errorDetails.DefaultMessage.String;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    message += $": {text}";
                }
            }
        }
        catch
        {
            // Error rendering must never mask the primary load failure.
        }

        return new InvalidOperationException(message);
    }

    private string SerializeToXml(
        hkResource* resource,
        hkClassNameRegistry* classRegistry,
        hkTypeInfoRegistry* typeInfoRegistry)
    {
        if (resource == null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        if (classRegistry == null)
        {
            throw new ArgumentNullException(nameof(classRegistry));
        }

        if (typeInfoRegistry == null)
        {
            throw new ArgumentNullException(nameof(typeInfoRegistry));
        }

        var rootContainerName =
            Encoding.UTF8.GetBytes(RootLevelContainerClassName + "\0");

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"meddle-havok-{Guid.NewGuid():N}.xml");

        var tempPathBytes = Encoding.UTF8.GetBytes(tempPath + "\0");

        try
        {
            fixed (byte* rootNamePtr = rootContainerName)
            fixed (byte* tempPathPtr = tempPathBytes)
            {
                var rootName = new CStringPointer(rootNamePtr);

                // Resolve all required Havok metadata BEFORE constructing
                // hkOstream. Early failures here therefore own no native stream
                // lifetime at all.
                var rootClass = classRegistry->GetClassByName(rootName);
                if (rootClass == null)
                {
                    throw new InvalidOperationException(
                        $"{RootLevelContainerClassName} class not found in Havok registry.");
                }

                var contents = resource->GetContentsPointer(
                    rootName,
                    typeInfoRegistry);

                if (contents == null)
                {
                    throw new InvalidOperationException(
                        $"{RootLevelContainerClassName} contents not found in Havok resource.");
                }

                // hkOstream storage itself is stack-owned. Only the Havok
                // object's constructed state has to be explicitly destroyed.
                var oStream = stackalloc hkOstream[1];
                var streamConstructed = false;

                try
                {
                    oStream->Ctor(new CStringPointer(tempPathPtr));
                    streamConstructed = true;

                    if (oStream->StreamWriter.ptr == null)
                    {
                        throw new InvalidOperationException(
                            "hkOstream was constructed without a valid stream writer.");
                    }

                    var options = new hkSerializeUtil.SaveOptions
                    {
                        Flags = new hkFlags<hkSerializeUtil.SaveOptionBits, int>
                        {
                            Storage = (int)(
                                hkSerializeUtil.SaveOptionBits.TextFormat |
                                hkSerializeUtil.SaveOptionBits.SerializeIgnoredMembers |
                                hkSerializeUtil.SaveOptionBits.WriteAttributes)
                        }
                    };

                    hkResult result = default;

                    hkSerializeUtil.Save(
                        &result,
                        contents,
                        rootClass,
                        oStream->StreamWriter.ptr,
                        options);

                    if (result.Result != hkResult.hkResultEnum.Success)
                    {
                        throw new InvalidOperationException(
                            $"hkSerializeUtil.Save failed: {result.Result}.");
                    }
                }
                finally
                {
                    // Never invoke the destructor on unconstructed stack
                    // storage. If Ctor itself fails, there is no known valid
                    // hkOstream object to destroy.
                    if (streamConstructed)
                    {
                        oStream->Dtor();
                    }
                }
            }

            if (!File.Exists(tempPath))
            {
                throw new InvalidOperationException(
                    "Havok serialization reported success but produced no XML file.");
            }

            return File.ReadAllText(tempPath);
        }
        finally
        {
            // Temp-file cleanup is independent of hkOstream lifetime so it
            // still executes if metadata lookup, Ctor, Save, Dtor or file
            // reading fails.
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                // Cleanup failure must not turn a successfully converted
                // verification artifact into a plugin-wide failure, nor should
                // it hide the primary Havok exception currently unwinding.
                logger.LogWarning(
                    ex,
                    "Failed to delete temporary Havok XML file {Path}",
                    tempPath);
            }
        }
    }
}

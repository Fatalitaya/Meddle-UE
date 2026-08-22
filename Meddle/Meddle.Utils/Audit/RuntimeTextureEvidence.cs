namespace Meddle.Utils.Audit;

public sealed record RuntimeTextureGeneratedEvidence(string Status, string Reason);

/// <summary>Evidence-only classification for runtime texture provenance.</summary>
public static class RuntimeTextureEvidence
{
    public static RuntimeTextureGeneratedEvidence ClassifyGeneratedStatus(
        string? sourcePath,
        string? resourcePath,
        bool hasExplicitRuntimeGeneratedEvidence)
    {
        if (hasExplicitRuntimeGeneratedEvidence)
        {
            return new RuntimeTextureGeneratedEvidence(
                "runtime-generated",
                "explicit runtime-generated texture evidence was observed");
        }

        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return new RuntimeTextureGeneratedEvidence(
                "unknown",
                "the runtime texture has no resource path and no independent runtime-generated evidence");
        }

        return new RuntimeTextureGeneratedEvidence(
            string.IsNullOrWhiteSpace(sourcePath) ? "runtime-provided" : "file",
            "a runtime resource path was observed");
    }
}

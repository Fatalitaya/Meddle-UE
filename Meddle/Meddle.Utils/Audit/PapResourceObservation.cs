namespace Meddle.Utils.Audit;

/// <summary>
/// Source of a PAP resource observation. Sync and async resource loads may
/// arrive from different game threads; the source is preserved as evidence.
/// </summary>
public enum PapResourceObservationSource
{
    Sync,
    Async
}

/// <summary>
/// One observed PAP resource path. This is process-level temporal evidence:
/// it does NOT attribute the PAP to a specific actor or animation.
/// </summary>
public sealed record PapResourceObservation(
    string Path,
    DateTime LastSeenUtc,
    PapResourceObservationSource Source,
    int SeenCount);

namespace Meddle.Utils.Export.Ue;

public enum UeResourceAvailability
{
    Available,
    Unavailable,
    Unresolved
}

public static class UeResourceReadPolicy
{
    public const string MissingSourceObservationCode = "ResourceSourceNotObserved";
    public const string MissingSourceObservationMessage =
        "A resource-bearing runtime object was observed, but no logical or resolved source path was captured.";

    public static UeResourceAvailability GetAvailability(UeResourceReadStatus status)
    {
        return status switch
        {
            UeResourceReadStatus.Success => UeResourceAvailability.Available,
            UeResourceReadStatus.NotFound => UeResourceAvailability.Unavailable,
            UeResourceReadStatus.ReadFailed => UeResourceAvailability.Unavailable,
            UeResourceReadStatus.Unresolved => UeResourceAvailability.Unresolved,
            UeResourceReadStatus.MissingPath => UeResourceAvailability.Unavailable,
            _ => UeResourceAvailability.Unavailable
        };
    }

    public static string GetObservationCode(UeResourceReadStatus status)
    {
        return status switch
        {
            UeResourceReadStatus.NotFound => "ResourceUnavailable",
            UeResourceReadStatus.ReadFailed => "ResourceReadFailed",
            UeResourceReadStatus.Unresolved => "ResourceUnresolved",
            UeResourceReadStatus.MissingPath => "MissingResourcePath",
            _ => "ResourceUnavailable"
        };
    }

    public static string GetObservationMessage(UeResourceReadStatus status, Exception? error = null)
    {
        return status switch
        {
            UeResourceReadStatus.ReadFailed => $"Resource read failed: {error?.Message}",
            UeResourceReadStatus.Unresolved =>
                "Runtime path could not be parsed or resolved to actual resource bytes; refusing to fall back to vanilla SqPack.",
            UeResourceReadStatus.MissingPath => "A referenced resource has no logical or resolved path.",
            _ => "Resource could not be captured."
        };
    }
}

using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Utils.Audit;

namespace Meddle.Plugin.Audit;

/// <summary>
/// Shared helpers for runtime audit collectors. Every helper is fail-closed:
/// null pointers and unexpected runtime states produce observations instead of
/// exceptions.
/// </summary>
internal static unsafe class AuditCollectorUtil
{
    public static CharacterBase* TryGetCharacterBase(nint actorAddress)
    {
        if (actorAddress == 0)
        {
            return null;
        }

        var character = (Character*)actorAddress;
        var drawObject = character->DrawObject;
        if (drawObject == null)
        {
            return null;
        }

        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
        {
            return null;
        }

        return (CharacterBase*)drawObject;
    }

    public static RuntimeAuditObservation Observation(
        RuntimeAuditCollectorContext context,
        RuntimeAuditCategory category,
        string sourceType,
        object? value,
        RuntimeAuditAvailability availability = RuntimeAuditAvailability.Available,
        string? error = null)
    {
        return new RuntimeAuditObservation
        {
            Time = context.CapturedAtUtc.ToString("O"),
            ActorId = context.ActorId,
            Category = category.ToString(),
            SourceType = sourceType,
            Value = value,
            Availability = availability,
            Error = error
        };
    }

    public static RuntimeAuditCollectorResult Unavailable(
        RuntimeAuditCollectorContext context,
        RuntimeAuditCategory category,
        string sourceType,
        string reason)
    {
        return new RuntimeAuditCollectorResult
        {
            Status = RuntimeAuditAvailability.Unavailable,
            Error = reason,
            Observations =
            {
                Observation(context, category, sourceType, null, RuntimeAuditAvailability.Unavailable, reason)
            }
        };
    }

    public static RuntimeAuditCollectorResult Unknown(
        RuntimeAuditCollectorContext context,
        RuntimeAuditCategory category,
        string sourceType,
        string reason)
    {
        return new RuntimeAuditCollectorResult
        {
            Status = RuntimeAuditAvailability.Unknown,
            Error = reason,
            Observations =
            {
                Observation(context, category, sourceType, null, RuntimeAuditAvailability.Unknown, reason)
            }
        };
    }

    public static RuntimeAuditCollectorResult Failed(
        RuntimeAuditCollectorContext context,
        RuntimeAuditCategory category,
        string sourceType,
        Exception exception)
    {
        return Unavailable(
            context,
            category,
            sourceType,
            $"collector failed closed: {exception.GetType().Name}: {exception.Message}");
    }
}

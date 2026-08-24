using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>
/// Collects runtime animation resource evidence: partial skeleton handle
/// identities and recent process-level PAP resource loads observed through the
/// optional GetResource hooks. This collector performs no heavy IO and no
/// Havok conversion; it only reads the bounded evidence buffer and identity
/// fields already exposed by the runtime.
///
/// PAP paths are NOT actor-attributed: the hooks are process-level.
/// </summary>
public sealed unsafe class AnimationEvidenceCollector : IRuntimeAuditCollector
{
    private readonly PapResourceTracker papResourceTracker;

    public AnimationEvidenceCollector(PapResourceTracker papResourceTracker)
    {
        this.papResourceTracker = papResourceTracker;
    }

    public RuntimeAuditCategory Category => RuntimeAuditCategory.AnimationEvidence;

    public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
    {
        try
        {
            papResourceTracker.EnsureStarted();
            var characterBase = AuditCollectorUtil.TryGetCharacterBase(context.ActorAddress);
            var snapshot = papResourceTracker.GetSnapshot();

            var status = AnimationEvidenceAvailabilityPolicy.Evaluate(
                characterBaseAvailable: characterBase != null,
                syncHookAvailable: snapshot.SyncAvailable,
                asyncHookAvailable: snapshot.AsyncAvailable);

            var partials = new List<object?>();
            if (characterBase != null && characterBase->Skeleton != null)
            {
                var skeleton = characterBase->Skeleton;
                for (var i = 0; i < skeleton->PartialSkeletonCount; i++)
                {
                    partials.Add(CapturePartialSkeleton(&skeleton->PartialSkeletons[i], i));
                }
            }

            var result = new RuntimeAuditCollectorResult { Status = status };
            result.Observations.Add(AuditCollectorUtil.Observation(
                context,
                Category,
                "AnimationEvidence",
                new Dictionary<string, object?>
                {
                    ["partialSkeletons"] = partials,
                    ["papHooks"] = new Dictionary<string, object?>
                    {
                        ["sync"] = new Dictionary<string, object?>
                        {
                            ["available"] = snapshot.SyncAvailable,
                            ["reason"] = snapshot.SyncError
                        },
                        ["async"] = new Dictionary<string, object?>
                        {
                            ["available"] = snapshot.AsyncAvailable,
                            ["reason"] = snapshot.AsyncError
                        }
                    },
                    ["recentPapLoads"] = snapshot.Recent
                        .Select(observation => (object)new Dictionary<string, object?>
                        {
                            ["path"] = observation.Path,
                            ["lastSeenUtc"] = observation.LastSeenUtc.ToString("O"),
                            ["source"] = observation.Source.ToString(),
                            ["seenCount"] = observation.SeenCount
                        })
                        .ToList(),
                    ["papAttribution"] = "Process-level temporal evidence only; no actor attribution is implied."
                }));

            return result;
        }
        catch (Exception ex)
        {
            return AuditCollectorUtil.Failed(context, Category, "AnimationEvidence", ex);
        }
    }

    private static object? CapturePartialSkeleton(PartialSkeleton* partial, int index)
    {
        if (partial == null)
        {
            return new Dictionary<string, object?>
            {
                ["partialSkeletonIndex"] = index,
                ["available"] = false
            };
        }

        var handle = partial->SkeletonResourceHandle;
        return new Dictionary<string, object?>
        {
            ["partialSkeletonIndex"] = index,
            ["handlePath"] = handle == null ? null : handle->FileName.ParseString(),
            ["resourceIdentityAvailable"] = handle != null,
            ["poseCount"] = partial->HavokPoses.Length
        };
    }
}

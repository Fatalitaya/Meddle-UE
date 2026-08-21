using Meddle.Plugin.Audit.Collectors;
using Meddle.Plugin.Services;
using Meddle.Utils.Audit;

namespace Meddle.Plugin.Audit;

/// <summary>
/// Runs all audit collectors against one actor and produces a session. The
/// capture itself is fail-closed: a failing collector never aborts the other
/// collectors.
/// </summary>
public sealed unsafe class RuntimeAuditCapture
{
    private readonly IReadOnlyList<IRuntimeAuditCollector> collectors;

    public RuntimeAuditCapture(PbdHooks pbdHooks)
    {
        collectors =
        [
            new CharacterCompositionCollector(),
            new SkeletonCompositionCollector(),
            new MaterialCollector(),
            new RuntimeTextureCollector(),
            new DeformerCollector(pbdHooks),
            new CustomizeCollector()
        ];
    }

    public RuntimeAuditSession Capture(
        string captureId,
        nint actorAddress,
        string actorName,
        string objectKind)
    {
        var capturedAtUtc = DateTime.UtcNow;
        var context = new RuntimeAuditCollectorContext(
            $"0x{actorAddress:X}",
            actorName,
            objectKind,
            actorAddress,
            capturedAtUtc);

        return RuntimeAuditRunner.RunAll(captureId, context, collectors);
    }
}

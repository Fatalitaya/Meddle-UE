using Dalamud.Plugin.Services;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;
using Microsoft.Extensions.Logging;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.Audit;

/// <summary>
/// Optional runtime information auditing service.
///
/// Disabled by default (<see cref="Configuration.AuditConfiguration.Enabled"/>).
/// When enabled it can start/stop captures, persist them to the temporary
/// audit dump format and diff two captures. This service never touches the
/// existing export pipeline.
/// </summary>
public sealed class RuntimeAuditService : IService, IDisposable
{
    private const int MaxObservationsPerCapture = 10000;

    private readonly Configuration config;
    private readonly ILogger<RuntimeAuditService> logger;
    private readonly IObjectTable objectTable;
    private readonly SigUtil sigUtil;
    private readonly IFramework framework;
    private readonly RuntimeAuditCapture capture;
    private readonly object gate = new();

    private RuntimeAuditSession? activeSession;
    private string? activeCaptureId;
    private long lastCollectionTickMs;
    private bool disposed;

    public RuntimeAuditService(
        Configuration config,
        ILogger<RuntimeAuditService> logger,
        IObjectTable objectTable,
        SigUtil sigUtil,
        IFramework framework,
        PbdHooks pbdHooks)
    {
        this.config = config;
        this.logger = logger;
        this.objectTable = objectTable;
        this.sigUtil = sigUtil;
        this.framework = framework;
        capture = new RuntimeAuditCapture(pbdHooks);
    }

    public bool Enabled => config.AuditConfig.Enabled;

    public bool IsCapturing
    {
        get
        {
            lock (gate)
            {
                return activeCaptureId != null;
            }
        }
    }

    public string? ActiveCaptureId
    {
        get
        {
            lock (gate)
            {
                return activeCaptureId;
            }
        }
    }

    public string OutputDirectory => config.AuditConfig.OutputDirectory;

    public RuntimeAuditSession? CaptureOnce(string captureId)
    {
        if (!Enabled)
        {
            logger.LogInformation("Runtime audit is disabled; enable it in configuration before capturing.");
            return null;
        }

        var target = ResolveTarget();
        if (target == null)
        {
            logger.LogWarning("Runtime audit capture failed: no valid character target.");
            return null;
        }

        var safeId = SanitizeCaptureId(captureId);
        var session = capture.Capture(safeId, target.Value.Address, target.Value.Name, target.Value.Kind);
        Persist(session);
        return session;
    }

    public bool StartCapture(string captureId)
    {
        if (!Enabled)
        {
            logger.LogInformation("Runtime audit is disabled; enable it in configuration before capturing.");
            return false;
        }

        lock (gate)
        {
            if (activeCaptureId != null)
            {
                logger.LogWarning("Runtime audit capture already active ({CaptureId}).", activeCaptureId);
                return false;
            }

            var target = ResolveTarget();
            if (target == null)
            {
                logger.LogWarning("Runtime audit start failed: no valid character target.");
                return false;
            }

            var safeId = SanitizeCaptureId(captureId);
            var session = capture.Capture(safeId, target.Value.Address, target.Value.Name, target.Value.Kind);
            activeSession = session;
            activeCaptureId = safeId;
            lastCollectionTickMs = Environment.TickCount64;
            framework.Update += OnFrameworkUpdate;
            logger.LogInformation(
                "Runtime audit capture '{CaptureId}' started for {ActorName} ({ObjectKind}).",
                safeId,
                target.Value.Name,
                target.Value.Kind);
            return true;
        }
    }

    public RuntimeAuditSession? StopCapture()
    {
        lock (gate)
        {
            if (activeCaptureId == null || activeSession == null)
            {
                return null;
            }

            var captureId = activeCaptureId;
            var session = activeSession;
            activeCaptureId = null;
            activeSession = null;
            framework.Update -= OnFrameworkUpdate;
            Persist(session);
            logger.LogInformation("Runtime audit capture '{CaptureId}' stopped ({ObservationCount} observations).",
                captureId, session.Observations.Count);
            return session;
        }
    }

    public RuntimeAuditDiffResult? Diff(string beforeCaptureId, string afterCaptureId, out string? error)
    {
        error = null;
        var before = LoadCapture(beforeCaptureId);
        if (before == null)
        {
            error = $"capture '{beforeCaptureId}' not found in {OutputDirectory}";
            return null;
        }

        var after = LoadCapture(afterCaptureId);
        if (after == null)
        {
            error = $"capture '{afterCaptureId}' not found in {OutputDirectory}";
            return null;
        }

        var diff = RuntimeAuditDiff.Diff(before, after);
        var diffPath = Path.Combine(
            OutputDirectory,
            $"diff-{SanitizeCaptureId(beforeCaptureId)}-{SanitizeCaptureId(afterCaptureId)}.json");
        PersistDiff(diff, beforeCaptureId, afterCaptureId, diffPath);
        return diff;
    }

    public RuntimeAuditSession? LoadCapture(string captureId)
    {
        var directory = Path.Combine(OutputDirectory, SanitizeCaptureId(captureId));
        return RuntimeAuditSerializer.LoadAsync(directory).GetAwaiter().GetResult();
    }

    public IReadOnlyList<string> ListCaptures()
    {
        if (!Directory.Exists(OutputDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(OutputDirectory)
                        .Where(directory => File.Exists(Path.Combine(directory, RuntimeAuditFormat.RuntimeJson)))
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .Cast<string>()
                        .ToArray();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var intervalSeconds = config.AuditConfig.ContinuousCollectionIntervalSeconds;
        if (intervalSeconds <= 0)
        {
            intervalSeconds = 1f;
        }

        var now = Environment.TickCount64;
        if (now - lastCollectionTickMs < (long)(intervalSeconds * 1000))
        {
            return;
        }

        lock (gate)
        {
            lastCollectionTickMs = now;
            if (activeCaptureId == null || activeSession == null)
            {
                return;
            }

            if (activeSession.Observations.Count >= MaxObservationsPerCapture)
            {
                logger.LogWarning(
                    "Runtime audit capture '{CaptureId}' reached the observation cap ({Cap}); stopping collection.",
                    activeCaptureId,
                    MaxObservationsPerCapture);
                StopCaptureLocked();
                return;
            }

            var target = ResolveTarget();
            if (target == null)
            {
                return;
            }

            var tick = capture.Capture(activeCaptureId, target.Value.Address, target.Value.Name, target.Value.Kind);
            foreach (var observation in tick.Observations)
            {
                activeSession.AddObservation(observation);
            }
        }
    }

    private void StopCaptureLocked()
    {
        var captureId = activeCaptureId;
        var session = activeSession;
        activeCaptureId = null;
        activeSession = null;
        framework.Update -= OnFrameworkUpdate;
        if (captureId != null && session != null)
        {
            Persist(session);
        }
    }

    private unsafe (nint Address, string Name, string Kind)? ResolveTarget()
    {
        var localPlayer = sigUtil.GetLocalPlayer();
        if (localPlayer != null)
        {
            string name = localPlayer->NameString;
            return ((nint)localPlayer, name, localPlayer->ObjectKind.ToString());
        }

        var characters = objectTable.GetCharacters();
        if (characters.Length == 0)
        {
            return null;
        }

        var character = (CSCharacter*)characters[0].Address;
        string fallbackName = character->NameString;
        return ((nint)character, fallbackName, character->ObjectKind.ToString());
    }

    private void Persist(RuntimeAuditSession session)
    {
        var directory = Path.Combine(OutputDirectory, session.CaptureId);
        Task.Run(async () =>
        {
            try
            {
                await RuntimeAuditSerializer.WriteAsync(session, directory).ConfigureAwait(false);
                logger.LogInformation("Runtime audit capture '{CaptureId}' written to {Directory}.",
                    session.CaptureId, directory);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to write runtime audit capture '{CaptureId}' to {Directory}.",
                    session.CaptureId, directory);
            }
        });
    }

    private void PersistDiff(RuntimeAuditDiffResult diff, string beforeCaptureId, string afterCaptureId, string path)
    {
        Task.Run(async () =>
        {
            try
            {
                await RuntimeAuditSerializer.WriteDiffAsync(diff, beforeCaptureId, afterCaptureId, path)
                                            .ConfigureAwait(false);
                logger.LogInformation("Runtime audit diff written to {Path}.", path);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to write runtime audit diff to {Path}.", path);
            }
        });
    }

    private static string SanitizeCaptureId(string? captureId)
    {
        if (string.IsNullOrWhiteSpace(captureId))
        {
            return $"capture-{DateTime.Now:yyyyMMdd-HHmmss}";
        }

        var sanitized = new string(captureId
                                   .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
                                   .ToArray());
        return string.IsNullOrWhiteSpace(sanitized)
            ? $"capture-{DateTime.Now:yyyyMMdd-HHmmss}"
            : sanitized;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (gate)
        {
            if (activeCaptureId != null)
            {
                framework.Update -= OnFrameworkUpdate;
                activeCaptureId = null;
                activeSession = null;
            }
        }
    }
}

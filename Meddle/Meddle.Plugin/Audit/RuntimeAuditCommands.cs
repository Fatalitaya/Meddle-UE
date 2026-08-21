using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Audit;

/// <summary>
/// Chat commands for the runtime audit instrument:
///
///   /paudit capture &lt;name&gt;   one-shot capture of the local character
///   /paudit start &lt;name&gt;    start continuous observation
///   /paudit stop                stop and persist the continuous capture
///   /paudit diff &lt;a&gt; &lt;b&gt; compare two captures
///   /paudit list                list persisted captures
///   /paudit status              show audit state
///   /paudit enable|disable      toggle audit (disabled by default)
/// </summary>
public sealed class RuntimeAuditCommands : IService, IDisposable
{
    private const string Command = "/paudit";

    private readonly ICommandManager commandManager;
    private readonly RuntimeAuditService service;
    private readonly Configuration config;
    private readonly ILogger<RuntimeAuditCommands> logger;
    private bool disposed;

    public RuntimeAuditCommands(
        ICommandManager commandManager,
        RuntimeAuditService service,
        Configuration config,
        ILogger<RuntimeAuditCommands> logger)
    {
        this.commandManager = commandManager;
        this.service = service;
        this.config = config;
        this.logger = logger;
        commandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage =
                "Meddle runtime audit. Subcommands: capture <name> | start <name> | stop | diff <a> <b> | list | status | enable | disable"
        });
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        commandManager.RemoveHandler(Command);
    }

    private void OnCommand(string command, string args)
    {
        var parts = (args ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            ShowHelp();
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "capture":
                HandleCapture(parts.Length > 1 ? parts[1] : null);
                break;
            case "start":
                HandleStart(parts.Length > 1 ? parts[1] : null);
                break;
            case "stop":
                HandleStop();
                break;
            case "diff":
                if (parts.Length < 3)
                {
                    logger.LogInformation("Usage: /paudit diff <before> <after>");
                }
                else
                {
                    HandleDiff(parts[1], parts[2]);
                }

                break;
            case "list":
                HandleList();
                break;
            case "status":
                HandleStatus();
                break;
            case "enable":
                config.AuditConfig.Enabled = true;
                config.Save();
                logger.LogInformation("Runtime audit enabled.");
                break;
            case "disable":
                config.AuditConfig.Enabled = false;
                config.Save();
                logger.LogInformation("Runtime audit disabled.");
                break;
            default:
                ShowHelp();
                break;
        }
    }

    private void HandleCapture(string? captureId)
    {
        var session = service.CaptureOnce(captureId ?? "capture");
        if (session == null)
        {
            logger.LogWarning("Audit capture failed. Check that audit is enabled and a character is loaded.");
            return;
        }

        var summary = string.Join(", ",
            session.CollectorStatus.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        logger.LogInformation(
            "Audit capture '{CaptureId}' complete: {ObservationCount} observations. Collector status: {Summary}",
            session.CaptureId,
            session.Observations.Count,
            summary);
    }

    private void HandleStart(string? captureId)
    {
        if (service.StartCapture(captureId ?? "capture"))
        {
            logger.LogInformation("Audit capture '{CaptureId}' started. Use /paudit stop to finish.", service.ActiveCaptureId);
        }
    }

    private void HandleStop()
    {
        var session = service.StopCapture();
        if (session == null)
        {
            logger.LogInformation("No audit capture is currently running.");
            return;
        }

        logger.LogInformation(
            "Audit capture '{CaptureId}' stopped: {ObservationCount} observations.",
            session.CaptureId,
            session.Observations.Count);
    }

    private void HandleDiff(string before, string after)
    {
        var diff = service.Diff(before, after, out var error);
        if (diff == null)
        {
            logger.LogWarning("Audit diff failed: {Error}", error);
            return;
        }

        var summary = diff.Summarize();
        logger.LogInformation(
            "Audit diff {Before} -> {After}: added={Added} changed={Changed} removed={Removed}",
            before,
            after,
            summary["added"],
            summary["changed"],
            summary["removed"]);
    }

    private void HandleList()
    {
        var captures = service.ListCaptures();
        if (captures.Count == 0)
        {
            logger.LogInformation("No audit captures found in {Directory}.", service.OutputDirectory);
            return;
        }

        logger.LogInformation("Audit captures in {Directory}: {Captures}", service.OutputDirectory, string.Join(", ", captures));
    }

    private void HandleStatus()
    {
        logger.LogInformation(
            "Runtime audit: enabled={Enabled} capturing={Capturing} active={Active} output={Output}",
            service.Enabled,
            service.IsCapturing,
            service.ActiveCaptureId ?? "-",
            service.OutputDirectory);
    }

    private void ShowHelp()
    {
        logger.LogInformation(
            "Usage: /paudit capture <name> | start <name> | stop | diff <a> <b> | list | status | enable | disable");
    }
}

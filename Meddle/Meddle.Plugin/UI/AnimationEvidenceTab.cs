using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

/// <summary>
/// Debug-only animation resource evidence tab. Responsibilities are separated
/// from the existing <see cref="AnimationTab"/>: this tab shows optional PAP
/// hook capability, recent process-level PAP loads, selected-actor partial
/// skeleton identities and manual SKLB/PAP evidence export. It never performs
/// animation playback/import logic.
/// </summary>
public sealed class AnimationEvidenceTab : ITab
{
    private readonly PapResourceTracker papResourceTracker;
    private readonly AnimationEvidenceExportService evidenceExportService;
    private readonly CommonUi commonUi;
    private readonly INotificationManager notificationManager;
    private readonly ILogger<AnimationEvidenceTab> logger;
    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };

    private ICharacter? selectedCharacter;
    private string manualPath = "";

    public AnimationEvidenceTab(
        PapResourceTracker papResourceTracker,
        AnimationEvidenceExportService evidenceExportService,
        CommonUi commonUi,
        INotificationManager notificationManager,
        ILogger<AnimationEvidenceTab> logger)
    {
        this.papResourceTracker = papResourceTracker;
        this.evidenceExportService = evidenceExportService;
        this.commonUi = commonUi;
        this.notificationManager = notificationManager;
        this.logger = logger;
    }

    public string Name => "Animation Evidence";
    public int Order => 7;
    public MenuType MenuType => MenuType.Debug;

    public void Draw()
    {
        // Lazy, idempotent hook startup: hooks are only installed once the
        // debug tab (or an audit capture) actually requests them.
        papResourceTracker.EnsureStarted();
        var snapshot = papResourceTracker.GetSnapshot();

        DrawHookStatus(snapshot);
        ImGui.Separator();
        DrawRecentPapLoads(snapshot);
        ImGui.Separator();
        DrawCharacterSklb();
        ImGui.Separator();
        DrawManualPath();

        fileDialog.Draw();
    }

    public void Dispose()
    {
        // nothing owned by the tab
    }

    private void DrawHookStatus(PapResourceTracker.PapResourceTrackerSnapshot snapshot)
    {
        ImGui.TextUnformatted("PAP Resource Hooks");
        DrawCapability("Sync PAP resource hook", snapshot.SyncAvailable, snapshot.SyncError);
        DrawCapability("Async PAP resource hook", snapshot.AsyncAvailable, snapshot.AsyncError);
    }

    private void DrawCapability(string label, bool available, string? error)
    {
        var text = available ? "Available" : "Unavailable";
        if (!available && !string.IsNullOrWhiteSpace(error))
        {
            text += $" ({error})";
        }

        ImGui.TextUnformatted($"{label}: {text}");
    }

    private void DrawRecentPapLoads(PapResourceTracker.PapResourceTrackerSnapshot snapshot)
    {
        ImGui.TextUnformatted("Recent PAP resources (process-level temporal evidence; no actor attribution)");
        if (snapshot.Recent.Count == 0)
        {
            ImGui.TextUnformatted("No PAP loads observed in the current bounded history.");
            return;
        }

        if (!ImGui.BeginTable("##RecentPapTable", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
        {
            return;
        }

        ImGui.TableSetupColumn("Last Seen UTC");
        ImGui.TableSetupColumn("Source");
        ImGui.TableSetupColumn("Count");
        ImGui.TableSetupColumn("Path");
        ImGui.TableSetupColumn("Export");
        ImGui.TableHeadersRow();

        foreach (var observation in snapshot.Recent)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(observation.LastSeenUtc.ToString("O"));
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(observation.Source.ToString());
            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(observation.SeenCount.ToString());
            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(observation.Path);
            ImGui.TableSetColumnIndex(4);
            using var id = ImRaii.PushId(observation.Path);
            if (ImGui.Button("Export"))
            {
                var path = observation.Path;
                fileDialog.SaveFolderDialog(
                    "Export PAP Evidence",
                    "pap-evidence",
                    (result, outputPath) =>
                    {
                        if (!result)
                        {
                            return;
                        }

                        SafeExport(() => evidenceExportService.ExportPapEvidence(path, outputPath), path);
                    });
            }
        }

        ImGui.EndTable();
    }

    private unsafe void DrawCharacterSklb()
    {
        commonUi.DrawCharacterSelect(ref selectedCharacter);
        if (selectedCharacter == null)
        {
            return;
        }

        var character = (Character*)selectedCharacter.Address;
        var drawObject = character->DrawObject;
        var characterBase = drawObject != null ? (CharacterBase*)drawObject : null;
        if (characterBase == null || characterBase->Skeleton == null)
        {
            ImGui.TextUnformatted("Selected actor has no skeleton.");
            return;
        }

        var skeleton = characterBase->Skeleton;
        ImGui.TextUnformatted("Partial skeletons (runtime .sklb identities)");
        if (skeleton->PartialSkeletonCount == 0)
        {
            ImGui.TextUnformatted("No partial skeletons.");
            return;
        }

        if (!ImGui.BeginTable("##SklbTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
        {
            return;
        }

        ImGui.TableSetupColumn("Partial Index");
        ImGui.TableSetupColumn("Handle Path");
        ImGui.TableSetupColumn("Export");
        ImGui.TableHeadersRow();

        for (var i = 0; i < skeleton->PartialSkeletonCount; i++)
        {
            var partial = &skeleton->PartialSkeletons[i];
            var handle = partial->SkeletonResourceHandle;
            var handlePath = handle == null ? null : handle->FileName.ParseString();

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(i.ToString());
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(handlePath ?? "(no handle)");
            ImGui.TableSetColumnIndex(2);
            using var id = ImRaii.PushId(i);
            if (ImGui.Button("Export") && handlePath != null)
            {
                var path = handlePath;
                fileDialog.SaveFolderDialog(
                    "Export SKLB Evidence",
                    "sklb-evidence",
                    (result, outputPath) =>
                    {
                        if (!result)
                        {
                            return;
                        }

                        SafeExport(() => evidenceExportService.ExportSklbEvidence(path, outputPath), path);
                    });
            }
        }

        ImGui.EndTable();
    }

    private void DrawManualPath()
    {
        ImGui.TextUnformatted("Manual SKLB/PAP path");
        ImGui.InputText("##ManualEvidencePath", ref manualPath, 512);
        ImGui.SameLine();
        if (!ImGui.Button("Export"))
        {
            return;
        }

        var path = manualPath.Trim();
        var extension = Path.GetExtension(path);
        if (extension.Equals(".sklb", StringComparison.OrdinalIgnoreCase))
        {
            fileDialog.SaveFolderDialog(
                "Export SKLB Evidence",
                "sklb-evidence",
                (result, outputPath) =>
                {
                    if (!result)
                    {
                        return;
                    }

                    SafeExport(() => evidenceExportService.ExportSklbEvidence(path, outputPath), path);
                });
        }
        else if (extension.Equals(".pap", StringComparison.OrdinalIgnoreCase))
        {
            fileDialog.SaveFolderDialog(
                "Export PAP Evidence",
                "pap-evidence",
                (result, outputPath) =>
                {
                    if (!result)
                    {
                        return;
                    }

                    SafeExport(() => evidenceExportService.ExportPapEvidence(path, outputPath), path);
                });
        }
        else
        {
            notificationManager.AddNotification(new Notification
            {
                Content = "Only .sklb and .pap paths are supported.",
                Type = NotificationType.Error
            });
        }
    }

    /// <summary>
    /// All evidence export failures are caught, logged and surfaced as a
    /// notification; they must never propagate out of the ImGui draw.
    /// </summary>
    private void SafeExport(Action export, string description)
    {
        try
        {
            export();
            notificationManager.AddNotification(new Notification
            {
                Content = $"Exported animation evidence: {description}",
                Type = NotificationType.Success
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Animation evidence export failed: {Description}", description);
            notificationManager.AddNotification(new Notification
            {
                Content = $"Export failed: {description}: {ex.Message}",
                Type = NotificationType.Error
            });
        }
    }
}

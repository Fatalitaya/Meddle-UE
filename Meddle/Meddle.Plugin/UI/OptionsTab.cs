using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using Meddle.Plugin.Models;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public class OptionsTab : ITab
{
    public MenuType MenuType => MenuType.Default;
    private readonly Configuration config;

    public OptionsTab(Configuration config)
    {
        this.config = config;
    }

    public void Dispose() { }

    public string Name => Loc.Text("Options");
    public int Order => (int) WindowOrder.Options;

    public void Draw()
    {
        var language = (int)config.Language;
        if (ImGui.Combo("语言 / Language###UiLanguage", ref language,
                "跟随 Dalamud / Auto\0English\0简体中文\0"))
        {
            config.Language = (UiLanguage)language;
            config.Save();
        }

        var exportDirectory = config.ExportDirectory;
        if (ImGui.InputText(Loc.Label("DefaultExportDirectory"), ref exportDirectory, 256))
        {
            config.ExportDirectory = exportDirectory;
            config.Save();
        }
        
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.Redo.ToIconString()))
            {
                config.ExportDirectory = Configuration.DefaultExportDirectory;
                config.Save();
            }
        }
        
        if (exportDirectory.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), Loc.Text("WarningExportingToTheTempDirectoryMayCauseFilesTo"));
        }
        
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.Text("ResetToDefaultExportDirectory"));
            ImGui.EndTooltip();
        }
        
        if (ImGui.Button(Loc.Label("OpenOutputFolder")))
        {
            Process.Start("explorer.exe", config.ExportDirectory);
        }
        
        var openOnLoad = config.OpenOnLoad;
        if (ImGui.Checkbox(Loc.Label("OpenMainWindowOnLoad"), ref openOnLoad))
        {
            config.OpenOnLoad = openOnLoad;
            config.Save();
        }

        var debug = config.OpenDebugMenuOnLoad;
        if (ImGui.Checkbox(Loc.Label("OpenDebugWindowOnLoad"), ref debug))
        {
            config.OpenDebugMenuOnLoad = debug;
            config.Save();
        }
        
        var update = config.UpdateConfig.ShowUpdateWindow;
        if (ImGui.Checkbox(Loc.Label("ShowUpdateWindowWhenANewVersionIsLoaded"), ref update))
        {
            config.UpdateConfig.ShowUpdateWindow = update;
            config.Save();
        }
        
        ImGui.Separator();

        // DrawExportType();

        // var includePose = config.ExportConfig.ExportPose;
        // if (ImGui.Checkbox("Include Pose", ref includePose))
        // {
        //     config.ExportConfig.ExportPose = includePose;
        //     config.Save();
        // }
        //
        // ImGui.SameLine();
        // using (ImRaii.PushFont(UiBuilder.IconFont))
        // {
        //     ImGui.Text(FontAwesomeIcon.QuestionCircle.ToIconString());
        // }
        //
        // if (ImGui.IsItemHovered())
        // {
        //     ImGui.BeginTooltip();
        //     ImGui.Text("Includes pose as a track on exported models with frame 0 as the currently applied pose.");
        //     ImGui.EndTooltip();
        // }

        // DrawPoseMode();
        // DrawCharacterTextureMode();
        
        // ImGui.Separator();
        
        var playerNameOverride = config.PlayerNameOverride;
        if (ImGui.InputText(Loc.Label("PlayerNameOverride"), ref playerNameOverride, 64))
        {
            config.PlayerNameOverride = playerNameOverride;
            config.Save();
        }
        
        ImGui.Separator();
        
        var minimumNotificationLogLevel = config.MinimumNotificationLogLevel;
        if (ImGui.BeginCombo(Loc.Label("MinimumNotificationLogLevel"), minimumNotificationLogLevel.ToString()))
        {
            foreach (var level in (LogLevel[])Enum.GetValues(typeof(LogLevel)))
            {
                if (ImGui.Selectable(level.ToString(), level == minimumNotificationLogLevel))
                {
                    config.MinimumNotificationLogLevel = level;
                    config.Save();
                }
            }

            ImGui.EndCombo();
        }

        var disableUserUiHide = config.DisableUserUiHide;
        if (ImGui.Checkbox(Loc.Label("DisableUserUIHide"), ref disableUserUiHide))
        {
            config.DisableUserUiHide = disableUserUiHide;
            config.Save();
        }

        var disableAutomaticUiHide = config.DisableAutomaticUiHide;
        if (ImGui.Checkbox(Loc.Label("DisableAutomaticUIHide"), ref disableAutomaticUiHide))
        {
            config.DisableAutomaticUiHide = disableAutomaticUiHide;
            config.Save();
        }

        var disableCutsceneUiHide = config.DisableCutsceneUiHide;
        if (ImGui.Checkbox(Loc.Label("DisableCutsceneUIHide"), ref disableCutsceneUiHide))
        {
            config.DisableCutsceneUiHide = disableCutsceneUiHide;
            config.Save();
        }

        var disableGposeUiHide = config.DisableGposeUiHide;
        if (ImGui.Checkbox(Loc.Label("DisableGposeUIHide"), ref disableGposeUiHide))
        {
            config.DisableGposeUiHide = disableGposeUiHide;
            config.Save();
        }
        
        var displayDebugInfo = config.DisplayDebugInfo;
        if (ImGui.Checkbox(Loc.Label("DisplayDebugInfo"), ref displayDebugInfo))
        {
            config.DisplayDebugInfo = displayDebugInfo;
            config.Save();
        }
        
        var openFolderOnExport = config.OpenFolderOnExport;
        if (ImGui.Checkbox(Loc.Label("OpenFolderOnExportCompletion"), ref openFolderOnExport))
        {
            config.OpenFolderOnExport = openFolderOnExport;
            config.Save();
        }
    }
    
    // private void DrawCharacterTextureMode()
    // {
    //     var characterTexture = config.ExportConfig.TextureMode;
    //     if (ImGui.BeginCombo("Character Texture Mode", characterTexture.ToString()))
    //     {
    //         foreach (var mode in Enum.GetValues<TextureMode>())
    //         {
    //             if (ImGui.Selectable(mode.ToString(), mode == characterTexture))
    //             {
    //                 config.ExportConfig.TextureMode = mode;
    //                 config.Save();
    //             }
    //         }
    //
    //         ImGui.EndCombo();
    //     }
    //     
    //     if (!Enum.IsDefined(typeof(TextureMode), config.ExportConfig.TextureMode))
    //     {
    //         config.ExportConfig.TextureMode = TextureMode.Bake;
    //         config.Save();
    //     }
    // }
    
    // private void DrawExportType()
    // {
    //     var exportType = config.ExportConfig.ExportType;
    //     if (ImGui.BeginCombo("Export Type", exportType.ToString()))
    //     {
    //         foreach (var type in Enum.GetValues<ExportType>())
    //         {
    //             var selected = exportType.HasFlag(type);
    //             using var style = ImRaii.PushStyle(ImGuiStyleVar.Alpha, selected ? 1 : 0.5f);
    //             if (ImGui.Selectable(type.ToString(), selected, ImGuiSelectableFlags.DontClosePopups))
    //             {
    //                 if (selected)
    //                 {
    //                     exportType &= ~type;
    //                 }
    //                 else
    //                 {
    //                     exportType |= type;
    //                 }
    //             }
    //         }
    //
    //         ImGui.EndCombo();
    //     }
    //     
    //     if (exportType == 0)
    //     {
    //         exportType = Configuration.DefaultExportType;
    //     }
    //     
    //     if (exportType != config.ExportConfig.ExportType)
    //     {
    //         config.ExportConfig.ExportType = exportType;
    //         config.Save();
    //     }
    // }
}

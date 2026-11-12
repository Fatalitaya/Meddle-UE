using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using Meddle.Plugin.Models;

namespace Meddle.Plugin.UI;

public class AboutTab : ITab
{
    public void Dispose() { }

    public string Name => Loc.Text("About");
    public MenuType MenuType => MenuType.Default;
    public int Order => (int) WindowOrder.About;

    public void Draw()
    {
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(); 
        ImGui.Text(Loc.Format("Version", assemblyVersion));
        
        ImGui.TextWrapped(Loc.Text("MeddleIsAToolThatAllowsYouToExportModels"));
        ImGui.TextWrapped(Loc.Text("ItIsStillInDevelopmentAndMayNotWorkAs"));
        ImGui.TextWrapped(Loc.Text("PleaseReportAnyIssuesOnTheGitHubRepository"));

        Vector2 mainButtonSize = Vector2.Zero;
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 1f, 1)))
        {
            if (ImGui.Button("GitHub", mainButtonSize))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.MeddleUrl, UseShellExecute = true
                });
            }
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(1f, 0.25f, 0.25f, 1)))
        {
            if (ImGui.Button(Loc.Label("ReportAnIssue"), mainButtonSize))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.MeddleBugReportUrl, UseShellExecute = true
                });
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.Text("PleaseBeSureToIncludeAnyRelevantLogsFromXllog"));
            }
        }
        
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.95f, 0.54f, 0.15f, 1)))
        {
            if (ImGui.Button(Loc.Label("BlenderAddon"), mainButtonSize))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.MeddleToolsUrl, UseShellExecute = true
                });
            }
        }

        if (ImGui.CollapsingHeader(Loc.Label("HowDoesMeddleReadThisData")))
        {
            ImGui.TextWrapped(Loc.Text("MeddleUsesACombinationOfTheFollowing"));
            ImGui.BulletText(Loc.Text("ReadingTheSqpackFilesPackagedWithTheGame"));
            ImGui.BulletText(Loc.Text("ReadingOnDiskFilesFromPathsThatAreOverwrittenBy"));
            ImGui.BulletText(Loc.Text("ReadingTheGameSMemoryToRetrievePathsAndOther"));
            ImGui.BulletText(Loc.Text("ReadingDataDirectlyFromTheGPUToRetrieveTextureInformation"));
        }

        ImGui.Text(Loc.Text("Credits"));
        const int nameWidth = 130;
        Vector2 buttonSize = new(130, 0);
        using (ImRaii.Table("Credits", 2, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn(Loc.Label("Name"), ImGuiTableColumnFlags.WidthFixed, nameWidth);
            ImGui.TableSetupColumn(Loc.Label("Contribution"), ImGuiTableColumnFlags.None);
            ImGui.TableHeadersRow();

            foreach (var (name, role, link) in UserCredits)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                if (ImGui.Button(name, buttonSize))
                {
                    Process.Start(new ProcessStartInfo {FileName = link, UseShellExecute = true});
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(role);
            }
        }

        ImGui.TextWrapped(Loc.Text("SpecialThanksToTheFollowingProjectsAndTheirRespectiveDevelopers"));
        using (ImRaii.Table("Project Credits", 2, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn(Loc.Label("Project"), ImGuiTableColumnFlags.WidthFixed, nameWidth);
            ImGui.TableSetupColumn(Loc.Label("Usage"), ImGuiTableColumnFlags.None);
            ImGui.TableHeadersRow();

            foreach (var (name, role, link) in ProjectCredits)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                if (ImGui.Button(name, buttonSize))
                {
                    Process.Start(new ProcessStartInfo {FileName = link, UseShellExecute = true});
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(role);
            }
        }
    }

    private static List<(string, string, string)> UserCredits =>
    [
        ("PassiveModding", Loc.Text("Developer"), Constants.MeddleUrl),
        ("Asriel", Loc.Text("CreditAsriel"), "https://github.com/WorkingRobot")
    ];

    private static List<(string, string, string)> ProjectCredits =>
    [
        ("Xande", Loc.Text("CreditXande"), "https://github.com/xivdev/Xande"),
        ("Penumbra", Loc.Text("CreditPenumbra"), "https://github.com/xivdev/Penumbra"),
        ("Lumina", Loc.Text("CreditLumina"), "https://github.com/NotAdam/Lumina/"),
        ("Pathfinder", Loc.Text("CreditPathfinder"), "https://github.com/chirpxiv/ffxiv-pathfinder")
    ];
}

using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow
{
    public const ParsedInstanceType DefaultDrawTypes = ParsedInstanceType.Character |
                                                       ParsedInstanceType.Housing |
                                                       ParsedInstanceType.Terrain |
                                                       ParsedInstanceType.BgPart |
                                                       ParsedInstanceType.Light |
                                                       ParsedInstanceType.SharedGroup |
                                                       ParsedInstanceType.Camera |
                                                       ParsedInstanceType.EnvLighting;

    public class LayoutConfig
    {
        // Overlay options
        public ParsedInstanceType DrawTypes { get; set; } = DefaultDrawTypes;
        public bool DrawOverlay { get; set; } = true;
        public bool DrawChildren { get; set; }
        public bool TraceToParent { get; set; } = true;
        public bool OrderByDistance { get; set; } = true;
        public bool TraceToHovered { get; set; } = true;
        public float WorldCutoffDistance { get; set; } = 100f;
        public Vector4 WorldDotColor { get; set; } = new(1f, 1f, 1f, 0.5f);
        public bool IncludeSharedGroupsWhereSubItemsAreWithinRange { get; set; } = true;
        public bool HideOffscreenCharacters { get; set; } = true;
        public int MaxItemCount { get; set; } = 100;
        
        public OriginAdjustment OriginAdjustment { get; set; } = OriginAdjustment.Camera;
    }
    
    public enum OriginAdjustment
    {
        Player,
        Camera,
        Origin
    }
    

    private void DrawOptions()
    {
        if (ImGui.CollapsingHeader(Loc.Label("LayoutOverlayOptions")))
        {
            var dotColor = config.LayoutConfig.WorldDotColor;
            if (ImGui.ColorEdit4(Loc.Label("LayoutDotColor"), ref dotColor, ImGuiColorEditFlags.NoInputs))
            {
                config.LayoutConfig.WorldDotColor = dotColor;
                config.Save();
            }

            var drawOverlay = config.LayoutConfig.DrawOverlay;
            if (ImGui.Checkbox(Loc.Label("LayoutDrawOverlay"), ref drawOverlay))
            {
                config.LayoutConfig.DrawOverlay = drawOverlay;
                config.Save();
            }

            var drawChildren = config.LayoutConfig.DrawChildren;
            if (ImGui.Checkbox(Loc.Label("LayoutDrawChildren"), ref drawChildren))
            {
                config.LayoutConfig.DrawChildren = drawChildren;
                config.Save();
            }
            
            var traceToParent = config.LayoutConfig.TraceToParent;
            if (drawChildren && ImGui.Checkbox(Loc.Label("LayoutTracetoParent"), ref traceToParent))
            {
                config.LayoutConfig.TraceToParent = traceToParent;
                config.Save();
            }

            var traceToHovered = config.LayoutConfig.TraceToHovered;
            if (ImGui.Checkbox(Loc.Label("LayoutTracetoHovered"), ref traceToHovered))
            {
                config.LayoutConfig.TraceToHovered = traceToHovered;
                config.Save();
            }
        }
        
        if (ImGui.CollapsingHeader(Loc.Label("LayoutParseOptions")))
        {
            var cutoff = config.LayoutConfig.WorldCutoffDistance;
            if (ImGui.DragFloat(Loc.Label("LayoutCutoffDistance"), ref cutoff, 1, 0, 10000))
            {
                config.LayoutConfig.WorldCutoffDistance = cutoff;
                config.Save();
            }

            ImGui.Text(Loc.Format("CurrentOrigin", searchOrigin.X, searchOrigin.Y, searchOrigin.Z));
            var originAdjustment = config.LayoutConfig.OriginAdjustment;
            if (EnumExtensions.DrawEnumDropDown(Loc.Label("LayoutOrigin"), ref originAdjustment))
            {
                config.LayoutConfig.OriginAdjustment = originAdjustment;
                config.Save();
            }

            ImGui.SameLine();
            UiUtil.HintCircle(Loc.Text("LayoutOriginHint"));

            if (config.LayoutConfig.OriginAdjustment != OriginAdjustment.Camera)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), Loc.Text("OriginWarning"));
            }

            var drawTypes = config.LayoutConfig.DrawTypes;
            if (ImGui.BeginCombo(Loc.Label("LayoutDrawTypes"), drawTypes.GetDescription()))
            {
                foreach (var type in Enum.GetValues<ParsedInstanceType>())
                {
                    var selected = drawTypes.HasFlag(type);
                    using var style = ImRaii.PushStyle(ImGuiStyleVar.Alpha, selected ? 1 : 0.5f);
                    if (ImGui.Selectable(type.ToString(), selected, ImGuiSelectableFlags.DontClosePopups))
                    {
                        if (selected)
                        {
                            drawTypes &= ~type;
                        }
                        else
                        {
                            drawTypes |= type;
                        }
                    }
                }

                ImGui.EndCombo();
            }

            if (drawTypes == 0)
            {
                drawTypes = DefaultDrawTypes;
            }

            if (drawTypes != config.LayoutConfig.DrawTypes)
            {
                config.LayoutConfig.DrawTypes = drawTypes;
                config.Save();
            }
        }

        if (ImGui.CollapsingHeader(Loc.Label("LayoutListOptions")))
        {
            var includeSharedGroupsWhereSubItemsAreVisible = config.LayoutConfig.IncludeSharedGroupsWhereSubItemsAreWithinRange;
            if (ImGui.Checkbox(Loc.Label("LayoutIncludeSharedGroupsWhereSubItemsAreVisible"), ref includeSharedGroupsWhereSubItemsAreVisible))
            {
                config.LayoutConfig.IncludeSharedGroupsWhereSubItemsAreWithinRange = includeSharedGroupsWhereSubItemsAreVisible;
                config.Save();
            }
            ImGui.SameLine();
            UiUtil.HintCircle(Loc.Text("LayoutSharedHint"));
            
            var hideOffscreenCharacters = config.LayoutConfig.HideOffscreenCharacters;
            if (ImGui.Checkbox(Loc.Label("LayoutHideOffscreenCharacters"), ref hideOffscreenCharacters))
            {
                config.LayoutConfig.HideOffscreenCharacters = hideOffscreenCharacters;
                config.Save();
            }
        
            var orderByDistance = config.LayoutConfig.OrderByDistance;
            if (ImGui.Checkbox(Loc.Label("LayoutOrderbyDistance"), ref orderByDistance))
            {
                config.LayoutConfig.OrderByDistance = orderByDistance;
                config.Save();
            }

            var maxItemCount = config.LayoutConfig.MaxItemCount;
            if (ImGui.DragInt(Loc.Label("LayoutMaxItemCount"), ref maxItemCount, 1, 1, 50000))
            {
                config.LayoutConfig.MaxItemCount = maxItemCount;
                config.Save();
            }
        }
    }
    
    // private static T[] GetFlags<T>(T flags) where T : Enum
    // {
    //     var list = new List<T>();
    //     foreach (T type in Enum.GetValues(typeof(T)))
    //     {
    //         if (flags.HasFlag(type))
    //         {
    //             list.Add(type);
    //         }
    //     }
    //
    //     return list.ToArray();
    // }
}

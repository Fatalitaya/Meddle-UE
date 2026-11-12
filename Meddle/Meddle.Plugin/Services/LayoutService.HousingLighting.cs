using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Meddle.Plugin.Models.Structs;

namespace Meddle.Plugin.Services;

public partial class LayoutService
{
    private unsafe HousingLightState? CaptureHousingLighting(LayoutManager* layout)
    {
        var world = sigUtil.GetLayoutWorld();
        var manager = HousingManager.Instance();
        if (layout == null || world == null || layout != world->ActiveLayout || layout->HousingType != 2
            || layout->IndoorAreaData == null || manager == null || manager->IndoorTerritory == null
            || manager->CurrentTerritory != (HousingTerritory*)manager->IndoorTerritory) return null;

        var inverted = manager->IndoorTerritory->InvertedBrightness;
        // Client 2026.11.03, 0x1406F4030: this is the applied layout multiplier.
        // IndoorTerritory.BrightnessCurrent/Target are transition scratch values and can be zero at rest.
        var multiplier = *(float*)((byte*)layout->IndoorAreaData + 0x80);
        return inverted <= 5 && float.IsFinite(multiplier) && multiplier is >= 0 and <= 1
            ? new((byte)(5 - inverted), multiplier) : null;
    }

    private static unsafe HashSet<nint> HousingLightRoots(LayoutManager* layout)
    {
        var roots = new HashSet<nint>();
        if (layout->OutdoorAreaData == null) return roots;
        // Client 2026.11.03, 0x1406F94F0: four vectors of controlled shared groups,
        // starting at area + 0x6F58 with stride 0xD0. Only their descendants receive
        // the housing light multiplier; ordinary furnishing lights keep their own intensity.
        for (var floor = 0; floor < 4; floor++)
        {
            var vector = (nint**)((byte*)layout->OutdoorAreaData + 0x6F58 + floor * 0xD0);
            for (var entry = vector[0]; entry != vector[1]; entry++)
                if (*entry != 0) roots.Add(*entry);
        }
        return roots;
    }
}

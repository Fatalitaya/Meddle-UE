using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using Meddle.Formats.Files;
using Meddle.Plugin.Models.Layout;
using Transform = Meddle.Plugin.Models.Transform;

namespace Meddle.Plugin.Services;

public partial class LayoutService
{
    public unsafe uint? CurrentTerritoryId()
    {
        var world = sigUtil.GetLayoutWorld();
        return world == null || world->ActiveLayout == null || world->ActiveLayout->InitState != 7
            ? null : world->ActiveLayout->TerritoryTypeId;
    }
    public record TerritoryRuntimeNode(uint Type, uint InstanceId, byte PrefabIndex, bool Active,
        Transform Transform, string Path, ParsedStain? Stain, ParsedBgPartsInstance? Model, TerritoryRuntimeNode[] Children);
    public record TerritoryRuntimeLayer(ushort GroupKey, ushort Key, TerritorySceneFile.Festival Festival, TerritoryRuntimeNode[] Nodes);
    public record TerritorySnapshot(uint TerritoryId, uint FilterKey, uint ContentId,
        HashSet<TerritorySceneFile.Festival> Festivals, TerritoryRuntimeLayer[] Layers);

    // Called on the framework thread. No pointers survive the snapshot and no overlay/range filters apply.
    public unsafe TerritorySnapshot? CaptureTerritory(uint territoryId)
    {
        var world = sigUtil.GetLayoutWorld();
        if (world == null || world->ActiveLayout == null) return null;
        var layout = world->ActiveLayout;
        if (layout->TerritoryTypeId != territoryId || layout->InitState != 7) return null;
        if (layout->InsideFestivalTransitionLayerUpdate)
            throw new InvalidOperationException("节庆布景正在切换，请稍后重试。");
        var festivals = new HashSet<TerritorySceneFile.Festival>();
        foreach (var f in layout->ActiveFestivals)
        {
            // Festival is a pair of ushort values, matching the on-disk layer header.
            var value = f;
            var words = (ushort*)&value;
            if (words[0] != 0) festivals.Add(new(words[0], words[1]));
        }
        var layers = new List<TerritoryRuntimeLayer>();
        foreach (var (key, pointer) in layout->Layers)
        {
            var layer = pointer.Value;
            if (layer == null) continue;
            var nodes = new List<TerritoryRuntimeNode>();
            foreach (var (_, instance) in layer->Instances)
            {
                var node = Capture(instance.Value, null);
                if (node != null) nodes.Add(node);
            }
            layers.Add(new(layer->LayerGroupId, key, new(layer->FestivalId, layer->FestivalSubId), nodes.ToArray()));
        }
        return new(territoryId, layout->LayerFilterKey, layout->CfcId, festivals, layers.ToArray());

        TerritoryRuntimeNode? Capture(ILayoutInstance* instance, ParsedStain? inheritedStain)
        {
            if (instance == null || instance->Id.Type is not (InstanceType.BgPart or InstanceType.SharedGroup)) return null;
            var transform = instance->GetTransformImpl();
            if (transform == null) return null;
            var path = instance->GetPrimaryPath();
            var children = new List<TerritoryRuntimeNode>();
            ParsedBgPartsInstance? model = null;
            var stain = inheritedStain;
            if (instance->Id.Type == InstanceType.BgPart)
            {
                model = ParseBgPart((BgPartsLayoutInstance*)instance) as ParsedBgPartsInstance;
                if (model != null) model.Stain = inheritedStain;
            }
            else
            {
                var shared = (SharedGroupLayoutInstance*)instance;
                if (shared->StainInfo != null)
                {
                    var stainId = shared->StainInfo->ChosenStainIndex != 0
                        ? shared->StainInfo->ChosenStainIndex : shared->StainInfo->DefaultStainIndex;
                    if (stainId != 0) stain = StainProvider.GetStain(stainId) ?? inheritedStain;
                }
                foreach (var child in shared->Instances.Instances)
                {
                    if (child.Value == null) continue;
                    var node = Capture(child.Value->Instance, stain);
                    if (node != null) children.Add(node);
                }
            }
            return new((uint)instance->Id.Type, instance->Id.InstanceKey, instance->IndexInPrefab,
                instance->IsActive || (!instance->IsPrimaryLoaded() && instance->WantToBeActive()),
                new Transform(*transform), path.HasValue ? path.ToString() : "",
                stain, model, children.ToArray());
        }
    }
}

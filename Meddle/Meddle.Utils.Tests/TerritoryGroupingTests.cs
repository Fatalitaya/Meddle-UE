using Meddle.Utils.Export;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class TerritoryGroupingTests
{
    [TestMethod]
    public void RepeatedResource_DistinguishesRuntimeAppearanceEvenWithSameStainId()
    {
        var a = Item(1) with { LinearStain = new(.2f,.3f,.4f,1) };
        var b = Item(2) with { LinearStain = new(.5f,.3f,.4f,1) };
        var c = a with { Item = 3, LayoutColor = new(1,.5f,1,1) };
        var d = a with { Item = 4 };
        var e = a with { Item = 5, EmissivePower = .5f };
        var groups = TerritoryGrouping.Build([a,b,c,d,e], TerritoryGroupMode.RepeatedResource);
        Assert.HasCount(4, groups);
        CollectionAssert.AreEqual(new[] { 1,4 }, groups[0].Items);
    }

    private static TerritoryGroupItem<int> Item(int id, string path = "bg/tree.mdl", string material = "", int slot = -1,
        uint stain = 0, string layer = "bg.lgb:10") => new(id, path, material, slot, stain, layer);

    [TestMethod]
    public void RepeatedResource_PreservesEveryOccurrenceAcrossLayers()
    {
        var groups = TerritoryGrouping.Build([Item(1), Item(2, layer: "planmap.lgb:20"), Item(3, path: "BG/TREE.MDL")], TerritoryGroupMode.RepeatedResource);
        Assert.HasCount(1, groups);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, groups[0].Items);
    }

    [TestMethod]
    public void RepeatedResource_DistinguishesFullPathMaterialSlotAndStain()
    {
        var groups = TerritoryGrouping.Build(new[]
        {
            Item(1), Item(2, path: "bg/other/tree.mdl"), Item(3, material: "bg/red.mtrl", slot: 0),
            Item(4, material: "bg/red.mtrl", slot: 1), Item(5, stain: 12), Item(6, material: "bg/blue.mtrl", slot: 0)
        }, TerritoryGroupMode.RepeatedResource);
        Assert.HasCount(6, groups);
        Assert.AreEqual(6, groups.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void LayerGrouping_UsesSourceIdentityEvenWithRepeatedModels()
    {
        var groups = TerritoryGrouping.Build(new[] { Item(1), Item(2, path: "bg/rock.mdl"), Item(3, layer: "other.lgb:10") }, TerritoryGroupMode.Layer);
        Assert.HasCount(2, groups);
        CollectionAssert.AreEqual(new[] { 1, 2 }, groups[0].Items);
        CollectionAssert.AreEqual(new[] { 3 }, groups[1].Items);
    }

    [TestMethod]
    public void EveryModeRetainsExactlyTheSameOccurrences()
    {
        var items = new[] { Item(1), Item(2), Item(3, layer: "Terrain", path: "bg/map/bgplate/0000.mdl"), Item(4, stain: 8) };
        foreach (var mode in Enum.GetValues<TerritoryGroupMode>())
        {
            var groups = TerritoryGrouping.Build(items, mode);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4 }, groups.SelectMany(g => g.Items).ToArray());
            Assert.IsTrue(groups.All(g => g.Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0));
            if (mode == TerritoryGroupMode.All) Assert.HasCount(1, groups);
            if (mode == TerritoryGroupMode.Instance) Assert.HasCount(4, groups);
        }
    }
}

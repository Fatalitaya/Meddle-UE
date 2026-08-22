using System.Text.Json;
using Meddle.Utils.Audit;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class RuntimeCharacterSnapshotTests
{
    [TestMethod]
    public void Build_PreservesUnknownsAndAppearanceRelationships()
    {
        var session = Session(
            Observation("EquipmentVisibility", new Dictionary<string, object?> { ["slots"] = new object?[]
            {
                new Dictionary<string, object?> { ["slot"] = 0, ["slotName"] = "Head", ["equipped"] = true, ["visible"] = false, ["hidden"] = true, ["runtimeFlags"] = 4u },
                new Dictionary<string, object?> { ["slot"] = 1, ["slotName"] = "Body", ["equipped"] = false, ["visible"] = false, ["hidden"] = null }
            }}),
            Observation("VisorState", new Dictionary<string, object?> { ["supported"] = null, ["active"] = true, ["state"] = "active", ["affectedObjects"] = Array.Empty<object?>() }),
            Observation("Attachment", new Dictionary<string, object?> { ["attachments"] = new object?[]
            {
                new Dictionary<string, object?> { ["parent"] = "0x1", ["child"] = "0x2", ["socket"] = new Dictionary<string, object?> { ["boneIndex"] = 17, ["name"] = null }, ["transform"] = null }
            }}),
            Observation("Weapon", new Dictionary<string, object?> { ["weapons"] = new object?[]
            {
                new Dictionary<string, object?> { ["slot"] = "MainHand", ["visible"] = true, ["resource"] = "weapon.mdl", ["model"] = new object?[] { "weapon.mdl" }, ["materials"] = Array.Empty<object?>(), ["drawObject"] = "0x2" },
                new Dictionary<string, object?> { ["slot"] = "OffHand", ["visible"] = false, ["resource"] = null, ["model"] = null, ["materials"] = null, ["drawObject"] = null }
            }}),
            Observation("DrawObject", new Dictionary<string, object?> { ["models"] = new object?[]
            {
                new Dictionary<string, object?> { ["logicalPath"] = "chara/a.mdl", ["resource"] = "mods/a.mdl" }
            }}));

        var snapshot = RuntimeCharacterSnapshotBuilder.Build(session);
        Assert.AreEqual(EquipmentHideReason.HiddenUnknown, snapshot.Appearance.Equipment[0].HideReason);
        Assert.IsNull(snapshot.Appearance.Equipment[1].HideReason);
        Assert.IsNull(snapshot.Appearance.Visor.Supported);
        Assert.AreEqual(2, snapshot.Appearance.Components.Count);
        Assert.AreEqual("MainHand", snapshot.Appearance.Components[0].Slot);
        Assert.AreEqual(17, snapshot.Appearance.Components[0].Attachment!.BoneIndex);
        Assert.IsNull(snapshot.Appearance.Components[0].Attachment!.BoneNameIfAvailable);
        Assert.AreEqual("resolved", snapshot.Resources.Single().Provenance);
        Assert.AreEqual(true, snapshot.Resources.Single().OverrideDetected);

        var json = JsonSerializer.Serialize(snapshot, RuntimeAuditSerializer.JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<RuntimeCharacterSnapshot>(json, RuntimeAuditSerializer.JsonOptions);
        Assert.IsNotNull(roundTrip);
        Assert.AreEqual(RuntimeCharacterSnapshot.Contract, roundTrip!.Format);
    }

    [TestMethod]
    public void Build_RuntimeFlagReasonRequiresExplicitEvidence()
    {
        var session = Session(Observation("EquipmentVisibility", new Dictionary<string, object?> { ["slots"] = new object?[]
        {
            new Dictionary<string, object?> { ["slot"] = 0, ["equipped"] = true, ["visible"] = false, ["hidden"] = true, ["hiddenByRuntimeFlag"] = true }
        }}));
        Assert.AreEqual(EquipmentHideReason.HiddenByRuntimeFlag,
            RuntimeCharacterSnapshotBuilder.Build(session).Appearance.Equipment.Single().HideReason);
    }

    [TestMethod]
    public void Build_EqualLogicalAndResolvedPath_DoesNotInventOriginalProvenance()
    {
        var session = Session(Observation("DrawObject", new Dictionary<string, object?>
        {
            ["models"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["logicalPath"] = "chara/a.mdl",
                    ["resource"] = "chara/a.mdl"
                }
            }
        }));

        var resource = RuntimeCharacterSnapshotBuilder.Build(session).Resources.Single();

        Assert.IsNull(resource.OverrideDetected);
        Assert.AreEqual("unknown", resource.Provenance);
    }

    [TestMethod]
	public void Build_ModelSlotOrdinal_DoesNotInventSemanticNodeType()
    {
        var session = Session(Observation("DrawObject", new Dictionary<string, object?>
        {
            ["models"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["slotIndex"] = 0u,
                    ["modelAddress"] = "0x10",
                    ["resource"] = "chara/a.mdl"
                }
            }
        }));

		Assert.AreEqual(
			"AppearanceComponent",
			RuntimeCharacterSnapshotBuilder.Build(session).Appearance.Attachments.Nodes.Single(n => n.Id == "0x10").Type);
	}

	[TestMethod]
	public void Build_WeaponArrayOrdinalWithoutSemanticEvidence_StaysUnknown()
	{
		var session = Session(Observation("Weapon", new Dictionary<string, object?>
		{
			["weapons"] = new object?[]
			{
				new Dictionary<string, object?>
				{
					["weaponIndex"] = 0,
					["slot"] = null,
					["semanticSlotStatus"] = "Unknown",
					["visible"] = true
				}
			}
		}));

		Assert.AreEqual("unknown", RuntimeCharacterSnapshotBuilder.Build(session).Appearance.Components.Single().Slot);
	}

    private static RuntimeAuditSession Session(params RuntimeAuditObservation[] observations) => new()
    {
        CaptureId = "snapshot", CapturedAtUtc = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
        ActorId = "0x1", ActorName = "Test", ObjectKind = "Pc", Observations = observations.ToList()
    };

    private static RuntimeAuditObservation Observation(string source, object value) => new()
    {
        Time = "2026-08-21T00:00:00Z", ActorId = "0x1", Category = "test", SourceType = source,
        Value = value, Availability = RuntimeAuditAvailability.Available
    };
}

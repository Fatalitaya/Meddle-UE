using Meddle.Utils.Audit;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class LinkedAttachTraversalTests
{
    private const nint VTable = 0xCAFE;
    private const nint A = 0x1000;
    private const nint B = 0x2000;
    private const nint C = 0x3000;

    [TestMethod]
    public void Traverse_NormalList_ReturnsHeadAndForwardOrder()
    {
        var reader = Reader(
            (A, Node(A, previous: 0, next: B)),
            (B, Node(B, previous: A, next: C)),
            (C, Node(C, previous: B, next: 0)));

        var result = LinkedAttachTraversal.Traverse(B, VTable, reader);

        Assert.AreEqual(LinkedAttachTraversalStatus.Completed, result.Status);
        CollectionAssert.AreEqual(
            new[] { A, B, C },
            result.Nodes.Select(n => n.Address).ToArray());
    }

    [TestMethod]
    public void Traverse_SingleNode_ReturnsCompleted()
    {
        var reader = Reader((A, Node(A, previous: 0, next: 0)));

        var result = LinkedAttachTraversal.Traverse(A, VTable, reader);

        Assert.AreEqual(LinkedAttachTraversalStatus.Completed, result.Status);
        Assert.AreEqual(1, result.Nodes.Count);
        Assert.AreEqual(A, result.Nodes[0].Address);
    }

    [TestMethod]
    public void Traverse_ForwardCycle_ReturnsCycleDetected()
    {
        var reader = Reader(
            (A, Node(A, previous: 0, next: B)),
            (B, Node(B, previous: A, next: C)),
            (C, Node(C, previous: B, next: B)));

        var result = LinkedAttachTraversal.Traverse(A, VTable, reader);

        Assert.AreEqual(LinkedAttachTraversalStatus.CycleDetected, result.Status);
        CollectionAssert.AreEqual(
            new[] { A, B, C },
            result.Nodes.Select(n => n.Address).ToArray());
    }

    [TestMethod]
    public void Traverse_PreviousCycle_ReturnsCycleDetected()
    {
        var reader = Reader(
            (A, Node(A, previous: B, next: 0)),
            (B, Node(B, previous: A, next: 0)));

        var result = LinkedAttachTraversal.Traverse(A, VTable, reader);

        Assert.AreEqual(LinkedAttachTraversalStatus.CycleDetected, result.Status);
    }

    [TestMethod]
    public void Traverse_OverBudget_ReturnsBudgetExceeded()
    {
        const int total = LinkedAttachTraversal.MaxAttachTraversalNodes * 4;
        var nodes = new Dictionary<nint, AttachNodeData>();
        for (var i = 0; i < total; i++)
        {
            var address = 0x10000 + (i * 0x100);
            var previous = i == 0 ? 0 : 0x10000 + ((i - 1) * 0x100);
            var next = i == total - 1 ? 0 : 0x10000 + ((i + 1) * 0x100);
            nodes[address] = Node(address, previous, next);
        }

        var result = LinkedAttachTraversal.Traverse(0x10000, VTable, new FakeReader(nodes));

        Assert.AreEqual(LinkedAttachTraversalStatus.BudgetExceeded, result.Status);
        Assert.IsTrue(result.Nodes.Count > 0);
        Assert.IsTrue(result.Nodes.Count < total);
    }

    [TestMethod]
    public void Traverse_StartVTableMismatch_ReturnsInvalidStartNode()
    {
        var reader = Reader((A, Node(A, previous: 0, next: 0, vtable: VTable + 1)));

        var result = LinkedAttachTraversal.Traverse(A, VTable, reader);

        Assert.AreEqual(LinkedAttachTraversalStatus.InvalidStartNode, result.Status);
        Assert.AreEqual(0, result.Nodes.Count);
    }

    [TestMethod]
    public void Traverse_StartVTableMismatch_DoesNotReadPayload()
    {
        var reader = Reader((A, Node(A, previous: 0, next: 0, vtable: VTable + 1)));

        var result = LinkedAttachTraversal.Traverse(A, VTable, reader);

        Assert.AreEqual(LinkedAttachTraversalStatus.InvalidStartNode, result.Status);
        Assert.AreEqual(0, reader.PayloadReadCount);
    }

    [TestMethod]
    public void Traverse_MidChainVTableMismatch_StopsBeforeFollowingInvalidNext()
    {
        var reader = Reader(
            (A, Node(A, previous: 0, next: B)),
            (B, Node(B, previous: A, next: C, vtable: VTable + 1)),
            (C, Node(C, previous: B, next: 0)));

        var result = LinkedAttachTraversal.Traverse(A, VTable, reader);

        Assert.AreEqual(LinkedAttachTraversalStatus.VTableMismatch, result.Status);
        Assert.AreEqual(1, result.Nodes.Count);
        Assert.AreEqual(A, result.Nodes[0].Address);
    }

    [TestMethod]
    public void Traverse_MidChainVTableMismatch_DoesNotReadMiddlePayload()
    {
        var reader = Reader(
            (A, Node(A, previous: 0, next: B)),
            (B, Node(B, previous: A, next: C, vtable: VTable + 1)),
            (C, Node(C, previous: B, next: 0)));

        var result = LinkedAttachTraversal.Traverse(A, VTable, reader);

        Assert.AreEqual(LinkedAttachTraversalStatus.VTableMismatch, result.Status);
        CollectionAssert.AreEqual(new[] { A }, result.Nodes.Select(n => n.Address).ToArray());
        Assert.IsFalse(reader.PayloadReadAddresses.Contains(B));
    }

    [TestMethod]
    public void Traverse_NoVTableCapability_ReturnsUnavailable()
    {
        var reader = Reader((A, Node(A, previous: 0, next: 0)));

        var result = LinkedAttachTraversal.Traverse(A, expectedVTable: 0, reader);

        Assert.AreEqual(LinkedAttachTraversalStatus.Unavailable, result.Status);
    }

    [TestMethod]
    public void InterpretExecuteType_UnknownValue_ReturnsUnknownNotError()
    {
        var (interpretation, status) = LinkedAttachSemantics.InterpretExecuteType(2);

        Assert.IsNull(interpretation);
        Assert.AreEqual("Unknown", status);
    }

    [TestMethod]
    public void InterpretExecuteType_Weapon_ReturnsObservedWeapon()
    {
        var (interpretation, status) = LinkedAttachSemantics.InterpretExecuteType(4);

        Assert.AreEqual("Weapon", interpretation);
        Assert.AreEqual("Observed", status);
    }

    [TestMethod]
    public void KnownSet_IncludesWeaponDrawObjectRegardlessOfVisibility()
    {
        // Phase A: weapon existence is a non-zero DrawData.DrawObject. Visibility
        // is intentionally not part of the known-set input, so a hidden weapon
        // (same non-zero draw object) is still considered known.
        var known = LinkedAttachKnownSet.Build(
            character: 0x10,
            owner: 0x20,
            weaponDrawObjects: [0x30, 0x40],
            mountDrawObject: 0,
            companionDrawObject: 0,
            ornamentDrawObject: 0);

        Assert.IsTrue(known.Contains(0x30));
        Assert.IsTrue(known.Contains(0x40));
        Assert.IsFalse(known.Contains(0));
    }

    [TestMethod]
    public void CombineAttachmentAvailability_ExplicitAvailableAndLinkedCompleted_IsAvailable()
    {
        var status = LinkedAttachAvailability.Combine(
            explicitAvailable: true,
            linkedAvailability: RuntimeAuditAvailability.Available);

        Assert.AreEqual(RuntimeAuditAvailability.Available, status);
    }

    [TestMethod]
    public void CombineAttachmentAvailability_ExplicitAvailableAndLinkedUnavailable_IsUnknown()
    {
        var status = LinkedAttachAvailability.Combine(
            explicitAvailable: true,
            linkedAvailability: RuntimeAuditAvailability.Unavailable);

        Assert.AreEqual(RuntimeAuditAvailability.Unknown, status);
    }

    [TestMethod]
    public void CombineAttachmentAvailability_ExplicitAvailableAndLinkedUnknown_IsUnknown()
    {
        var status = LinkedAttachAvailability.Combine(
            explicitAvailable: true,
            linkedAvailability: RuntimeAuditAvailability.Unknown);

        Assert.AreEqual(RuntimeAuditAvailability.Unknown, status);
    }

    private static AttachNodeData Node(
        nint address,
        nint previous,
        nint next,
        int executeType = 0,
        nint vtable = VTable) => new(
        address,
        vtable,
        previous,
        next,
        executeType,
        TargetSkeleton: 0,
        OwnerUnion: 0,
        AttachmentCount: 0,
        AttachmentsPointer: 0);

    private static FakeReader Reader(params (nint, AttachNodeData)[] entries)
    {
        return new FakeReader(entries.ToDictionary(e => e.Item1, e => e.Item2));
    }

    private sealed class FakeReader : IAttachNodeReader
    {
        private readonly IReadOnlyDictionary<nint, AttachNodeData> nodes;

        public FakeReader(IReadOnlyDictionary<nint, AttachNodeData> nodes)
        {
            this.nodes = nodes;
        }

        public int PayloadReadCount { get; private set; }

        public List<nint> PayloadReadAddresses { get; } = [];

        public bool TryReadVTable(nint address, out nint vtable)
        {
            if (nodes.TryGetValue(address, out var node))
            {
                vtable = node.VTable;
                return true;
            }

            vtable = 0;
            return false;
        }

        public bool TryReadPayload(nint address, nint vtable, out AttachNodeData node)
        {
            PayloadReadCount++;
            PayloadReadAddresses.Add(address);
            return nodes.TryGetValue(address, out node);
        }
    }
}

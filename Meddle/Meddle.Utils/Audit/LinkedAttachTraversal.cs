namespace Meddle.Utils.Audit;

/// <summary>
/// Raw, untyped snapshot of one native <c>MeddleAttach</c> node. Pointer-like
/// fields are kept as <see cref="nint"/> so the traversal algorithm stays
/// independent of FFXIVClientStructs and can be unit tested without a game
/// process.
/// </summary>
public readonly record struct AttachNodeData(
    nint Address,
    nint VTable,
    nint Previous,
    nint Next,
    int ExecuteType,
    nint TargetSkeleton,
    nint OwnerUnion,
    int AttachmentCount,
    nint AttachmentsPointer);

/// <summary>
/// Reads one native attach node in two phases so callers never dereference the
/// node payload before validating its vtable. The production implementation
/// performs the unsafe dereferences; tests provide an in-memory fake that can
/// observe whether the payload was read.
/// </summary>
public interface IAttachNodeReader
{
    /// <summary>Reads only the vtable pointer at <paramref name="address"/>.</summary>
    bool TryReadVTable(nint address, out nint vtable);

    /// <summary>
    /// Reads the remaining node payload at <paramref name="address"/>. The
    /// caller must have already validated <paramref name="vtable"/>; this method
    /// must not dereference any payload field when the address is untrusted.
    /// </summary>
    bool TryReadPayload(nint address, nint vtable, out AttachNodeData node);
}

public enum LinkedAttachTraversalStatus
{
    Completed,
    Unavailable,
    CycleDetected,
    BudgetExceeded,
    VTableMismatch,
    InvalidStartNode,
    Failed
}

/// <summary>
/// Result of a bounded linked-list traversal. <see cref="Nodes"/> contains the
/// valid nodes that were safely read before termination. An incomplete
/// traversal is never reported as an empty <see cref="LinkedAttachTraversalStatus.Completed"/> result.
/// </summary>
public sealed record LinkedAttachTraversalResult(
    LinkedAttachTraversalStatus Status,
    IReadOnlyList<AttachNodeData> Nodes,
    string? Reason);

/// <summary>
/// Bounded, cycle-aware, vtable-validated traversal of the native linked
/// attach structure. This is the Phase B safety boundary: nodes are only
/// dereferenced (beyond their vtable) after their vtable matches the expected
/// attach vtable, and every previous/next pointer is followed only within a
/// hard budget with cycle detection.
/// </summary>
public static class LinkedAttachTraversal
{
    /// <summary>
    /// Hard limit on the total number of node reads across both the reverse
    /// (find head) and forward (collect) passes.
    /// </summary>
    public const int MaxAttachTraversalNodes = 128;

    public static LinkedAttachTraversalResult Traverse(
        nint startAddress,
        nint expectedVTable,
        IAttachNodeReader reader)
    {
        if (startAddress == 0)
        {
            return Result(LinkedAttachTraversalStatus.Unavailable, [], "start node address is null");
        }

        if (expectedVTable == 0)
        {
            return Result(LinkedAttachTraversalStatus.Unavailable, [], "attach vtable capability unavailable");
        }

        int steps = 0;
        if (++steps > MaxAttachTraversalNodes)
        {
            return Result(
                LinkedAttachTraversalStatus.BudgetExceeded,
                [],
                $"exceeded max attach traversal nodes ({MaxAttachTraversalNodes})");
        }

        if (!ReadNode(startAddress, expectedVTable, reader, out var startNode, out var startError))
        {
            return Result(
                startError == LinkedAttachTraversalStatus.VTableMismatch
                    ? LinkedAttachTraversalStatus.InvalidStartNode
                    : startError,
                [],
                startError == LinkedAttachTraversalStatus.VTableMismatch
                    ? $"start node 0x{startAddress:X} vtable does not match expected 0x{expectedVTable:X}"
                    : $"failed to read start node at 0x{startAddress:X}");
        }

        // Walk back to the head, validating vtable before trusting any node's
        // Previous pointer.
        var reverseVisited = new HashSet<nint> { startAddress };
        var headAddress = startAddress;
        var headNode = startNode;
        while (headNode.Previous != 0)
        {
            var previousAddress = headNode.Previous;
            if (!reverseVisited.Add(previousAddress))
            {
                return Result(
                    LinkedAttachTraversalStatus.CycleDetected,
                    [],
                    $"cycle detected while walking previous pointers at 0x{previousAddress:X}");
            }

            if (++steps > MaxAttachTraversalNodes)
            {
                return Result(
                    LinkedAttachTraversalStatus.BudgetExceeded,
                    [],
                    $"exceeded max attach traversal nodes ({MaxAttachTraversalNodes})");
            }

            if (!ReadNode(previousAddress, expectedVTable, reader, out var previousNode, out var previousError))
            {
                return Result(
                    previousError,
                    [],
                    previousError == LinkedAttachTraversalStatus.VTableMismatch
                        ? $"previous node 0x{previousAddress:X} vtable does not match expected 0x{expectedVTable:X}"
                        : $"failed to read previous node at 0x{previousAddress:X}");
            }

            headAddress = previousAddress;
            headNode = previousNode;
        }

        // Walk forward from the head, collecting valid nodes.
        var forwardVisited = new HashSet<nint>();
        var nodes = new List<AttachNodeData>();
        var currentAddress = headAddress;
        while (currentAddress != 0)
        {
            if (!forwardVisited.Add(currentAddress))
            {
                return Result(
                    LinkedAttachTraversalStatus.CycleDetected,
                    nodes,
                    $"cycle detected in forward traversal at 0x{currentAddress:X}");
            }

            if (++steps > MaxAttachTraversalNodes)
            {
                return Result(
                    LinkedAttachTraversalStatus.BudgetExceeded,
                    nodes,
                    $"exceeded max attach traversal nodes ({MaxAttachTraversalNodes})");
            }

            AttachNodeData currentNode;
            if (currentAddress == headAddress)
            {
                currentNode = headNode;
            }
            else if (!ReadNode(currentAddress, expectedVTable, reader, out currentNode, out var currentError))
            {
                return Result(
                    currentError,
                    nodes,
                    currentError == LinkedAttachTraversalStatus.VTableMismatch
                        ? $"node 0x{currentAddress:X} vtable does not match expected 0x{expectedVTable:X}"
                        : $"failed to read node at 0x{currentAddress:X}");
            }

            nodes.Add(currentNode);
            currentAddress = currentNode.Next;
        }

        return Result(LinkedAttachTraversalStatus.Completed, nodes, null);
    }

    private static bool ReadNode(
        nint address,
        nint expectedVTable,
        IAttachNodeReader reader,
        out AttachNodeData node,
        out LinkedAttachTraversalStatus errorStatus)
    {
        node = default;
        errorStatus = LinkedAttachTraversalStatus.Failed;

        if (address == 0)
        {
            return false;
        }

        if (!reader.TryReadVTable(address, out var actualVTable))
        {
            return false;
        }

        if (actualVTable != expectedVTable)
        {
            errorStatus = LinkedAttachTraversalStatus.VTableMismatch;
            return false;
        }

        if (!reader.TryReadPayload(address, actualVTable, out node))
        {
            return false;
        }

        return true;
    }

    private static LinkedAttachTraversalResult Result(
        LinkedAttachTraversalStatus status,
        IReadOnlyList<AttachNodeData> nodes,
        string? reason) => new(status, nodes, reason);
}

/// <summary>
/// Reverse-engineered ExecuteType observations. Values are recorded as raw
/// evidence and must not be promoted into an unproven domain enum.
/// </summary>
public static class LinkedAttachSemantics
{
    public static (string? Interpretation, string Status) InterpretExecuteType(int executeType) => executeType switch
    {
        0 => ("Root/Mount/UnmountedPlayer", "Observed"),
        3 => ("Ornament/MountedPlayer", "Observed"),
        4 => ("Weapon", "Observed"),
        _ => (null, "Unknown")
    };
}

/// <summary>
/// Builds the known-object exclusion set for linked-attach resolution. The
/// caller supplies weapon draw objects by address. Existence is represented by
/// a non-zero address only; visibility (<c>IsHidden</c>) is intentionally not a
/// parameter because Phase A established that hidden weapons still exist.
/// </summary>
public static class LinkedAttachKnownSet
{
    public static IReadOnlySet<nint> Build(
        nint character,
        nint owner,
        IEnumerable<nint> weaponDrawObjects,
        nint mountDrawObject,
        nint companionDrawObject,
        nint ornamentDrawObject)
    {
        var known = new HashSet<nint>();
        Add(known, character);
        Add(known, owner);
        foreach (var weapon in weaponDrawObjects)
        {
            Add(known, weapon);
        }

        Add(known, mountDrawObject);
        Add(known, companionDrawObject);
        Add(known, ornamentDrawObject);
        return known;
    }

    private static void Add(HashSet<nint> set, nint address)
    {
        if (address != 0)
        {
            set.Add(address);
        }
    }
}

/// <summary>
/// Aggregation policy for the Attachment audit category. Explicit evidence and
/// the optional linked traversal are separate layers: incomplete linked
/// evidence downgrades the category to <see cref="RuntimeAuditAvailability.Unknown"/>,
/// but it never erases an explicit <see cref="RuntimeAuditAvailability.Available"/>
/// observation.
/// </summary>
public static class LinkedAttachAvailability
{
    public static RuntimeAuditAvailability Combine(
        bool explicitAvailable,
        RuntimeAuditAvailability linkedAvailability)
    {
        if (!explicitAvailable)
        {
            return RuntimeAuditAvailability.Unavailable;
        }

        return linkedAvailability == RuntimeAuditAvailability.Available
            ? RuntimeAuditAvailability.Available
            : RuntimeAuditAvailability.Unknown;
    }
}

namespace Meddle.Utils.Export.Ue;

public enum DeformerObservationState
{
    /// <summary>
    /// Runtime hook observed a valid deformer instance.
    /// </summary>
    Observed,

    /// <summary>
    /// Runtime hook observed that the character has no deformer.
    /// </summary>
    ObservedNone,

    /// <summary>
    /// Runtime hook executed but returned incomplete information.
    /// </summary>
    ObservedIncomplete,

    /// <summary>
    /// No runtime observation was captured.
    /// </summary>
    NotObserved,

    /// <summary>
    /// Object type does not support deformers.
    /// </summary>
    NotApplicable
}
public sealed record DeformerObservation
{
    public DeformerObservationState State { get; init; }
    public ushort? RaceSexId { get; init; }
    public ushort? DeformerId { get; init; }
    public string? PbdPath { get; init; }
}

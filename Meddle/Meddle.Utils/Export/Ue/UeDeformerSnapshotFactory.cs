namespace Meddle.Utils.Export.Ue;

public static class UeDeformerStatus
{
    public const string Observed = "Observed";
    public const string ObservedNone = "ObservedNone";
    public const string ObservedIncomplete = "ObservedIncomplete";
    public const string NotObserved = "NotObserved";
    public const string NotApplicable = "NotApplicable";
}

public static class UeDeformerSnapshotFactory
{
    public static UeDeformerSnapshot FromObservation(
        DeformerObservation? observation,
        bool isHuman)
    {
        if (!isHuman)
        {
            return NotApplicable();
        }

        if (observation == null)
        {
            return NotObserved();
        }

        return observation.State switch
        {
            DeformerObservationState.Present => Observed(observation),
            DeformerObservationState.Absent => ObservedNone(),
            DeformerObservationState.Incomplete => ObservedIncomplete(observation),
            _ => NotObserved()
        };
    }

    public static UeDeformerSnapshot NotApplicable()
    {
        return new UeDeformerSnapshot
        {
            Status = UeDeformerStatus.NotApplicable
        };
    }

    public static UeDeformerSnapshot Observed(DeformerObservation observation)
    {
        return new UeDeformerSnapshot
        {
            Status = UeDeformerStatus.Observed,
            RaceSexId = observation.RaceSexId,
            DeformerId = observation.DeformerId,
            PbdResourcePath = observation.PbdPath
        };
    }

    public static UeDeformerSnapshot ObservedNone()
    {
        return new UeDeformerSnapshot
        {
            Status = UeDeformerStatus.ObservedNone
        };
    }

    public static UeDeformerSnapshot ObservedIncomplete(DeformerObservation observation)
    {
        return new UeDeformerSnapshot
        {
            Status = UeDeformerStatus.ObservedIncomplete,
            RaceSexId = observation.RaceSexId,
            DeformerId = observation.DeformerId,
            PbdResourcePath = observation.PbdPath
        };
    }

    public static UeDeformerSnapshot NotObserved()
    {
        return new UeDeformerSnapshot
        {
            Status = UeDeformerStatus.NotObserved
        };
    }
}

namespace Meddle.Utils.Audit;

/// <summary>
/// Observation categories collected by the runtime audit framework.
/// Each collector reports whether its data was Available, Unavailable or Unknown.
/// </summary>
public enum RuntimeAuditCategory
{
    /// <summary>Actual runtime model graph: CharacterBase / DrawObject / Model resources.</summary>
    CharacterComposition,

    /// <summary>Runtime skeleton composition evidence. Evidence only; no SKLB parsing.</summary>
    SkeletonEvidence,

    /// <summary>Runtime material state: shader identity, texture bindings, exposed parameters.</summary>
    Material,

    /// <summary>Runtime texture bindings and identities.</summary>
    RuntimeTexture,

    /// <summary>Runtime deformation information: deformer objects, morph systems, inputs.</summary>
    Deformer,

    /// <summary>Currently resolved character appearance data in FFXIV-native representation.</summary>
    Customize,
    /// <summary>Per-slot equipped, enabled and visible runtime state.</summary>
    EquipmentVisibility,

    /// <summary>Runtime visor flags and observable affected draw slots.</summary>
    VisorState,

    /// <summary>Runtime draw-object attachment relationships.</summary>
    Attachment,

    /// <summary>Main-hand and off-hand runtime draw objects.</summary>
    Weapon
}

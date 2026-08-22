namespace Meddle.Utils.Audit;

/// <summary>
/// Pure, testable description of a draw-object root whose models must be
/// visited by the material and runtime-texture collectors. OwnerKind comes
/// from an explicit runtime owning field; it is not inferred from a model slot
/// or resource name.
/// </summary>
public readonly record struct RuntimeMaterialRootEvidence(
    nint Address,
    string OwnerKind,
    int? WeaponIndex);

public readonly record struct RuntimeWeaponDrawObjectEvidence(
    nint Address,
    int WeaponIndex,
    bool IsCharacterBase);

public static class RuntimeMaterialRootTraversal
{
    public static IReadOnlyList<RuntimeMaterialRootEvidence> Build(
        nint actorCharacterBase,
        IEnumerable<RuntimeWeaponDrawObjectEvidence> weapons)
    {
        var result = new List<RuntimeMaterialRootEvidence>();
        var seen = new HashSet<nint>();
        if (actorCharacterBase != 0 && seen.Add(actorCharacterBase))
        {
            result.Add(new RuntimeMaterialRootEvidence(actorCharacterBase, "Actor", null));
        }

        foreach (var weapon in weapons)
        {
            if (weapon.Address == 0 || !weapon.IsCharacterBase || !seen.Add(weapon.Address))
            {
                continue;
            }

            result.Add(new RuntimeMaterialRootEvidence(
                weapon.Address,
                "Weapon",
                weapon.WeaponIndex));
        }

        return result;
    }
}

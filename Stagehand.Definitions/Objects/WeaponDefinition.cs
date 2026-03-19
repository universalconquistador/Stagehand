using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Definitions.Objects;

/// <summary>
/// The definition of a weapon in a Stage definition.
/// </summary>
public class WeaponDefinition : ObjectDefinition
{
    /// <summary>
    /// The ID of the model set of the weapon model to create.
    /// </summary>
    public int ModelSetId { get; set; }

    /// <summary>
    /// The secondary ID of the weapon model to create.
    /// </summary>
    public int SecondaryId { get; set; }

    /// <summary>
    /// The variant ID of the weapon model to create.
    /// </summary>
    public int Variant { get; set; }

    /// <summary>
    /// The index of the dye to use for the primary dye channel, or 0 to leave it undyed.
    /// </summary>
    public int PrimaryDye { get; set; }

    /// <summary>
    /// The index of the dye to use for the secondary dye channel, or 0 to leave it undyed.
    /// </summary>
    public int SecondaryDye { get; set; }

    /// <summary>
    /// Which animation variant to use for the weapon.
    /// </summary>
    public int AnimationVariant { get; set; }

    /// <inheritdoc/>
    public override ObjectDefinition Clone()
    {
        var result = new WeaponDefinition();
        CopyTo(result);
        return result;
    }

    /// <inheritdoc/>
    public override void CopyTo(ObjectDefinition other)
    {
        base.CopyTo(other);

        if (other is WeaponDefinition otherWeapon)
        {
            otherWeapon.ModelSetId = ModelSetId;
            otherWeapon.SecondaryId = SecondaryId;
            otherWeapon.Variant = Variant;
            otherWeapon.PrimaryDye = PrimaryDye;
            otherWeapon.SecondaryDye = SecondaryDye;
            otherWeapon.AnimationVariant = AnimationVariant;
        }
    }

    public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
    {
        return TVisitor.VisitWeaponDefinition(this, ref param);
    }
}

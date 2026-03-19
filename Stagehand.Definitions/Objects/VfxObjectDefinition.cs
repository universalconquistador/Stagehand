using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Definitions.Objects;

/// <summary>
/// The definition of a VFX object in a Stage definition.
/// </summary>
public class VfxObjectDefinition : ObjectDefinition
{
    /// <summary>
    /// The game path of the .avfx resource to show.
    /// </summary>
    public string VfxGamePath { get; set; } = "";

    /// <summary>
    /// The color to tint the VFX.
    /// </summary>
    /// <remarks>
    /// Use <see cref="Vector4.One"/> to not tint the VFX at all.
    /// </remarks>
    public Vector4 Color { get; set; } = Vector4.One;

    /// <inheritdoc/>
    public override ObjectDefinition Clone()
    {
        var result = new VfxObjectDefinition();
        CopyTo(result);
        return result;
    }

    /// <inheritdoc/>
    public override void CopyTo(ObjectDefinition other)
    {
        base.CopyTo(other);

        if (other is VfxObjectDefinition otherVfxObject)
        {
            otherVfxObject.VfxGamePath = VfxGamePath;
            otherVfxObject.Color = Color;
        }
    }

    public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
    {
        return TVisitor.VisitVfxObjectDefinition(this, ref param);
    }
}

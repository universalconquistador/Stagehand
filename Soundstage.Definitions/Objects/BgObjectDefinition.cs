using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Soundstage.Definitions.Objects;

/// <summary>
/// The definition of a background object in a Soundstage definition.
/// </summary>
public class BgObjectDefinition : ObjectDefinition
{
    /// <summary>
    /// The game path of the model for the object.
    /// </summary>
    public string ModelGamePath { get; set; } = "";

    /// <summary>
    /// The dither opacity of the object, between fully opaque at 1.0 and fully transparent at 0.0.
    /// </summary>
    public float Opacity { get; set; } = 1.0f;

    /// <summary>
    /// The color to dye the object's model, as RGBA values from 0.0 to 1.0.
    /// </summary>
    public Vector4 DyeColor { get; set; } = Vector4.One;

    public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
    {
        return TVisitor.VisitBgObjectDefinition(this, ref param);
    }
}

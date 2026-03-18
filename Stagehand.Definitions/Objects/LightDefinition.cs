using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Definitions.Objects;

/// <summary>
/// The different shapes that a light can have.
/// </summary>
public enum LightShape
{
    /// <summary>
    /// A light that illuminates the entire scene evenly.
    /// </summary>
    /// <remarks>
    /// Seems to only function in exterior location.
    /// </remarks>
    Ambient,

    /// <summary>
    /// A light that illuminates objects in all directions from its center.
    /// </summary>
    Point,

    /// <summary>
    /// A light that illuminates objects in a cone in the direction of its +Z axis.
    /// </summary>
    Spot,

    /// <summary>
    /// A light that illuminates objects in a paralleliped in the direction of its +Z axis.
    /// </summary>
    Flat,
}

/// <summary>
/// The different ways a light's intensity falls off with distance.
/// </summary>
public enum LightFalloffFunction
{
    /// <summary>
    /// The light's intensity decreases linearly with the distance to the surface being lit.
    /// </summary>
    Linear,

    /// <summary>
    /// The light's intensity decreases quadratically with the distance to the surface being lit.
    /// </summary>
    Quadratic,

    /// <summary>
    /// The light's intensity decreases with the cube of the distance to the surface being lit.
    /// </summary>
    Cubic,
}

/// <summary>
/// The definition of a light in a Stage definition.
/// </summary>
public class LightDefinition : ObjectDefinition
{
    /// <summary>
    /// Whether the light casts specular highlights on objects.
    /// </summary>
    public bool EnableSpecularHighlights { get; set; } = true;

    /// <summary>
    /// Whether dynamic objects cast shadows from this light.
    /// </summary>
    public bool EnableDynamicShadows { get; set; } = true;

    /// <summary>
    /// Whether characters cast shadows from this light.
    /// </summary>
    public bool EnableCharacterShadows { get; set; } = true;

    /// <summary>
    /// Whether static objects cast shadows from this light.
    /// </summary>
    public bool EnableObjectShadows { get; set; } = true;

    /// <summary>
    /// The shape of the illumination from this light.
    /// </summary>
    public LightShape Shape { get; set; } = LightShape.Point;

    /// <summary>
    /// The color of the light.
    /// </summary>
    public Vector3 Color { get; set; } = Vector3.One;

    /// <summary>
    /// The intensity of the light.
    /// </summary>
    public float Intensity { get; set; } = 4.0f;

    // TODO: Could include AxisAlignedBounds but for now let's assume they are unlimited

    /// <summary>
    /// The distance to the beginning of the shadow frustum, or the closest distance that will cast a shadow.
    /// </summary>
    /// <remarks>
    /// The closer this is to zero, the exponentially less depth precision the shadows will have, so set it as far away as you reasonably can.
    /// </remarks>
    public float ShadowPlaneNear { get; set; } = 0.05f;

    /// <summary>
    /// The distance to the end of the shadow frustum, or the farthest distance that will cast a shadow.
    /// </summary>
    /// <remarks>
    /// The farther this is from <see cref="ShadowPlaneNear"/>, the less precision the shadows will have, so set it as near as you reasonably can.
    /// </remarks>
    public float ShadowPlaneFar { get; set; } = 100.0f;

    /// <summary>
    /// How the light's intensity decreases with the distance to the surface being lit.
    /// </summary>
    /// <remarks>
    /// This combines with <see cref="FalloffFactor"/> and is clamped by <see cref="Range"/> to determine the final falloff.
    /// </remarks>
    public LightFalloffFunction FalloffFunction { get; set; } = LightFalloffFunction.Quadratic;

    /// <summary>
    /// The X and Y angles that the light paralleliped is skewed by, if its <see cref="Shape"/> is <see cref="LightShape.Flat"/>.
    /// </summary>
    public Vector2 FlatLightSkewAngleDegrees { get; set; }

    /// <summary>
    /// A scalar to increases or decrease the amount that the light gets dimmer with distance to the surface being lit.
    /// </summary>
    /// <remarks>
    /// This combines with <see cref="FalloffFunction"/> and is clamped by <see cref="Range"/> to determine the final falloff.
    /// </remarks>
    public float FalloffFactor { get; set; } = 1.0f;

    /// <summary>
    /// The angle of the cone of full brightness from a light, if its <see cref="Shape"/> is <see cref="LightShape.Spot"/>.
    /// </summary>
    public float SpotLightAngleDegrees { get; set; } = 30.0f;

    /// <summary>
    /// The angle over which the cone of light dims from full brightness to none, if its <see cref="Shape"/> is <see cref="LightShape.Spot"/> or <see cref="LightShape.Flat"/>.
    /// </summary>
    public float AngularFalloffDegrees { get; set; } = 5.0f;

    /// <summary>
    /// The maximum distance that the light can reach.
    /// </summary>
    /// <remarks>
    /// This clamps the combination of <see cref="FalloffFunction"/> and <see cref="FalloffFactor"/> to determine the final falloff.
    /// </remarks>
    public float Range { get; set; } = 100.0f;

    /// <summary>
    /// The distance that characters must be within to cast shadows.
    /// </summary>
    public float CharacterShadowRange { get; set; } = 100.0f;

    public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
    {
        return TVisitor.VisitLightDefinition(this, ref param);
    }
}

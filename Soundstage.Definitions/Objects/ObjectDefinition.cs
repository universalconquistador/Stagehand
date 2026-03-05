using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;

namespace Soundstage.Definitions.Objects;

/// <summary>
/// The base class for the definition of an object in a soundstage definition.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(BgObjectDefinition), typeDiscriminator: "BgObject")]
[JsonDerivedType(typeof(LightDefinition), typeDiscriminator: "Light")]
[JsonDerivedType(typeof(VfxObjectDefinition), typeDiscriminator: "VfxObject")]
[JsonDerivedType(typeof(WeaponDefinition), typeDiscriminator: "Weapon")]
public abstract class ObjectDefinition
{
    /// <summary>
    /// A user-facing name to identify this object when editing. Can be blank and doesn't have to be unique.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// The position of this object in world space.
    /// </summary>
    public Vector3 Position { get; set; } = Vector3.Zero;

    /// <summary>
    /// The rotation of this object in world space, given as pitch, yaw, and roll angles in degrees.
    /// </summary>
    public Vector3 RotationPitchYawRollDegrees { get; set; } = Vector3.Zero;

    /// <summary>
    /// The world-space scale of this object along its X, Y, and Z axes.
    /// </summary>
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>
    /// Computes the rotation of this object in world space as a quaternion.
    /// </summary>
    public Quaternion RotationQuaternion => Quaternion.CreateFromYawPitchRoll(DegreesToRadians(RotationPitchYawRollDegrees.Y), DegreesToRadians(RotationPitchYawRollDegrees.X), DegreesToRadians(RotationPitchYawRollDegrees.Z));

    // TODO: Penumbra support
    //public Guid PenumbraCollection { get; set; } = Guid.Empty;

    public abstract TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        where TVisitor : IObjectVisitor<TParam, TResult>;

    private static float DegreesToRadians(float degrees)
    {
        return degrees * MathF.PI / 180.0f;
    }
}

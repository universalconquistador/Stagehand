using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Stagehand.Definitions.Objects;

/// <summary>
/// The base class for the definition of an object in a Stage definition.
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
    [JsonIgnore]
    public Quaternion RotationQuaternion
    {
        get => Quaternion.CreateFromYawPitchRoll(DegreesToRadians(RotationPitchYawRollDegrees.Y), DegreesToRadians(RotationPitchYawRollDegrees.X), DegreesToRadians(RotationPitchYawRollDegrees.Z));
        set
        {
            // The formula I'm using for quat -> PYR is z-up, so swizzle the dimensions around
            float x = value.Z;
            float y = value.X;
            float z = value.Y;
            float w = value.W;

            var roll = MathF.Atan2((2 * w * x) + (2 * y * z), 1 - (2 * x * x) - (2 * y * y));
            var pitch = MathF.Asin((2 * w * y) - (2 * z * x));
            var yaw = MathF.Atan2((2 * w * z) + (2 * x * y), 1 - (2 * y * y) - (2 * z * z));

            RotationPitchYawRollDegrees = new Vector3(RadiansToDegrees(pitch), RadiansToDegrees(yaw), RadiansToDegrees(roll));
        }
    }

    // TODO: Penumbra support
    //public Guid PenumbraCollection { get; set; } = Guid.Empty;

    public abstract TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        where TVisitor : IObjectVisitor<TParam, TResult>;

    private static float DegreesToRadians(float degrees)
    {
        return degrees * MathF.PI / 180.0f;
    }

    private static float RadiansToDegrees(float radians)
    {
        return radians * 180.0f / MathF.PI;
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Stagehand.Definitions.Objects;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Stagehand.Live;

/// <summary>
/// A Stagehand object that has been created in the game.
/// </summary>
public interface ILiveObject : IDisposable
{
    /// <summary>
    /// Attempts to take on the property values in the given object definition.
    /// </summary>
    /// <param name="definition">The object definition, whose concrete type must match this live object.</param>
    /// <param name="modpack">The modpack to use for the object.</param>
    /// <returns>True if the update was successful, or false if this live object cannot be updated with the given object definition.</returns>
    bool TryUpdate(ObjectDefinition definition, Vector3 parentTranslation, Quaternion parentRotation, float parentUniformScale, ILiveModpack? modpack);

    /// <summary>
    /// Attempts to get the oriented bounds of this live object.
    /// </summary>
    /// <param name="orientedBounds"></param>
    /// <returns>True if the call succeeded, or false if it failed (e.g. a resource is not yet loaded).</returns>
    bool TryGetOrientedBounds(out FFXIVClientStructs.FFXIV.Common.Math.OrientedBounds orientedBounds);
}

internal abstract unsafe class LiveObject : ILiveObject
{
    /// <summary>
    /// The destructor flags to pass when freeing automatically allocated memory.
    /// </summary>
    protected const int DestroyFlagsFree = 1;

    protected Object* ObjectPtr { get; set; }

    public virtual Vector3 Position { get => ObjectPtr->Position; set => ObjectPtr->Position = value; }
    public virtual Quaternion Rotation { get => ObjectPtr->Rotation; set => ObjectPtr->Rotation = value; }
    public virtual Vector3 Scale { get => ObjectPtr->Scale; set => ObjectPtr->Scale = value; }

    public ILiveModpack? Modpack { get; }

    public LiveObject(Object* objectPtr, ILiveModpack? modpack)
    {
        ObjectPtr = objectPtr;
        Modpack = modpack;
    }

    public virtual void Dispose()
    {
        ObjectPtr = null;
    }

    public static void ApplyParentTransform(ref Vector3 localTranslation, ref Quaternion localRotation, ref Vector3 localScale, Vector3 parentTranslation, Quaternion parentRotation, float parentUniformScale)
    {
        var rotation = parentRotation * localRotation;

        var translation = Vector3.Transform(localTranslation, parentRotation);
        translation *= parentUniformScale;

        var scale = localScale * parentUniformScale;

        translation += parentTranslation;

        localTranslation = translation;
        localRotation = rotation;
        localScale = scale;
    }

    public abstract bool TryUpdate(ObjectDefinition definition, Vector3 parentTranslation, Quaternion parentRotation, float parentUniformScale, ILiveModpack? modpack);

    public abstract bool TryGetOrientedBounds(out FFXIVClientStructs.FFXIV.Common.Math.OrientedBounds orientedBounds);
}

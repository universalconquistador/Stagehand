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
    /// <returns>True if the update was successful, or false if this live object cannot be updated with the given object definition.</returns>
    bool TryUpdate(ObjectDefinition definition);
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

    public Guid PenumbraCollectionId { get; }

    public LiveObject(Object* objectPtr)
    {
        ObjectPtr = objectPtr;
    }

    public virtual void Dispose()
    {
        ObjectPtr = null;
    }

    public abstract bool TryUpdate(ObjectDefinition definition);
}

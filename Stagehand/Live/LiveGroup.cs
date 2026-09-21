using FFXIVClientStructs.FFXIV.Common.Math;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Stagehand.Live;

internal class LiveGroup : ILiveObject
{
    public Matrix4x4 WorldTransform { get; private set; }

    private readonly ILiveObjectService _liveObjectService;
    private readonly Dictionary<string, ILiveObject> _liveObjects;

    public LiveGroup(Matrix4x4 worldTransform, ILiveObjectService liveObjectService, IReadOnlyDictionary<string, ILiveModpack> modpacks, GroupDefinition definition)
    {
        WorldTransform = worldTransform;
        _liveObjectService = liveObjectService;
        _liveObjects = new();

        Matrix4x4.Decompose(WorldTransform, out var scale, out var rotation, out var translation);

        UpdateChildObjects(definition, translation, rotation, scale, modpacks);
    }

    public void Dispose()
    {
        foreach (var child in _liveObjects.Values)
        {
            child.Dispose();
        }
        _liveObjects.Clear();
    }

    // TODO: Pull this somewhere common to be shared by all objects that are composed of child objects.
    public static bool TryGetGroupOrientedBounds(Matrix4x4 worldTransform, IEnumerable<OrientedBounds?> children, out OrientedBounds orientedBounds)
    {
        if (children.Count() == 1)
        {
            var first = children.First();
            orientedBounds = first ?? default;
            return first != null;
        }
        else if (children.Any() && Matrix4x4.Invert(worldTransform, out var worldToLocal) && Matrix4x4.Decompose(worldTransform, out var worldScale, out var worldRotation, out var worldTranslation))
        {
            Vector3 minPoint = new Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue);
            Vector3 maxPoint = new Vector3(Single.MinValue, Single.MinValue, Single.MinValue);

            foreach (var child in children)
            {
                if (child != null)
                {
                    var childBounds = child.Value;

                    var childCenter = childBounds.Transform.Translation;
                    var childX = (Vector3)System.Numerics.Vector.AsVector3(((System.Numerics.Matrix4x4)childBounds.Transform).X);
                    var childY = (Vector3)System.Numerics.Vector.AsVector3(((System.Numerics.Matrix4x4)childBounds.Transform).Y);
                    var childZ = (Vector3)System.Numerics.Vector.AsVector3(((System.Numerics.Matrix4x4)childBounds.Transform).Z);
                    for (int x = 0; x < 2; x++)
                    {
                        var xSign = x > 0 ? 1 : -1;
                        for (int y = 0; y < 2; y++)
                        {
                            var ySign = y > 0 ? 1 : -1;
                            for (int z = 0; z < 2; z++)
                            {
                                var zSign = z > 0 ? 1 : -1;

                                var worldSpaceCorner = childCenter + childX * childBounds.HalfExtents.X * xSign + childY * childBounds.HalfExtents.Y * ySign + childZ * childBounds.HalfExtents.Z * zSign;
                                var localCorner = Vector3.Transform(worldSpaceCorner, worldToLocal);
                                minPoint = Vector3.Min(minPoint, localCorner);
                                maxPoint = Vector3.Max(maxPoint, localCorner);
                            }
                        }
                    }
                }
            }

            var localCenter = (minPoint + maxPoint) * 0.5f;
            var localHalfExtents = (maxPoint - minPoint) * 0.5f;
            var worldCenter = Vector3.Transform(localCenter, worldTransform);
            orientedBounds = new OrientedBounds() { Transform = System.Numerics.Matrix4x4.CreateFromQuaternion(worldRotation) * System.Numerics.Matrix4x4.CreateTranslation(worldCenter), HalfExtents = localHalfExtents * worldScale };
            return true;
        }
        else
        {
            orientedBounds = default;
            return false;
        }
    }

    public bool TryGetOrientedBounds(out OrientedBounds orientedBounds)
    {
        return TryGetGroupOrientedBounds(WorldTransform, _liveObjects.Values.Select<ILiveObject?, OrientedBounds?>(obj =>
        {
            if (obj != null)
            {
                obj.TryGetOrientedBounds(out var bounds);
                return bounds;
            }
            else
            {
                return null;
            }
        }), out orientedBounds);
    }

    private void UpdateChildObjects(GroupDefinition groupDefinition, Vector3 position, Quaternion rotation, Vector3 scale, IReadOnlyDictionary<string, ILiveModpack> modpacks)
    {
        // Remove any objects that are not in the new definition
        foreach (var existingObject in _liveObjects)
        {
            if (!groupDefinition.Objects.ContainsKey(existingObject.Key))
            {
                _liveObjects.Remove(existingObject.Key);
                existingObject.Value.Dispose();
            }
        }

        foreach (var newObject in groupDefinition.Objects)
        {
            if (_liveObjects.TryGetValue(newObject.Key, out var existingObject))
            {
                var obj = _liveObjectService.UpdateOrRecreateObject(existingObject, newObject.Value, position, rotation, scale.X, modpacks);
                if (obj != null)
                {
                    _liveObjects[newObject.Key] = obj;
                }
                else
                {
                    _liveObjects.Remove(newObject.Key);
                }
            }
            else
            {
                var obj = _liveObjectService.CreateObject(newObject.Value, position, rotation, scale.X, modpacks);
                if (obj != null)
                {
                    _liveObjects.Add(newObject.Key, obj);
                }
            }
        }
    }

    public bool TryUpdate(ObjectDefinition definition, System.Numerics.Vector3 parentTranslation, System.Numerics.Quaternion parentRotation, float parentUniformScale, IReadOnlyDictionary<string, ILiveModpack> modpacks)
    {
        if (definition.IsDisabled)
        {
            return false;
        }

        // Don't need to react to modpack changes, as groups aren't moddable

        if (definition is GroupDefinition groupDefinition)
        {
            var position = groupDefinition.Position;
            var rotation = groupDefinition.RotationQuaternion;
            var scale = groupDefinition.Scale;
            LiveObject.ApplyParentTransform(ref position, ref rotation, ref scale, parentTranslation, parentRotation, parentUniformScale);

            WorldTransform = System.Numerics.Matrix4x4.CreateScale(scale) * System.Numerics.Matrix4x4.CreateFromQuaternion(rotation) * System.Numerics.Matrix4x4.CreateTranslation(position);

            UpdateChildObjects(groupDefinition, position, rotation, scale, modpacks);

            return true;
        }
        else
        {
            return false;
        }
    }
}

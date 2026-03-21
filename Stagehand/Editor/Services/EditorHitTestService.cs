using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Common.Math;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Services;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Transactions;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Stagehand.Editor.Services;

/// <summary>
/// A 3D shape in the editor that represents hit-testing collision for an object definition editor.
/// </summary>
public interface IEditorHitTestShape
{
    /// <summary>
    /// The object definition editor that this shape is for.
    /// </summary>
    /// <remarks>
    /// A single definition editor can have an arbitrary number of shapes.
    /// </remarks>
    IObjectDefinitionEditor Editor { get; }

    /// <summary>
    /// A sphere in world space that completely encloses this shape, used for broadphase culling.
    /// </summary>
    SphereBounds SphereBounds { get; }

    /// <summary>
    /// Computes whether the given ray hits this shape.
    /// </summary>
    /// <param name="rayStart">The start position of the ray in world space.</param>
    /// <param name="rayDirection">The direction of the ray in world space.</param>
    /// <param name="hitPosition">The position of the hit, if any.</param>
    /// <param name="hitNormal">The normal vector of the surface that was hit, if any.</param>
    /// <returns>Whether this shape is hit by the given ray.</returns>
    bool HitTest(Vector3 rayStart, Vector3 rayDirection, out Vector3 hitPosition, out Vector3 hitNormal);
}

public class EditorHitTestSphere : IEditorHitTestShape
{
    public IObjectDefinitionEditor Editor { get; }
    public SphereBounds Sphere { get; set; }

    SphereBounds IEditorHitTestShape.SphereBounds => Sphere;

    public EditorHitTestSphere(IObjectDefinitionEditor editor, SphereBounds sphere)
    {
        Editor = editor;
        Sphere = sphere;
    }

    public bool HitTest(Vector3 rayStart, Vector3 rayDirection, out Vector3 hitPosition, out Vector3 hitNormal)
    {
        if (Sphere.IntersectsRay(new Ray(rayStart, rayDirection), out var hitPoint))
        {
            hitPosition = hitPoint;
            hitNormal = Vector3.Normalize(hitPosition - (Vector3)Sphere.CenterPoint);
            return true;
        }
        else
        {
            hitPosition = rayStart;
            hitNormal = Vector3.Zero;
            return false;
        }
    }
}

public class EditorHitTestModel : IEditorHitTestShape
{
    private readonly IModelBvhCacheService _bvhCacheService;
    private readonly IDataManager _dataManager;

    public IObjectDefinitionEditor Editor { get; }

    #region Transform

    private bool _recomputeTransform = true;

    private Matrix4x4 _transform = Matrix4x4.Identity;
    protected Matrix4x4 Transform
    {
        get
        {
            RecomputeTransform();
            return _transform;
        }
    }

    private Matrix4x4 _inverseTransform = Matrix4x4.Identity;
    protected Matrix4x4 InverseTransform
    {
        get
        {
            RecomputeTransform();
            return _inverseTransform;
        }
    }

    public Vector3 Position
    {
        get;
        set
        {
            field = value;
            InvalidateTransform();
        }
    }

    public Quaternion Rotation
    {
        get;
        set
        {
            field = value;
            InvalidateTransform();
        }
    }

    public Vector3 Scale
    {
        get;
        set
        {
            field = value;
            InvalidateTransform();
        }
    }

    private void InvalidateTransform()
    {
        _recomputeTransform = true;
        _isWorldBoundsValid = false;
    }

    private void RecomputeTransform()
    {
        if (_recomputeTransform)
        {
            _recomputeTransform = false;
            _transform = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
            Matrix4x4.Invert(Transform, out _inverseTransform);
        }
    }

    #endregion

    #region Model Resource Path

    private bool _isModelResourcePathValid = false;
    public string ModelResourcePath
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;

                _isLocalBoundsValid = false;
                _isWorldBoundsValid = false;

                _isModelResourcePathValid = !string.IsNullOrEmpty(ModelResourcePath)
                    && _dataManager.FileExists(ModelResourcePath);

                // Start loading the bvh immediately
                if (_isModelResourcePathValid)
                {
                    _bvhCacheService.TryIntersectModel(ModelResourcePath, Vector3.Zero, Vector3.One, out _, out _);
                }
            }
        }
    }

    #endregion

    #region Bounds

    private bool _isLocalBoundsValid = false;
    private Vector3 _localBoundsCenter;
    private Vector3 _localBoundsHalfExtents;

    private bool _isWorldBoundsValid = false;
    private SphereBounds _worldBounds = default;
    public SphereBounds WorldBounds
    {
        get
        {
            if (!_isWorldBoundsValid)
            {
                if (_isModelResourcePathValid)
                {
                    // Do we need to try querying the bounds again?
                    if (!_isLocalBoundsValid)
                    {
                        if (_bvhCacheService.TryGetBounds(ModelResourcePath, out var boundsMin, out var boundsMax))
                        {
                            _localBoundsCenter = (boundsMin + boundsMax) * 0.5f;
                            _localBoundsHalfExtents = (boundsMax - boundsMin) * 0.5f;

                            _isLocalBoundsValid = true;
                        }
                    }

                    if (!_isLocalBoundsValid)
                    {
                        // Still don't have bounds for this model yet
                        _worldBounds = new SphereBounds() { CenterPoint = Vector3.Zero, Radius = 0.0f };
                    }
                    else
                    {
                        var worldBoundsCenter = Vector3.Transform(_localBoundsCenter, Transform);
                        var worldBoundsToCorner = Vector3.TransformNormal(_localBoundsHalfExtents, Transform);

                        _worldBounds = new SphereBounds() { CenterPoint = worldBoundsCenter, Radius = worldBoundsToCorner.Length() };
                        _isWorldBoundsValid = true;
                    }
                }
                else
                {
                    // Invalid path has empty bounds
                    _worldBounds = new SphereBounds() { CenterPoint = Vector3.Zero, Radius = 0.0f };
                }
            }
            return _worldBounds;
        }
    }

    #endregion

    SphereBounds IEditorHitTestShape.SphereBounds => WorldBounds;

    public EditorHitTestModel(IObjectDefinitionEditor editor, string modelResourcePath, IModelBvhCacheService bvhCacheService, IDataManager dataManager)
    {
        Editor = editor;
        _bvhCacheService = bvhCacheService;
        _dataManager = dataManager;

        ModelResourcePath = modelResourcePath;

        Position = Vector3.Zero;
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;
    }

    public bool HitTest(Vector3 rayStart, Vector3 rayDirection, out Vector3 hitPosition, out Vector3 hitNormal)
    {
        if (_isModelResourcePathValid)
        {
            var localRayStart = Vector3.Transform(rayStart, InverseTransform);
            var localRayDirection = Vector3.TransformNormal(rayDirection, InverseTransform);

            var result = _bvhCacheService.TryIntersectModel(ModelResourcePath, localRayStart, localRayDirection, out var localIntersectionPoint, out var localIntersectionNormal);

            hitPosition = result ? Vector3.Transform(localIntersectionPoint, Transform) : rayStart;
            hitNormal = result ? Vector3.TransformNormal(localIntersectionNormal, Transform) : Vector3.Zero;
            return result;
        }
        else
        {
            hitPosition = rayStart;
            hitNormal = Vector3.Zero;
            return false;
        }
    }
}

/// <summary>
/// Manages the list of hit-testable shapes for the object editors in the Stage being edited.
/// </summary>
public interface IEditorHitTestService
{
    /// <summary>
    /// Adds the given shape to the hit test service.
    /// </summary>
    /// <remarks>
    /// Does not check if the shape is already present in the hit test service.
    /// Undefined behavior may occur if a shape is added twice.
    /// </remarks>
    /// <param name="shape">The shape to add.</param>
    void AddShape(IEditorHitTestShape shape);

    /// <summary>
    /// Removes the first occurrence of the given shape from the hit test service, if it exists.
    /// </summary>
    /// <param name="shape">The shape to remove.</param>
    void RemoveShape(IEditorHitTestShape shape);

    /// <summary>
    /// Determines which shape, if any, the given ray intersects first.
    /// </summary>
    /// <param name="rayStart">The start point of the ray in world space.</param>
    /// <param name="rayDirection">The direction of the ray in world space.</param>
    /// <param name="hitShape">The hit-test shape that was first hit by the ray, if any.</param>
    /// <param name="hitPosition">The position in world space where the ray hit the shape, if any.</param>
    /// <param name="hitNormal">The world space normal vector of the surface on the shape that was hit, if any.</param>
    /// <returns>True if a shape was hit, and false otherwise.</returns>
    bool HitTestShapes(Vector3 rayStart, Vector3 rayDirection, [NotNullWhen(true)] out IEditorHitTestShape? hitShape, out Vector3 hitPosition, out Vector3 hitNormal);
}

internal class EditorHitTestService : IEditorHitTestService
{
    private List<IEditorHitTestShape> _allShapes = new();
    private SpinLock _shapeLock = new();

    public void AddShape(IEditorHitTestShape shape)
    {
        bool lockHeld = false;
        while (!lockHeld)
        {
            _shapeLock.Enter(ref lockHeld);
        }

        try
        {
            _allShapes.Add(shape);
        }
        finally
        {
            _shapeLock.Exit();
        }
    }

    public void RemoveShape(IEditorHitTestShape shape)
    {
        bool lockHeld = false;
        while (!lockHeld)
        {
            _shapeLock.Enter(ref lockHeld);
        }

        try
        {
            for (int i = 0; i < _allShapes.Count; i++)
            {
                if (_allShapes[i] == shape)
                {
                    // Swap the last one in
                    _allShapes[i] = _allShapes[_allShapes.Count - 1];
                    _allShapes.RemoveAt(_allShapes.Count - 1);
                    break;
                }
            }
        }
        finally
        {
            _shapeLock.Exit();
        }
    }

    public bool HitTestShapes(Vector3 rayStart, Vector3 rayDirection, [NotNullWhen(true)] out IEditorHitTestShape? hitShape, out Vector3 hitPosition, out Vector3 hitNormal)
    {
        hitShape = null;
        hitPosition = rayStart;
        hitNormal = Vector3.Zero;
        float nearestDistanceSquared = float.MaxValue;
        var ray = new Ray(rayStart, rayDirection);

        bool lockHeld = false;
        while (!lockHeld)
        {
            _shapeLock.Enter(ref lockHeld);
        }

        try
        {
            for (int i = 0; i < _allShapes.Count; i++)
            {
                if (_allShapes[i].SphereBounds.IntersectsRay(ray, out var boundingSphereHit)
                    && ((Vector3)boundingSphereHit - rayStart).LengthSquared() < nearestDistanceSquared
                    && _allShapes[i].HitTest(rayStart, rayDirection, out var shapeHitPosition, out var shapeHitNormal))
                {
                    var distanceSquared = (shapeHitPosition - rayStart).LengthSquared();
                    if (distanceSquared < nearestDistanceSquared)
                    {
                        nearestDistanceSquared = distanceSquared;
                        hitShape = _allShapes[i];
                        hitPosition = shapeHitPosition;
                        hitNormal = shapeHitNormal;
                    }
                }
            }
        }
        finally
        {
            _shapeLock.Exit();
        }

        return hitShape != null;
    }
}

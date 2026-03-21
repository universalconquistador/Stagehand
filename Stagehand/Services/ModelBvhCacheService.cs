using Dalamud.Plugin.Services;
using Lumina.Data.Files;
using Microsoft.Extensions.Logging;
using Stagehand.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Services;

/// <summary>
/// Generates <see cref="StaticBvh"/>s for .mdl files on demand in the background, and then frees them
/// when they are no longer being used for a certain period of time.
/// </summary>
public interface IModelBvhCacheService
{
    /// <summary>
    /// Performs a hit test of the given ray against the vertices in the .mdl resource with the given path,
    /// loading the model lazily and returning false if it has not been loaded.
    /// </summary>
    /// <param name="modelResourcePath">The game path of the .mdl resource to load.</param>
    /// <param name="rayStart">The starting location of the ray to test, relative to the model.</param>
    /// <param name="rayDirection">The direction of the ray to test, relative to the model.</param>
    /// <param name="intersectionPoint">The point where the nearest intersection occurs, if any.</param>
    /// <param name="intersectionNormal">The true normal of the triangle hit, if any.</param>
    /// <returns>
    /// True if the model was in the cache and was hit by the ray, false if the model
    /// is still being loaded or was not hit by the ray.
    /// </returns>
    bool TryIntersectModel(string modelResourcePath, Vector3 rayStart, Vector3 rayDirection, out Vector3 intersectionPoint, out Vector3 intersectionNormal);

    /// <summary>
    /// Gets the axis-aligned bounds of the vertices in the .mdl resource with the given path if it has been
    /// loaded by <see cref="TryIntersectModel(string, Vector3, Vector3, out Vector3, out Vector3)"/>.
    /// </summary>
    /// <param name="modelResourcePath">The game path of the .mdl resource.</param>
    /// <param name="boundsMin">The minimum corner of the model's bounding box, relative to the model..</param>
    /// <param name="boundsMax">The maximum corner of the model's bounding box, relative to the model.</param>
    /// <returns>True if the model had been loaded and the bounds were found, or false if the model has not been loaded.</returns>
    bool TryGetBounds(string modelResourcePath, out Vector3 boundsMin, out Vector3 boundsMax);
}

internal class ModelBvhCacheService : IModelBvhCacheService, IDisposable
{
    private class CachedStaticBvh : IDisposable
    {
        private bool _isDisposed = false;
        private int _concurrentUsages = 0;
        private ManualResetEventSlim _disposeGate = new ManualResetEventSlim(true);

        private StaticBvh? _bvh;
        
        public bool HasBounds { get; private set; }
        public Vector3 BoundsMin { get; private set; }
        public Vector3 BoundsMax { get; private set; }

        private Task _loadTask;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public CachedStaticBvh(string filename, IDataManager dataManager, ILogger logger)
        {
            _loadTask = Task.Run(() => LoadBvh(filename, dataManager, logger));
        }

        private async Task LoadBvh(string filename, IDataManager dataManager, ILogger logger)
        {
            try
            {
                var model = await dataManager.GetFileAsync<MdlFile>(filename, _cts.Token);
                _bvh = new StaticBvh(model);
                _bvh.GetBounds(out var boundsMin, out var boundsMax);
                BoundsMin = boundsMin;
                BoundsMax = boundsMax;
                HasBounds = true;
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("BVH build cancelled for {path}.", filename);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to build BVH for {path}!", filename);
            }
        }

        // Use atomic increment/decrement to count the number of current users of the static bvh
        // and keep a ManualResetEventSlim that gates the dispose function

        // This is probably mega overkill, right? Can we count on no threads still performing intersection
        // when we dispose?

        public bool IntersectsRay(Vector3 rayStart, Vector3 rayDirection, out Vector3 intersectionPoint, out Vector3 intersectionNormal)
        {
            if (_bvh != null)
            {
                int concurrentUsages = Interlocked.Increment(ref _concurrentUsages);
                if (concurrentUsages == 1)
                {
                    _disposeGate.Reset();
                }
                if (Volatile.Read(ref _isDisposed))
                {
                    // A dispose was already started, so bail with an empty result
                    if (Interlocked.Decrement(ref _concurrentUsages) == 0)
                    {
                        _disposeGate.Set();
                    }

                    intersectionPoint = rayStart;
                    intersectionNormal = Vector3.Zero;
                    return false;
                }
                else
                {
                    bool result = _bvh.IntersectsRay(rayStart, rayDirection, out intersectionPoint, out intersectionNormal);

                    if (Interlocked.Decrement(ref _concurrentUsages) == 0)
                    {
                        _disposeGate.Set();
                    }

                    return result;
                }
            }
            else
            {
                intersectionPoint = rayStart;
                intersectionNormal = Vector3.Zero;
                return false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, true) == false)
            {
                _cts.Cancel();

                // wait for any usages of the bvh to finish, by waiting on the manual reset event
                _disposeGate.Wait();

                _bvh?.Dispose();
                _bvh = null;

                _disposeGate.Dispose();
            }
        }
    }

    private readonly IDataManager _dataManager;
    private readonly ILogger _logger;
    private readonly IClientState _clientState;

    private ConcurrentDictionary<string, CachedStaticBvh> _bvhCache = new();

    public ModelBvhCacheService(IDataManager dataManager, ILogger<ModelBvhCacheService> logger, IClientState clientState)
    {
        _dataManager = dataManager;
        _logger = logger;
        _clientState = clientState;

        clientState.TerritoryChanged += OnTerritoryChanged;
    }

    private void OnTerritoryChanged(ushort obj)
    {
        _logger.LogDebug("Starting fresh BVH cache...");
        var oldCache = Interlocked.Exchange(ref _bvhCache, new ConcurrentDictionary<string, CachedStaticBvh>());

        _ = Task.Run(() =>
        {
            foreach (var pair in oldCache)
            {
                pair.Value.Dispose();
            }
            oldCache.Clear();
            _logger.LogDebug("Old BVH cache disposed.");
        });
    }

    public bool TryIntersectModel(string modelResourcePath, Vector3 rayStart, Vector3 rayDirection, out Vector3 intersectionPoint, out Vector3 intersectionNormal)
    {
        var cachedBvh = _bvhCache.GetOrAdd(modelResourcePath, path => new CachedStaticBvh(path, _dataManager, _logger));
        return cachedBvh.IntersectsRay(rayStart, rayDirection, out intersectionPoint, out intersectionNormal);
    }

    public bool TryGetBounds(string modelResourcePath, out Vector3 boundsMin, out Vector3 boundsMax)
    {
        if (_bvhCache.TryGetValue(modelResourcePath, out var cachedBvh) && cachedBvh.HasBounds)
        {
            boundsMin = cachedBvh.BoundsMin;
            boundsMax = cachedBvh.BoundsMax;
            return true;
        }
        else
        {
            boundsMin = Vector3.Zero;
            boundsMax = Vector3.Zero;
            return false;
        }
    }

    public void Dispose()
    {
        _clientState.TerritoryChanged -= OnTerritoryChanged;
    }
}

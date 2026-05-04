using Stagehand.Definitions;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Stagehand.Live;

public class LiveStage : IDisposable
{
    private readonly Dictionary<string, ILiveObject> _liveObjects = new();
    private readonly Dictionary<string, ILiveModpack> _liveModpacks = new();

    private readonly ILiveObjectService _liveObjectService;
    private readonly IResourceRedirectionService _resourceRedirectionService;

    private readonly object _modificationLock = new();

    public LiveStage(StageDefinition definition, ILiveObjectService liveObjectService, IResourceRedirectionService resourceRedirectionService)
    {
        _liveObjectService = liveObjectService;
        _resourceRedirectionService = resourceRedirectionService;
        Update(definition);
    }

    public void Update(StageDefinition newDefinition)
    {
        lock (_modificationLock)
        {
            // Remove any modpacks that are not in the new definition
            foreach (var existingModpack in _liveModpacks)
            {
                if (!newDefinition.EmbeddedModpacks.ContainsKey(existingModpack.Key))
                {
                    _liveModpacks.Remove(existingModpack.Key);
                    existingModpack.Value.Dispose();
                }
            }

            foreach (var newModpack in newDefinition.EmbeddedModpacks)
            {
                if (_liveModpacks.TryGetValue(newModpack.Key, out var existingModpack))
                {
                    var newEffectsHash = ResourceRedirectionHelpers.HashModpackEffects(newModpack.Value.ModdedResources);
                    if (existingModpack.EffectsHash != newEffectsHash)
                    {
                        existingModpack.Dispose();
                        _liveModpacks[newModpack.Key] = _resourceRedirectionService.CreateModpack($"LiveStage-{newDefinition.Info.Name}-{newModpack.Value.DisplayName}", newModpack.Value.ModdedResources);
                    }
                }
                else
                {
                    _liveModpacks.Add(newModpack.Key, _resourceRedirectionService.CreateModpack($"LiveStage-{newDefinition.Info.Name}-{newModpack.Value.DisplayName}", newModpack.Value.ModdedResources));
                }
            }

            // Remove any objects that are not in the new definition
            foreach (var existingObject in _liveObjects)
            {
                if (!newDefinition.Objects.ContainsKey(existingObject.Key))
                {
                    _liveObjects.Remove(existingObject.Key);
                    existingObject.Value.Dispose();
                }
            }

            foreach (var newObject in newDefinition.Objects)
            {
                ILiveModpack? newModpack = null;
                if (newObject.Value.ModpackId != string.Empty)
                {
                    _liveModpacks.TryGetValue(newObject.Value.ModpackId, out newModpack);
                }
                if (_liveObjects.TryGetValue(newObject.Key, out var existingObject))
                {
                    var obj = _liveObjectService.UpdateOrRecreateObject(existingObject, newObject.Value, newModpack);
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
                    var obj = _liveObjectService.CreateObject(newObject.Value, newModpack);
                    if (obj != null)
                    {
                        _liveObjects.Add(newObject.Key, obj);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        lock(_modificationLock)
        {
            foreach (var obj in _liveObjects)
            {
                obj.Value.Dispose();
            }
            _liveObjects.Clear();
            foreach (var modpack in _liveModpacks)
            {
                modpack.Value.Dispose();
            }
            _liveModpacks.Clear();
        }
    }
}

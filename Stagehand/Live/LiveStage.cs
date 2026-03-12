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

    private readonly ILiveObjectService _liveObjectService;

    private readonly object _modificationLock = new();

    public LiveStage(StageDefinition definition, ILiveObjectService liveObjectService)
    {
        _liveObjectService = liveObjectService;
        Update(definition);
    }

    public void Update(StageDefinition newDefinition)
    {
        lock (_modificationLock)
        {
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
                if (_liveObjects.TryGetValue(newObject.Key, out var existingObject))
                {
                    var obj = _liveObjectService.UpdateOrRecreateObject(existingObject, newObject.Value);
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
                    var obj = _liveObjectService.CreateObject(newObject.Value);
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
        }
    }
}

using Stagehand.Definitions;
using Stagehand.Live;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Stagehand.Services;

/// <summary>
/// Creates and destroys live Stagehands.
/// </summary>
public interface ILiveStagehandService
{
    /// <summary>
    /// Uses the given Stagehand definition to update the live Stagehand with the given key,
    /// or create one if it does not exist.
    /// </summary>
    /// <param name="key">The key for the live Stagehand to create or update.</param>
    /// <param name="definition">The Stagehand definition for the live Stagehand.</param>
    /// <returns>The live Stagehand that was created or updated.</returns>
    LiveStagehand CreateOrUpdateLiveStagehand(string key, StagehandDefinition definition);

    /// <summary>
    /// Searches for a live Stagehand with the given key.
    /// </summary>
    /// <param name="key">The key of the live Stagehand to search for.</param>
    /// <param name="Stagehand">The live Stagehand that was found, if any.</param>
    /// <returns>True if a matching live Stagehand was returned, or false otherwise.</returns>
    bool TryGetLiveStagehand(string key, [NotNullWhen(true)] out LiveStagehand? Stagehand);

    /// <summary>
    /// Destroys the live Stagehand with the given key, if one exists.
    /// </summary>
    /// <param name="key">The key of the live Stagehand to destroy.</param>
    /// <returns>Whether a live Stagehand was found with the given key and destroyed.</returns>
    bool TryDestroyLiveStagehand(string key);

    /// <summary>
    /// Destroys all the Stagehands currently live.
    /// </summary>
    void DestroyAllLiveStagehands();
}

public static class LiveStagehandHelpers
{
    /// <summary>
    /// Returns a live Stagehand key that uniquely identifies the Stagehand with the definition located at the given full path.
    /// </summary>
    /// <param name="fullFilename">The full path to the Stagehand definition.</param>
    /// <returns>The key for the live Stagehand.</returns>
    public static string MakeLocalStagehandKey(string fullFilename)
    {
        return $"file://{fullFilename}";
    }

    /// <summary>
    /// Returns a live Stagehand key that uniquely identifies a temporary Stagehand with the given namespace and ID strings.
    /// </summary>
    /// <param name="id">The ID string of the temporary Stagehand.</param>
    /// <param name="namespace">The namespace of the temporary Stagehand.</param>
    /// <returns>The key for the temporary Stagehand.</returns>
    public static string MakeTemporaryStagehandKey(string id, string @namespace)
    {
        // These are not actual URIs, we don't need to escape anything. These just need to be unique
        // and never clash with local Stagehands.
        return $"temp://{@namespace}/{id}";
    }
}

internal class LiveStagehandService : ILiveStagehandService, IDisposable
{
    private readonly ILiveObjectService _liveObjectService;

    private readonly ConcurrentDictionary<string, LiveStagehand> _liveStagehands = new();

    public LiveStagehandService(ILiveObjectService liveObjectService)
    {
        _liveObjectService = liveObjectService;
    }

    public LiveStagehand CreateOrUpdateLiveStagehand(string key, StagehandDefinition definition)
    {
        return _liveStagehands.AddOrUpdate(key, k => new LiveStagehand(definition, _liveObjectService), (k, existing) =>
        {
            existing.Update(definition);
            return existing;
        });
    }

    public bool TryGetLiveStagehand(string key, [NotNullWhen(true)] out LiveStagehand? Stagehand)
    {
        return _liveStagehands.TryGetValue(key, out Stagehand);
    }

    public bool TryDestroyLiveStagehand(string key)
    {
        if (_liveStagehands.TryRemove(key, out var liveStagehand))
        {
            liveStagehand.Dispose();
            return true;
        }
        else
        {
            return false;
        }
    }

    public void DestroyAllLiveStagehands()
    {
        foreach (var Stagehand in _liveStagehands)
        {
            Stagehand.Value.Dispose();
            _liveStagehands.TryRemove(Stagehand);
        }
    }

    public void Dispose()
    {
        DestroyAllLiveStagehands();
    }
}

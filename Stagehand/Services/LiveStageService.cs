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
/// Creates and destroys live Stages.
/// </summary>
public interface ILiveStageService
{
    /// <summary>
    /// Uses the given Stage definition to update the live Stage with the given key,
    /// or create one if it does not exist.
    /// </summary>
    /// <param name="key">The key for the live Stage to create or update.</param>
    /// <param name="definition">The Stage definition for the live Stage.</param>
    /// <returns>The live Stage that was created or updated.</returns>
    LiveStage CreateOrUpdateLiveStage(string key, StageDefinition definition);

    /// <summary>
    /// Searches for a live Stage with the given key.
    /// </summary>
    /// <param name="key">The key of the live Stage to search for.</param>
    /// <param name="stage">The live Stage that was found, if any.</param>
    /// <returns>True if a matching live Stage was returned, or false otherwise.</returns>
    bool TryGetLiveStage(string key, [NotNullWhen(true)] out LiveStage? stage);

    /// <summary>
    /// Destroys the live Stage with the given key, if one exists.
    /// </summary>
    /// <param name="key">The key of the live Stage to destroy.</param>
    /// <returns>Whether a live Stage was found with the given key and destroyed.</returns>
    bool TryDestroyLiveStage(string key);

    /// <summary>
    /// Destroys all the Stages currently live.
    /// </summary>
    void DestroyAllLiveStages();
}

internal static class LiveStageHelpers
{
    /// <summary>
    /// Returns a live Stage key that uniquely identifies the Stage with the definition located at the given full path.
    /// </summary>
    /// <param name="fullFilename">The full path to the Stage definition.</param>
    /// <returns>The key for the live Stage.</returns>
    public static string MakeLocalStageKey(string fullFilename)
    {
        return $"file://{fullFilename}";
    }

    /// <summary>
    /// Returns a live Stage key that uniquely identifies a temporary Stage with the given namespace and ID strings.
    /// </summary>
    /// <param name="id">The ID string of the temporary Stage.</param>
    /// <param name="namespace">The namespace of the temporary Stage.</param>
    /// <returns>The key for the temporary Stage.</returns>
    public static string MakeTemporaryStageKey(string id, string @namespace)
    {
        // These are not actual URIs, we don't need to escape anything. These just need to be unique
        // and never clash with local Stages.
        return $"temp://{@namespace}/{id}";
    }
}

internal class LiveStageService : ILiveStageService, IDisposable
{
    private readonly ILiveObjectService _liveObjectService;

    private readonly ConcurrentDictionary<string, LiveStage> _liveStages = new();

    public LiveStageService(ILiveObjectService liveObjectService)
    {
        _liveObjectService = liveObjectService;
    }

    public LiveStage CreateOrUpdateLiveStage(string key, StageDefinition definition)
    {
        return _liveStages.AddOrUpdate(key, k => new LiveStage(definition, _liveObjectService), (k, existing) =>
        {
            existing.Update(definition);
            return existing;
        });
    }

    public bool TryGetLiveStage(string key, [NotNullWhen(true)] out LiveStage? stage)
    {
        return _liveStages.TryGetValue(key, out stage);
    }

    public bool TryDestroyLiveStage(string key)
    {
        if (_liveStages.TryRemove(key, out var liveStage))
        {
            liveStage.Dispose();
            return true;
        }
        else
        {
            return false;
        }
    }

    public void DestroyAllLiveStages()
    {
        foreach (var stage in _liveStages)
        {
            stage.Value.Dispose();
            _liveStages.TryRemove(stage);
        }
    }

    public void Dispose()
    {
        DestroyAllLiveStages();
    }
}

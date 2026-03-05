using Soundstage.Definitions;
using Soundstage.Live;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Soundstage.Services;

/// <summary>
/// Creates and destroys live soundstages.
/// </summary>
public interface ILiveSoundstageService
{
    /// <summary>
    /// Uses the given soundstage definition to update the live soundstage with the given key,
    /// or create one if it does not exist.
    /// </summary>
    /// <param name="key">The key for the live soundstage to create or update.</param>
    /// <param name="definition">The soundstage definition for the live soundstage.</param>
    /// <returns>The live soundstage that was created or updated.</returns>
    LiveSoundstage CreateOrUpdateLiveSoundstage(string key, SoundstageDefinition definition);

    /// <summary>
    /// Searches for a live soundstage with the given key.
    /// </summary>
    /// <param name="key">The key of the live soundstage to search for.</param>
    /// <param name="soundstage">The live soundstage that was found, if any.</param>
    /// <returns>True if a matching live soundstage was returned, or false otherwise.</returns>
    bool TryGetLiveSoundstage(string key, [NotNullWhen(true)] out LiveSoundstage? soundstage);

    /// <summary>
    /// Destroys the live soundstage with the given key, if one exists.
    /// </summary>
    /// <param name="key">The key of the live soundstage to destroy.</param>
    /// <returns>Whether a live soundstage was found with the given key and destroyed.</returns>
    bool TryDestroyLiveSoundstage(string key);

    /// <summary>
    /// Destroys all the soundstages currently live.
    /// </summary>
    void DestroyAllLiveSoundstages();
}

public static class LiveSoundstageHelpers
{
    /// <summary>
    /// Returns a live soundstage key that uniquely identifies the soundstage with the definition located at the given full path.
    /// </summary>
    /// <param name="fullFilename">The full path to the soundstage definition.</param>
    /// <returns>The key for the live soundstage.</returns>
    public static string MakeLocalSoundstageKey(string fullFilename)
    {
        return $"file://{fullFilename}";
    }

    /// <summary>
    /// Returns a live soundstage key that uniquely identifies a temporary soundstage with the given namespace and ID strings.
    /// </summary>
    /// <param name="id">The ID string of the temporary soundstage.</param>
    /// <param name="namespace">The namespace of the temporary soundstage.</param>
    /// <returns>The key for the temporary soundstage.</returns>
    public static string MakeTemporarySoundstageKey(string id, string @namespace)
    {
        // These are not actual URIs, we don't need to escape anything. These just need to be unique
        // and never clash with local soundstages.
        return $"temp://{@namespace}/{id}";
    }
}

internal class LiveSoundstageService : ILiveSoundstageService, IDisposable
{
    private readonly ILiveObjectService _liveObjectService;

    private readonly ConcurrentDictionary<string, LiveSoundstage> _liveSoundstages = new();

    public LiveSoundstageService(ILiveObjectService liveObjectService)
    {
        _liveObjectService = liveObjectService;
    }

    public LiveSoundstage CreateOrUpdateLiveSoundstage(string key, SoundstageDefinition definition)
    {
        return _liveSoundstages.AddOrUpdate(key, k => new LiveSoundstage(definition, _liveObjectService), (k, existing) =>
        {
            existing.Update(definition);
            return existing;
        });
    }

    public bool TryGetLiveSoundstage(string key, [NotNullWhen(true)] out LiveSoundstage? soundstage)
    {
        return _liveSoundstages.TryGetValue(key, out soundstage);
    }

    public bool TryDestroyLiveSoundstage(string key)
    {
        if (_liveSoundstages.TryRemove(key, out var liveSoundstage))
        {
            liveSoundstage.Dispose();
            return true;
        }
        else
        {
            return false;
        }
    }

    public void DestroyAllLiveSoundstages()
    {
        foreach (var soundstage in _liveSoundstages)
        {
            soundstage.Value.Dispose();
            _liveSoundstages.TryRemove(soundstage);
        }
    }

    public void Dispose()
    {
        DestroyAllLiveSoundstages();
    }
}

using HQIPC;

namespace Soundstage.Api;

/// <summary>
/// The Dalamud IPC interface for working with the Soundstage plugin.
/// </summary>
/// <remarks>
/// Use <see cref="SoundstageApi.CreateIpcClient(Dalamud.Plugin.IDalamudPluginInterface)"/> to connect to Soundstage.
/// </remarks>
[IpcInterface("Soundstage")]
public interface ISoundstageApi
{
    event Action<IReadOnlyList<string>> VisibleSoundstagesChanged;

    //
    // LOCAL SOUNDSTAGE DEFINITIONS
    //
    // Soundstage keeps track of the metadata about the player's local soundstage definitions they have available.
    //


    //
    // VISIBLE SOUNDSTAGES
    //
    // 
    //

    /// <summary>
    /// Gets the IDs of all the currently visible soundstages of the given types.
    /// </summary>
    /// <param name="includeLocal">Whether to include the player's local soundstages that are visible.</param>
    /// <param name="includeTemporary">Whether to include the programmatically created temporary soundstages that are visible.</param>
    /// <param name="includeEditing">Whether to include the soundstage currently being edited, if any.</param>
    /// <returns></returns>
    IReadOnlyList<string> GetVisibleSoundstageIds(bool includeLocal, bool includeTemporary, bool includeEditing);

    /// <summary>
    /// Shows or hides the given soundstage with or without fading, if one exists with the given ID.
    /// </summary>
    /// <param name="soundstageId">The ID of the soundstage to show or hide.</param>
    /// <param name="fade">Whether to fade the soundstage's visibility, or set it immediately.</param>
    /// <returns>A <c>Task</c> to track the fade if specified, returning success or failure. If failure, will return immediately.</returns>
    Task<bool> TrySetSoundstageVisibilityAsync(string soundstageId, bool fade);


    //
    // TEMPORARY SOUNDSTAGES
    //
    // A temporary soundstage is created programmatically and only lasts until the Soundstage plugin is unloaded.
    // Temporary soundstages do not appear in the player's local soundstage list.
    //

    /// <summary>
    /// Creates or updates the temporary soundstage with the given ID, if the given definition string is valid.
    /// </summary>
    /// <param name="definitionString">
    /// A string containing the serialized soundstage definition.
    /// <br />
    /// This should be obtained by calling <c>SoundstageDefinition.ToDefinitionString()</c>.
    /// </param>
    /// <param name="soundstageId">
    /// An ID to uniquely identify the temporary soundstage you want to create or update.
    /// <br />
    /// Consider prefixing with your plugin ID if you don't want other plugins to mess with your temporary soundstages.
    /// <br />
    /// It is invalid to specify the filename of one of the user's soundstages, so I recommend not using filenames at all.
    /// </param>
    /// <param name="debugName">A display name to assign to the soundstage to identify it when debugging.</param>
    /// <returns>
    /// True if the operation was a success, or false if it failed
    /// (e.g. if the definition string could not be deserialized into a soundstage definition.)
    /// </returns>
    bool TryCreateOrUpdateTemporarySoundstage(string definitionString, string soundstageId, string debugName);

    /// <summary>
    /// Destroys the temporary soundstage, if any, with the given temporary ID.
    /// </summary>
    /// <remarks>
    /// This will also hide it immediately if it is visible. It is encouraged to fade out temporary soundstages via
    /// <see cref="TrySetSoundstageVisibilityAsync(string, bool)"/> before destroying them.
    /// </remarks>
    /// <param name="soundstageId">The ID of the temporary soundstage to destroy.</param>
    /// <returns>Whether a temporary soundstage was found with the given ID and destroyed.</returns>
    bool TryDestroyTemporarySoundstage(string soundstageId);
}

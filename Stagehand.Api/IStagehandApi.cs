using HQIPC;

namespace Stagehand.Api;

/// <summary>
/// The Dalamud IPC interface for working with the Stagehand plugin.
/// </summary>
/// <remarks>
/// Use <see cref="StagehandApi.CreateIpcClient(Dalamud.Plugin.IDalamudPluginInterface)"/> to connect to Stagehand.
/// </remarks>
[IpcInterface("Stagehand")]
public interface IStagehandApi
{
    event Action<IReadOnlyList<string>> VisibleStagesChanged;

    //
    // LOCAL STAGE DEFINITIONS
    //
    // Stagehand keeps track of the metadata about the player's local Stage definitions they have available.
    //


    //
    // VISIBLE STAGES
    //
    // 
    //

    /// <summary>
    /// Gets the IDs of all the currently visible Stage of the given types.
    /// </summary>
    /// <param name="includeLocal">Whether to include the player's local Stage that are visible.</param>
    /// <param name="includeTemporary">Whether to include the programmatically created temporary Stage that are visible.</param>
    /// <param name="includeEditing">Whether to include the Stage currently being edited, if any.</param>
    /// <returns></returns>
    IReadOnlyList<string> GetVisibleStageIds(bool includeLocal, bool includeTemporary, bool includeEditing);

    /// <summary>
    /// Shows or hides the given Stage with or without fading, if one exists with the given ID.
    /// </summary>
    /// <param name="stageId">The ID of the Stage to show or hide.</param>
    /// <param name="fade">Whether to fade the Stage's visibility, or set it immediately.</param>
    /// <returns>A <c>Task</c> to track the fade if specified, returning success or failure. If failure, will return immediately.</returns>
    Task<bool> TrySetStageVisibilityAsync(string stageId, bool fade);


    //
    // TEMPORARY STAGES
    //
    // A temporary Stage is created programmatically and only lasts until the Stagehand plugin is unloaded.
    // Temporary Stages do not appear in the player's local Stage list.
    //

    /// <summary>
    /// Creates or updates the temporary Stage with the given ID, if the given definition string is valid.
    /// </summary>
    /// <param name="definitionString">
    /// A string containing the serialized Stage definition.
    /// <br />
    /// This should be obtained by calling <c>StageDefinition.ToDefinitionString()</c>.
    /// </param>
    /// <param name="stageId">
    /// An ID to uniquely identify the temporary Stage you want to create or update.
    /// <br />
    /// Consider prefixing with your plugin ID if you don't want other plugins to mess with your temporary Stage.
    /// <br />
    /// It is invalid to specify the filename of one of the user's Stage, so I recommend not using filenames at all.
    /// </param>
    /// <param name="debugName">A display name to assign to the Stage to identify it when debugging.</param>
    /// <returns>
    /// True if the operation was a success, or false if it failed
    /// (e.g. if the definition string could not be deserialized into a Stage definition.)
    /// </returns>
    bool TryCreateOrUpdateTemporaryStage(string definitionString, string stageId, string debugName);

    /// <summary>
    /// Destroys the temporary Stage, if any, with the given temporary ID.
    /// </summary>
    /// <remarks>
    /// This will also hide it immediately if it is visible. It is encouraged to fade out temporary Stages via
    /// <see cref="TrySetStageVisibilityAsync(string, bool)"/> before destroying them.
    /// </remarks>
    /// <param name="stageId">The ID of the temporary Stage to destroy.</param>
    /// <returns>Whether a temporary Stage was found with the given ID and destroyed.</returns>
    bool TryDestroyTemporaryStage(string stageId);
}

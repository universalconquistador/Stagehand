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
    event Action<IReadOnlyList<string>> VisibleStagehandsChanged;

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
    /// Gets the IDs of all the currently visible Stagehands of the given types.
    /// </summary>
    /// <param name="includeLocal">Whether to include the player's local Stagehands that are visible.</param>
    /// <param name="includeTemporary">Whether to include the programmatically created temporary Stagehands that are visible.</param>
    /// <param name="includeEditing">Whether to include the Stagehand currently being edited, if any.</param>
    /// <returns></returns>
    IReadOnlyList<string> GetVisibleStagehandIds(bool includeLocal, bool includeTemporary, bool includeEditing);

    /// <summary>
    /// Shows or hides the given Stagehand with or without fading, if one exists with the given ID.
    /// </summary>
    /// <param name="StagehandId">The ID of the Stagehand to show or hide.</param>
    /// <param name="fade">Whether to fade the Stagehand's visibility, or set it immediately.</param>
    /// <returns>A <c>Task</c> to track the fade if specified, returning success or failure. If failure, will return immediately.</returns>
    Task<bool> TrySetStagehandVisibilityAsync(string StagehandId, bool fade);


    //
    // TEMPORARY StagehandS
    //
    // A temporary Stagehand is created programmatically and only lasts until the Stagehand plugin is unloaded.
    // Temporary Stagehands do not appear in the player's local Stagehand list.
    //

    /// <summary>
    /// Creates or updates the temporary Stagehand with the given ID, if the given definition string is valid.
    /// </summary>
    /// <param name="definitionString">
    /// A string containing the serialized Stage definition.
    /// <br />
    /// This should be obtained by calling <c>StageDefinition.ToDefinitionString()</c>.
    /// </param>
    /// <param name="StagehandId">
    /// An ID to uniquely identify the temporary Stagehand you want to create or update.
    /// <br />
    /// Consider prefixing with your plugin ID if you don't want other plugins to mess with your temporary Stagehands.
    /// <br />
    /// It is invalid to specify the filename of one of the user's Stagehands, so I recommend not using filenames at all.
    /// </param>
    /// <param name="debugName">A display name to assign to the Stagehand to identify it when debugging.</param>
    /// <returns>
    /// True if the operation was a success, or false if it failed
    /// (e.g. if the definition string could not be deserialized into a Stage definition.)
    /// </returns>
    bool TryCreateOrUpdateTemporaryStagehand(string definitionString, string StagehandId, string debugName);

    /// <summary>
    /// Destroys the temporary Stagehand, if any, with the given temporary ID.
    /// </summary>
    /// <remarks>
    /// This will also hide it immediately if it is visible. It is encouraged to fade out temporary Stagehands via
    /// <see cref="TrySetStagehandVisibilityAsync(string, bool)"/> before destroying them.
    /// </remarks>
    /// <param name="StagehandId">The ID of the temporary Stagehand to destroy.</param>
    /// <returns>Whether a temporary Stagehand was found with the given ID and destroyed.</returns>
    bool TryDestroyTemporaryStagehand(string StagehandId);
}

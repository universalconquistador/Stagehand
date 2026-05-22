using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Api;

public partial interface IStagehandApi
{
    //
    // TEMPORARY STAGES
    //
    // A temporary Stage is created programmatically and only lasts until the Stagehand plugin is unloaded.
    // Temporary Stages do not appear in the player's local Stage list.
    //

    /// <summary>
    /// Creates or updates the temporary Stage with the given ID, if the given definition string is valid.
    /// </summary>
    /// <remarks>
    /// To show the created stage, call <see cref="TrySetTemporaryStageVisible(string, bool)"/>.
    /// </remarks>
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
    /// Sets whether the temporary stage with the given stage ID is visible, if it exists.
    /// </summary>
    /// <remarks>
    /// Note that all temporary stages are hidden when the player's current location changes.
    /// Consider destroying or re-showing the stage in response to <see cref="IStagehandApi.LocationChanged"/>.
    /// </remarks>
    /// <param name="stageId">The ID of the temporary stage to set the visibility of.</param>
    /// <param name="visible">The new visibility of the temporary stage.</param>
    /// <returns>True if the temporary stage was found with the given ID and its visibility set.</returns>
    bool TrySetTemporaryStageVisible(string stageId, bool visible);

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

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Api;

public readonly record struct LocalStageDefinition(string Filename, string Name, string VersionString, bool IsVisible, Vector3 Translation, Quaternion Rotation, float UniformScale);

public partial interface IStagehandApi
{
    //
    // LOCAL STAGE DEFINITIONS
    //
    // A local Stage definition is a JSON file in the player's Stage definition folder. Plugins can use the Stagehand API to
    // read the available local Stage definitions, but plugins cannot modify them or access their auto-load conditions.
    // Auto-load conditions are only permitted to be set directly by the player in the Stagehand UI. A plugin that needs to show
    // a Stage should create and show a temporary Stage using the temporary Stage API.
    //

    /// <summary>
    /// Gets the list of local Stage definitions.
    /// </summary>
    /// <returns>All the local Stage definitions.</returns>
    LocalStageDefinition[] GetLocalStageDefinitions();

    /// <summary>
    /// Raised when the list of local Stage definitions or any of their info has changed.
    /// </summary>
    /// <remarks>
    /// This event is throttled and may be raised up to one second after the changes occur.
    /// <br />
    /// Use <see cref="GetLocalStageDefinitions"/> to get the new list of local Stage definitions.
    /// </remarks>
    event Action LocalStageDefinitionsChanged;

    /// <summary>
    /// Raised when a local Stage definition has been edited by the user (i.e. saved in the Stagehand editor).
    /// </summary>
    /// <remarks>
    /// The parameter is the full disk path to the definition that was edited.
    /// </remarks>
    event Action<string> LocalStageDefinitionEdited;
}

using Dalamud.Plugin.Ipc.Exceptions;
using HQIPC;

namespace Stagehand.Api;

/// <summary>
/// An API version number, with a major component for breaking changes and a minor component for non-breaking additions.
/// </summary>
/// <param name="Major">The major revision number, incremented when a breaking change is made to the API.</param>
/// <param name="Minor">The minor revision number, incremented when a non-breaking addition is made to the API.</param>
public readonly record struct ApiRevision(int Major, int Minor)
{
    /// <summary>
    /// Creates a string representation of this API revision with the major and minor components.
    /// </summary>
    /// <returns>This revision as a string.</returns>
    public override string ToString()
    {
        return $"{Major}.{Minor}";
    }
}

/// <summary>
/// Provides access to the Stagehand API over Dalamud IPC.
/// </summary>
public static partial class StagehandApi
{
    /// <summary>
    /// The Stagehand API revision of this version of the Stagehand.Api library.
    /// </summary>
    public static readonly ApiRevision LibraryApiRevision = new(Major: 0, Minor: 2);
}

/// <summary>
/// The Dalamud IPC interface for working with the Stagehand plugin.
/// </summary>
/// <remarks>
/// Use <see cref="StagehandApi.CreateIpcClient(Dalamud.Plugin.IDalamudPluginInterface)"/> to connect to Stagehand.
/// </remarks>
[IpcInterface("Stagehand")]
public partial interface IStagehandApi
{
    /// <summary>
    /// Gets the revision of the Stagehand API provided by the Stagehand plugin.
    /// </summary>
    /// <returns>The API revision of the Stagehand plugin.</returns>
    ApiRevision GetPluginApiRevision();
}

/// <summary>
/// The possible states of the Stagehand IPC API.
/// </summary>
public enum StagehandApiAvailability
{
    /// <summary>
    /// The Stagehand IPC API is available for use.
    /// </summary>
    Available = 0,

    /// <summary>
    /// The Stagehand IPC API is unavailable because the Stagehand plugin is disabled or not installed.
    /// </summary>
    StagehandMissing = 1,

    /// <summary>
    /// The Stagehand IPC API is unavailable because the installed version of the Stagehand plugin is too old.
    /// </summary>
    StagehandTooOld = 2,

    /// <summary>
    /// The Stagehand IPC API is unavailable because the installed version of the Stagehand plugin is too new.
    /// </summary>
    StagehandTooNew = 3,
}

/// <summary>
/// Helpers for consuming the Staghand IPC API.
/// </summary>
public static class StagehandApiExtensions
{
    /// <summary>
    /// Checks whether a compatible version of the Stagehand plugin is installed and available to provide the IPC API.
    /// </summary>
    /// <param name="stagehandApi">The Stagehand API client.</param>
    /// <returns>Whether the Stagehand IPC API is available.</returns>
    public static StagehandApiAvailability CheckApiAvailability(this IStagehandApi stagehandApi)
    {
        ApiRevision stagehandRevision;
        try
        {
            stagehandRevision = stagehandApi.GetPluginApiRevision();
        }
        catch (IpcNotReadyError)
        {
            // The Stagehand API IPCs are not registered because Stagehand is disabled or not installed.
            return StagehandApiAvailability.StagehandMissing;
        }

        var libraryRevision = StagehandApi.LibraryApiRevision;
        if (stagehandRevision.Major < libraryRevision.Major
            || (stagehandRevision.Major == libraryRevision.Major && stagehandRevision.Minor < libraryRevision.Minor))
        {
            // The Stagehand plugin was built against an older version of the Stagehand.Api library and therefore is missing features that this version expects.
            return StagehandApiAvailability.StagehandTooOld;
        }

        if (stagehandRevision.Major > libraryRevision.Major)
        {
            // The Stagehand plugin was built against a newer version of the Stagehand.Api library that has made breaking changes since this version.
            return StagehandApiAvailability.StagehandTooNew;
        }

        // The Stagehand plugin was built against either this version of the Stagehand.Api library or a newer one with non-breaking additions.
        return StagehandApiAvailability.Available;
    }
}

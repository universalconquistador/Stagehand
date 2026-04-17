using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Definitions;

/// <summary>
/// A collection of file swaps and replacements to be applied to objects.
/// </summary>
public class EmbeddedModpackDefinition
{
    /// <summary>
    /// The user-designated name of this modpack. Does not affect functionality in any way.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The directory name of the Penumbra mod this embedded modpack was created from, if any.
    /// </summary>
    /// <remarks>
    /// This lets the editor prompt the user to update this embedded modpack to reflect any changes
    /// in the source Penumbra mod.
    /// </remarks>
    public string PenumbraSourceModDirectory { get; set; } = string.Empty;

    /// <summary>
    /// The version string of the Penumbra mod that this modpack was last based on, if any.
    /// </summary>
    public string PenumbraSourceModVersion { get; set; } = string.Empty;

    /// <summary>
    /// A mapping from redirected game paths to the game paths to redirect them to.
    /// </summary>
    public Dictionary<string, string> FileRedirections { get; set; } = new();

    /// <summary>
    /// A mapping from replaced game paths to the file bytes to replace them with.
    /// </summary>
    public Dictionary<string, byte[]> FileReplacements { get; set; } = new();

    /// <summary>
    /// Computes a hash of the effects this modpack has, including file redirections and replacements.
    /// </summary>
    /// <remarks>
    /// Does not include properties that do not effect the game, such as display name and penumbra info.
    /// </remarks>
    /// <returns></returns>
    public string ComputeEffectiveHash()
    {
        // TODO: Implement
        throw new NotImplementedException();
    }
}

using Stagehand.Definitions.ModResources;
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
    /// A mapping from modded game paths to the resource to use for them.
    /// </summary>
    public Dictionary<string, ModResourceDefinition> ModdedResources { get; set; } = new();
}

using Dalamud.Configuration;
using Stagehand.Services;
using System;
using System.Collections.Generic;

namespace Stagehand;

[Serializable]
public class StagehandConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    /// <summary>
    /// The full path, not ending in a slash, to the directory to store the player's local Stage definitions in.
    /// </summary>
    public string DefinitionLibraryPath { get; set; } = "";

    /// <summary>
    /// A mapping from the full path of a local definition .json file to the conditions under which it should
    /// be automatically shown.
    /// </summary>
    /// <remarks>
    /// Don't edit this directly; go through <see cref="ILocalDefinitionService"/>.
    /// </remarks>
    public Dictionary<string, List<AutomaticShowCondition>> AutomaticShowConditions { get; set; } = new();

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

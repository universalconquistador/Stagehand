using Dalamud.Configuration;
using Stagehand.Services;
using System;
using System.Collections.Generic;

namespace Stagehand;

public enum HoverPreviewMode
{
    None,
    NearPlayer,
    EditorObject,
}

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

    /// <summary>
    /// How to preview assets when they are hovered in the Asset Library.
    /// </summary>
    public HoverPreviewMode AssetLibraryPreviewMode { get; set; } = HoverPreviewMode.NearPlayer;

    public bool LogMemoryResourceUntouched { get; set; } = true;
    public bool LogMemoryResourceHandled { get; set; } = true;

    public bool LogModpackResourceUntouched { get; set; } = true;
    public bool LogModpackResourceHandled { get; set; } = true;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

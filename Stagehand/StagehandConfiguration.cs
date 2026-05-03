using Dalamud.Configuration;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.IO;

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
    private const string AutosaveFolderName = "autosave";

    public int Version { get; set; } = 0;

    /// <summary>
    /// The full path, not ending in a slash, to the directory to store the player's local Stage definitions in.
    /// </summary>
    public string DefinitionLibraryPath { get; set; } = "";

    /// <summary>
    /// The given path to the directory to periodically autosave Stage definitions into while they are being edited.
    /// </summary>
    /// <remarks>
    /// Set to the empty string to use the default value.
    /// </remarks>
    public string AutosavePath { get; set; } = "";

    /// <summary>
    /// The absolute path to the definition autosave directory.
    /// </summary>
    public string FinalAutosavePath => AutosavePath != "" ? AutosavePath : Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), AutosaveFolderName);

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

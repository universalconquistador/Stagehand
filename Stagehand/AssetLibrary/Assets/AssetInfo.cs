using Stagehand.Definitions.Objects;
using Stagehand.Live;
using System.Numerics;

namespace Stagehand.AssetLibrary.Assets;

/// <summary>
/// The base class for information about an asset in the asset library.
/// </summary>
public record class AssetInfo(string DisplayName, AssetType Type, string ID)
{
    /// <summary>
    /// Draws the properties of this asset into the selected asset pane of the asset library.
    /// </summary>
    public virtual void DrawProperties()
    { }

    /// <summary>
    /// Creates a live object at the given location and rotation to preview this asset.
    /// </summary>
    public virtual ILiveObject? CreatePreviewObject(ILiveObjectService liveObjectService, Vector3 location, Quaternion rotation)
    {
        return null;
    }

    /// <summary>
    /// Creates a new object definition for adding this asset to a Stage definition.
    /// </summary>
    public virtual ObjectDefinition? CreateObjectDefinition(Vector3 location, Quaternion rotation)
    {
        return null;
    }
}

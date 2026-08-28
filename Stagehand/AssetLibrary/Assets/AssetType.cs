using Dalamud.Interface;

namespace Stagehand.AssetLibrary.Assets;

/// <summary>
/// A type of asset in the asset library.
/// </summary>
/// <param name="DisplayName">The user-facing name of this asset type.</param>
/// <param name="DisplayDescription">The description of this asset type.</param>
/// <param name="Icon">The icon for this asset type.</param>
public record class AssetType(string DisplayName, string DisplayDescription, FontAwesomeIcon Icon)
{
    public static readonly AssetType<MdlResourceAssetInfo> MdlResource = new("Model Resource", ".mdl", FontAwesomeIcon.Cube);
    public static readonly AssetType<AvfxResourceAssetInfo> AvfxResource = new("VFX Resource", ".avfx", FontAwesomeIcon.WandSparkles);
    public static readonly AssetType<ResourceAssetInfo> SgbResource = new("Shared Group Resource", ".sgb", FontAwesomeIcon.Archive);
    public static readonly AssetType<ScdResourceAssetInfo> ScdResource = new("Sound Resource", ".scd", FontAwesomeIcon.VolumeUp);
}

public record class AssetType<TAssetInfo>(string DisplayName, string DisplayDescription, FontAwesomeIcon Icon) : AssetType(DisplayName, DisplayDescription, Icon);

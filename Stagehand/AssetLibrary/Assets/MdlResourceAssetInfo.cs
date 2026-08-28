using Stagehand.Definitions.Objects;
using Stagehand.Live;
using System.Numerics;

namespace Stagehand.AssetLibrary.Assets;

/// <summary>
/// Asset info for a model resource (*.mdl)
/// </summary>
public record class MdlResourceAssetInfo(string DisplayName, string GamePath) : ResourceAssetInfo(DisplayName, AssetType.MdlResource, GamePath)
{
    public override ILiveObject? CreatePreviewObject(ILiveObjectService liveObjectService, Vector3 location, Quaternion rotation)
    {
        return liveObjectService.CreateBgObject(GamePath, location, rotation, Vector3.One, modpack: null);
    }

    public override ObjectDefinition? CreateObjectDefinition(Vector3 location, Quaternion rotation)
    {
        return new BgObjectDefinition()
        {
            DisplayName = DisplayName,
            ModelGamePath = GamePath,
            Position = location,
            RotationQuaternion = rotation,
        };
    }
}

using Stagehand.Definitions.Objects;
using Stagehand.Live;
using System.Numerics;

namespace Stagehand.AssetLibrary.Assets;

/// <summary>
/// Asset info for a VFX resource (*.avfx)
/// </summary>
public record class AvfxResourceAssetInfo(string DisplayName, string GamePath) : ResourceAssetInfo(DisplayName, AssetType.AvfxResource, GamePath)
{
    public override ILiveObject? CreatePreviewObject(ILiveObjectService liveObjectService, Vector3 location, Quaternion rotation)
    {
        return liveObjectService.CreateVfx(GamePath, location, rotation, Vector3.One, Vector4.One, modpack: null);
    }

    public override ObjectDefinition? CreateObjectDefinition(Vector3 location, Quaternion rotation)
    {
        return new VfxObjectDefinition()
        {
            DisplayName = DisplayName,
            VfxGamePath = GamePath,
            Position = location,
            RotationQuaternion = rotation,
        };
    }
}

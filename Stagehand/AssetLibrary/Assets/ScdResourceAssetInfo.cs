using Stagehand.Definitions.Objects;
using Stagehand.Live;
using System.Numerics;

namespace Stagehand.AssetLibrary.Assets;

/// <summary>
/// Asset info for a Sound resource (*.scd)
/// </summary>
public record class ScdResourceAssetInfo(string DisplayName, string GamePath) : ResourceAssetInfo(DisplayName, AssetType.ScdResource, GamePath)
{
    public override ILiveObject? CreatePreviewObject(ILiveObjectService liveObjectService, Vector3 location, Quaternion rotation)
    {
        return liveObjectService.CreateSound(GamePath, soundIndex: 0, volume: 1.0f, fadeInDuration: 0.0f, speed: 1.0f, isPositional: true, location, modpack: null);
    }

    public override ObjectDefinition? CreateObjectDefinition(Vector3 location, Quaternion rotation)
    {
        return new SoundObjectDefinition()
        {
            DisplayName = DisplayName,
            SoundGamePath = GamePath,
            Position = location,
            RotationQuaternion = rotation,
        };
    }
}

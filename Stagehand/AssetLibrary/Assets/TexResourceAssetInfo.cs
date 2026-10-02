using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.AssetLibrary.Assets;

public record class TexResourceAssetInfo(string DisplayName, string GamePath) : ResourceAssetInfo(DisplayName, AssetType.TexResource, GamePath)
{
}

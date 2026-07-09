using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.AssetThumbnailer;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; }

    public string OutputDirectory { get; set; } = "";

    public string LastQueueDirectory { get; set; } = "";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.ApiDemo;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; }

    public bool EnableTrail { get; set; } = true;
    public float PlacementIntervalSeconds { get; set; } = 1.0f;
    public float PlacementLifespanSeconds { get; set; } = 5.0f;
    public string PlacementVFXGamePath { get; set; } = "bg/ffxiv/fst_f1/common/vfx/eff/b0941trp1a_o.avfx";
    public float PlacementScale { get; set; } = 1.0f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

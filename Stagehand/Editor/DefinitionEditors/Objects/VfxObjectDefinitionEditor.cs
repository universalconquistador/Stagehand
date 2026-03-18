using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class VfxObjectDefinitionEditor : IObjectDefinitionEditor<VfxObjectDefinition>
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("VFX", "An instance of a visual effect.", FontAwesomeIcon.WandSparkles);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    private readonly IDataManager _dataManager;

    public string VfxGamePath
    {
        get => Definition.VfxGamePath;
        set => SetPropertyValue(value => Definition.VfxGamePath = value, value);
    }

    public Vector4 Color
    {
        get => Definition.Color;
        set => SetPropertyValue(value => Definition.Color = value, value);
    }

    public VfxObjectDefinitionEditor(IServiceProvider serviceProvider, VfxObjectDefinition definition, string key, StageDefinitionEditor stage) : base(serviceProvider, definition, key, stage)
    {
        _dataManager = serviceProvider.GetRequiredService<IDataManager>();
    }

    public override void DrawProperties()
    {
        base.DrawProperties();

        string vfxGamePath = VfxGamePath;
        if (ImGui.InputText("VFX Resource (Game Path: .avfx)", ref vfxGamePath, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            VfxGamePath = vfxGamePath;
        }

        bool exists = _dataManager.GameData.FileExists(VfxGamePath);
        var icon = exists ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.ExclamationCircle;
        float propertiesColumnWidth = (ImGui.GetContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) * 0.333f;
        ImGui.SameLine(ImGui.GetContentRegionMax().X - propertiesColumnWidth - 16.0f * ImGuiHelpers.GlobalScale);
        using (ImRaii.PushColor(ImGuiCol.Text, exists ? ImGuiColors.HealerGreen : ImGuiColors.DPSRed))
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextUnformatted(icon.ToIconString());
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(exists ? "Game path exists" : "Game path does not exist");
            }
        }

        Vector4 color = Color;
        if (ImGui.ColorEdit4("Color", ref color))
        {
            Color = color;
        }
    }
}

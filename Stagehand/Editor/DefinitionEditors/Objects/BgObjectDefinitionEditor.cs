using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Style;
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

internal class BgObjectDefinitionEditor : IObjectDefinitionEditor<BgObjectDefinition>
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Background Object", "A static mesh in the scene.", FontAwesomeIcon.Cube);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    private readonly IDataManager _dataManager;

    public string ModelGamePath
    {
        get => Definition.ModelGamePath;
        set => SetPropertyValue(value => Definition.ModelGamePath = value, value);
    }

    public float Opacity
    {
        get => Definition.Opacity;
        set => SetPropertyValue(value => Definition.Opacity = value, value);
    }

    public Vector4 DyeColor
    {
        get => Definition.DyeColor;
        set => SetPropertyValue(value => Definition.DyeColor = value, value);
    }

    public BgObjectDefinitionEditor(IServiceProvider serviceProvider, BgObjectDefinition definition, string key, StageDefinitionEditor stage) : base(serviceProvider, definition, key, stage)
    {
        _dataManager = serviceProvider.GetRequiredService<IDataManager>();
    }

    public override void DrawProperties()
    {
        base.DrawProperties();

        string modelGamePath = ModelGamePath;
        if (ImGui.InputText("Model (Game Path: .mdl)", ref modelGamePath, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ModelGamePath = modelGamePath;
        }

        bool exists = _dataManager.GameData.FileExists(ModelGamePath);
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

        float opacity = Opacity;
        if (ImGui.SliderFloat("Opacity", ref opacity, vMin: 0.0f, vMax: 1.0f))
        {
            Opacity = opacity;
        }

        Vector4 dyeColor = DyeColor;
        if (ImGui.ColorEdit4("Dye Color", ref dyeColor))
        {
            DyeColor = dyeColor;
        }
    }
}

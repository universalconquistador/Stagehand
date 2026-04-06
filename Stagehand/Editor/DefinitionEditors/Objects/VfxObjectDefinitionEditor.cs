using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stagehand.Definitions.Objects;
using Stagehand.Windows;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class VfxObjectDefinitionEditor : ObjectDefinitionEditor<VfxObjectDefinition>
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("VFX", "An instance of a visual effect.", FontAwesomeIcon.WandSparkles);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    private readonly IDataManager _dataManager;
    private readonly IAssetLibraryWindow _assetLibraryWindow;

    public string VfxGamePath
    {
        get => Definition.VfxGamePath;
        set => SetPropertyValue(value => Definition.VfxGamePath = value, value, Definition.VfxGamePath);
    }

    public Vector4 Color
    {
        get => Definition.Color;
        set => SetPropertyValue(value => Definition.Color = value, value, Definition.Color);
    }

    public VfxObjectDefinitionEditor(IServiceProvider serviceProvider, VfxObjectDefinition definition, string key, StageDefinitionEditor stage) : base(serviceProvider, definition, key, stage)
    {
        _dataManager = serviceProvider.GetRequiredService<IDataManager>();
        _assetLibraryWindow = serviceProvider.GetRequiredService<IAssetLibraryWindow>();
    }

    protected override void SetDisplayNameInternal(string displayName)
    {
        base.SetDisplayNameInternal(displayName);
        if (IsSelected)
        {
            _assetLibraryWindow.SetSelectionCallback(DisplayName, "VFX", AssetType.AvfxResource, () => IsInStage && IsSelected, asset => VfxGamePath = asset.GamePath);
        }
    }

    public override void Selected()
    {
        base.Selected();

        _assetLibraryWindow.SetSelectionCallback(DisplayName, "VFX", AssetType.AvfxResource, () => IsInStage && IsSelected, asset => VfxGamePath = asset.GamePath);
    }

    protected override void OnDrawProperties()
    {
        base.OnDrawProperties();

        string vfxGamePath = VfxGamePath;
        if (ImGui.InputText("VFX Path", ref vfxGamePath, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
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
        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
        if (ImGuiComponents.IconButton(IAssetLibraryWindow.Icon, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight())))
        {
            _assetLibraryWindow.Show();
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted("Open the Asset Library");
            }
        }

        Vector4 color = Color;
        if (ImGui.ColorEdit4("Color", ref color))
        {
            Color = color;
        }
    }
}

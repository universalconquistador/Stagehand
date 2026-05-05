using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using Stagehand.Windows;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class BgObjectDefinitionEditor : ObjectDefinitionEditor<BgObjectDefinition>
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Background Object", "A static mesh in the scene.", FontAwesomeIcon.Cube);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    private readonly IDataManager _dataManager;
    private readonly IEditorHitTestService _hitTestService;
    private readonly IAssetLibraryWindow _assetLibraryWindow;
    private readonly EditorHitTestModel _hitTestModel;

    public string ModelGamePath
    {
        get => Definition.ModelGamePath;
        set => SetPropertyValue(SetModelGamePathInternal, value, Definition.ModelGamePath);
    }

    public float Opacity
    {
        get => Definition.Opacity;
        set => SetPropertyValue(value => Definition.Opacity = value, value, Definition.Opacity);
    }

    public Vector4 DyeColor
    {
        get => Definition.DyeColor;
        set => SetPropertyValue(value => Definition.DyeColor = value, value, Definition.DyeColor);
    }

    public BgObjectDefinitionEditor(IServiceProvider serviceProvider, BgObjectDefinition definition, string key, StageDefinitionEditor stage) : base(serviceProvider, definition, key, stage)
    {
        _dataManager = serviceProvider.GetRequiredService<IDataManager>();
        _hitTestService = serviceProvider.GetRequiredService<IEditorHitTestService>();
        _assetLibraryWindow = serviceProvider.GetRequiredService<IAssetLibraryWindow>();
        _hitTestModel = new EditorHitTestModel(this, ModelGamePath, serviceProvider.GetRequiredService<IModelBvhCacheService>(), serviceProvider.GetRequiredService<IDataManager>())
        {
            Position = Position,
            Rotation = RotationQuaternion,
            Scale = Scale,
            Modpack = GetPreviewModpack(),
        };
    }

    public override void AddedToStage()
    {
        base.AddedToStage();

        _hitTestService.AddShape(_hitTestModel);
    }

    protected override void SetPositionInternal(Vector3 position)
    {
        base.SetPositionInternal(position);
        _hitTestModel.Position = Position;
    }

    protected override void SetRotationPitchYawRollDegreesInternal(Vector3 rotationPYRDegrees)
    {
        base.SetRotationPitchYawRollDegreesInternal(rotationPYRDegrees);
        _hitTestModel.Rotation = RotationQuaternion;
    }

    protected override void SetRotationQuaternionInternal(Quaternion rotationQuaternion)
    {
        base.SetRotationQuaternionInternal(rotationQuaternion);
        _hitTestModel.Rotation = RotationQuaternion;
    }

    protected override void SetScaleInternal(Vector3 scale)
    {
        base.SetScaleInternal(scale);
        _hitTestModel.Scale = Scale;
    }

    protected virtual void SetModelGamePathInternal(string modelGamePath)
    {
        Definition.ModelGamePath = modelGamePath;
        _hitTestModel.ModelResourcePath = ModelGamePath;
    }

    protected override void SetDisplayNameInternal(string displayName)
    {
        base.SetDisplayNameInternal(displayName);
        if (IsSelected)
        {
            _assetLibraryWindow.SetSelectionCallback(DisplayName, "Model Path", AssetType.MdlResource, () => IsInStage && IsSelected, asset => ModelGamePath = asset.GamePath);
        }
    }

    protected override void SetModpackIdInternal(string modpackId)
    {
        base.SetModpackIdInternal(modpackId);
        _hitTestModel.Modpack = GetPreviewModpack();
    }

    public override void RemovedFromStage()
    {
        _hitTestService.RemoveShape(_hitTestModel);

        base.RemovedFromStage();
    }

    public override void Selected()
    {
        base.Selected();

        _assetLibraryWindow.SetSelectionCallback(DisplayName, "Model Path", AssetType.MdlResource, () => IsInStage && IsSelected, asset => ModelGamePath = asset.GamePath);
    }

    protected override void OnDrawProperties()
    {
        base.OnDrawProperties();

        string modelGamePath = ModelGamePath;
        if (ImGui.InputText("Model Path", ref modelGamePath, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
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

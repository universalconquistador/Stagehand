using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stagehand.AssetLibrary.Assets;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class VfxObjectDefinitionEditor : ObjectDefinitionEditor<VfxObjectDefinition>
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("VFX", "An instance of a visual effect.", FontAwesomeIcon.WandSparkles);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

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

    }

    protected override void SetDisplayNameInternal(string displayName)
    {
        base.SetDisplayNameInternal(displayName);
        if (IsSelected)
        {
            AssetLibraryWindow.SetSelectionCallback(DisplayName, "VFX", AssetType.AvfxResource, () => IsInStage && IsSelected, asset => VfxGamePath = asset.GamePath);
        }
    }

    public override void Selected()
    {
        base.Selected();

        AssetLibraryWindow.SetSelectionCallback(DisplayName, "VFX", AssetType.AvfxResource, () => IsInStage && IsSelected, asset => VfxGamePath = asset.GamePath);
    }

    protected override void OnDrawProperties()
    {
        base.OnDrawProperties();

        string vfxGamePath = VfxGamePath;
        if (DrawResourceGamePath("VFX Path", ref vfxGamePath, AssetType.AvfxResource))
        {
            VfxGamePath = vfxGamePath;
        }

        Vector4 color = Color;
        if (ImGui.ColorEdit4("Color", ref color))
        {
            Color = color;
        }
    }
}

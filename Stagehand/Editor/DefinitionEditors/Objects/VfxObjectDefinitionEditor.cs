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
using Stagehand.Editor.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class VfxObjectDefinitionEditor : ObjectDefinitionEditor<VfxObjectDefinition>
{
    private const float HitTestRadius = 0.25f;
    
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("VFX", "An instance of a visual effect.", FontAwesomeIcon.WandSparkles);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    private readonly IEditorHitTestService _hitTestService;
    private readonly EditorHitTestSphere _hitTestSphere;

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
        _hitTestService = serviceProvider.GetRequiredService<IEditorHitTestService>();
        _hitTestSphere = new EditorHitTestSphere(this, new FFXIVClientStructs.FFXIV.Common.Math.SphereBounds() { CenterPoint = definition.Position, Radius = HitTestRadius });
    }

    public override void AddedToStage()
    {
        base.AddedToStage();

        _hitTestService.AddShape(_hitTestSphere);
    }

    public override void RemovedFromStage()
    {
        _hitTestService.RemoveShape(_hitTestSphere);

        base.RemovedFromStage();
    }

    public override bool TryGetOrientedBounds(out FFXIVClientStructs.FFXIV.Common.Math.OrientedBounds orientedBounds)
    {
        orientedBounds = new()
        {
            Transform = WorldTransformNoScale,
            HalfExtents = new(HitTestRadius * 0.5f),
        };
        return true;
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

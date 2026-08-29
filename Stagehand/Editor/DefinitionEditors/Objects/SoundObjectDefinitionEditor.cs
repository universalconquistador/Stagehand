using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.AssetLibrary.Assets;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class SoundObjectDefinitionEditor : ObjectDefinitionEditor<SoundObjectDefinition>
{
    private const float HitTestRadius = 0.25f;

    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Sound", "An instance of a sound.", FontAwesomeIcon.VolumeUp);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    private readonly IEditorHitTestService _hitTestService;
    private readonly EditorHitTestSphere _hitTestSphere;
    
    public string SoundGamePath
    {
        get => Definition.SoundGamePath;
        set => SetPropertyValue(value => Definition.SoundGamePath = value, value, Definition.SoundGamePath);
    }

    public int SoundIndex
    {
        get => Definition.SoundIndex;
        set => SetPropertyValue(value => Definition.SoundIndex = value, value, Definition.SoundIndex);
    }

    public float Volume
    {
        get => Definition.Volume;
        set => SetPropertyValue(value => Definition.Volume = value, value, Definition.Volume);
    }

    public float FadeInDurationSeconds
    {
        get => Definition.FadeInDurationSeconds;
        set => SetPropertyValue(value => Definition.FadeInDurationSeconds = value, value, Definition.FadeInDurationSeconds);
    }

    public float Speed
    {
        get => Definition.Speed;
        set => SetPropertyValue(value => Definition.Speed = value, value, Definition.Speed);
    }

    public bool IsPositional
    {
        get => Definition.IsPositional;
        set => SetPropertyValue(value => Definition.IsPositional = value, value, Definition.IsPositional);
    }

    public SoundObjectDefinitionEditor(IServiceProvider serviceProvider, SoundObjectDefinition definition, string key, StageDefinitionEditor stage) : base(serviceProvider, definition, key, stage)
    {
        _hitTestService = serviceProvider.GetRequiredService<IEditorHitTestService>();
        _hitTestSphere = new EditorHitTestSphere(this, new FFXIVClientStructs.FFXIV.Common.Math.SphereBounds() { CenterPoint = definition.Position, Radius = HitTestRadius });
    }

    public override void AddedToStage()
    {
        base.AddedToStage();

        _hitTestService.AddShape(_hitTestSphere);
    }

    protected override void SetPositionInternal(Vector3 position)
    {
        base.SetPositionInternal(position);
        _hitTestSphere.Sphere = _hitTestSphere.Sphere with { CenterPoint = WorldPosition };
    }

    public override void SetParentTransform(Vector3 parentTranslation, Quaternion parentRotation, float parentUniformScale)
    {
        base.SetParentTransform(parentTranslation, parentRotation, parentUniformScale);
        _hitTestSphere.Sphere = _hitTestSphere.Sphere with { CenterPoint = WorldPosition };
    }

    public override void RemovedFromStage()
    {
        _hitTestService.RemoveShape(_hitTestSphere);

        base.RemovedFromStage();
    }

    protected override void SetDisplayNameInternal(string displayName)
    {
        base.SetDisplayNameInternal(displayName);

        if (IsSelected)
        {
            SetAssetLibraryTarget();
        }
    }

    public override void Selected()
    {
        base.Selected();

        SetAssetLibraryTarget();
    }

    private void SetAssetLibraryTarget()
    {
        AssetLibraryWindow.SetSelectionCallback(DisplayName, "Sound", AssetType.ScdResource, () => IsInStage && IsSelected, asset => SoundGamePath = asset.GamePath);
    }

    protected override void OnDrawProperties()
    {
        base.OnDrawProperties();

        string soundGamePath = SoundGamePath;
        if (DrawResourceGamePath("Sound Path", ref soundGamePath, AssetType.ScdResource))
        {
            SoundGamePath = soundGamePath;
        }

        int soundIndex = SoundIndex;
        if (ImGui.InputInt("Sound Index", ref soundIndex))
        {
            SoundIndex = soundIndex;
        }

        // TODO: Play widget for playing the selected sound index in the selected sound resource

        float volume = Volume;
        if (ImGui.SliderFloat("Volume", ref volume, vMin: 0.0f, vMax: 2.0f))
        {
            Volume = volume;
        }

        float fadeInSeconds = FadeInDurationSeconds;
        if (ImGui.DragFloat("Fade In (Seconds)", ref fadeInSeconds, vSpeed: 0.01f, vMin: 0.0f, vMax: float.MaxValue))
        {
            FadeInDurationSeconds = fadeInSeconds;
        }

        float speed = Speed;
        if (ImGui.DragFloat("Speed", ref speed, vSpeed: 0.01f, vMin: 0.01f, vMax: float.MaxValue))
        {
            Speed = speed;
        }

        bool isPositional = IsPositional;
        if (ImGui.Checkbox("Is Positional", ref isPositional))
        {
            IsPositional = isPositional;
        }
    }

    protected override void DrawOverlays(IOverlayDrawContext obj)
    {
        var color = ComputeOverlayColor();

        var transform = WorldTransformNoScale;
        var localX = transform.X.AsVector3();
        var localY = transform.Y.AsVector3();
        var localZ = transform.Z.AsVector3();

        obj.DrawCircle(WorldPosition, localX, localY, HitTestRadius * 0.55f, IsSelected ? 2.0f : 1.0f, color);
        obj.DrawCircle(WorldPosition, localY, localZ, HitTestRadius * 0.55f, IsSelected ? 2.0f : 1.0f, color);
        obj.DrawCircle(WorldPosition, localZ, localX, HitTestRadius * 0.55f, IsSelected ? 2.0f : 1.0f, color);
    }
}

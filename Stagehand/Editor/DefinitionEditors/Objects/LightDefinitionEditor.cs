using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class LightDefinitionEditor : IObjectDefinitionEditor<LightDefinition>
{
    private const float HitTestRadius = 0.10f;

    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Light", "A light source.", FontAwesomeIcon.Lightbulb);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    private readonly IEditorHitTestService _hitTestService;
    private readonly EditorHitTestSphere _hitTestSphere;

    // Light

    public LightShape Shape
    {
        get => Definition.Shape;
        set => SetPropertyValue(value => Definition.Shape = value, value);
    }

    public Vector3 Color
    {
        get => Definition.Color;
        set => SetPropertyValue(value => Definition.Color = value, value);
    }

    public float Intensity
    {
        get => Definition.Intensity;
        set => SetPropertyValue(value => Definition.Intensity = value, value);
    }

    public bool EnableSpecularHighlights
    {
        get => Definition.EnableSpecularHighlights;
        set => SetPropertyValue(value => Definition.EnableSpecularHighlights = value, value);
    }

    public Vector2 FlatLightSkewAngleDegrees
    {
        get => Definition.FlatLightSkewAngleDegrees;
        set => SetPropertyValue(value => Definition.FlatLightSkewAngleDegrees = value, value);
    }

    public float SpotLightAngleDegrees
    {
        get => Definition.SpotLightAngleDegrees;
        set => SetPropertyValue(value => Definition.SpotLightAngleDegrees = value, value);
    }

    public float AngularFalloffDegrees
    {
        get => Definition.AngularFalloffDegrees;
        set => SetPropertyValue(value => Definition.AngularFalloffDegrees = value, value);
    }

    // Falloff

    public LightFalloffFunction FalloffFunction
    {
        get => Definition.FalloffFunction;
        set => SetPropertyValue(value => Definition.FalloffFunction = value, value);
    }

    public float FalloffFactor
    {
        get => Definition.FalloffFactor;
        set => SetPropertyValue(value => Definition.FalloffFactor = value, value);
    }

    public float Range
    {
        get => Definition.Range;
        set => SetPropertyValue(value => Definition.Range = value, value);
    }

    // Shadow

    public bool EnableObjectShadows
    {
        get => Definition.EnableObjectShadows;
        set => SetPropertyValue(value => Definition.EnableObjectShadows = value, value);
    }

    public bool EnableCharacterShadows
    {
        get => Definition.EnableCharacterShadows;
        set => SetPropertyValue(value => Definition.EnableCharacterShadows = value, value);
    }

    public bool EnableDynamicShadows
    {
        get => Definition.EnableDynamicShadows;
        set => SetPropertyValue(value => Definition.EnableDynamicShadows = value, value);
    }

    public float ShadowPlaneNear
    {
        get => Definition.ShadowPlaneNear;
        set => SetPropertyValue(value => Definition.ShadowPlaneNear = value, value);
    }

    public float ShadowPlaneFar
    {
        get => Definition.ShadowPlaneFar;
        set => SetPropertyValue(value => Definition.ShadowPlaneFar = value, value);
    }

    public float CharacterShadowRange
    {
        get => Definition.CharacterShadowRange;
        set => SetPropertyValue(value => Definition.CharacterShadowRange = value, value);
    }

    public LightDefinitionEditor(IServiceProvider serviceProvider, LightDefinition definition, string key, StageDefinitionEditor stage) : base(serviceProvider, definition, key, stage)
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
        _hitTestSphere.Sphere = _hitTestSphere.Sphere with { CenterPoint = position };
    }

    public override void RemovedFromStage()
    {
        _hitTestService.RemoveShape(_hitTestSphere);

        base.RemovedFromStage();
    }

    public override void DrawProperties()
    {
        base.DrawProperties();

        // Light properties

        float labelWidth = (ImGui.GetContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) * 0.333f;
        float propertiesColumnWidth = (ImGui.GetContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) - labelWidth;

        float shapeButtonWidth = (propertiesColumnWidth - ImGui.GetStyle().ItemInnerSpacing.X * 3.0f) / 4.0f;
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Sun, "Ambient", defaultColor: Shape == LightShape.Ambient ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive] : null, size: new Vector2(shapeButtonWidth, 0.0f)))
        {
            Shape = LightShape.Ambient;
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Lightbulb, "Point", defaultColor: Shape == LightShape.Point ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive] : null, size: new Vector2(shapeButtonWidth, 0.0f)))
        {
            Shape = LightShape.Point;
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Mountain, "Spot", defaultColor: Shape == LightShape.Spot ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive] : null, size: new Vector2(shapeButtonWidth, 0.0f)))
        {
            Shape = LightShape.Spot;
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Box, "Flat", defaultColor: Shape == LightShape.Flat ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive] : null, size: new Vector2(shapeButtonWidth, 0.0f)))
        {
            Shape = LightShape.Flat;
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.TextUnformatted("Shape");

        var color = Color;
        if (ImGui.ColorEdit3("Color", ref color))
        {
            Color = color;
        }

        var intensity = Intensity;
        if (ImGui.DragFloat("Intensity", ref intensity, vSpeed: 0.01f, vMin: 0.0f, vMax: 20.0f))
        {
            Intensity = intensity;
        }

        var enableSpecularHighlights = EnableSpecularHighlights;
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - labelWidth - ImGui.GetFrameHeight());
        if (ImGui.Checkbox("Specular Highlights", ref enableSpecularHighlights))
        {
            EnableSpecularHighlights = enableSpecularHighlights;
        }

        if (Shape == LightShape.Flat)
        {
            var flatLightSkewAngleDegrees = FlatLightSkewAngleDegrees;
            if (ImGui.SliderFloat2("Skew Angle", ref flatLightSkewAngleDegrees, vMin: -90.0f, vMax: 90.0f))
            {
                FlatLightSkewAngleDegrees = flatLightSkewAngleDegrees;
            }
        }

        if (Shape == LightShape.Spot)
        {
            var spotLightAngleDegrees = SpotLightAngleDegrees;
            if (ImGui.SliderFloat("Cone Angle", ref spotLightAngleDegrees, vMin: 0.0f, vMax: 90.0f))
            {
                SpotLightAngleDegrees = spotLightAngleDegrees;
            }
        }

        if (Shape == LightShape.Spot || Shape == LightShape.Flat)
        {
            var angularFalloffDegrees = AngularFalloffDegrees;
            if (ImGui.SliderFloat("Angular Falloff", ref angularFalloffDegrees, vMin: 0.0f, vMax: 90.0f))
            {
                AngularFalloffDegrees = angularFalloffDegrees;
            }
        }

        ImGuiHelpers.ScaledDummy(4.0f);

        // Falloff

        float falloffButtonWidth = (propertiesColumnWidth - ImGui.GetStyle().ItemInnerSpacing.X * 2.0f) / 3.0f;
        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], FalloffFunction == LightFalloffFunction.Linear))
        {
            if (ImGui.Button("Linear", size: new Vector2(falloffButtonWidth, 0.0f)))
            {
                FalloffFunction = LightFalloffFunction.Linear;
            }
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], FalloffFunction == LightFalloffFunction.Quadratic))
        {
            if (ImGui.Button("Quadratic", size: new Vector2(falloffButtonWidth, 0.0f)))
            {
                FalloffFunction = LightFalloffFunction.Quadratic;
            }
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], FalloffFunction == LightFalloffFunction.Cubic))
        {
            if (ImGui.Button("Cubic", size: new Vector2(falloffButtonWidth, 0.0f)))
            {
                FalloffFunction = LightFalloffFunction.Cubic;
            }
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.TextUnformatted("Falloff Function");

        var falloffFactor = FalloffFactor;
        if (ImGui.SliderFloat("Falloff Factor", ref falloffFactor, vMin: 0.0f, vMax: 3.0f))
        {
            FalloffFactor = falloffFactor;
        }

        var range = Range;
        if (ImGui.SliderFloat("Range", ref range, vMin: 0.0f, vMax: 300.0f))
        {
            Range = range;
        }

        ImGuiHelpers.ScaledDummy(4.0f);

        // Shadow

        float shadowButtonWidth = (propertiesColumnWidth - ImGui.GetStyle().ItemInnerSpacing.X * 2.0f) / 3.0f;
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Shapes, "Objects", defaultColor: EnableObjectShadows ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive] : null, size: new Vector2(shadowButtonWidth, 0.0f)))
        {
            EnableObjectShadows = !EnableObjectShadows;
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Male, "Characters", defaultColor: EnableCharacterShadows ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive] : null, size: new Vector2(shadowButtonWidth, 0.0f)))
        {
            EnableCharacterShadows = !EnableCharacterShadows;
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ArrowsTurnToDots, "Dynamic", defaultColor: EnableDynamicShadows ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive] : null, size: new Vector2(shadowButtonWidth, 0.0f)))
        {
            EnableDynamicShadows = !EnableDynamicShadows;
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.TextUnformatted("Shadow Casting");

        var shadowPlaneNear = ShadowPlaneNear;
        var shadowPlaneFar = ShadowPlaneFar;
        if (ImGui.DragFloatRange2("Shadow Range", ref shadowPlaneNear, ref shadowPlaneFar))
        {
            ShadowPlaneNear = shadowPlaneNear;
            ShadowPlaneFar = shadowPlaneFar;
        }

        var characterShadowRange = CharacterShadowRange;
        if (ImGui.DragFloat("Character Shadow Range", ref characterShadowRange, vMax: 300.0f))
        {
            CharacterShadowRange = characterShadowRange;
        }
    }
}

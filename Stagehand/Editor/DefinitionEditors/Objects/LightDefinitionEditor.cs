using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class LightDefinitionEditor : ObjectDefinitionEditor<LightDefinition>
{
    private const float HitTestRadius = 0.25f;

    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Light", "A light source.", FontAwesomeIcon.Lightbulb);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    private readonly IEditorHitTestService _hitTestService;
    private readonly EditorHitTestSphere _hitTestSphere;

    // Light

    public LightShape Shape
    {
        get => Definition.Shape;
        set => SetPropertyValue(value => Definition.Shape = value, value, Definition.Shape);
    }

    public Vector3 Color
    {
        get => Definition.Color;
        set => SetPropertyValue(value => Definition.Color = value, value, Definition.Color);
    }

    public float Intensity
    {
        get => Definition.Intensity;
        set => SetPropertyValue(value => Definition.Intensity = value, value, Definition.Intensity);
    }

    public bool EnableSpecularHighlights
    {
        get => Definition.EnableSpecularHighlights;
        set => SetPropertyValue(value => Definition.EnableSpecularHighlights = value, value, Definition.EnableSpecularHighlights);
    }

    public Vector2 FlatLightSkewAngleDegrees
    {
        get => Definition.FlatLightSkewAngleDegrees;
        set => SetPropertyValue(value => Definition.FlatLightSkewAngleDegrees = value, value, Definition.FlatLightSkewAngleDegrees);
    }

    public float SpotLightAngleDegrees
    {
        get => Definition.SpotLightAngleDegrees;
        set => SetPropertyValue(value => Definition.SpotLightAngleDegrees = value, value, Definition.SpotLightAngleDegrees);
    }

    public float AngularFalloffDegrees
    {
        get => Definition.AngularFalloffDegrees;
        set => SetPropertyValue(value => Definition.AngularFalloffDegrees = value, value, Definition.AngularFalloffDegrees);
    }

    // Falloff

    public LightFalloffFunction FalloffFunction
    {
        get => Definition.FalloffFunction;
        set => SetPropertyValue(value => Definition.FalloffFunction = value, value, Definition.FalloffFunction);
    }

    public float FalloffFactor
    {
        get => Definition.FalloffFactor;
        set => SetPropertyValue(value => Definition.FalloffFactor = value, value, Definition.FalloffFactor);
    }

    public float Range
    {
        get => Definition.Range;
        set => SetPropertyValue(value => Definition.Range = value, value, Definition.Range);
    }

    // Shadow

    public bool EnableObjectShadows
    {
        get => Definition.EnableObjectShadows;
        set => SetPropertyValue(value => Definition.EnableObjectShadows = value, value, Definition.EnableObjectShadows);
    }

    public bool EnableCharacterShadows
    {
        get => Definition.EnableCharacterShadows;
        set => SetPropertyValue(value => Definition.EnableCharacterShadows = value, value, Definition.EnableCharacterShadows);
    }

    public bool EnableDynamicShadows
    {
        get => Definition.EnableDynamicShadows;
        set => SetPropertyValue(value => Definition.EnableDynamicShadows = value, value, Definition.EnableDynamicShadows);
    }

    public float ShadowPlaneNear
    {
        get => Definition.ShadowPlaneNear;
        set => SetPropertyValue(value => Definition.ShadowPlaneNear = value, value, Definition.ShadowPlaneNear);
    }

    public float ShadowPlaneFar
    {
        get => Definition.ShadowPlaneFar;
        set => SetPropertyValue(value => Definition.ShadowPlaneFar = value, value, Definition.ShadowPlaneFar);
    }

    public float CharacterShadowRange
    {
        get => Definition.CharacterShadowRange;
        set => SetPropertyValue(value => Definition.CharacterShadowRange = value, value, Definition.CharacterShadowRange);
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

    protected override void DrawOverlays(IOverlayDrawContext obj)
    {
        var color = ComputeOverlayColor();
        if (Shape == LightShape.Ambient)
        {
            var transform = WorldTransformNoScale;
            var localX = transform.X.AsVector3() * HitTestRadius;
            var localY = transform.Y.AsVector3() * HitTestRadius;
            var localZ = transform.Z.AsVector3() * HitTestRadius;

            Span<Vector3> points = stackalloc Vector3[]
            {
                new(0.3f, 0.5f, 0.0f),
                new(0.6f, -0.2f, 0.0f),
                new(-0.4f, 0.1f, 0.0f),
            };

            for (int i = 0; i < points.Length; i++)
            {
                obj.DrawLine(transform.Translation + points[i].X * localX + points[i].Y * localY - localZ, transform.Translation + points[i].X * localX + points[i].Y * localY + localZ, IsSelected ? 2.0f : 1.0f, color);
            }
        }
        else if (Shape == LightShape.Point)
        {
            // Selection icon
            var transform = WorldTransformNoScale;
            var localX = transform.X.AsVector3();
            var localY = transform.Y.AsVector3();
            var localZ = transform.Z.AsVector3();

            obj.DrawCircle(WorldPosition, localX, localY, HitTestRadius * 0.55f, IsSelected ? 2.0f : 1.0f, color);
            obj.DrawCircle(WorldPosition, localY, localZ, HitTestRadius * 0.55f, IsSelected ? 2.0f : 1.0f, color);
            obj.DrawCircle(WorldPosition, localZ, localX, HitTestRadius * 0.55f, IsSelected ? 2.0f : 1.0f, color);

            if (IsSelected)
            {
                // Range sphere
                obj.DrawCircle(WorldPosition, localX, localY, Range, 2.0f, color);
                obj.DrawCircle(WorldPosition, localY, localZ, Range, 2.0f, color);
                obj.DrawCircle(WorldPosition, localZ, localX, Range, 2.0f, color);
            }
        }
        else if (Shape == LightShape.Spot)
        {
            if (IsSelected)
            {
                // Primary cone
                obj.DrawCone(WorldTransformNoScale, 0.5f * SpotLightAngleDegrees * MathF.PI / 180.0f, Range, 2.0f, color);

                // Falloff cone
                obj.DrawCone(WorldTransformNoScale, 0.5f * (SpotLightAngleDegrees + AngularFalloffDegrees) * MathF.PI / 180.0f, Range, 1.0f, color with { W = color.W * 0.4f });
            }
            else
            {
                // Selection icon
                obj.DrawCone(WorldTransformNoScale, 0.5f * SpotLightAngleDegrees * MathF.PI / 180.0f, HitTestRadius * 0.85f, 1.0f, color);
            }
        }
        else if (Shape == LightShape.Flat)
        {
            // Selection plane
#if false   // Use rotation for skew
            var transform = Matrix4x4.CreateRotationX(FlatLightSkewAngleDegrees.X * MathF.PI / 180.0f) * Matrix4x4.CreateRotationY(FlatLightSkewAngleDegrees.Y * MathF.PI / 180.0f) * WorldTransform;
#else
            var transform = WorldTransform;
#endif
            var localHalfX = transform.X.AsVector3() * 0.5f;
            var localHalfY = transform.Y.AsVector3() * 0.5f;
            var localZ = transform.Z.AsVector3();

            obj.DrawLine(WorldPosition + localHalfX + localHalfY, WorldPosition + localHalfX - localHalfY, IsSelected ? 2.0f : 1.0f, color);
            obj.DrawLine(WorldPosition + localHalfX - localHalfY, WorldPosition - localHalfX - localHalfY, IsSelected ? 2.0f : 1.0f, color);
            obj.DrawLine(WorldPosition - localHalfX - localHalfY, WorldPosition - localHalfX + localHalfY, IsSelected ? 2.0f : 1.0f, color);
            obj.DrawLine(WorldPosition - localHalfX + localHalfY, WorldPosition + localHalfX + localHalfY, IsSelected ? 2.0f : 1.0f, color);

            var skewVectorX = Vector3.Normalize(transform.X.AsVector3()) * Range * MathF.Tan(FlatLightSkewAngleDegrees.Y * MathF.PI / 180.0f);
            var skewVectorY = -Vector3.Normalize(transform.Y.AsVector3()) * Range * MathF.Tan(FlatLightSkewAngleDegrees.X * MathF.PI / 180.0f);

            obj.DrawLine(WorldPosition, WorldPosition + Vector3.Normalize(localZ) * HitTestRadius, IsSelected ? 2.0f : 1.0f, color);

            if (IsSelected)
            {
#if false       // Use rotation for skew
                Vector3 farSide = WorldPosition + localZ * Range;
#else
                Vector3 farSide = WorldPosition + localZ * Range + skewVectorX + skewVectorY;
#endif

                obj.DrawLine(farSide + localHalfX + localHalfY, farSide + localHalfX - localHalfY, 2.0f, color);
                obj.DrawLine(farSide + localHalfX - localHalfY, farSide - localHalfX - localHalfY, 2.0f, color);
                obj.DrawLine(farSide - localHalfX - localHalfY, farSide - localHalfX + localHalfY, 2.0f, color);
                obj.DrawLine(farSide - localHalfX + localHalfY, farSide + localHalfX + localHalfY, 2.0f, color);

                obj.DrawLine(WorldPosition + localHalfX + localHalfY, farSide + localHalfX + localHalfY, 2.0f, color);
                obj.DrawLine(WorldPosition + localHalfX - localHalfY, farSide + localHalfX - localHalfY, 2.0f, color);
                obj.DrawLine(WorldPosition - localHalfX - localHalfY, farSide - localHalfX - localHalfY, 2.0f, color);
                obj.DrawLine(WorldPosition - localHalfX + localHalfY, farSide - localHalfX + localHalfY, 2.0f, color);
            }
        }
    }

    protected override void OnDrawProperties()
    {
        base.OnDrawProperties();

        // Light properties

        float labelWidth = (ImGui.GetContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) * 0.333f;
        float propertiesColumnWidth = (ImGui.GetContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) - labelWidth;

        float shapeButtonWidth = ((propertiesColumnWidth - ImGui.GetStyle().ItemInnerSpacing.X * 3.0f) / 4.0f) / ImGuiHelpers.GlobalScale;
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
        if (ImGui.DragFloat("Intensity", ref intensity, vSpeed: 0.025f, vMin: 0.0f, vMax: 100.0f))
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
            if (ImGui.SliderFloat("Cone Angle", ref spotLightAngleDegrees, vMin: 0.0f, vMax: 179f))
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

        float shadowButtonWidth = ((propertiesColumnWidth - ImGui.GetStyle().ItemInnerSpacing.X * 2.0f) / 3.0f) / ImGuiHelpers.GlobalScale;
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

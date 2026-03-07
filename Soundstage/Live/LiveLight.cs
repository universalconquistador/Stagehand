using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Soundstage.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using RenderLightShape = FFXIVClientStructs.FFXIV.Client.Graphics.Render.LightShape;
using DefinitionLightShape = Soundstage.Definitions.Objects.LightShape;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;
using RenderLight = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Light;
using SceneLight = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Light;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics;

namespace Soundstage.Live;

internal sealed unsafe class LiveLight : LiveDrawObject
{
    private readonly IFramework _framework;

    private SceneLight* SceneLightPtr => (SceneLight*)ObjectPtr;

    public LightFlags LightFlags { get => SceneLightPtr->RenderLight->LightFlags; set => SceneLightPtr->RenderLight->LightFlags = value; }
    public RenderLightShape LightShape { get => SceneLightPtr->RenderLight->LightShape; set => SceneLightPtr->RenderLight->LightShape = value; }
    public Vector3 Color { get => SceneLightPtr->RenderLight->Color; set => SceneLightPtr->RenderLight->Color = value; }
    public float Intensity { get => SceneLightPtr->RenderLight->Intensity; set => SceneLightPtr->RenderLight->Intensity = value; }
    public float Range { get => SceneLightPtr->RenderLight->Range; set => SceneLightPtr->RenderLight->Range = value; }
    public LightFalloffType FalloffType { get => SceneLightPtr->RenderLight->FalloffType; set => SceneLightPtr->RenderLight->FalloffType = value; }
    public float FalloffFactor { get => SceneLightPtr->RenderLight->FalloffFactor; set => SceneLightPtr->RenderLight->FalloffFactor = value; }
    public float SpotLightAngleDegrees { get => SceneLightPtr->RenderLight->SpotLightAngleDegrees; set => SceneLightPtr->RenderLight->SpotLightAngleDegrees = value; }
    public float AngularFalloffDegrees { get => SceneLightPtr->RenderLight->AngularFalloffDegrees; set => SceneLightPtr->RenderLight->AngularFalloffDegrees = value; }
    public float CharacterShadowRange { get => SceneLightPtr->RenderLight->CharacterShadowRange; set => SceneLightPtr->RenderLight->CharacterShadowRange = value; }
    public float ShadowPlaneNear { get => SceneLightPtr->RenderLight->ShadowPlaneNear; set => SceneLightPtr->RenderLight->ShadowPlaneNear = value; }
    public float ShadowPlaneFar { get => SceneLightPtr->RenderLight->ShadowPlaneFar; set => SceneLightPtr->RenderLight->ShadowPlaneFar = value; }
    public Vector2 FlatLightSkewAngleDegrees { get => SceneLightPtr->RenderLight->FlatLightSkewAngleDegrees; set => SceneLightPtr->RenderLight->FlatLightSkewAngleDegrees = value; }

    public LiveLight(IFramework framework, SceneLight* sceneLight)
        : base((DrawObject*)sceneLight)
    {
        if (sceneLight == null)
            throw new ArgumentNullException(nameof(sceneLight));

        _framework = framework;
        _framework.Update += OnFrameworkUpdate;

        Position = Vector3.Zero;
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;

        SceneLightPtr->RenderLight->Transform = (Transform*)&SceneLightPtr->DrawObject.Position;
        LightShape = RenderLightShape.PointLight;
        LightFlags = LightFlags.SpecularHighlights;

        Color = Vector3.One;
        Intensity = 1.0f;
        SceneLightPtr->RenderLight->MaxRange = RenderLight.UnlimitedMaxRange;

        FalloffType = LightFalloffType.Quadratic;
        FalloffFactor = 1.0f;
        SpotLightAngleDegrees = 45.0f;
        AngularFalloffDegrees = 0.5f;
        Range = 35.0f;
        SceneLightPtr->RenderLight->FlatLightSkewAngleDegrees = new Vector2(0.0f, 0.0f);

        CharacterShadowRange = 110.0f;
        ShadowPlaneNear = 0.5f;
        ShadowPlaneFar = 17.0f;

        Update();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Update();
    }

    public void Update()
    {
        SceneLightPtr->UpdateMaterials();
    }

    public override void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;

        SceneLightPtr->CleanupRender();
        SceneLightPtr->Dtor(DestroyFlagsFree);

        base.Dispose();
    }

    public override bool TryUpdate(ObjectDefinition definition)
    {
        if (definition is LightDefinition lightDefinition)
        {
            LightFlags flags = default;
            if (lightDefinition.EnableSpecularHighlights)
            {
                flags |= LightFlags.SpecularHighlights;
            }
            if (lightDefinition.EnableDynamicShadows)
            {
                flags |= LightFlags.DynamicShadows;
            }
            if (lightDefinition.EnableCharacterShadows)
            {
                flags |= LightFlags.CharacterShadows;
            }
            if (lightDefinition.EnableObjectShadows)
            {
                flags |= LightFlags.ObjectShadows;
            }
            LightFlags = flags;

            LightShape = lightDefinition.Shape switch
            {
                DefinitionLightShape.Ambient => RenderLightShape.WorldLight,
                DefinitionLightShape.Point => RenderLightShape.PointLight,
                DefinitionLightShape.Spot => RenderLightShape.SpotLight,
                DefinitionLightShape.Flat => RenderLightShape.FlatLight,
                _ => RenderLightShape.PointLight,
            };

            Color = lightDefinition.Color;
            Intensity = lightDefinition.Intensity;

            ShadowPlaneNear = lightDefinition.ShadowPlaneNear;
            ShadowPlaneFar = lightDefinition.ShadowPlaneFar;
            FalloffType = lightDefinition.FalloffFunction switch
            {
                LightFalloffFunction.Linear => LightFalloffType.Linear,
                LightFalloffFunction.Quadratic => LightFalloffType.Quadratic,
                LightFalloffFunction.Cubic => LightFalloffType.Cubic,
                _ => LightFalloffType.Cubic,
            };
            FlatLightSkewAngleDegrees = lightDefinition.FlatLightSkewAngleDegrees;
            FalloffFactor = lightDefinition.FalloffFactor;
            SpotLightAngleDegrees = lightDefinition.SpotLightAngleDegrees;
            AngularFalloffDegrees = lightDefinition.AngularFalloffDegrees;
            Range = lightDefinition.Range;
            CharacterShadowRange = lightDefinition.CharacterShadowRange;

            Position = lightDefinition.Position;
            Rotation = lightDefinition.RotationQuaternion;
            Scale = lightDefinition.Scale;
            SceneLightPtr->UpdateTransforms(false);

            Update();
            return true;
        }
        else
        {
            return false;
        }
    }
}

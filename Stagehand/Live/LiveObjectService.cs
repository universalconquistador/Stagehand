using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using RenderLightShape = FFXIVClientStructs.FFXIV.Client.Graphics.Render.LightShape;
using SceneLight = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Light;

namespace Stagehand.Live;

/// <summary>
/// Creates live objects in the FFXIV game scene.
/// </summary>
public interface ILiveObjectService
{
    /// <summary>
    /// Creates a new live light object with the given light shape.
    /// </summary>
    /// <param name="shape">The light shape for the new light.</param>
    /// <returns>The new live light, or null if it could not be created.</returns>
    ILiveObject? CreateLight(RenderLightShape shape);

    /// <summary>
    /// Creates a new live VFX object with the given VFX resource and transformation.
    /// </summary>
    /// <param name="vfxResourceGamePath">The game path to the .avfx resource of the effect to create.</param>
    /// <param name="position">The world space position to create the new VFX object at.</param>
    /// <param name="rotation">The world space rotation to create the new VFX object at.</param>
    /// <param name="scale">The world space scale to create the new VFX object with.</param>
    /// <param name="color">The tint to apply to the new VFX object, or <see cref="Vector4.One"/> to apply no tint.</param>
    /// <returns>The new live VFX object, or null if it could not be created.</returns>
    ILiveObject? CreateVfx(string vfxResourceGamePath, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color);

    /// <summary>
    /// Creates a new live background object with the given model and transformation.
    /// </summary>
    /// <param name="modelGamePath">The game path to the .mdl resource with the model to create.</param>
    /// <param name="position">The world space position to create the new background object at.</param>
    /// <param name="rotation">The world space rotation to create the new background object at.</param>
    /// <param name="scale">The world space scale to create the new background object with.</param>
    /// <returns>The new live background object, or null if it could not be created.</returns>
    ILiveObject? CreateBgObject(string modelGamePath, Vector3 position, Quaternion rotation, Vector3 scale);

    /// <summary>
    /// Creates a new live weapon object from the given model IDs, dye templates, and transformation.
    /// </summary>
    /// <param name="modelSetId">The ID of the model set to create the weapon from.</param>
    /// <param name="secondaryId">The secondary ID of the model to create.</param>
    /// <param name="variant">The variant ID of the model to create.</param>
    /// <param name="stain0">The index of the dye template to use for the primary dye, or zero to apply no dye.</param>
    /// <param name="stain1">The index of the dye template to use for the secondary dye, or zero to apply no dye.</param>
    /// <param name="position">The world space position to create the new weapon object at.</param>
    /// <param name="rotation">The world space rotation to create the new weapon object at.</param>
    /// <param name="scale">The world space scale to create the new weapon object with.</param>
    /// <returns>The new live weapon object, or null if it could not be created.</returns>
    ILiveObject? CreateWeapon(ushort modelSetId, ushort secondaryId, ushort variant, byte stain0, byte stain1, Vector3 position, Quaternion rotation, Vector3 scale);

    /// <summary>
    /// Creates a new live object according to the given object definition.
    /// </summary>
    /// <param name="definition">The object definition specifying the object to create.</param>
    /// <returns>The new live object, or null if it could not be created.</returns>
    ILiveObject? CreateObject(ObjectDefinition definition);

    /// <summary>
    /// Updates the given live object with the given new definition, or disposes and creates a new live object if the new
    /// definition was not compatible with the given live object.
    /// </summary>
    /// <param name="obj">The live object to update or recreate.</param>
    /// <param name="newDefinition">The object definition to apply.</param>
    /// <returns>Either the given live object if it was successfully created, a new live object if the given one was
    /// not compatible and destroyed, or null if a new live object could not be created.</returns>
    ILiveObject? UpdateOrRecreateObject(ILiveObject obj, ObjectDefinition newDefinition);
}

internal unsafe partial class LiveObjectService : ILiveObjectService, IDisposable
{
    private readonly IFramework _framework;

    public LiveObjectService(IFramework framework)
    {
        _framework = framework;
    }

    public ILiveObject? CreateLight(RenderLightShape shape)
    {
        SceneLight* light = null;

        fixed (byte* poolBytesPtr = "Stagehand.Light\0"u8)
        {
            light = SceneLight.Create(shape, poolBytesPtr, null);
        }

        if (light == null)
        {
            // TODO: Log!
            return null;
        }

        var result = new LiveLight(_framework, light);

        return result;
    }

    public ILiveObject? CreateVfx(string vfxResourceGamePath, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color)
    {
        VfxObject* vfxObject;

        Span<byte> pathBytes = stackalloc byte[Encoding.UTF8.GetByteCount(vfxResourceGamePath) + 1];
        Encoding.UTF8.GetBytes(vfxResourceGamePath, pathBytes);
        pathBytes[pathBytes.Length - 1] = 0;

        fixed (byte* pathBytesPtr = pathBytes)
        {
            fixed (byte* poolBytesPtr = "Stagehand.VfxObject\0"u8)
            {
                vfxObject = VfxObject.Create(pathBytesPtr, poolBytesPtr);
            }
        }

        if (vfxObject == null)
        {
            // TODO: Log!
            return null;
        }

        var result = new LiveVfxObject(vfxObject);

        result.Position = position;
        result.Rotation = rotation;
        result.Scale = scale;

        result.Color = color;

        return result;
    }

    public ILiveObject? CreateBgObject(string modelGamePath, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        BgObject* bgObject;

        Span<byte> pathBytes = stackalloc byte[Encoding.UTF8.GetByteCount(modelGamePath) + 1];
        Encoding.UTF8.GetBytes(modelGamePath, pathBytes);
        pathBytes[pathBytes.Length - 1] = 0;

        fixed (byte* pathBytesPtr = pathBytes)
        {
            fixed (byte* poolBytesPtr = "Stagehand.BgObject\0"u8)
            {
                bgObject = BgObject.Create(pathBytesPtr, poolBytesPtr, null);
            }
        }

        if (bgObject == null)
        {
            // TODO: Log!
            return null;
        }

        bgObject->Position = position;
        bgObject->Rotation = rotation;
        bgObject->Scale = scale;

        if (bgObject->ModelResourceHandle != null && bgObject->ModelResourceHandle->LoadState >= 7)
        {
            bgObject->UpdateTransforms(false);
        }

        var result = new LiveBgObject(bgObject);


        return result;
    }

    public ILiveObject? CreateWeapon(ushort modelSetId, ushort secondaryId, ushort variant, byte stain0, byte stain1, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Weapon* weapon;

        WeaponCreateInfo createInfo = new WeaponCreateInfo()
        {
            WeaponModelId =
            {
                Id = modelSetId,
                Type = secondaryId,
                Variant = variant,
                Stain0 = stain0,
                Stain1 = stain1,
            },
            AnimationVariant = 0,
        };
        weapon = Weapon.Create(&createInfo);

        if (weapon == null)
        {
            // TODO: Log!
            return null;
        }

        weapon->Position = position;
        weapon->Rotation = rotation;
        weapon->Scale = scale;

        weapon->UpdateTransforms(false);

        return new LiveWeapon(weapon);
    }

    public void Dispose()
    {
        // TODO: Emergency cleanup of any leftover live objects
    }

    public ILiveObject? CreateObject(ObjectDefinition definition)
    {
        LiveObjectFactoryParams factoryParams = new()
        {
            LiveObjectService = this,
        };
        return definition.Visit<LiveObjectFactory, LiveObjectFactoryParams, ILiveObject?>(ref factoryParams);
    }

    public ILiveObject? UpdateOrRecreateObject(ILiveObject obj, ObjectDefinition newDefinition)
    {
        ILiveObject? result = obj;
        if (!obj.TryUpdate(newDefinition))
        {
            obj.Dispose();
            result = CreateObject(newDefinition);
        }

        return result;
    }

    private record struct LiveObjectFactoryParams(ILiveObjectService LiveObjectService);

    private sealed class LiveObjectFactory : IObjectVisitor<LiveObjectFactoryParams, ILiveObject?>
    {
        public static ILiveObject? VisitBgObjectDefinition(BgObjectDefinition definition, ref LiveObjectFactoryParams param)
        {
            var bgObject = param.LiveObjectService.CreateBgObject(definition.ModelGamePath, definition.Position, definition.RotationQuaternion, definition.Scale);
            bgObject?.TryUpdate(definition);
            return bgObject;
        }

        public static ILiveObject? VisitLightObjectDefinition(LightDefinition definition, ref LiveObjectFactoryParams param)
        {
            var light = param.LiveObjectService.CreateLight(definition.Shape switch { LightShape.Ambient => RenderLightShape.WorldLight, LightShape.Point => RenderLightShape.PointLight, LightShape.Spot => RenderLightShape.SpotLight, LightShape.Flat => RenderLightShape.FlatLight, _ => RenderLightShape.PointLight });
            light?.TryUpdate(definition);
            return light;
        }

        public static ILiveObject? VisitVfxObjectDefinition(VfxObjectDefinition definition, ref LiveObjectFactoryParams param)
        {
            var vfxObject = param.LiveObjectService.CreateVfx(definition.VfxGamePath, definition.Position, definition.RotationQuaternion, definition.Scale, definition.Color);
            //vfxObject?.TryUpdate(definition);
            return vfxObject;
        }

        public static ILiveObject? VisitWeaponObjectDefinition(WeaponDefinition definition, ref LiveObjectFactoryParams param)
        {
            var weaponObject = param.LiveObjectService.CreateWeapon((ushort)definition.ModelSetId, (ushort)definition.SecondaryId, (ushort)definition.Variant, (byte)definition.PrimaryDye, (byte)definition.SecondaryDye, definition.Position, definition.RotationQuaternion, definition.Scale);
            //weaponObject?.TryUpdate(definition);
            return weaponObject;
        }
    }
}

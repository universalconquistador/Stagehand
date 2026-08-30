#define ENABLE_RENDER_HOOK_TESTS

using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Hooking;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Lumina.Models.Materials;
using Microsoft.Extensions.Hosting;
using Stagehand.Api;
using Stagehand.Live;
using Stagehand.Services;
using Stagehand.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Interop.DirectX;
using static FFXIVClientStructs.Interop.ThreadLocals;
using static Stagehand.Live.LiveVfxObject;
using Light = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Light;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Stagehand.Windows;

public unsafe partial class DebugWindow : Window, IHostedService, IDisposable
{
    private const string DebugWindowCommand = "/stagehanddebug";

    private enum BoundsMode
    {
        Sphere,
        AxisAligned,
        Oriented,
    }

    private readonly ILogger _logger;
    private readonly IFramework _framework;
    private readonly ICommandManager _commandManager;
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly IObjectTable _objectTable;
    private readonly IGameGui _gameGui;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;

    private readonly WindowSystem _windowSystem;
    private readonly IOverlayService _overlayService;
    private readonly ILiveObjectService _liveObjectService;
    private readonly IModelBvhCacheService _modelBvhCacheService;
    private readonly IResourceRedirectionService _resourceRedirectionService;

    private List<ILiveObject> createdObjects = new List<ILiveObject>();

    private Object* _selectedObject = null;
    private BoundsMode _boundsMode = BoundsMode.Oriented;

#if ENABLE_RENDER_HOOK_TESTS
    //private Hook<FFXIVClientStructs.FFXIV.Client.Graphics.Render.TerrainRenderer.Delegates.QueueRenderJob> _terrainRendererQueueRenderJobHook;
    //private Hook<FFXIVClientStructs.FFXIV.Client.Graphics.Render.ModelRenderer.Delegates.QueueRenderJob> _modelRendererQueueRenderJobHook;
    private Hook<FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.ImmediateContext.Delegates.DoClearCommandViaDraw> _clearCommandDrawHook;
    private Hook<AtkServer.Delegates.ProcessUICommands> _atkServerProcessUICommandsHook;

    Vector4 _clearColorValue = Vector4.Zero;
    float _clearDepthValue = 0.0f;
    byte _clearStencilValue = 0;
    //int _clearType = 2;
    bool _clearColor = false;
    bool _clearDepth = false;
    bool _clearStencil = false;
#endif

    private bool _suppressInput = false;

    public DebugWindow(ILogger<DebugWindow> logger, IFramework framework, ICommandManager commandManager, IGameInteropProvider gameInteropProvider, IObjectTable objectTable, IGameGui gameGui, IClientState clientState, IPlayerState playerState, WindowSystem windowSystem, IOverlayService overlayService, ILiveObjectService liveObjectService, IModelBvhCacheService modelBvhCacheService, IResourceRedirectionService resourceRedirectionService)
        : base("Stagehand Debug", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        _logger = logger;
        _framework = framework;
        _commandManager = commandManager;
        _gameInteropProvider = gameInteropProvider;
        _objectTable = objectTable;
        _gameGui = gameGui;
        _clientState = clientState;
        _playerState = playerState;

        _windowSystem = windowSystem;
        _overlayService = overlayService;
        _liveObjectService = liveObjectService;
        _modelBvhCacheService = modelBvhCacheService;
        _resourceRedirectionService = resourceRedirectionService;

#if ENABLE_RENDER_HOOK_TESTS

        //_terrainRendererQueueRenderJobHook = gameInteropProvider.HookFromAddress<FFXIVClientStructs.FFXIV.Client.Graphics.Render.TerrainRenderer.Delegates.QueueRenderJob>(FFXIVClientStructs.FFXIV.Client.Graphics.Render.TerrainRenderer.MemberFunctionPointers.QueueRenderJob_Sig, TerrainRendererQueueRenderJob);
        //_terrainRendererQueueRenderJobHook.Enable();

        //_modelRendererQueueRenderJobHook = gameInteropProvider.HookFromAddress<FFXIVClientStructs.FFXIV.Client.Graphics.Render.ModelRenderer.Delegates.QueueRenderJob>(FFXIVClientStructs.FFXIV.Client.Graphics.Render.ModelRenderer.MemberFunctionPointers.QueueRenderJob_Sig, ModelRendererQueueRenderJob);
        //_modelRendererQueueRenderJobHook.Enable();

        _clearCommandDrawHook = gameInteropProvider.HookFromAddress<ImmediateContext.Delegates.DoClearCommandViaDraw>(ImmediateContext.Addresses.DoClearCommandViaDraw.Value, ClearCommandDraw_Detour);
        _clearCommandDrawHook.Enable();

        _atkServerProcessUICommandsHook = gameInteropProvider.HookFromAddress<AtkServer.Delegates.ProcessUICommands>(AtkServer.Addresses.ProcessUICommandsAlt.Value, AtkServerProcessUICommands_Detour);
        _atkServerProcessUICommandsHook.Enable();
#endif

        //ImDrawDataBuilder builder = new ImDrawDataBuilder();
        //builder.Clear

        // Set ImGui context

        //using (var drawData = BufferBackedImDrawData.Create())
        //{
        //    //drawData.ListPtr;
        //}

        // Clear ImGui context
    }

#if ENABLE_RENDER_HOOK_TESTS

    private static int frameCount = 0;
    //private long TerrainRendererQueueRenderJob(FFXIVClientStructs.FFXIV.Client.Graphics.Render.TerrainRenderer* thisPtr)
    //{
    //    var result = _terrainRendererQueueRenderJobHook.Original.Invoke(thisPtr);

    //    //var tlsIndex = 0;
    //    //var threadLocals = *(GetTEB()->ThreadLocalStoragePointer + tlsIndex);
    //    //Context* context = *(Context**)(threadLocals + 0x238);
    //    //var clearCommand = (RenderCommandClearDepth*)context->AllocateCommand((ulong)Marshal.SizeOf<RenderCommandClearDepth>());

    //    //AddClearCommand(context);

    //    return result;
    //}

    //private long ModelRendererQueueRenderJob(FFXIVClientStructs.FFXIV.Client.Graphics.Render.ModelRenderer* thisPtr)
    //{

    //    //var tlsIndex = 0;
    //    //var threadLocals = (ThreadLocals*)*(GetTEB()->ThreadLocalStoragePointer + tlsIndex);
    //    ////Context* context = *(Context**)(threadLocals + 0x238);
    //    //var context = threadLocals->GraphicsKernelContext;
    //    //var clearCommand = (RenderCommandClearDepth*)context->AllocateCommand(4, (ulong)Marshal.SizeOf<RenderCommandClearDepth>());

    //    //AddClearCommand(context);

    //    var result = _modelRendererQueueRenderJobHook.Original.Invoke(thisPtr);

    //    return result;
    //}

    Context* GetContext()
    {
        return ThreadLocals.ThreadLocalInstance()->GraphicsKernelContext;
    }

    private void AtkServerProcessUICommands_Detour(AtkServer* atkServer, bool unk)
    {
        var context = GetContext();

        //var backbuffer = Device.Instance()->SwapChain->BackBuffer;
        var backbuffer = RenderTargetManager.Instance()->SwapChainBackBuffer;
        context->SetRenderTargets(1, &backbuffer, null, 0, 0, 0, 0);
        AddClearCommand(context);

        _atkServerProcessUICommandsHook?.Original.Invoke(atkServer, unk);
    }

    private void ClearCommandDraw_Detour(ImmediateContext* context, RenderCommandClear* clearCommand)
    {
        if (clearCommand->ClearFlags == ClearFlags.None)
        {
            var customRenderCommand = (CustomRenderCommand*)clearCommand;
            if (customRenderCommand->Guard1 == CustomRenderCommand.GuardValue && customRenderCommand->Guard2 == CustomRenderCommand.GuardValue && customRenderCommand->Callback.IsAllocated)
            {
                try
                {
                    customRenderCommand->Callback.Target.Invoke(context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception in custom render command!");
                }
                finally
                {
                    customRenderCommand->Callback.Dispose();
                }
            }
        }
        _clearCommandDrawHook?.OriginalDisposeSafe?.Invoke(context, clearCommand);
    }

    private void AddClearCommand(Context* context)
    {
        var clearCommand = (RenderCommandClear*)context->AllocateSpecificCommand(RenderCommandType.Clear, (ulong)Marshal.SizeOf<RenderCommandClear>());

        //clearCommand->SwitchType = 4;
        clearCommand->ClearFlags = (_clearColor ? ClearFlags.Color : ClearFlags.None) | (_clearDepth ? ClearFlags.Depth : ClearFlags.None) | (_clearStencil ? ClearFlags.Stencil : ClearFlags.None);
        clearCommand->ColorB = _clearColorValue.X;
        clearCommand->ColorG = _clearColorValue.Y;
        clearCommand->ColorR = _clearColorValue.Z;
        clearCommand->ColorA = _clearColorValue.W;
        clearCommand->ClearDepth = _clearDepthValue;
        clearCommand->ClearStencil = _clearStencilValue; // 0xFF00;
        clearCommand->ClearRectanglePtr = (FFXIVClientStructs.FFXIV.Common.Math.IntRectangle*)0;

        context->PushBackCommand(clearCommand);
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x40)]
    private struct CustomRenderCommand
    {
        public const ulong GuardValue = 0xDEADBEEFDEADBEEFul;

        [FieldOffset(0x00)] public RenderCommandClear BaseCommand;
        [FieldOffset(0x08)] public GCHandle<Action<Pointer<ImmediateContext>>> Callback;
        // Stomp over the rest of the clear color and the depth and stencil values with 16 sentinel bytes just to be absolutely sure this is a custom render command
        [FieldOffset(0x10)] public ulong Guard1;
        [FieldOffset(0x18)] public ulong Guard2;
    }

    private void AddCustomCommand(Context* context, Action<Pointer<ImmediateContext>> callback)
    {
        var customCommand = (CustomRenderCommand*)context->AllocateSpecificCommand(RenderCommandType.Clear, (ulong)Marshal.SizeOf<CustomRenderCommand>());
        customCommand->BaseCommand.ClearFlags = ClearFlags.None;
        customCommand->Callback = new(callback);
        customCommand->Guard1 = CustomRenderCommand.GuardValue;
        customCommand->Guard2 = CustomRenderCommand.GuardValue;
        customCommand->BaseCommand.ClearRectanglePtr = null;

        context->PushBackCommand(customCommand);
    }

    private void AddTestDrawingCommand(Context* context)
    {
        //_logger.LogWarning("Queueing test drawing command!");

        AddCustomCommand(context, context =>
        {
            //Device.Instance()->D3D11Forwarder
            var immediateContext = (ID3D11DeviceContext*)context.Value->D3D11DeviceContext_2;
            using (var stateBackup = new D3D11DeviceContextStateBackup((D3D_FEATURE_LEVEL)Device.Instance()->D3DFeatureLevel, immediateContext))
            {
                immediateContext->ClearState();
                // TODO: Draw in the scene!
                // g_Client::Graphics::Scene::CameraManager_PtrInstance->Cameras[g_Client::Graphics::Scene::CameraManager_PtrInstance->CameraIndex];
            }
            //_logger.LogWarning("In test drawing command!");
        });
    }
#endif

    public void Dispose()
    {
#if ENABLE_RENDER_HOOK_TESTS
        _atkServerProcessUICommandsHook?.Dispose();
        _clearCommandDrawHook?.Dispose();
        //_modelRendererQueueRenderJobHook?.Dispose();
        //_terrainRendererQueueRenderJobHook?.Dispose();
#endif

        foreach (var item in createdObjects)
        {
            item.Dispose();
        }
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _selectedObject = null;
    }

    BoundsMode[] allBoundsModes = Enum.GetValues<BoundsMode>();
    bool _scrollSelectionInfoFrame = false;
    public override void Draw()
    {
#if ENABLE_RENDER_HOOK_TESTS
        ImGui.ColorEdit4("Clear Color Value", ref _clearColorValue);
        ImGui.SliderFloat("Clear Depth Value", ref _clearDepthValue, 0.0f, 1.0f);
        ImGui.SliderByte("Clear Stencil Value", ref _clearStencilValue, 0, 255);
        ImGui.Checkbox("Clear Color", ref _clearColor);
        ImGui.Checkbox("Clear Depth", ref _clearDepth);
        ImGui.Checkbox("Clear Stencil", ref _clearStencil);
#endif

        //ImGui.Checkbox("SuppressInput", ref _suppressInput);

        if (StageLocation.TryGetLocation(_clientState, _playerState, out var location))
        {
            ImGui.LabelText("Location", $"World: {location.WorldId}, Territory: {location.TerritoryId}, Ward: {location.WardId}, Division: {location.DivisionId}, House: {location.HouseId}, Room: {location.RoomId}");
        }
        else
        {
            ImGui.LabelText("Location", "(none)");
        }

        int boundsModeIndex = allBoundsModes.IndexOf(_boundsMode);
        if (ImGui.Combo("Bounds Mode", ref boundsModeIndex, allBoundsModes, boundsMode => boundsMode.ToString()))
        {
            _boundsMode = allBoundsModes[boundsModeIndex];
        }

        if (ImGui.Button(_overlayService.IsPicking ? "Stop Picking" : "Start Picking"))
        {
            _overlayService.IsPicking = !_overlayService.IsPicking;
        }

        using (var table = ImRaii.Table("frame", 2))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("outliner");
                ImGui.TableSetupColumn("properties");

                ImGui.TableNextColumn();

                bool foundSelectedObject = false;

                using (var treeList = ImRaii.ListBox("###list", ImGui.GetContentRegionAvail()))
                {
                    if (treeList.Success)
                    {
                        var worldObject = World.Instance();

                        if (worldObject != null)
                        {
                            DrawObjectTree((Object*)worldObject, ref foundSelectedObject);
                        }
                        else
                        {
                            ImGui.TextDisabled("World.Instance() returned null.");
                        }

                        //var envManager = EnvManager.Instance();
                        //if (envManager != null)
                        //{
                        //    if (envManager->EnvSpace != null)
                        //    {
                        //        DrawObjectTree((Object*)envManager->EnvSpace, ref foundSelectedObject);
                        //    }

                        //    foreach (var thing in envManager->EnvScene->EnvSpaces)
                        //    {
                        //        DrawObjectTree((Object*)&thing, ref foundSelectedObject);
                        //    }

                        //    for (int i = 0; i < envManager->EnvScene->LocationCount; i++)
                        //    {
                        //        DrawObjectTree((Object*)envManager->EnvScene->Locations[i], ref foundSelectedObject);
                        //    }
                        //}
                    }
                }

                ImGui.TableNextColumn();

                if (!foundSelectedObject)
                {
                    _selectedObject = null;
                }

                if (_selectedObject != null)
                {
                    Vector3 position = _selectedObject->Position;
                    if (ImGui.DragFloat3("Position", ref position, 0.05f))
                    {
                        _selectedObject->Position = position;
                        if (_selectedObject->GetObjectType() == ObjectType.VfxObject || _selectedObject->GetObjectType() == ObjectType.BgObject || _selectedObject->GetObjectType() == ObjectType.Light)
                        {
                            ((DrawObject*)_selectedObject)->NotifyTransformChanged();
                        }
                    }

                    Quaternion rotation = _selectedObject->Rotation;
                    var rotationQuaternion = rotation.AsVector4();
                    if (ImGui.DragFloat4("Rotation", ref rotationQuaternion))
                    {
                        rotation = rotationQuaternion.AsQuaternion();
                        _selectedObject->Rotation = Quaternion.Normalize(rotation);
                        if (_selectedObject->GetObjectType() == ObjectType.VfxObject || _selectedObject->GetObjectType() == ObjectType.BgObject || _selectedObject->GetObjectType() == ObjectType.Light)
                        {
                            ((DrawObject*)_selectedObject)->NotifyTransformChanged();
                        }
                    }

                    switch (_selectedObject->GetObjectType())
                    {
                        case ObjectType.Terrain:
                        {

                            break;
                        }
                        case ObjectType.VfxObject:
                        {
                            var vfx = (VfxObject*)_selectedObject;

                            ResourceHandle* vfxResourceHandle = null;
                            var vfxResource = (VfxResourceInstance__Internal*)vfx->VfxResourceInstance;
                            if (vfxResource != null)
                            {
                                var resourceUnk = vfxResource->VfxResourceUnk;
                                if (resourceUnk != null)
                                {
                                    vfxResourceHandle = (ResourceHandle*)resourceUnk->ApricotResourceHandle;
                                }
                            }

                            DrawResourceHandle("Vfx Path", vfxResourceHandle);

                            var transparency = vfx->GetTransparency();
                            if (ImGui.SliderFloat("Transparency", ref transparency, vMin: 0.0f, vMax: 1.0f))
                            {
                                vfx->SetTransparency(transparency);
                            }

                            Vector4 color = vfx->Color;
                            if (ImGui.ColorEdit4("Color", ref color))
                            {
                                vfx->Color = color;
                            }

                            break;
                        }
                        case ObjectType.CharacterBase:
                        {
                            var character = (CharacterBase*)_selectedObject;

                            var transparency = character->GetTransparency();
                            if (ImGui.SliderFloat("Transparency", ref transparency, vMin: 0.0f, vMax: 1.0f))
                            {
                                character->SetTransparency(transparency);
                            }

                            switch (character->GetModelType())
                            {
                                case CharacterBase.ModelType.Human:

                                    break;
                                case CharacterBase.ModelType.DemiHuman:

                                    break;
                                case CharacterBase.ModelType.Monster:

                                    break;
                                case CharacterBase.ModelType.Weapon:
                                    var weapon = (Weapon*)character;
                                    bool weaponChanged = false;
                                    weaponChanged |= ImGui.DragUShort("Model Set ID", ref weapon->ModelSetId, vSpeed: 0.1f);
                                    weaponChanged |= ImGui.DragUShort("Secondary ID", ref weapon->SecondaryId, vSpeed: 0.1f);
                                    weaponChanged |= ImGui.DragUShort("Variant", ref weapon->Variant, vSpeed: 0.1f);
                                    weaponChanged |= ImGui.DragByte("Stain 0", ref weapon->Stain0, vSpeed: 0.1f);
                                    weaponChanged |= ImGui.DragByte("Stain 1", ref weapon->Stain1, vSpeed: 0.1f);
                                    ImGui.LabelText("Material ID", weapon->MaterialId.ToString());
                                    ImGui.LabelText("VFX ID", weapon->VfxId.ToString());

                                    if (weapon->Decal != null)
                                    {
                                        ImGui.LabelText("Decal", weapon->Decal->FileName.ToString());
                                    }
                                    else
                                    {
                                        ImGui.LabelText("Decal", "(null)");
                                    }

                                    if (weapon->ChangedData != null)
                                    {
                                        ImGui.Text("Changed Data:");
                                        ImGui.LabelText("Secondary ID", weapon->ChangedData->SecondaryId.ToString());
                                        ImGui.LabelText("Variant", weapon->ChangedData->Variant.ToString());
                                        ImGui.LabelText("Stain 0", weapon->ChangedData->Stain0.ToString());
                                        ImGui.LabelText("Stain 1", weapon->ChangedData->Stain1.ToString());
                                    }
                                    else
                                    {
                                        ImGui.LabelText("Changed Data", "(null)");
                                    }

                                    if (weaponChanged)
                                    {
                                        weapon->CleanupRender();
                                        WeaponCreateInfo newModel = new WeaponCreateInfo()
                                        {
                                            WeaponModelId =
                                            {
                                                Id = weapon->ModelSetId,
                                                Type = weapon->SecondaryId,
                                                Variant = weapon->Variant,
                                                Stain0 = weapon->Stain0,
                                                Stain1 = weapon->Stain1,
                                            },
                                            AnimationVariant = weapon->AnimationVariant,
                                        };
                                        weapon->Initialize(&newModel);

                                        World.Instance()->AddChild((Object*)weapon);
                                        weapon->OnAddedToWorld();

                                        //weapon->UpdateRender();
                                    }
                                    break;
                            }

                            break;
                        }
                        case ObjectType.Light:
                        {
                            var light = (Light*)_selectedObject;

                            var transparency = light->GetTransparency();
                            if (ImGui.SliderFloat("Transparency", ref transparency, vMin: 0.0f, vMax: 1.0f))
                            {
                                light->SetTransparency(transparency);
                            }

                            if (light->ProjectedCubemapTexture != null)
                            {
                                ImGui.TextDisabled(light->ProjectedCubemapTexture->FileName.ToString());
                            }
                            else
                            {
                                ImGui.TextDisabled("null projected texture handle");
                            }
                            break;
                        }
                        case ObjectType.BgObject:
                            {
                                var bgObject = (BgObject*)_selectedObject;

                                DrawResourceHandle("Model Path", (ResourceHandle*)bgObject->ModelResourceHandle);

                                var transparency = bgObject->GetTransparency();
                                if (ImGui.SliderFloat("Transparency", ref transparency, vMin: 0.0f, vMax: 1.0f))
                                {
                                    bgObject->SetTransparency(transparency);
                                }

                                if (bgObject->StainBuffer != null)
                                {
                                    Vector4 existingColor = bgObject->StainBuffer->LinearFloatColor;
                                    if (ImGui.ColorEdit4("Dye", ref existingColor))
                                    {
                                        var srgbColor = new Vector4(MathF.Sqrt(existingColor.X), MathF.Sqrt(existingColor.Y), MathF.Sqrt(existingColor.Z), existingColor.Z) * byte.MaxValue;
                                        var byteColor = new ByteColor() { R = (byte)srgbColor.X, G = (byte)srgbColor.Y, B = (byte)srgbColor.Z, A = (byte)srgbColor.W };

                                        bgObject->TrySetStainColor(byteColor);
                                    }
                                }

                                if (bgObject->ModelResourceHandle != null)
                                {
                                    ImGui.TextDisabled($"Model res load state: {bgObject->ModelResourceHandle->LoadState}");
                                }
                                else
                                {
                                    ImGui.TextDisabled("(null model res hanlde)");
                                }

                                break;
                            }
                        //case ObjectType.EnvLocation:
                        //    {
                        //        var envLocation = (EnvLocation*)_selectedObject;

                        //        DrawResourceHandle("Ambient Set Resource Handle", envLocation->AmbientSetResource);
                        //        DrawResourceHandle("Environment Cubemap Texture Resource Handle", (ResourceHandle*)envLocation->EnvironmentCubemapTextureResource);

                        //        break;
                        //    }
                        //case ObjectType.EnvSpace:
                        //    {
                        //        var envSpace = (EnvSpace*)_selectedObject;

                        //        DrawResourceHandle("Env Set Resource Handle", envSpace->EnvSetResourceHandle);
                        //        DrawResourceHandle("Sound Set Resource Handle", envSpace->SoundSetResourceHandle);

                        //        break;
                        //    }
                    }
                }
                else
                {
                    ImGui.TextDisabled("(no selection)");
                }

                ImGuiHelpers.ScaledDummy(5.0f);
                if (ImGui.Button("Add Point Light"))
                {
                    var newLight = _liveObjectService.CreateLight(FFXIVClientStructs.FFXIV.Client.Graphics.Render.LightShape.PointLight, modpack: null) as LiveLight;
                    if (newLight != null)
                    {
                        createdObjects.Add(newLight);
                        newLight.Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY * 2.0f;
                        newLight.Range = 100.0f;
                        newLight.Intensity = 10.0f;
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Add VFX"))
                {
                    var newVfx = _liveObjectService.CreateVfx("bg/ffxiv/fst_f1/common/vfx/eff/b0941trp1a_o.avfx", _objectTable.LocalPlayer?.Position ?? Vector3.Zero, Quaternion.Identity, Vector3.One, Vector4.One, modpack: null);
                    if (newVfx != null)
                    {
                        createdObjects.Add(newVfx);
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Add BgObject"))
                {
                    var newObj = _liveObjectService.CreateBgObject("bgcommon/hou/indoor/general/0401/bgparts/fun_b0_m0401.mdl", _objectTable.LocalPlayer?.Position ?? Vector3.Zero, Quaternion.Identity, Vector3.One, modpack: null);
                    if (newObj != null)
                    {
                        createdObjects.Add(newObj);
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Add Weapon"))
                {
                    var newWeapon = _liveObjectService.CreateWeapon(1801, 8, 1, 0, 0, (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY, Quaternion.Identity, Vector3.One, modpack: null); // Storm Officer's Knives
                    if (newWeapon != null)
                    {
                        createdObjects.Add(newWeapon);
                    }
                }

                if (ImGui.Button($"Dispose All {createdObjects.Count}###dispose"))
                {
                    foreach (var obj in createdObjects)
                    {
                        obj.Dispose();
                    }

                    createdObjects.Clear();
                }
            }
        }
    }

    private void DrawResourceHandle(string label, ResourceHandle* resourceHandle)
    {
        if (resourceHandle == null)
        {
            ImGui.LabelText(label, "(null)");
        }
        else
        {
            string path = "(overflow reading string)";
            //try
            //{
                path = resourceHandle->FileName.ToString();
            //}
            //catch (OverflowException)
            //{ }
            ImGui.LabelText(label, path);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ImGui.SetClipboardText(path);
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(path);
                    ImGui.Separator();
                    ImGui.TextDisabled("Click to copy");
                }
            }
            using (ImRaii.PushIndent())
            {
                ImGui.TextDisabled($"Load state: {resourceHandle->LoadState}");
            }
        }
    }

    private void DrawObjectTree(Object* obj, ref bool foundSelectedObject)
    {
        bool isLeaf = obj->ChildObject == null;

        string type = obj->GetObjectType().ToString();
        string summary = "";

        if (obj == _selectedObject)
        {
            foundSelectedObject = true;
        }

        if (obj->GetObjectType() == ObjectType.VfxObject)
        {
            var vfxObject = (VfxObject*)obj;
            var vfxResource = (LiveVfxObject.VfxResourceInstance__Internal*)vfxObject->VfxResourceInstance;
            if (vfxResource != null)
            {
                var resourceUnk = vfxResource->VfxResourceUnk;
                if (resourceUnk != null)
                {
                    var apricotResourceHandle = resourceUnk->ApricotResourceHandle;
                    if (apricotResourceHandle != null)
                    {
                        summary = apricotResourceHandle->FileName.ToString();
                    }
                    else
                    {
                        summary = "(null ApricotResourceHandle)";
                    }
                }
                else
                {
                    summary = "(null VfxResourceUnk)";
                }
            }
            else
            {
                summary = "(null VfxResourceInstance)";
            }
        }
        else if (obj->GetObjectType() == ObjectType.BgObject)
        {
            var bgObject = (BgObject*)obj;
            var modelResourceHandle = bgObject->ModelResourceHandle;
            if (modelResourceHandle != null)
            {
                summary = modelResourceHandle->FileName.ToString();
            }
            else
            {
                summary = "(null ModelResourceHandle)";
            }
        }
        else if (obj->GetObjectType() == ObjectType.CharacterBase)
        {
            var character = (CharacterBase*)obj;
            type = character->GetModelType().ToString();
        }

        using (var treeNode = ImRaii.TreeNode($"{type} @ {(nint)obj:X8} {summary}", ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.OpenOnDoubleClick | (isLeaf ? ImGuiTreeNodeFlags.Leaf : ImGuiTreeNodeFlags.None) | (obj == _selectedObject ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None)))
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                _selectedObject = obj;
                foundSelectedObject = true;
            }
            if (treeNode.Success)
            {
                foreach (var child in obj->ChildObjects)
                {
                    DrawObjectTree(child, ref foundSelectedObject);
                }

                if (obj->GetObjectType() == ObjectType.EnvSpace)
                {
                    var space = (EnvSpace*)obj;
                    if (space->EnvLocation != null)
                    {
                        DrawObjectTree((Object*)space->EnvLocation, ref foundSelectedObject);
                    }
                }
            }
        }

        if (_selectedObject == obj && _scrollSelectionInfoFrame)
        {
            ImGui.SetScrollHereY();
            _scrollSelectionInfoFrame = false;
        }
    }

    private void DrawObjectOverlays(Object* obj, IOverlayDrawContext overlay, Ray mouseRay, ref float nearestDistanceSq, ref Object* nearestObject)
    {
        // Draw this object

        var type = obj->GetObjectType();
        var xDir = Vector3.Transform(Vector3.UnitX, obj->Rotation);
        var yDir = Vector3.Transform(Vector3.UnitY, obj->Rotation);
        var zDir = Vector3.Transform(Vector3.UnitZ, obj->Rotation);

        if (type == ObjectType.BgObject || type == ObjectType.Light || type == ObjectType.CharacterBase || type == ObjectType.VfxObject || type == ObjectType.Decal || type == ObjectType.EnvSpace || type == ObjectType.EnvLocation)
        {
            var drawObj = (DrawObject*)obj;

            // Check for mouse hit
            bool mouseHit = false;
            bool preciseHit = false;

            if (type == ObjectType.BgObject)
            {
                var bgObject = (BgObject*)drawObj;
                // NOTE: Accessing the bounds of a BgObject that has not yet loaded causes an access violation. Seems like strange design but whatever.
                if (bgObject->ModelResourceHandle->LoadState >= 7 && !bgObject->ModelResourceHandle->FileName.ToString().Contains("lightshaft", StringComparison.Ordinal))
                {
                    FFXIVClientStructs.FFXIV.Common.Math.SphereBounds outSphereBounds;
                    mouseHit = drawObj->ComputeSphereBounds(&outSphereBounds)->IntersectsRay(mouseRay, out var hitPoint);
                    
                    if (mouseHit /*&& (hitPoint - mouseRay.Origin).SqrMagnitude < nearestDistanceSq*/)
                    {
                        Matrix4x4 translationMatrix = Matrix4x4.CreateTranslation(obj->Position);
                        Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(obj->Rotation);
                        Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(obj->Scale);

                        Matrix4x4 matrix = scaleMatrix * rotationMatrix * translationMatrix;
                        if (Matrix4x4.Invert(matrix, out var inverseMatrix))
                        {
                            var localSpaceStart = Vector3.Transform(mouseRay.Origin, inverseMatrix);
                            var localSpaceDirection = Vector3.TransformNormal(mouseRay.Direction, inverseMatrix);

                            // Reverse resolve the modpack and original input resource name from the final BgObject resource handle path
                            ILiveModpack? modpack = null;
                            string modelPath = bgObject->ModelResourceHandle->FileName.ToString();
                            if (_resourceRedirectionService.TryParseModpackPath(modelPath, out modpack, out var gamePath))
                            {
                                if (modpack.ReverseResolveResourcePath(gamePath, out var originalModelPath))
                                {
                                    modelPath = originalModelPath;
                                }
                            }

                            if (_modelBvhCacheService.TryIntersectModel(modelPath, modpack, localSpaceStart, localSpaceDirection, out var intersectionPoint, out var intersectionNormal))
                            {
                                preciseHit = true;
                                var worldSpaceIntersection = Vector3.Transform(intersectionPoint, matrix);
                                var worldSpaceNormal = Vector3.TransformNormal(intersectionNormal, matrix);

                                var distanceSquared = (worldSpaceIntersection - (Vector3)mouseRay.Origin).LengthSquared();
                                if (distanceSquared < nearestDistanceSq)
                                {
                                    nearestDistanceSq = distanceSquared;
                                    nearestObject = obj;
                                }
                            }
                        }
                    }
                }
            }
            


            if (obj == _selectedObject || (_overlayService.IsPicking && mouseHit))
            {
                overlay.DrawLine(obj->Position, (Vector3)obj->Position + xDir, 1.0f, new Vector4(1.0f, 0.2f, 0.2f, 1.0f));
                overlay.DrawLine(obj->Position, (Vector3)obj->Position + yDir, 1.0f, new Vector4(0.2f, 1.0f, 0.2f, 1.0f));
                overlay.DrawLine(obj->Position, (Vector3)obj->Position + zDir, 1.0f, new Vector4(0.2f, 0.2f, 1.0f, 1.0f));

                Vector4 boundsColor = obj == _selectedObject ? Vector4.One : (preciseHit ? new Vector4(0.3f, 1.0f, 0.5f, 1.0f) : (mouseHit ? new Vector4(0.5f, 0.5f, 0.1f, 1.0f) : Vector4.One));

                switch (_boundsMode)
                {
                    case BoundsMode.Sphere:
                        FFXIVClientStructs.FFXIV.Common.Math.SphereBounds sphereBounds = new();
                        drawObj->ComputeSphereBounds(&sphereBounds);

                        overlay.DrawCircle(sphereBounds.CenterPoint, xDir, yDir, sphereBounds.Radius, 1.0f, new Vector4(0.2f, 0.2f, 1.0f, 1.0f));
                        overlay.DrawCircle(sphereBounds.CenterPoint, xDir, zDir, sphereBounds.Radius, 1.0f, new Vector4(0.2f, 1.0f, 0.2f, 1.0f));
                        overlay.DrawCircle(sphereBounds.CenterPoint, zDir, yDir, sphereBounds.Radius, 1.0f, new Vector4(1.0f, 0.2f, 0.2f, 1.0f));

                        break;
                    case BoundsMode.AxisAligned:
                        FFXIVClientStructs.FFXIV.Common.Math.AxisAlignedBounds alignedBounds = new();
                        drawObj->ComputeAxisAlignedBounds(&alignedBounds);

                        overlay.DrawBox(Matrix4x4.CreateTranslation(alignedBounds.Center), alignedBounds.HalfExtents, 1.0f, boundsColor);

                        break;
                    case BoundsMode.Oriented:
                        FFXIVClientStructs.FFXIV.Common.Math.OrientedBounds orientedBounds = new();
                        drawObj->ComputeOrientedBounds(&orientedBounds);

                        overlay.DrawBox(orientedBounds.Transform, orientedBounds.HalfExtents, 1.0f, boundsColor);

                        break;
                }

                var playerPos = _objectTable.LocalPlayer?.Position ?? Vector3.Zero;
                var hitTestRay = new Ray(playerPos, Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY, _objectTable.LocalPlayer?.Rotation ?? 0.0f)));
                FFXIVClientStructs.FFXIV.Common.Math.Vector3 outVector = new();
                bool hit = drawObj->HitTestBounds(&hitTestRay, &outVector);
                hit = drawObj->HitTestBoundsNoOutput(&hitTestRay);
                overlay.DrawLine(hitTestRay.Origin, hitTestRay.Origin + hitTestRay.Direction, 2.0f, hit ? new Vector4(0.2f, 1.0f, 0.2f, 1.0f) : new Vector4(1.0f, 0.2f, 0.2f, 1.0f));
                if (hit)
                {
                    overlay.DrawCross(Matrix4x4.CreateTranslation(outVector), 1.0f, new Vector4(0.2f, 1.0f, 0.2f, 1.0f));
                }
            }
            else
            {
                overlay.DrawLine(obj->Position, (Vector3)obj->Position + xDir, 1.0f, new Vector4(0.8f, 0.8f, 0.8f, 0.2f));
                overlay.DrawLine(obj->Position, (Vector3)obj->Position + yDir, 1.0f, new Vector4(0.8f, 0.8f, 0.8f, 0.2f));
                overlay.DrawLine(obj->Position, (Vector3)obj->Position + zDir, 1.0f, new Vector4(0.8f, 0.8f, 0.8f, 0.2f));
            }
        }
        else
        {
            if (obj == _selectedObject)
            {
                overlay.DrawLine(obj->Position, (Vector3)obj->Position + xDir, 1.0f, new Vector4(0.8f, 0.8f, 0.8f, 0.2f));
                overlay.DrawLine(obj->Position, (Vector3)obj->Position + yDir, 1.0f, new Vector4(0.8f, 0.8f, 0.8f, 0.2f));
                overlay.DrawLine(obj->Position, (Vector3)obj->Position + zDir, 1.0f, new Vector4(0.8f, 0.8f, 0.8f, 0.2f));
            }
        }


        switch (type)
        {
            case ObjectType.BgObject:
                {

                    break;
                }
            case ObjectType.Light:
                {

                    break;
                }
            case ObjectType.CharacterBase:
                {

                    break;
                }
            case ObjectType.VfxObject:
                {

                    break;
                }
        }

        // Recurse
        foreach (var child in obj->ChildObjects)
        {
            DrawObjectOverlays(child, overlay, mouseRay, ref nearestDistanceSq, ref nearestObject);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _windowSystem.AddWindow(this);
        _commandManager.AddHandler(DebugWindowCommand, new Dalamud.Game.Command.CommandInfo(OnDebugWindowCommandInvoked)
        {
            HelpMessage = "Show the Stagehand debug window for inspecting Scene objects"
        });

        _overlayService.DrawOverlays += OnDrawOverlay;

        return Task.CompletedTask;
    }

    private void OnDrawOverlay(IOverlayDrawContext overlay)
    {
        if (!IsOpen)
        {
            return;
        }

        var worldObject = World.Instance();
        var cameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();

        if (worldObject != null && cameraManager != null)
        {
            var activeCamera = cameraManager->CurrentCamera;
            var mouseRay = activeCamera->ScreenPointToRay(ImGui.GetMousePos());
            float nearestDistanceSq = float.MaxValue;
            Object* nearestObject = null;
            DrawObjectOverlays((Object*)worldObject, overlay, mouseRay, ref nearestDistanceSq, ref nearestObject);

            //var envManager = EnvManager.Instance();
            //if (envManager != null)
            //{
            //    if (envManager->EnvSpace != null)
            //    {
            //        DrawObjectOverlays((Object*)envManager->EnvSpace, overlay, mouseRay, ref nearestDistanceSq, ref nearestObject);
            //    }

            //    foreach (var thing in envManager->EnvScene->EnvSpaces)
            //    {
            //        DrawObjectOverlays((Object*)&thing, overlay, mouseRay, ref nearestDistanceSq, ref nearestObject);
            //    }

            //    for (int i = 0; i < envManager->EnvScene->LocationCount; i++)
            //    {
            //        DrawObjectOverlays((Object*)envManager->EnvScene->Locations[i], overlay, mouseRay, ref nearestDistanceSq, ref nearestObject);
            //    }
            //}


            if (nearestObject != null && _overlayService.IsPicking)
            {
                ImGui.GetIO().WantCaptureMouse = true;
                
                if (ImGui.GetIO().MouseClicked[0])
                {
                    _selectedObject = nearestObject;
                    _overlayService.IsPicking = false;
                    _scrollSelectionInfoFrame = true;
                }
            }
        }
    }

    private void OnDebugWindowCommandInvoked(string command, string args)
    {
        Toggle();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _commandManager.RemoveHandler(DebugWindowCommand);
        _windowSystem.RemoveWindow(this);

        return Task.CompletedTask;
    }
}

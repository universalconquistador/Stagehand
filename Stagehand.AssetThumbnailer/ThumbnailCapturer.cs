using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Data.Files;
using Stagehand.Api;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Stagehand.AssetThumbnailer;

public class ThumbnailCapturer
{
    private readonly IStagehandApi _stagehandApi;
    private readonly IFramework _framework;
    private readonly IDataManager _dataManager;
    private readonly IGameInteropProvider _gameInteropProvider;

    private const string CaptureStagePath = @"F:\Soundstages\Samples\[UC] Stagehand Shapes - Thumbnailer.json";
    private const string BackgroundTempStageId = "capture";
    private const string AssetTempStageId = "asset";

    private static readonly Vector3 _cameraPosition = new(-1.75f, 102.4f, 0.9f); // new(-0.747f, 101.703f, 2.811f);
    private const float _cameraYaw = 13.741f;
    private const float _cameraPitch = -24.292f;
    private static readonly Vector3 _backgroundOrigin = new(0.0f, 100.0f, 0.0f);


    private IntPtr _camera = IntPtr.Zero;
    private IntPtr _mainCamera = IntPtr.Zero;

    // This needs its own class because we can't make ThumbnailCapturer unsafe because it has async stuff
    private unsafe class SignatureHolder
    {
        [Signature("33 D2 48 8D 05 ?? ?? ?? ?? 89 51 10")]
        public readonly delegate* unmanaged<Camera*, void> _cameraCtor = null!;
    }
    private readonly SignatureHolder _signatureHolder;
    private Hook<AtkServer.Delegates.ProcessUICommands> _atkServerProcessUICommandsHook = null!;
    private Hook<AtkServer.Delegates.ProcessUICommandsAlt> _atkServerProcessUICommandsAltHook = null!;

    public ThumbnailCapturer(IStagehandApi stagehandApi, IFramework framework, IDataManager dataManager, IGameInteropProvider gameInteropProvider)
    {
        _stagehandApi = stagehandApi;
        _framework = framework;
        _dataManager = dataManager;
        _gameInteropProvider = gameInteropProvider;

        _signatureHolder = new();
        gameInteropProvider.InitializeFromAttributes(_signatureHolder);
    }

    public async Task SetUpCaptureAsync()
    {
        var captureStageDefinition = await LoadCaptureStageDefinitionAsync();

        await _framework.Run(() =>
        {
            _stagehandApi.TryCreateOrUpdateTemporaryStage(captureStageDefinition.ToDefinitionString(), BackgroundTempStageId, "Stagehand Thumbnailer Capture Background Stage");
            _stagehandApi.TrySetTemporaryStageVisible(BackgroundTempStageId, visible: true);

            SetUpCamera();
            unsafe
            {
                //UIModule.Instance()->GetRaptureAtkModule()->IsUiVisible = false;

                _atkServerProcessUICommandsHook = _gameInteropProvider.HookFromAddress<AtkServer.Delegates.ProcessUICommands>(AtkServer.Addresses.ProcessUICommands.Value, AtkServerProcessUICommands_Detour);
                _atkServerProcessUICommandsHook.Enable();

                _atkServerProcessUICommandsAltHook = _gameInteropProvider.HookFromAddress<AtkServer.Delegates.ProcessUICommandsAlt>(AtkServer.Addresses.ProcessUICommandsAlt.Value, AtkServerProcessUICommandsAlt_Detour);
                _atkServerProcessUICommandsAltHook.Enable();
            }
        });

        await Task.Delay(TimeSpan.FromSeconds(1.0f));
    }

    private unsafe void SetUpCamera()
    {
        Debug.Assert(_framework.IsInFrameworkUpdateThread);
        Camera* camera = (Camera*)Marshal.AllocHGlobal(Marshal.SizeOf<Camera>());
        _camera = (IntPtr)camera;
        _signatureHolder._cameraCtor(camera);
        FFXIVClientStructs.FFXIV.Common.Math.Vector3 position = _cameraPosition;
        FFXIVClientStructs.FFXIV.Common.Math.Vector3 forward = _backgroundOrigin + new Vector3(0.0f, 0.3f, 0.0f); // Vector3.Normalize(_backgroundOrigin - _cameraPosition);
        FFXIVClientStructs.FFXIV.Common.Math.Vector3 up = Vector3.UnitY;
        camera->TryUpdateState(&position, &forward, &up, 0.1f, 100.0f, 60.0f * MathF.PI / 180.0f, 1.0f);

        var cameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();

        _mainCamera = (IntPtr)(Camera*)cameraManager->Cameras[1];
        cameraManager->Cameras[1] = camera;
        cameraManager->SetCamera(1);
    }

    private unsafe void AtkServerProcessUICommands_Detour(AtkServer* atkServer, bool unk)
    {
        //_atkServerProcessUICommandsHook?.Original.Invoke(atkServer, unk);
    }

    private unsafe void AtkServerProcessUICommandsAlt_Detour(AtkServer* atkServer, bool unk)
    {
        //_atkServerProcessUICommandsAltHook?.Original.Invoke(atkServer, unk);
    }

    public async Task CaptureAssetAsync(string gamePath, string outputDirectory, string outputBaseFilename)
    {
        // Set up the preview of the asset (overwrites any previous preview stage, will be cleaned up in TearDownCaptureAsync)
        var assetStage = new StageDefinition();
        var assetObject = CreateAssetPreviewObjectDefinition(gamePath);
        assetStage.Objects.Add("asset", assetObject);
        await _framework.Run(() =>
        {
            _stagehandApi.TryCreateOrUpdateTemporaryStage(assetStage.ToDefinitionString(), AssetTempStageId, "Stagehand Thumbnailer Capture Asset Stage");
            _stagehandApi.TrySetTemporaryStageVisible(AssetTempStageId, visible: true);
        });

        await _framework.RunOnTick(() =>
        {

        });

        await Task.Delay(TimeSpan.FromSeconds(0.5f));

        // TODO: Read back the texture!
    }

    public async Task TearDownCaptureAsync()
    {
        await _framework.Run(() =>
        {
            _atkServerProcessUICommandsAltHook?.Disable();
            _atkServerProcessUICommandsAltHook?.Dispose();

            _atkServerProcessUICommandsHook?.Disable();
            _atkServerProcessUICommandsHook?.Dispose();

            _stagehandApi.TryDestroyTemporaryStage(AssetTempStageId);
            _stagehandApi.TryDestroyTemporaryStage(BackgroundTempStageId);

            TearDownCamera();
            unsafe
            {
                //UIModule.Instance()->GetRaptureAtkModule()->IsUiVisible = true;
            }
        });
    }

    private unsafe void TearDownCamera()
    {
        Debug.Assert(_framework.IsInFrameworkUpdateThread);
        var cameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();
        cameraManager->Cameras[1] = (Camera*)_mainCamera;
        cameraManager->SetCamera(1);

        Camera* camera = (Camera*)_camera;
        camera->CleanupRender();
        camera->Dtor(0);

        Marshal.FreeHGlobal(_camera);
        _camera = IntPtr.Zero;
    }

    private Task<StageDefinition> LoadCaptureStageDefinitionAsync()
    {
        return Task.Run(() =>
        {
            using (var stream = new FileStream(CaptureStagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (StageDefinition.TryParseJSONStream(stream, out var definition))
                {
                    return definition;
                }
                else
                {
                    throw new Exception("Could not find or parse capture stage! (" + CaptureStagePath + ")");
                }
            }
        });
    }

    private ObjectDefinition CreateAssetPreviewObjectDefinition(string gamePath)
    {
        Vector3 boundsMin = Vector3.Zero;
        Vector3 boundsMax = Vector3.Zero;
        ObjectDefinition result;
        if (gamePath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            var mdlResource = _dataManager.GetFile<MdlFile>(gamePath);
            if (mdlResource != null)
            {
                boundsMin = new Vector3(mdlResource.BoundingBoxes.Min[0], mdlResource.BoundingBoxes.Min[1], mdlResource.BoundingBoxes.Min[2]);
                boundsMax = new Vector3(mdlResource.BoundingBoxes.Max[0], mdlResource.BoundingBoxes.Max[1], mdlResource.BoundingBoxes.Max[2]);
            }

            result = new BgObjectDefinition()
            {
                DisplayName = "Preview BgObject",
                ModelGamePath = gamePath,
                Position = _backgroundOrigin, // TODO: Compensate vertical position to sit on floor!
                Scale = new(1.0f), // TODO: Compensate scale!
            };
        }
        else if (gamePath.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase))
        {
            var avfxResource = _dataManager.GetFile(gamePath);
            if (avfxResource != null)
            {
                avfxResource.Reader.Position = 0;
                avfxResource.Reader.ReadInt32();
                var size = avfxResource.Reader.ReadInt32();

                bool clipBoxEnabled = false;
                float clipBoxX = 0.0f;
                float clipBoxY = 0.0f;
                float clipBoxZ = 0.0f;
                float clipBoxSizeX = 0.0f;
                float clipBoxSizeY = 0.0f;
                float clipBoxSizeZ = 0.0f;

                var start = avfxResource.Reader.Position;
                while ((avfxResource.Reader.Position - start) < size)
                {
                    var propId = avfxResource.Reader.ReadUInt32();
                    var propLength = avfxResource.Reader.ReadInt32();
                    var propStart = avfxResource.Reader.Position;

                    switch (propId)
                    {
                        case 0x6243756C: // 'bCul' big endian
                            clipBoxEnabled = avfxResource.Reader.ReadInt32() != 0;
                            break;
                        case 0x43425078: // 'CBPx' big endian
                            clipBoxX = avfxResource.Reader.ReadSingle();
                            break;
                        case 0x43425079:
                            clipBoxY = avfxResource.Reader.ReadSingle();
                            break;
                        case 0x4342507A:
                            clipBoxZ = avfxResource.Reader.ReadSingle();
                            break;
                        case 0x43425378:
                            clipBoxSizeX = avfxResource.Reader.ReadSingle();
                            break;
                        case 0x43425379:
                            clipBoxSizeY = avfxResource.Reader.ReadSingle();
                            break;
                        case 0x4342537A:
                            clipBoxSizeZ = avfxResource.Reader.ReadSingle();
                            break;
                    }

                    avfxResource.Reader.Position = propStart + propLength + CalculateAvfxPadding(propLength);
                }

                if (clipBoxEnabled)
                {
                    var boundsCenter = new Vector3(clipBoxX, clipBoxY, clipBoxZ);
                    var boundsSize = new Vector3(clipBoxSizeX, clipBoxSizeY, clipBoxSizeZ);
                    boundsMin = boundsCenter - boundsSize * 0.5f;
                    boundsMax = boundsCenter + boundsSize * 0.5f;
                }
            }

            result = new VfxObjectDefinition()
            {
                DisplayName = "Preview VFX Object",
                VfxGamePath = gamePath,
                Position = _backgroundOrigin, // TODO: Compensate vertical position to sit on floor!
                Scale = new(1.0f), // TODO: Compensate scale!
            };
        }
        else
        {
            throw new Exception("Unsupported asset type!");
        }

        if (boundsMin != boundsMax)
        {
            var boundsSize = boundsMax - boundsMin;
            var largestSize = boundsSize.X;
            if (boundsSize.Y > largestSize)
                largestSize = boundsSize.Y;
            if (boundsSize.Z > largestSize)
                largestSize = boundsSize.Z;

            result.Scale = new(1.0f / largestSize);
            result.Position += new Vector3(0.0f, -boundsMin.Y * result.Scale.Y + 0.01f, 0.0f);
        }

        return result;
    }

    // From VfxEditor
    private static int CalculateAvfxPadding(int size) => size % 4 == 0 ? 0 : 4 - size % 4;
}

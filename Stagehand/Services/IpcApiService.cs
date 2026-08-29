using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Stagehand.Api;
using Stagehand.Definitions;
using Stagehand.Editor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Services;

internal class IpcApiService : IHostedService, IStagehandApi
{
    private static readonly TimeSpan _localDefinitionDebounceTime = TimeSpan.FromSeconds(0.5f);

    private record IpcTemporaryStage(StageDefinition Definition, Vector3 Translation, Quaternion Rotation, float UniformScale, string DebugName, string PluginInternalName)
    {
        public Vector3 Translation { get; set; } = Translation;
        public Quaternion Rotation { get; set; } = Rotation;
        public float UniformScale { get; set; } = UniformScale;
    }

    private readonly ILogger _logger;
    private readonly IDalamudPluginInterface _dalamudPluginInterface;
    private readonly IFramework _framework;
    private readonly IGameStateService _gameStateService;
    private readonly ILiveStageService _liveStageService;
    private readonly IEditorService _editorService;
    private readonly LocalStageService _localStageService;
    private readonly ILocalDefinitionService _localDefinitionService;

    private IDisposable? _apiProvider;

    private readonly ConcurrentDictionary<string, IpcTemporaryStage> _temporaryStages = new();

    private LocalStageDefinition[] _localStageDefinitions = Array.Empty<LocalStageDefinition>();
    private int _updatingLocalDefinitions = 0;
    private CancellationTokenSource _shutdownTokenSource = new();

    public IpcApiService(ILogger<IpcApiService> logger, IDalamudPluginInterface dalamudPluginInterface, IFramework framework, IGameStateService gameStateService, ILiveStageService liveStageService, IEditorService editorService, LocalStageService localStageService, ILocalDefinitionService localDefinitionService)
    {
        _logger = logger;
        _dalamudPluginInterface = dalamudPluginInterface;
        _framework = framework;
        _gameStateService = gameStateService;
        _liveStageService = liveStageService;
        _editorService = editorService;
        _localStageService = localStageService;
        _localDefinitionService = localDefinitionService;

        _localStageService.VisibleStagesChanged += OnVisibleStagesChanged;
        _localDefinitionService.LocalDefinitionsChanged += OnLocalDefinitionsChanged;

        _gameStateService.LocationChanged += OnLocationChanged;

        _editorService.EditorSaved += OnEditorSaved;

        InvalidateLocalDefinitions();
    }

    private void OnEditorSaved(string definitionFilename)
    {
        LocalStageDefinitionEdited?.Invoke(definitionFilename);
    }

    private void OnLocationChanged(StageLocation location)
    {
        // Hide all the temporary stages
        foreach (var temporaryStageKey in _temporaryStages.Keys)
        {
            _liveStageService.TryDestroyLiveStage(temporaryStageKey);
        }

        LocationChanged?.Invoke(location);
    }

    private void OnLocalDefinitionsChanged(IReadOnlyList<string> removedDefinitions, IReadOnlyList<string> addedDefinitions, IReadOnlyList<string> modifiedDefinitions)
    {
        InvalidateLocalDefinitions();
    }

    private void OnVisibleStagesChanged()
    {
        InvalidateLocalDefinitions();
    }

    private void InvalidateLocalDefinitions()
    {
        bool alreadyUpdating = Interlocked.Exchange(ref _updatingLocalDefinitions, 1) == 1;

        if (!alreadyUpdating)
        {
            _framework.RunOnTick(() =>
            {
                if (!_shutdownTokenSource.IsCancellationRequested)
                {
                    var result = _localDefinitionService.LocalDefinitions.Select(pair => new LocalStageDefinition(pair.Key, pair.Value.Info.Name, pair.Value.Info.VersionString, _liveStageService.TryGetLiveStage(LiveStageHelpers.MakeLocalStageKey(pair.Key), out var liveStage), liveStage?.Translation ?? Vector3.Zero, liveStage?.Rotation ?? Quaternion.Identity, liveStage?.UniformScale ?? 1.0f));
                    _localStageDefinitions = result.ToArray();
                    LocalStageDefinitionsChanged?.Invoke();
                }

                Interlocked.Exchange(ref _updatingLocalDefinitions, 0);
            }, _localDefinitionDebounceTime, cancellationToken: _shutdownTokenSource.Token);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _apiProvider = StagehandApi.RegisterIpcProvider(this, _dalamudPluginInterface);

        _logger.LogDebug("Stagehand API {major}.{minor} available.", GetPluginApiRevision().Major, GetPluginApiRevision().Minor);

        return Task.CompletedTask;
    }

    #region IStagehandApi

    public event Action<StageLocation>? LocationChanged;

    public ApiRevision GetPluginApiRevision()
    {
        return StagehandApi.LibraryApiRevision;
    }

    public StageLocation GetLocation()
    {
        return _gameStateService.Location;
    }

    #endregion

    #region IStagehandApi.Temporary

    public bool TryCreateOrUpdateTemporaryStage(string definitionString, string stageId, string debugName)
    {
        return TryCreateOrUpdateTemporaryStageWithTransform(definitionString, stageId, translation: Vector3.Zero, rotation: Quaternion.Identity, uniformScale: 1.0f, debugName);
    }
    
    public bool TryCreateOrUpdateTemporaryStageWithTransform(string definitionString, string stageId, Vector3 translation, Quaternion rotation, float uniformScale, string debugName)
    {
        if (StageDefinition.TryParseDefinitionString(definitionString, out var definition))
        {
            var key = LiveStageHelpers.MakeTemporaryStageKey(stageId, "temp");
            string pluginInternalName = ""; // TODO: Get calling plugin name!
            _temporaryStages[key] = new(definition, translation, rotation, uniformScale, debugName, pluginInternalName);
            _liveStageService.CreateOrUpdateLiveStage(key, definition, translation, rotation, uniformScale);
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool TrySetTemporaryStageTransform(string stageId, Vector3 translation, Quaternion rotation, float uniformScale)
    {
        var key = LiveStageHelpers.MakeTemporaryStageKey(stageId, "temp");
        if (!_temporaryStages.TryGetValue(key, out var existingTemporaryStage) || !_liveStageService.TryGetLiveStage(key, out var liveStage))
        {
            return false;
        }
        existingTemporaryStage.Translation = translation;
        existingTemporaryStage.Rotation = rotation;
        existingTemporaryStage.UniformScale = uniformScale;
        liveStage.Update(existingTemporaryStage.Definition, translation, rotation, uniformScale);
        return true;
    }

    public bool TryDestroyTemporaryStage(string stageId)
    {
        var key = LiveStageHelpers.MakeTemporaryStageKey(stageId, "temp");
        var found = _temporaryStages.TryRemove(key, out _);
        if (found)
        {
            _liveStageService.TryDestroyLiveStage(key);
        }
        return found;
    }

    public bool TrySetTemporaryStageVisible(string stageId, bool visible)
    {
        var key = LiveStageHelpers.MakeTemporaryStageKey(stageId, "temp");
        if (visible)
        {
            if (_temporaryStages.TryGetValue(key, out var tempStage))
            {
                _liveStageService.CreateOrUpdateLiveStage(key, tempStage.Definition, tempStage.Translation, tempStage.Rotation, tempStage.UniformScale);
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return _liveStageService.TryDestroyLiveStage(key);
        }
    }

    #endregion

    #region IStagehandApi.Local

    public event Action? LocalStageDefinitionsChanged;

    public event Action<string>? LocalStageDefinitionEdited;

    public LocalStageDefinition[] GetLocalStageDefinitions()
    {
        return _localStageDefinitions;
    }

    #endregion

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdownTokenSource.Cancel();

        _editorService.EditorSaved -= OnEditorSaved;

        _localStageService.VisibleStagesChanged -= OnVisibleStagesChanged;

        _apiProvider?.Dispose();
        _apiProvider = null;

        _logger.LogDebug("Stagehand API shut down.");

        return Task.CompletedTask;
    }
}

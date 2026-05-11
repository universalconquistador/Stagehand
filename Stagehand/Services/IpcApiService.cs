using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Stagehand.Api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Services;

internal class IpcApiService : IHostedService, IStagehandApi
{
    private static readonly TimeSpan _localDefinitionDebounceTime = TimeSpan.FromSeconds(0.5f);

    private readonly ILogger _logger;
    private readonly IDalamudPluginInterface _dalamudPluginInterface;
    private readonly IFramework _framework;
    private readonly ILiveStageService _liveStageService;
    private readonly LocalStageService _localStageService;
    private readonly ILocalDefinitionService _localDefinitionService;

    private IDisposable? _apiProvider;

    private LocalStageDefinition[] _localStageDefinitions = Array.Empty<LocalStageDefinition>();
    private int _updatingLocalDefinitions = 0;
    private CancellationTokenSource _shutdownTokenSource = new();

    public IpcApiService(ILogger<IpcApiService> logger, IDalamudPluginInterface dalamudPluginInterface, IFramework framework, ILiveStageService liveStageService, LocalStageService localStageService, ILocalDefinitionService localDefinitionService)
    {
        _logger = logger;
        _dalamudPluginInterface = dalamudPluginInterface;
        _framework = framework;
        _liveStageService = liveStageService;
        _localStageService = localStageService;
        _localDefinitionService = localDefinitionService;

        _localStageService.VisibleStagesChanged += OnVisibleStagesChanged;
        _localDefinitionService.LocalDefinitionsChanged += OnLocalDefinitionsChanged;

        InvalidateLocalDefinitions();
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
                    var result = _localDefinitionService.LocalDefinitions.Select(pair => new LocalStageDefinition(pair.Key, pair.Value.Info.Name, pair.Value.Info.VersionString, _liveStageService.TryGetLiveStage(LiveStageHelpers.MakeLocalStageKey(pair.Key), out _)));
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

    public ApiRevision GetPluginApiRevision()
    {
        return StagehandApi.LibraryApiRevision;
    }

    #endregion

    #region IStagehandApi.Temporary

    public bool TryCreateOrUpdateTemporaryStage(string definitionString, string stageId, string debugName)
    {
        // TODO: Implement!
        throw new NotImplementedException();
    }

    public bool TryDestroyTemporaryStage(string stageId)
    {
        // TODO: Implement!
        throw new NotImplementedException();
    }

    public bool TrySetTemporaryStageVisible(string stageId, bool visible)
    {
        // TODO: Implement!
        throw new NotImplementedException();
    }

    #endregion

    #region IStagehandApi.Local

    public event Action? LocalStageDefinitionsChanged;

    public LocalStageDefinition[] GetLocalStageDefinitions()
    {
        return _localStageDefinitions;
    }

    #endregion

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdownTokenSource.Cancel();

        _localStageService.VisibleStagesChanged -= OnVisibleStagesChanged;

        _apiProvider?.Dispose();
        _apiProvider = null;

        _logger.LogDebug("Stagehand API shut down.");

        return Task.CompletedTask;
    }
}

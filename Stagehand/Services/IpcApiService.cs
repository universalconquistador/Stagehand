using Dalamud.Plugin;
using Microsoft.Extensions.Hosting;
using Stagehand.Api;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Services;

internal class IpcApiService : IHostedService, IStagehandApi
{
    private readonly ILogger _logger;
    private readonly IDalamudPluginInterface _dalamudPluginInterface;

    private IDisposable? _apiProvider;

    public IpcApiService(ILogger<IpcApiService> logger, IDalamudPluginInterface dalamudPluginInterface)
    {
        _logger = logger;
        _dalamudPluginInterface = dalamudPluginInterface;
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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _apiProvider?.Dispose();
        _apiProvider = null;

        _logger.LogDebug("Stagehand API shut down.");

        return Task.CompletedTask;
    }
}

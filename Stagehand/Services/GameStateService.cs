using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Stagehand.Api;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Services;

public interface IGameStateService
{
    /// <summary>
    /// The current location of the player.
    /// </summary>
    StageLocation Location { get; }

    /// <summary>
    /// Raised when the player changes location.
    /// </summary>
    event Action<StageLocation> LocationChanged;
}

internal class GameStateService : IGameStateService, IDisposable
{
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;

    private StageLocation _lastLocation;

    public StageLocation Location => _lastLocation;
    public event Action<StageLocation>? LocationChanged;

    public GameStateService(IFramework framework, IClientState clientState, IPlayerState playerState)
    {
        _framework = framework;
        _clientState = clientState;
        _playerState = playerState;

        StageLocation.TryGetLocation(_clientState, _playerState, out var location);

        _framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        StageLocation.TryGetLocation(_clientState, _playerState, out var location);

        if (location != _lastLocation)
        {
            _lastLocation = location;
            LocationChanged?.Invoke(location);
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
    }
}

using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stagehand.Definitions;
using Stagehand.Editor;
using Stagehand.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Services;

/// <summary>
/// Shows and hides live Stages for the local definitions according to automatic rules and manual commands.
/// </summary>
internal class LocalStageService : IHostedService
{
    private readonly ILogger _logger;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly ILocalDefinitionService _localDefinitionService;
    private readonly ILiveStageService _liveStageService;
    private readonly IEditorService _editorService;

    private readonly ConcurrentDictionary<string, bool> _manualVisibilitySettings = new();
    private Location _lastLocation;

    public LocalStageService(ILogger<LocalStageService> logger, IFramework framework, IClientState clientState, IPlayerState playerState, ILocalDefinitionService localDefinitionService, ILiveStageService liveStageService, IEditorService editorService, StagehandConfiguration configuration)
    {
        _logger = logger;
        _framework = framework;
        _clientState = clientState;
        _playerState = playerState;
        _localDefinitionService = localDefinitionService;
        _liveStageService = liveStageService;
        _editorService = editorService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _localDefinitionService.LocalDefinitionsChanged += OnLocalDefinitionsChanged;
        _localDefinitionService.AutomaticShowConditionsChanged += OnAutomaticShowConditionsChanged;

        _editorService.EditorOpened += OnEditorOpened;
        _editorService.EditorClosed += OnEditorClosed;

        _framework.Update += Update;

        return Task.CompletedTask;
    }

    private void OnEditorOpened(string definitionPath)
    {
        RefreshVisibility(definitionPath);
    }

    private void OnEditorClosed(string definitionPath)
    {
        RefreshVisibility(definitionPath);
    }

    private void Update(IFramework framework)
    {
        Location.TryGetLocation(_clientState, _playerState, out var location);

        if (location != _lastLocation)
        {
            _lastLocation = location;
            RefreshLocation();
        }
    }

    private void OnAutomaticShowConditionsChanged(string path)
    {
        RefreshVisibility(path);
    }

    public void SetManualVisibility(string path, bool value)
    {
        _manualVisibilitySettings[path] = value;
    }

    private void OnLocalDefinitionsChanged(IReadOnlyList<string> removedDefinitions, IReadOnlyList<string> addedDefinitions, IReadOnlyList<string> modifiedDefinitions)
    {
        _framework.RunOnFrameworkThread(() =>
        {
            if (!Location.TryGetLocation(_clientState, _playerState, out var location))
                return;

            foreach (var removed in removedDefinitions)
            {
                _liveStageService.TryDestroyLiveStage(LiveStageHelpers.MakeLocalStageKey(removed));
            }

            // Show new Stages that meet their show conditions
            foreach (var added in addedDefinitions)
            {
                if (_localDefinitionService.LocalDefinitions.TryGetValue(added, out var metadata)
                    && metadata.AutomaticShowConditions.Any(condition => condition.Evaluate(location)))
                {
                    try
                    {
                        using (FileStream stream = new FileStream(added, FileMode.Open, FileAccess.Read))
                        {
                            var definition = JsonSerializer.Deserialize<StageDefinition>(stream, StageDefinition.StandardSerializerOptions);
                            if (definition != null)
                            {
                                _liveStageService.CreateOrUpdateLiveStage(LiveStageHelpers.MakeLocalStageKey(added), definition);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception loading {path} to instantiate!", added);
                    }
                }
            }

            // Only update the modified Stages that are already visible
            foreach (var modified in modifiedDefinitions)
            {
                if (_localDefinitionService.LocalDefinitions.TryGetValue(modified, out var metadata)
                    && _liveStageService.TryGetLiveStage(LiveStageHelpers.MakeLocalStageKey(modified), out var liveStage))
                {
                    try
                    {
                        using (FileStream stream = new FileStream(modified, FileMode.Open, FileAccess.Read))
                        {
                            var definition = JsonSerializer.Deserialize<StageDefinition>(stream, StageDefinition.StandardSerializerOptions);
                            if (definition != null)
                            {
                                liveStage.Update(definition);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception loading {path} to update!", modified);
                    }
                }
            }
        });
    }

    private void RefreshVisibility(string path)
    {
        var liveKey = LiveStageHelpers.MakeLocalStageKey(path);
        bool currentlyVisible = _liveStageService.TryGetLiveStage(liveKey, out var liveStage);

        if (!Location.TryGetLocation(_clientState, _playerState, out var location))
            return;

        bool shouldBeVisible = path != _editorService.OpenEditorFilename
            && _manualVisibilitySettings.GetValueOrDefault(path, _localDefinitionService.LocalDefinitions.TryGetValue(path, out var metadata)
            && metadata.AutomaticShowConditions.Any(condition => condition.Evaluate(location)));

        if (currentlyVisible && !shouldBeVisible)
        {
            _framework.RunOnFrameworkThread(() => _liveStageService.TryDestroyLiveStage(liveKey));
        }
        else if (shouldBeVisible && !currentlyVisible)
        {
            _framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        var definition = JsonSerializer.Deserialize<StageDefinition>(stream, StageDefinition.StandardSerializerOptions);
                        if (definition != null)
                        {
                            _liveStageService.CreateOrUpdateLiveStage(liveKey, definition);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception loading {path} to instantiate!", path);
                }
            });
        }
    }

    private void RefreshLocation()
    {
        _logger.LogDebug("Location change! Destroying all Stages...");
        _liveStageService.DestroyAllLiveStages();

        _manualVisibilitySettings.Clear();

        // HACK: Very sensitive timed delay! Loading BgObjects while loading a zone causes their dye to be blank.
        // Consider instead waiting for the screen to start fading in via RaptureAtkUnitManager.IsUiFading or AgentInterface.GameEvent.LoadingEnded.
        _framework.RunOnTick(() =>
        {
            if (!Location.TryGetLocation(_clientState, _playerState, out var location))
                return;
            foreach (var localDefinition in _localDefinitionService.LocalDefinitions)
            {
                if (localDefinition.Value.AutomaticShowConditions.Any(condition =>
                    condition.Evaluate(location)))
                {
                    _logger.LogDebug("Trying to auto show {file}!", localDefinition.Key);
                    try
                    {
                        using (FileStream stream = new FileStream(localDefinition.Key, FileMode.Open, FileAccess.Read))
                        {
                            var definition = JsonSerializer.Deserialize<StageDefinition>(stream, StageDefinition.StandardSerializerOptions);
                            if (definition != null)
                            {
                                _liveStageService.CreateOrUpdateLiveStage(LiveStageHelpers.MakeLocalStageKey(localDefinition.Key), definition);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception loading {path} to instantiate!", localDefinition.Key);
                    }
                }
            }

        }, TimeSpan.FromSeconds(2.0f));

    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _framework.Update -= Update;
        _editorService.EditorClosed -= OnEditorClosed;
        _editorService.EditorOpened -= OnEditorOpened;
        _localDefinitionService.AutomaticShowConditionsChanged -= OnAutomaticShowConditionsChanged;
        _localDefinitionService.LocalDefinitionsChanged -= OnLocalDefinitionsChanged;

        return Task.CompletedTask;
    }
}

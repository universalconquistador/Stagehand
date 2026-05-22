using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stagehand.Api;
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
    private readonly IGameStateService _gameStateService;
    private readonly ILocalDefinitionService _localDefinitionService;
    private readonly ILiveStageService _liveStageService;
    private readonly IEditorService _editorService;

    private readonly ConcurrentDictionary<string, bool> _manualVisibilitySettings = new();
    private StageLocation _lastLocation;

    public event Action? VisibleStagesChanged;

    public LocalStageService(ILogger<LocalStageService> logger, IFramework framework, IGameStateService gameStateService, ILocalDefinitionService localDefinitionService, ILiveStageService liveStageService, IEditorService editorService, StagehandConfiguration configuration)
    {
        _logger = logger;
        _framework = framework;
        _gameStateService = gameStateService;
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

        _gameStateService.LocationChanged += OnLocationChanged;

        return Task.CompletedTask;
    }

    private void OnLocationChanged(StageLocation obj)
    {
        RefreshLocation();
    }

    private void OnEditorOpened(string definitionPath)
    {
        RefreshVisibility(definitionPath);
    }

    private void OnEditorClosed(string definitionPath)
    {
        RefreshVisibility(definitionPath);
    }

    private void OnAutomaticShowConditionsChanged(string path)
    {
        RefreshVisibility(path);
    }

    public void SetManualVisibility(string path, bool value)
    {
        _manualVisibilitySettings[path] = value;
        string liveKey = LiveStageHelpers.MakeLocalStageKey(path);

        if (value)
        {
            if (!_liveStageService.TryGetLiveStage(liveKey, out _))
            {
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        var definition = JsonSerializer.Deserialize<StageDefinition>(stream, StageDefinition.StandardSerializerOptions);
                        if (definition != null)
                        {
                            _liveStageService.CreateOrUpdateLiveStage(liveKey, definition);
                            VisibleStagesChanged?.Invoke();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception loading {path} to instantiate!", path);
                }
            }
        }
        else
        {
            if (_liveStageService.TryDestroyLiveStage(liveKey))
            {
                VisibleStagesChanged?.Invoke();
            }
        }
    }

    private void OnLocalDefinitionsChanged(IReadOnlyList<string> removedDefinitions, IReadOnlyList<string> addedDefinitions, IReadOnlyList<string> modifiedDefinitions)
    {
        _framework.RunOnFrameworkThread(() =>
        {
            bool visibleChanged = false;

            foreach (var removed in removedDefinitions)
            {
                visibleChanged |= _liveStageService.TryDestroyLiveStage(LiveStageHelpers.MakeLocalStageKey(removed));
            }

            // Show new Stages that meet their show conditions
            foreach (var added in addedDefinitions)
            {
                if (_localDefinitionService.LocalDefinitions.TryGetValue(added, out var metadata)
                    && metadata.AutomaticShowConditions.Any(condition => condition.Evaluate(_gameStateService.Location)))
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

                        visibleChanged = true;
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
                        visibleChanged = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception loading {path} to update!", modified);
                    }
                }
            }

            if (visibleChanged)
            {
                VisibleStagesChanged?.Invoke();
            }
        });
    }

    private void RefreshVisibility(string path)
    {
        var liveKey = LiveStageHelpers.MakeLocalStageKey(path);
        bool currentlyVisible = _liveStageService.TryGetLiveStage(liveKey, out var liveStage);

        bool shouldBeVisible = path != _editorService.OpenEditorFilename
            && _manualVisibilitySettings.GetValueOrDefault(path, _localDefinitionService.LocalDefinitions.TryGetValue(path, out var metadata)
            && metadata.AutomaticShowConditions.Any(condition => condition.Evaluate(_gameStateService.Location)));

        if (currentlyVisible && !shouldBeVisible)
        {
            _framework.RunOnFrameworkThread(() =>
            {
                _liveStageService.TryDestroyLiveStage(liveKey);
                VisibleStagesChanged?.Invoke();
            });
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
                            VisibleStagesChanged?.Invoke();
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

        foreach (var localDefinition in _localDefinitionService.LocalDefinitions)
        {
            if (localDefinition.Value.AutomaticShowConditions.Any(condition =>
                condition.Evaluate(_gameStateService.Location)))
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

        VisibleStagesChanged?.Invoke();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gameStateService.LocationChanged -= OnLocationChanged;
        _editorService.EditorClosed -= OnEditorClosed;
        _editorService.EditorOpened -= OnEditorOpened;
        _localDefinitionService.AutomaticShowConditionsChanged -= OnAutomaticShowConditionsChanged;
        _localDefinitionService.LocalDefinitionsChanged -= OnLocalDefinitionsChanged;

        return Task.CompletedTask;
    }
}

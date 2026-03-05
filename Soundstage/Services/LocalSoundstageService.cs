using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soundstage.Definitions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Soundstage.Services;

/// <summary>
/// Shows and hides soundstages for the local definitions according to automatic rules and manual commands.
/// </summary>
internal class LocalSoundstageService : IHostedService
{
    private readonly ILogger _logger;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly ILocalDefinitionService _localDefinitionService;
    private readonly ILiveSoundstageService _liveSoundstageService;

    private readonly ConcurrentDictionary<string, bool> _manualVisibilitySettings = new();

    public LocalSoundstageService(ILogger<LocalSoundstageService> logger, IFramework framework, IClientState clientState, ILocalDefinitionService localDefinitionService, ILiveSoundstageService liveSoundstageService, SoundstageConfiguration configuration)
    {
        _logger = logger;
        _framework = framework;
        _clientState = clientState;
        _localDefinitionService = localDefinitionService;
        _liveSoundstageService = liveSoundstageService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _localDefinitionService.LocalDefinitionsChanged += OnLocalDefinitionsChanged;
        _localDefinitionService.AutomaticShowConditionsChanged += OnAutomaticShowConditionsChanged;
        // TODO: Detect instance changes and room (privage chambers/apartment) changes
        _clientState.TerritoryChanged += OnTerritoryChanged;

        OnTerritoryChanged(_clientState.TerritoryType);

        return Task.CompletedTask;
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
            foreach (var removed in removedDefinitions)
            {
                _liveSoundstageService.TryDestroyLiveSoundstage(LiveSoundstageHelpers.MakeLocalSoundstageKey(removed));
            }

            // Show new soundstages that meet their show conditions
            foreach (var added in addedDefinitions)
            {
                if (_localDefinitionService.LocalDefinitions.TryGetValue(added, out var metadata)
                    && metadata.AutomaticShowConditions.Any(condition => IsConditionActive(condition, _clientState.TerritoryType)))
                {
                    try
                    {
                        using (FileStream stream = new FileStream(added, FileMode.Open, FileAccess.Read))
                        {
                            var definition = JsonSerializer.Deserialize<SoundstageDefinition>(stream, SoundstageDefinition.StandardSerializerOptions);
                            if (definition != null)
                            {
                                _liveSoundstageService.CreateOrUpdateLiveSoundstage(LiveSoundstageHelpers.MakeLocalSoundstageKey(added), definition);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception loading {path} to instantiate!", added);
                    }
                }
            }

            // Only update the modified soundstages that are already is visible
            foreach (var modified in modifiedDefinitions)
            {
                if (_localDefinitionService.LocalDefinitions.TryGetValue(modified, out var metadata)
                    && metadata.AutomaticShowConditions.Any(condition => IsConditionActive(condition, _clientState.TerritoryType))
                    && _liveSoundstageService.TryGetLiveSoundstage(LiveSoundstageHelpers.MakeLocalSoundstageKey(modified), out var liveSoundstage))
                {
                    try
                    {
                        using (FileStream stream = new FileStream(modified, FileMode.Open, FileAccess.Read))
                        {
                            var definition = JsonSerializer.Deserialize<SoundstageDefinition>(stream, SoundstageDefinition.StandardSerializerOptions);
                            if (definition != null)
                            {
                                liveSoundstage.Update(definition);
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
        var liveKey = LiveSoundstageHelpers.MakeLocalSoundstageKey(path);
        bool currentlyVisible = _liveSoundstageService.TryGetLiveSoundstage(liveKey, out var liveSoundstage);

        bool shouldBeVisible = _manualVisibilitySettings.GetValueOrDefault(path, _localDefinitionService.LocalDefinitions.TryGetValue(path, out var metadata)
            && metadata.AutomaticShowConditions.Any(condition => IsConditionActive(condition, _clientState.TerritoryType)));

        if (currentlyVisible && !shouldBeVisible)
        {
            _framework.RunOnFrameworkThread(() => _liveSoundstageService.TryDestroyLiveSoundstage(liveKey));
        }
        else if (shouldBeVisible && !currentlyVisible)
        {
            _framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        var definition = JsonSerializer.Deserialize<SoundstageDefinition>(stream, SoundstageDefinition.StandardSerializerOptions);
                        if (definition != null)
                        {
                            _liveSoundstageService.CreateOrUpdateLiveSoundstage(liveKey, definition);
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

    private void OnTerritoryChanged(ushort obj)
    {
        _logger.LogDebug("Territory change! Destroying all soundstages...");
        _liveSoundstageService.DestroyAllLiveSoundstages();

        _manualVisibilitySettings.Clear();

        // HACK: Very sensitive timed delay! Loading BgObjects while loading a zone causes their dye to be blank.
        // Consider instead waiting for the screen to start fading in via RaptureAtkUnitManager.IsUiFading or AgentInterface.GameEvent.LoadingEnded.
        _framework.RunOnTick(() =>
        {
            foreach (var localDefinition in _localDefinitionService.LocalDefinitions)
            {
                if (localDefinition.Value.AutomaticShowConditions.Any(condition =>
                    IsConditionActive(condition, obj)))
                {
                    _logger.LogDebug("Trying to auto show {file}!", localDefinition.Key);
                    try
                    {
                        using (FileStream stream = new FileStream(localDefinition.Key, FileMode.Open, FileAccess.Read))
                        {
                            var definition = JsonSerializer.Deserialize<SoundstageDefinition>(stream, SoundstageDefinition.StandardSerializerOptions);
                            if (definition != null)
                            {
                                _liveSoundstageService.CreateOrUpdateLiveSoundstage(LiveSoundstageHelpers.MakeLocalSoundstageKey(localDefinition.Key), definition);
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

    private static bool IsConditionActive(AutomaticShowCondition condition, ushort territoryId)
    {
        return condition.TerritoryId == territoryId;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _clientState.TerritoryChanged -= OnTerritoryChanged;
        _localDefinitionService.AutomaticShowConditionsChanged -= OnAutomaticShowConditionsChanged;
        _localDefinitionService.LocalDefinitionsChanged -= OnLocalDefinitionsChanged;

        return Task.CompletedTask;
    }
}

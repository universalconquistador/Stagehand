using Microsoft.Extensions.Logging;
using Stagehand.Definitions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Services;

/// <summary>
/// Specifies a location in which to automatically show a local Stagehand definition.
/// </summary>
public record struct AutomaticShowCondition
{
    public ushort TerritoryId;
    public ushort WorldId;
    public ushort DivisionId;
    public ushort WardId;
    public ushort HouseId;
    public ushort RoomId;
}

/// <summary>
/// Metadata about a local Stagehand definition.
/// </summary>
public interface ILocalDefinitionMetadata
{
    /// <summary>
    /// The metadata info stored in the definition.
    /// </summary>
    StagehandInfo Info { get; }

    /// <summary>
    /// The last modified time of the local definition file.
    /// </summary>
    DateTimeOffset LastModified { get; }

    /// <summary>
    /// The conditions for automatically showing the local definition.
    /// </summary>
    IReadOnlyList<AutomaticShowCondition> AutomaticShowConditions { get; }
}

public delegate void LocalDefinitionsChangedDelegate(IReadOnlyList<string> removedDefinitions, IReadOnlyList<string> addedDefinitions, IReadOnlyList<string> modifiedDefinitions);

/// <summary>
/// Maintains a list of the user's local Stagehand definitions.
/// </summary>
public interface ILocalDefinitionService
{
    /// <summary>
    /// The directory where the local Stagehands are stored, without a trailing slash.
    /// </summary>
    string LocalDefinitionDirectory { get; set; }

    /// <summary>
    /// The full filenames and metadata for the local Stagehand definitions.
    /// </summary>
    IReadOnlyDictionary<string, ILocalDefinitionMetadata> LocalDefinitions { get; }

    /// <summary>
    /// Sets the automatic show conditions for the local Stagehand definition at the given full path.
    /// </summary>
    /// <param name="filename">The full path to the Stagehand definition.</param>
    /// <param name="newConditions">The new automatic show conditions to apply.</param>
    void SetAutomaticShowConditions(string filename, IEnumerable<AutomaticShowCondition> newConditions);

    /// <summary>
    /// Raised when local Stagehand definitions have been added, removed, or changed.
    /// </summary>
    event LocalDefinitionsChangedDelegate LocalDefinitionsChanged;

    /// <summary>
    /// Raised when the automatic show conditions for a given full path to a definition have changed.
    /// </summary>
    event Action<string> AutomaticShowConditionsChanged;
}

public class LocalDefinitionService : ILocalDefinitionService, IDisposable
{
    private class LocalDefinitionMetadata : ILocalDefinitionMetadata
    {
        public StagehandInfo Info { get; set; }
        public DateTimeOffset LastModified { get; set; }
        public IReadOnlyList<AutomaticShowCondition> AutomaticShowConditions { get; set; } = Array.Empty<AutomaticShowCondition>();
    }

    public string LocalDefinitionDirectory
    {
        get => _configuration.DefinitionLibraryPath;
        set
        {
            _configuration.DefinitionLibraryPath = value;
            _configuration.Save();
            OnLocalDefinitionDirectoryChanged();
        }
    }

    public IReadOnlyDictionary<string, ILocalDefinitionMetadata> LocalDefinitions => _localDefinitions;

    public event LocalDefinitionsChangedDelegate? LocalDefinitionsChanged;
    public event Action<string>? AutomaticShowConditionsChanged;

    private readonly ILogger _logger;
    private readonly StagehandConfiguration _configuration;

    private readonly FileSystemWatcher _definitionsWatcher;

    private ConcurrentDictionary<string, ILocalDefinitionMetadata> _localDefinitions = new();

    public LocalDefinitionService(ILogger<LocalDefinitionService> logger, StagehandConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _definitionsWatcher = !string.IsNullOrEmpty(LocalDefinitionDirectory) ? new FileSystemWatcher(LocalDefinitionDirectory) : new FileSystemWatcher();
        _definitionsWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
        _definitionsWatcher.Changed += this.OnDefinitionChanged;
        _definitionsWatcher.Created += this.OnDefinitionCreated;
        _definitionsWatcher.Deleted += this.OnDefinitionDeleted;
        _definitionsWatcher.Renamed += this.OnDefinitionRenamed;
        _definitionsWatcher.Error += this.OnDefinitionWatchError;
        _definitionsWatcher.Filter = "*.json";
        _definitionsWatcher.IncludeSubdirectories = true;

        if (!string.IsNullOrEmpty(_definitionsWatcher.Path))
        {
            _definitionsWatcher.EnableRaisingEvents = true;

            RescanDefinitions(_definitionsWatcher.Path);
        }
    }

    private void OnDefinitionWatchError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "Exception in definition watcher for {location}!", _definitionsWatcher.Path);
    }

    private void OnDefinitionRenamed(object sender, RenamedEventArgs e)
    {
        if (_localDefinitions.TryRemove(e.OldFullPath, out var priorDefinition))
        {
            if (e.FullPath.StartsWith(_definitionsWatcher.Path, StringComparison.OrdinalIgnoreCase))
            {
                _localDefinitions[e.FullPath] = priorDefinition;
            }
        }
        else if (e.FullPath.StartsWith(_definitionsWatcher.Path, StringComparison.OrdinalIgnoreCase) && TryLoadDefinitionFile(e.FullPath, out var definition))
        {
            AddOrUpdateMetadata(e.FullPath, definition);
        }

        LocalDefinitionsChanged?.Invoke([ e.OldFullPath ], [ e.FullPath ], Array.Empty<string>());
    }

    private void AddOrUpdateMetadata(string filename, StagehandDefinition definition)
    {
        _localDefinitions.AddOrUpdate(filename, f => new LocalDefinitionMetadata()
        {
            Info = definition.Info,
            LastModified = (DateTimeOffset)(new FileInfo(filename).LastWriteTimeUtc),
            AutomaticShowConditions = (IReadOnlyList<AutomaticShowCondition>?)_configuration.AutomaticShowConditions.GetValueOrDefault(filename) ?? Array.Empty<AutomaticShowCondition>(),
        }, (f, existing) =>
        {
            var meta = (LocalDefinitionMetadata)existing;
            meta.Info = definition.Info;
            meta.LastModified = (DateTimeOffset)(new FileInfo(filename).LastWriteTimeUtc);
            meta.AutomaticShowConditions = (IReadOnlyList<AutomaticShowCondition>?)_configuration.AutomaticShowConditions.GetValueOrDefault(filename) ?? Array.Empty<AutomaticShowCondition>();
            return existing;
        });
    }

    private void OnDefinitionDeleted(object sender, FileSystemEventArgs e)
    {
        _localDefinitions.TryRemove(e.FullPath, out _);

        LocalDefinitionsChanged?.Invoke([ e.FullPath ], Array.Empty<string>(), Array.Empty<string>());
    }

    private void OnDefinitionCreated(object sender, FileSystemEventArgs e)
    {
        if (TryLoadDefinitionFile(e.FullPath, out var definition))
        {
            AddOrUpdateMetadata(e.FullPath, definition);
            LocalDefinitionsChanged?.Invoke(Array.Empty<string>(), [ e.FullPath ], Array.Empty<string>());
        }
    }

    private void OnDefinitionChanged(object sender, FileSystemEventArgs e)
    {
        if (TryLoadDefinitionFile(e.FullPath, out var definition))
        {
            AddOrUpdateMetadata(e.FullPath, definition);
            LocalDefinitionsChanged?.Invoke(Array.Empty<string>(), Array.Empty<string>(), [ e.FullPath ]);
        }
    }

    private void OnLocalDefinitionDirectoryChanged()
    {
        RescanDefinitions(LocalDefinitionDirectory);
    }

    private int _isRescanning = 0;
    private void RescanDefinitions(string directory)
    {
        if (Interlocked.CompareExchange(ref _isRescanning, 1, 0) == 0)
        {
            _logger.LogDebug($"Rescanning local definitions in {directory}...");
            //_localDefinitions = new();
            var previousDefinitions = Interlocked.Exchange(ref _localDefinitions, new());

            Task.Run(() =>
            {
                List<string> addedFiles = new();
                List<string> removedFiles = new();
                List<string> modifiedFiles = new();
                try
                {
                    foreach (var filename in previousDefinitions.Keys)
                    {
                        if (!File.Exists(filename))
                        {
                            removedFiles.Add(filename);
                        }
                    }

                    foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    {
                        if (TryLoadDefinitionFile(file, out var definition))
                        {
                            AddOrUpdateMetadata(file, definition);

                            if (previousDefinitions.ContainsKey(file))
                            {
                                modifiedFiles.Add(file);
                            }
                            else
                            {
                                addedFiles.Add(file);
                            }
                        }
                        else
                        {
                            if (previousDefinitions.ContainsKey(file))
                            {
                                removedFiles.Add(file);
                            }
                        }
                    }
                }
                finally
                {
                    _isRescanning = 0;
                    LocalDefinitionsChanged?.Invoke(removedFiles, addedFiles, modifiedFiles);
                }
            });
        }
    }

    private bool TryLoadDefinitionFile(string filename, [NotNullWhen(true)] out StagehandDefinition? definition)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    definition = JsonSerializer.Deserialize<StagehandDefinition>(stream, options: StagehandDefinition.StandardSerializerOptions);
                }
                return definition != null;
            }
            catch (FileNotFoundException ex)
            {
                definition = null;
                return false;
            }
            catch (IOException ex)
            {
                // Oftentimes the FileSystemWatcher will alert us to a change while the file is still being written, which results
                // in an IOException. Wait and try again a few times, and hopefully the writing application will be done.
                lastException = ex;
                Thread.Sleep(5);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception loading definition from {filename}!", filename);
                definition = null;
                return false;
            }
        }

        _logger.LogWarning(lastException, "Tried a few times but could not load {filename}! Is it being written by another application?", filename);
        definition = null;
        return false;
    }

    public void Dispose()
    {
        _definitionsWatcher.EnableRaisingEvents = false;
        _definitionsWatcher.Dispose();
    }

    public void SetAutomaticShowConditions(string filename, IEnumerable<AutomaticShowCondition> newConditions)
    {
        _configuration.AutomaticShowConditions[filename] = newConditions.ToList();
        _configuration.Save();
        if (_localDefinitions.TryGetValue(filename, out var existingDefinition))
        {
            var meta = (LocalDefinitionMetadata)existingDefinition;
            meta.AutomaticShowConditions = newConditions.ToList();

            AutomaticShowConditionsChanged?.Invoke(filename);
        }
    }
}



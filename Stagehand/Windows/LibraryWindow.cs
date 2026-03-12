using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stagehand.Definitions;
using Stagehand.Services;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Windows;

internal class LibraryWindow : Window, IHostedService, IDisposable
{
    private const string LibraryWindowCommand = "/stagehand";

    private readonly ILogger _logger;
    private readonly IDalamudPluginInterface _dalamudPluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IDataManager _dataManager;
    private readonly IClientState _clientState;

    private readonly ILocalDefinitionService _localDefinitionService;
    private readonly ILiveStagehandService _liveStagehandService;
    private readonly LocalStagehandService _localStagehandService;
    private readonly WindowSystem _windowSystem;
    private readonly StagehandConfiguration _configuration;

    private string _selectedLocalDefinitionFilename = string.Empty;

    private List<ushort> _allTerritories;

    public LibraryWindow(ILogger<LibraryWindow> logger, IDalamudPluginInterface dalamudPluginInterface, ICommandManager commandManager, IDataManager dataManager, IClientState clientState, ILocalDefinitionService localDefinitionService, ILiveStagehandService liveStagehandService, LocalStagehandService localStagehandService, WindowSystem windowSystem, StagehandConfiguration configuration) : base($"Stagehand {dalamudPluginInterface.Manifest.AssemblyVersion}###StagehandLibrary")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        _logger = logger;
        _dalamudPluginInterface = dalamudPluginInterface;
        _commandManager = commandManager;
        _dataManager = dataManager;
        _clientState = clientState;

        _localDefinitionService = localDefinitionService;
        _liveStagehandService = liveStagehandService;
        _localStagehandService = localStagehandService;
        _windowSystem = windowSystem;
        _configuration = configuration;

        var territorySheet = dataManager.GetExcelSheet<TerritoryType>();
        _allTerritories = new List<ushort>(territorySheet.Count);
        for (int i = 0; i < territorySheet.Count; i++)
        {
            var row = territorySheet.GetRowAt(i);
            _allTerritories.Add((ushort)row.RowId);

            if (clientState.TerritoryType == row.RowId)
            {
                _autoLoadNewTerritoryIndex = i;
            }
        }
    }

    public void Dispose()
    {

    }

    private int _autoLoadNewTerritoryIndex;
    private byte[] _newDefinitionFilenameBuffer = new byte[260];
    public override void Draw()
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.X, 0.0f)))
        {
            using (ImRaii.Table("librarywindow", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.NoBordersInBodyUntilResize))
            {
                ImGui.TableSetupColumn("definitions", ImGuiTableColumnFlags.WidthFixed, 200);
                ImGui.TableSetupColumn("properties", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableNextColumn();

                ImGui.Text("My Stagehands");

                ImGui.TextDisabled($"...{Path.DirectorySeparatorChar}{Path.GetFileName(_configuration.DefinitionLibraryPath)}{Path.DirectorySeparatorChar}");
                if (ImGui.IsItemClicked())
                {
                    Process.Start("explorer", $"/root, {_configuration.DefinitionLibraryPath}");
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text(_configuration.DefinitionLibraryPath);
                        ImGui.Separator();
                        ImGui.TextDisabled("Click to open.");
                    }
                }

                var playerTerritory = _clientState.TerritoryType;

                float bottomBarHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2.0f;
                using (var listBox = ImRaii.ListBox("###Stagehands", ImGui.GetContentRegionAvail() - new Vector2(0.0f, bottomBarHeight + ImGui.GetStyle().ItemSpacing.Y)))
                {
                    if (listBox.Success)
                    {
                        // Create the directory hierarchy tree nodes based on path names without allocating objects or querying the filesystem
                        const ImGuiTreeNodeFlags commonFlags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.AllowItemOverlap /* | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick */ | ImGuiTreeNodeFlags.FramePadding;

                        string directory = _localDefinitionService.LocalDefinitionDirectory;
                        int directoryDepth = 0;
                        bool isInCollapsedDirectory = false;
                        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                        {
                            foreach (var localDefinition in _localDefinitionService.LocalDefinitions.OrderBy(pair => pair.Key, PathSorter.CurrentCultureIgnoreCase))
                            {
                                // Leave any directories that we are currently in that we shouldn't be in
                                while (!localDefinition.Key.StartsWith(directory) && directory.Length > 1 && ((directoryDepth > 0 || isInCollapsedDirectory)))
                                {
                                    if (!isInCollapsedDirectory)
                                    {
                                        ImGui.TreePop();
                                        directoryDepth -= 1;
                                    }
                                    isInCollapsedDirectory = false;
                                    directory = directory.Substring(0, directory.LastIndexOf(Path.DirectorySeparatorChar, directory.Length - 2));
                                }

                                // Enter any directories that are not already entered
                                int nextDirectorySeparator = localDefinition.Key.IndexOf(Path.DirectorySeparatorChar, directory.Length + 1);
                                bool isLeafVisible = !isInCollapsedDirectory;
                                while (nextDirectorySeparator >= 0)
                                {
                                    string subdirName = localDefinition.Key.Substring(directory.Length + 1, nextDirectorySeparator - directory.Length - 1);

                                    // The directory's treenode

                                    bool enteredDirectory;
                                    using (ImRaii.PushFont(UiBuilder.IconFont))
                                    {
                                        enteredDirectory = ImGui.TreeNodeEx($"{FontAwesomeIcon.Folder.ToIconString()}###{subdirName}", commonFlags);
                                    }

                                    ImGui.SameLine();
                                    ImGui.TextUnformatted($"  {subdirName}");

                                    directory += $"{Path.DirectorySeparatorChar}{subdirName}";
                                    if (enteredDirectory)
                                    {
                                        isInCollapsedDirectory = false;
                                        directoryDepth += 1;
                                        nextDirectorySeparator = localDefinition.Key.IndexOf(Path.DirectorySeparatorChar, directory.Length + 1);
                                    }
                                    else
                                    {
                                        isLeafVisible = false;
                                        isInCollapsedDirectory = true;
                                        break;
                                    }
                                }

                                if (isLeafVisible)
                                {
                                    // The file's treenode
                                    bool isVisible = _liveStagehandService.TryGetLiveStagehand(LiveStagehandHelpers.MakeLocalStagehandKey(localDefinition.Key), out _);
                                    using (ImRaii.PushFont(UiBuilder.IconFont))
                                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, isVisible))
                                    using (var fileTreeNode = ImRaii.TreeNode($"{(isVisible ? FontAwesomeIcon.Eye : FontAwesomeIcon.FileImage).ToIconString()}###{localDefinition.Key}", commonFlags | ImGuiTreeNodeFlags.Leaf | (localDefinition.Key == _selectedLocalDefinitionFilename ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None)))
                                    {
                                        if (ImGui.IsItemClicked())
                                        {
                                            _selectedLocalDefinitionFilename = localDefinition.Key;
                                            _autoLoadNewTerritoryIndex = _allTerritories.IndexOf((ushort)localDefinition.Value.Info.IntendedTerritoryType);
                                            if (_autoLoadNewTerritoryIndex < 0)
                                            {
                                                _autoLoadNewTerritoryIndex = _allTerritories.IndexOf(_clientState.TerritoryType);
                                            }
                                        }

                                        ImGui.SameLine();
                                        using (ImRaii.DefaultFont())
                                        using (ImRaii.Disabled(localDefinition.Value.Info.IntendedTerritoryType == 0 || localDefinition.Value.Info.IntendedTerritoryType != playerTerritory))
                                        {
                                            ImGui.TextUnformatted($"  {Path.GetFileName(localDefinition.Key)}");
                                        }
                                    }
                                }
                            }
                        }

                        for (int i = 0; i < directoryDepth; i++)
                        {
                            ImGui.TreePop();
                        }
                    }
                }
                if (ImGui.Button("New", new Vector2(ImGui.GetContentRegionMax().X / 4.0f, bottomBarHeight)))
                {
                    _newDefinitionFilenameBuffer.AsSpan().Clear();
                    ImGui.OpenPopup("CreateNewDefinition", ImGuiPopupFlags.None);
                }

                using (var popup = ImRaii.Popup("CreateNewDefinition"))
                {
                    if (popup.Success)
                    {
                        if (ImGui.InputTextWithHint("Name", "Name", _newDefinitionFilenameBuffer, ImGuiInputTextFlags.EnterReturnsTrue))
                        {
                            ImGui.CloseCurrentPopup();

                            string name = Encoding.UTF8.GetString(_newDefinitionFilenameBuffer.AsSpan().Slice(0, _newDefinitionFilenameBuffer.AsSpan().IndexOf((byte)0)));
                            string pathWithExtension;

                            // If the player ended the name with .json, detect it
                            // otherwise, add .json
                            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                            {
                                pathWithExtension = name;
                                name = Path.GetFileNameWithoutExtension(name);
                            }
                            else
                            {
                                pathWithExtension = name + ".json";
                                name = Path.GetFileName(name);
                            }

                            var newInfo = new StagehandInfo()
                            {
                                Name = name,
                                AuthorName = "",
                                Description = "New Stagehand",
                                VersionString = "0.0.1",
                                IntendedTerritoryType = _clientState.TerritoryType,
                            };

                            var newDefinition = new StagehandDefinition() { Info = newInfo };
                            string finalPath = Path.Combine(_localDefinitionService.LocalDefinitionDirectory, pathWithExtension);
                            try
                            {
                                string? directory = Path.GetDirectoryName(finalPath);
                                if (directory != null)
                                {
                                    Directory.CreateDirectory(directory);
                                }
                                using (var stream = new FileStream(finalPath, FileMode.Create, FileAccess.Write))
                                {
                                    JsonSerializer.Serialize(stream, newDefinition);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to write new Stagehand definition file {file}!", finalPath);
                            }
                        }
                    }
                }

                ImGui.TableNextColumn();
                if (!string.IsNullOrEmpty(_selectedLocalDefinitionFilename) && _localDefinitionService.LocalDefinitions.TryGetValue(_selectedLocalDefinitionFilename, out var selectedMetadata))
                {
                    ImGui.Text(selectedMetadata.Info.Name);
                    ImGui.TextDisabled("Version ");
                    ImGui.SameLine();
                    ImGui.Text(selectedMetadata.Info.VersionString);
                    ImGui.TextDisabled("By ");
                    ImGui.SameLine();
                    ImGui.Text(selectedMetadata.Info.AuthorName);
                    ImGui.TextDisabled("Last Modified ");
                    ImGui.SameLine();
                    ImGui.Text(selectedMetadata.LastModified.ToLocalTime().ToString());
                    ImGui.TextDisabled("For Location ");
                    ImGui.SameLine();
                    if (_dataManager.GetExcelSheet<TerritoryType>().TryGetRow((uint)selectedMetadata.Info.IntendedTerritoryType, out var territoryType) && territoryType.PlaceName.IsValid)
                    {
                        ImGui.Text($"{territoryType.PlaceName.Value.Name} ({selectedMetadata.Info.IntendedTerritoryType})");
                    }
                    else
                    {
                        ImGui.Text($"<Unknown> ({selectedMetadata.Info.IntendedTerritoryType})");
                    }
                    ImGuiHelpers.ScaledDummy(3.0f);
                    ImGui.TextWrapped(selectedMetadata.Info.Description);

                    ImGuiHelpers.ScaledDummy(3.0f);
                    ImGui.Separator();
                    ImGuiHelpers.ScaledDummy(3.0f);

                    ImGui.Text($"Automatic Loading ({selectedMetadata.AutomaticShowConditions.Count} location{(selectedMetadata.AutomaticShowConditions.Count == 1 ? string.Empty : "s")})");

                    foreach (var condition in selectedMetadata.AutomaticShowConditions)
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text($"{_dataManager.GetExcelSheet<TerritoryType>().GetRow(condition.TerritoryId).PlaceName.ValueNullable?.Name} ({condition.TerritoryId})");
                        ImGui.SameLine();
                        if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                        {
                            _localDefinitionService.SetAutomaticShowConditions(_selectedLocalDefinitionFilename, selectedMetadata.AutomaticShowConditions.Where(c => c != condition));
                        }
                    }

                    ImGuiHelpers.ScaledDummy(3.0f);

                    if (ImGui.Combo<ushort>("Location", ref _autoLoadNewTerritoryIndex, _allTerritories, id => $"{_dataManager.GetExcelSheet<TerritoryType>().GetRow(id).PlaceName.ValueNullable?.Name} ({id})" ?? ""))
                    {

                    }
                    if (ImGui.Button("Add"))
                    {
                        _localDefinitionService.SetAutomaticShowConditions(_selectedLocalDefinitionFilename, selectedMetadata.AutomaticShowConditions.Append(new AutomaticShowCondition() { TerritoryId = _allTerritories[_autoLoadNewTerritoryIndex] }));
                    }

                    ImGuiHelpers.ScaledDummy(3.0f);
                    ImGui.Separator();
                    ImGuiHelpers.ScaledDummy(3.0f);

                    string liveKey = LiveStagehandHelpers.MakeLocalStagehandKey(_selectedLocalDefinitionFilename);
                    bool isVisible = _liveStagehandService.TryGetLiveStagehand(liveKey, out _);

                    if (isVisible)
                    {
                        if (ImGui.Button("Hide"))
                        {
                            _liveStagehandService.TryDestroyLiveStagehand(liveKey);
                            _localStagehandService.SetManualVisibility(_selectedLocalDefinitionFilename, false);
                        }
                    }
                    else
                    {
                        if (ImGui.Button("Show"))
                        {
                            _localStagehandService.SetManualVisibility(_selectedLocalDefinitionFilename, true);
                            try
                            {
                                using (FileStream stream = new FileStream(_selectedLocalDefinitionFilename, FileMode.Open, FileAccess.Read))
                                {
                                    var definition = JsonSerializer.Deserialize<StagehandDefinition>(stream, StagehandDefinition.StandardSerializerOptions);
                                    if (definition != null)
                                    {
                                        _liveStagehandService.CreateOrUpdateLiveStagehand(liveKey, definition);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Exception loading {path} to instantiate!", _selectedLocalDefinitionFilename);
                            }
                        }
                    }
                }
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _windowSystem.AddWindow(this);

        _dalamudPluginInterface.UiBuilder.OpenMainUi += Toggle;

        _commandManager.AddHandler(LibraryWindowCommand, new CommandInfo(OnLibraryWindowCommandInvoked)
        {
            HelpMessage = "Show the Stagehand main window"
        });

        return Task.CompletedTask;
    }

    private void OnLibraryWindowCommandInvoked(string command, string args)
    {
        Toggle();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _commandManager.RemoveHandler(LibraryWindowCommand);

        _dalamudPluginInterface.UiBuilder.OpenMainUi -= Toggle;

        _windowSystem.RemoveWindow(this);

        return Task.CompletedTask;
    }
}

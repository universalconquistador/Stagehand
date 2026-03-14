using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
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
using FFXIVClientStructs.FFXIV.Client.Game;
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

    private const int TerritoryUseHousingOutdoor = 13;
    private const int TerritoryUseHousingIndoor = 14;

    // Company workshops have an IntendedTerritoryType for housing but do not actually support housing stuff
    // (e.g. cannot check the ward/division/house/room via HousingManager)
    private static readonly uint[] WorkshopTerritoryTypes =
    [  
        423, // Company Workshop - Mist
        424, // Company Workshop - The Goblet
        425, // Company Workshop - The Lavender Beds
        653, // Company Workshop - Shirogane
        984, // Company Workshop - Empyreum
    ];

    private readonly ILogger _logger;
    private readonly IDalamudPluginInterface _dalamudPluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IDataManager _dataManager;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;

    private readonly ILocalDefinitionService _localDefinitionService;
    private readonly ILiveStageService _liveStageService;
    private readonly LocalStageService _localStageService;
    private readonly WindowSystem _windowSystem;
    private readonly StagehandConfiguration _configuration;

    private string _selectedLocalDefinitionFilename = string.Empty;

    private List<SelectableTerritory> _allTerritories;
    private List<SelectableWorld> _allWorlds;
    private int[] _allHouses = Enumerable.Range(0, 31).ToArray();

    public LibraryWindow(ILogger<LibraryWindow> logger, IDalamudPluginInterface dalamudPluginInterface, ICommandManager commandManager, IDataManager dataManager, IClientState clientState, IPlayerState playerState, ILocalDefinitionService localDefinitionService, ILiveStageService liveStageService, LocalStageService localStageService, WindowSystem windowSystem, StagehandConfiguration configuration) : base($"Stagehand {dalamudPluginInterface.Manifest.AssemblyVersion}###StagehandLibrary")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 600),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        _logger = logger;
        _dalamudPluginInterface = dalamudPluginInterface;
        _commandManager = commandManager;
        _dataManager = dataManager;
        _clientState = clientState;
        _playerState = playerState;

        _localDefinitionService = localDefinitionService;
        _liveStageService = liveStageService;
        _localStageService = localStageService;
        _windowSystem = windowSystem;
        _configuration = configuration;

        var territorySheet = dataManager.GetExcelSheet<TerritoryType>();
        _allTerritories = new List<SelectableTerritory>();
        for (int i = 0; i < territorySheet.Count; i++)
        {
            var row = territorySheet.GetRowAt(i);
            if (row.PlaceName.IsValid && !string.IsNullOrEmpty(row.PlaceName.Value.Name.ToString()))
            {
                bool inHousingIndoor = row.TerritoryIntendedUse.RowId == TerritoryUseHousingIndoor && !WorkshopTerritoryTypes.Contains(row.RowId);
                bool inHousingOutdoor = inHousingIndoor || row.TerritoryIntendedUse.RowId == TerritoryUseHousingOutdoor;
                _allTerritories.Add(new SelectableTerritory($"{row.PlaceName.Value.Name.ToString()}{(inHousingIndoor ? " (Housing Indoors)" : (inHousingOutdoor ? " (Housing Outdoors)" : ""))}", (ushort)row.RowId, inHousingOutdoor, inHousingIndoor));
            }
        }
        _allTerritories = _allTerritories.OrderBy(territory => territory.DisplayName).ToList();
        for (int i = 0; i < _allTerritories.Count; i++)
        {
            if (clientState.TerritoryType == _allTerritories[i].Id)
            {
                _autoLoadNewTerritoryIndex = i;
            }
        }

        var worldSheet = dataManager.GetExcelSheet<World>();
        _allWorlds = new List<SelectableWorld>();
        for (int i = 0; i < worldSheet.Count; i++)
        {
            var row = worldSheet.GetRowAt(i);
            if (row.IsPublic)
            {
                _allWorlds.Add(new SelectableWorld($"{row.Name} ({row.DataCenter.Value.Name})", (ushort)row.RowId));
            }    
        }
        _allWorlds = _allWorlds.OrderBy(world => world.DisplayName).ToList();
        for (int i = 0; i < _allWorlds.Count; i++)
        {
            if (_allWorlds[i].Id == _playerState.CurrentWorld.RowId)
            {
                _autoLoadNewWorldIndex = i;
            }
        }
    }

    public void Dispose()
    {

    }

    private record class SelectableTerritory(string DisplayName, ushort Id, bool IsInHousingWard, bool IsInHousingRoom);
    private record class SelectableWorld(string DisplayName, ushort Id);

    private int _autoLoadNewTerritoryIndex;
    private string _autoLoadNewTerritoryFilter = "";

    private bool _autoLoadNewUseWorld = false;
    private int _autoLoadNewWorldIndex;
    private string _autoLoadNewWorldFilter = "";

    private bool _autoLoadNewUseWard = false;
    private int _autoLoadNewWardId = 0;
    private int _autoLoadNewDivisionIndex = 0;

    private bool _autoLoadNewUseHouse = false;
    private int _autoLoadNewHouseId = 1;
    private int _autoLoadNewRoomId = 0;

    private byte[] _newDefinitionFilenameBuffer = new byte[260];
    private string[] _divisions = [ "Main Division", "Subdivision" ];
    //private string[] _houses = [ "Apartment Building", "House 1", "House 2", "House 3", "House 4", "House 5", "House 6", "House 7", "House 8", "House 9", "House 10", "House 11", "House 12"]
    Func<int, string> houseIdToString = static id => id == 0 ? "Apartment Building" : $"House {id}";
    Func<int, string> houseIdToStringSubdivision = static id => id == 0 ? "Apartment Building (Subdivision)" : $"House {id + 30}";

    public unsafe override void Draw()
    {
        var defaultCellPadding = ImGui.GetStyle().CellPadding;
        using (ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.X, 0.0f)))
        {
            using (ImRaii.Table("librarywindow", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.NoBordersInBodyUntilResize))
            {
                ImGui.TableSetupColumn("definitions", ImGuiTableColumnFlags.WidthFixed, 300);
                ImGui.TableSetupColumn("properties", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableNextColumn();

                ImGui.Text("My Stages");
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

                ImGui.TableNextColumn();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Cog, ""));
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
                {
                    //_dalamudPluginInterface.Config
                }

                ImGui.TableNextColumn();

                var playerTerritory = HousingManager.GetOriginalHouseTerritoryTypeId();

                float bottomBarHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2.0f;
                using (var listBox = ImRaii.ListBox("###Stages", ImGui.GetContentRegionAvail() - new Vector2(0.0f, bottomBarHeight + ImGui.GetStyle().ItemSpacing.Y)))
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
                                    bool isVisible = _liveStageService.TryGetLiveStage(LiveStageHelpers.MakeLocalStageKey(localDefinition.Key), out _);
                                    using (ImRaii.PushFont(UiBuilder.IconFont))
                                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, isVisible))
                                    using (var fileTreeNode = ImRaii.TreeNode($"{(isVisible ? FontAwesomeIcon.Eye : FontAwesomeIcon.FileImage).ToIconString()}###{localDefinition.Key}", commonFlags | ImGuiTreeNodeFlags.Leaf | (localDefinition.Key == _selectedLocalDefinitionFilename ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None)))
                                    {
                                        if (ImGui.IsItemClicked())
                                        {
                                            _selectedLocalDefinitionFilename = localDefinition.Key;
                                            _autoLoadNewTerritoryIndex = -1;
                                            for (int i = 0; i < _allTerritories.Count; i++)
                                            {
                                                if (_allTerritories[i].Id == localDefinition.Value.Info.IntendedTerritoryType)
                                                {
                                                    _autoLoadNewTerritoryIndex = (ushort)i;
                                                }
                                            }
                                            if (_autoLoadNewTerritoryIndex < 0)
                                            {
                                                for (int i = 0; i < _allTerritories.Count; i++)
                                                {
                                                    if (_allTerritories[i].Id == _clientState.TerritoryType)
                                                    {
                                                        _autoLoadNewTerritoryIndex = (ushort)i;
                                                    }
                                                }
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

                            var newInfo = new StageInfo()
                            {
                                Name = name,
                                AuthorName = "",
                                Description = "New Stage",
                                VersionString = "0.0.1",
                                IntendedTerritoryType = (int)HousingManager.GetOriginalHouseTerritoryTypeId(),
                            };

                            var newDefinition = new StageDefinition() { Info = newInfo };
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
                                _logger.LogError(ex, "Failed to write new Stage definition file {file}!", finalPath);
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

                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() - defaultCellPadding.X);
                    using (ImRaii.PushStyle(ImGuiStyleVar.CellPadding, defaultCellPadding))
                    using (var table = ImRaii.Table("###AutoLoadConditions", 8, ImGuiTableFlags.PadOuterX))
                    {
                        if (table.Success)
                        {
                            ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.None, 2);
                            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.None, 3);
                            ImGui.TableSetupColumn("Ward", ImGuiTableColumnFlags.None, 1);
                            ImGui.TableSetupColumn("Division", ImGuiTableColumnFlags.None, 2);
                            ImGui.TableSetupColumn("House", ImGuiTableColumnFlags.None, 1);
                            ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.None, 1);
                            ImGui.TableSetupColumn("###Copy", ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.WidthFixed, ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Copy, ""));
                            ImGui.TableSetupColumn("###Delete", ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.WidthFixed, ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Trash, ""));

                            ImGui.TableHeadersRow();

                            Location.TryGetLocation(_clientState, _playerState, out var location);

                            int conditionIndex = 0;
                            foreach (var condition in selectedMetadata.AutomaticShowConditions)
                            {
                                ImGui.TableNextRow();

                                if (condition.Evaluate(location))
                                {
                                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(new Vector4(ImGuiColors.HealerGreen.AsVector3(), 0.2f)));
                                }

                                ImGui.TableNextColumn();
                                if (condition.WorldId != ushort.MaxValue)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    using (ImRaii.Disabled(location.WorldId != condition.WorldId))
                                    {
                                        ImGui.Text($"{_dataManager.GetExcelSheet<World>().GetRow(condition.WorldId).Name}");
                                    }
                                }

                                ImGui.TableNextColumn();
                                ImGui.AlignTextToFramePadding();
                                ImGui.Text($"{_dataManager.GetExcelSheet<TerritoryType>().GetRow(condition.TerritoryId).PlaceName.ValueNullable?.Name} ({condition.TerritoryId})");

                                ImGui.TableNextColumn();
                                if (condition.WardId != ushort.MaxValue)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    using (ImRaii.Disabled(location.WardId != condition.WardId))
                                    {
                                        ImGui.Text($"Ward {condition.WardId}");
                                    }
                                }

                                ImGui.TableNextColumn();
                                if (condition.DivisionId != ushort.MaxValue)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    using (ImRaii.Disabled(location.DivisionId != condition.DivisionId))
                                    {
                                        if (condition.DivisionId >= 1 && condition.DivisionId <= _divisions.Length)
                                        {
                                            ImGui.Text(_divisions[condition.DivisionId - 1]);
                                        }
                                        else
                                        {
                                            ImGui.Text($"Division {condition.DivisionId}");
                                        }
                                    }
                                }

                                ImGui.TableNextColumn();
                                if (condition.HouseId != ushort.MaxValue)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    using (ImRaii.Disabled(location.HouseId != condition.HouseId))
                                    {
                                        if (condition.HouseId >= 0 && condition.HouseId < _allHouses.Length)
                                        {
                                            ImGui.Text((condition.DivisionId == 2 ? houseIdToStringSubdivision : houseIdToString).Invoke(condition.HouseId));
                                        }
                                        else
                                        {
                                            ImGui.Text($"House {condition.HouseId}");
                                        }
                                    }
                                }

                                ImGui.TableNextColumn();
                                if (condition.RoomId != ushort.MaxValue)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    using (ImRaii.Disabled(location.RoomId != condition.RoomId))
                                    {
                                        ImGui.Text($"Room {condition.RoomId}");
                                    }
                                }

                                ImGui.TableNextColumn();
                                if (ImGuiComponents.IconButton($"###CopyCondition{conditionIndex}", FontAwesomeIcon.Copy))
                                {
                                    var json = JsonSerializer.Serialize(condition);
                                    ImGui.SetClipboardText(json);
                                }

                                ImGui.TableNextColumn();
                                if (ImGuiComponents.IconButton($"###DeleteCondition{conditionIndex}", FontAwesomeIcon.Trash))
                                {
                                    var newConditions = selectedMetadata.AutomaticShowConditions.ToList();
                                    newConditions.RemoveAt(conditionIndex);
                                    _localDefinitionService.SetAutomaticShowConditions(_selectedLocalDefinitionFilename, newConditions);
                                }

                                conditionIndex += 1;
                            }
                        }
                    }

                    ImGuiHelpers.ScaledDummy(3.0f);

                    if (ImGui.CollapsingHeader("Add Auto Load Location"))
                    {
                        // Territory
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X);
                        if (Utils.ImGuiExtensions.FilteredCombo("Location", ref _autoLoadNewTerritoryIndex, ref _autoLoadNewTerritoryFilter, _allTerritories, static territory => territory.DisplayName, "Location"))
                        {

                        }

                        var pasteIcon = FontAwesomeIcon.Paste;
                        var pasteWidth = ImGuiComponents.GetIconButtonWithTextWidth(pasteIcon, "");


                        var useCurrentIcon = FontAwesomeIcon.MapPin;
                        var useCurrentWidth = ImGuiComponents.GetIconButtonWithTextWidth(useCurrentIcon, "");
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - useCurrentWidth - ImGui.GetStyle().ItemSpacing.X - pasteWidth);
                        if (ImGuiComponents.IconButton(pasteIcon))
                        {
                            try
                            {
                                var pastedCondition = JsonSerializer.Deserialize<AutomaticShowCondition>(ImGui.GetClipboardText());

                                for (int i = 0; i < _allWorlds.Count; i++)
                                {
                                    if (_allWorlds[i].Id == pastedCondition.WorldId)
                                    {
                                        _autoLoadNewWorldIndex = i;
                                        _autoLoadNewUseWorld = true;
                                    }
                                }
                                if (pastedCondition.WorldId == ushort.MaxValue)
                                {
                                    _autoLoadNewUseWorld = false;
                                }

                                for (int i = 0; i < _allTerritories.Count; i++)
                                {
                                    if (_allTerritories[i].Id == pastedCondition.TerritoryId)
                                    {
                                        _autoLoadNewTerritoryIndex = i;
                                    }
                                }

                                if (pastedCondition.WardId != ushort.MaxValue)
                                {
                                    _autoLoadNewWardId = pastedCondition.WardId;
                                    _autoLoadNewUseWard = true;
                                }
                                else
                                {
                                    _autoLoadNewUseWard = false;
                                }

                                if (pastedCondition.DivisionId != ushort.MaxValue)
                                {
                                    _autoLoadNewDivisionIndex = pastedCondition.DivisionId - 1;
                                }

                                if (pastedCondition.HouseId != ushort.MaxValue)
                                {
                                    _autoLoadNewHouseId = pastedCondition.HouseId;
                                    _autoLoadNewUseHouse = true;
                                }
                                else
                                {
                                    _autoLoadNewUseHouse = false;
                                }

                                if (pastedCondition.RoomId != ushort.MaxValue)
                                {
                                    _autoLoadNewRoomId = pastedCondition.RoomId;
                                }

                            }
                            catch (JsonException ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse JSON!");
                            }
                        }
                        if (ImGui.IsItemHovered())
                        {
                            using (ImRaii.Tooltip())
                            {
                                ImGui.Text("Paste auto load condition");
                            }
                        }
                        ImGui.SameLine();
                        if (ImGuiComponents.IconButton(useCurrentIcon))
                        {
                            UseCurrentLocation();
                        }
                        if (ImGui.IsItemHovered())
                        {
                            using (ImRaii.Tooltip())
                            {
                                ImGui.Text("Use current location");
                            }
                        }

                        // World
                        ImGui.Checkbox("###LocationUseWorld", ref _autoLoadNewUseWorld);
                        if (ImGui.IsItemHovered())
                        {
                            using (ImRaii.Tooltip())
                            {
                                ImGui.Text("Only auto load on a specific World");
                            }
                        }
                        ImGui.SameLine();
                        if (_autoLoadNewUseWorld)
                        {
                            Utils.ImGuiExtensions.FilteredCombo("World", ref _autoLoadNewWorldIndex, ref _autoLoadNewWorldFilter, _allWorlds, static world => world.DisplayName, "World");
                        }
                        else
                        {
                            using (ImRaii.Disabled())
                            {
                                int currentItem = 0;
                                ImGui.Combo("World", ref currentItem, "(All Worlds)\0"u8);
                            }
                        }

                        if (_autoLoadNewTerritoryIndex >= 0 && _allTerritories[_autoLoadNewTerritoryIndex].IsInHousingWard)
                        {
                            // Ward & Division
                            using (ImRaii.Disabled(!_autoLoadNewUseWorld))
                            {
                                ImGui.Checkbox("###LocationUseWard", ref _autoLoadNewUseWard);
                            }
                            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                            {
                                using (ImRaii.Tooltip())
                                {
                                    ImGui.Text("Only auto load in a specific housing ward");
                                    if (!_autoLoadNewUseWorld)
                                    {
                                        ImGui.TextColored(ImGuiColors.DalamudRed, "Specify a World above before selecting a ward.");
                                    }
                                }
                            }
                            bool wardEnabled = _autoLoadNewUseWorld && _autoLoadNewUseWard;
                            using (ImRaii.Disabled(!wardEnabled))
                            {
                                ImGui.SameLine();
                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 3.0f + ImGui.GetStyle().ItemSpacing.X / 2.0f);
                                if (wardEnabled)
                                {
                                    ImGui.InputInt("###Ward", ref _autoLoadNewWardId);
                                }
                                else
                                {
                                    string dummy = "(All Wards)";
                                    ImGui.InputText("###WardDisabled", ref dummy);
                                }
                                ImGui.SameLine();
                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 2.0f + ImGui.GetStyle().ItemSpacing.X / 2.0f);
                                if (wardEnabled)
                                {
                                    ImGui.Combo("Ward & Division", ref _autoLoadNewDivisionIndex, _divisions);
                                }
                                else
                                {
                                    int currentItem = 0;
                                    ImGui.Combo("Ward & Division", ref currentItem, "(All Divisions)\0"u8);
                                }
                            }

                            if (_allTerritories[_autoLoadNewTerritoryIndex].IsInHousingRoom)
                            {
                                // House & Room
                                using (ImRaii.Disabled(!wardEnabled))
                                {
                                    ImGui.Checkbox("###LocationUseHouse", ref _autoLoadNewUseHouse);
                                }
                                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                                {
                                    using (ImRaii.Tooltip())
                                    {
                                        ImGui.Text("Only auto load in a specific house");
                                        if (!wardEnabled)
                                        {
                                            ImGui.TextColored(ImGuiColors.DalamudRed, "Specify a World and a ward above before selecting a house.");
                                        }
                                    }
                                }
                                bool houseEnabled = wardEnabled && _autoLoadNewUseHouse;
                                using (ImRaii.Disabled(!houseEnabled))
                                {
                                    ImGui.SameLine();
                                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 3.0f + ImGui.GetStyle().ItemSpacing.X / 2.0f);
                                    if (houseEnabled)
                                    {
                                        ImGui.Combo("###House", ref _autoLoadNewHouseId, _allHouses, _autoLoadNewDivisionIndex == 1 ? houseIdToStringSubdivision : houseIdToString);
                                    }
                                    else
                                    {
                                        int currentItem = 0;
                                        ImGui.Combo("###HouseDisabled", ref currentItem, "(All Houses & Apartments)\0"u8);
                                    }
                                    ImGui.SameLine();
                                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 2.0f + ImGui.GetStyle().ItemSpacing.X / 2.0f);
                                    if (houseEnabled)
                                    {
                                        ImGui.InputInt("House & Room", ref _autoLoadNewRoomId);
                                    }
                                    else
                                    {
                                        string dummy = "(All Rooms)";
                                        ImGui.InputText("House & Room##Disabled", ref dummy);
                                    }
                                }
                            }
                        }

                        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add"))
                        {
                            _localDefinitionService.SetAutomaticShowConditions(_selectedLocalDefinitionFilename, selectedMetadata.AutomaticShowConditions.Append(new AutomaticShowCondition()
                            {
                                TerritoryId = _allTerritories[_autoLoadNewTerritoryIndex].Id,
                                WorldId = _autoLoadNewUseWorld ? _allWorlds[_autoLoadNewWorldIndex].Id : ushort.MaxValue,
                                WardId = (_autoLoadNewUseWorld && _autoLoadNewUseWard) ? (ushort)_autoLoadNewWardId : ushort.MaxValue,
                                DivisionId = (_autoLoadNewUseWorld && _autoLoadNewUseWard) ? (ushort)(_autoLoadNewDivisionIndex + 1) : ushort.MaxValue,
                                HouseId = (_autoLoadNewUseWorld && _autoLoadNewUseWard && _autoLoadNewUseHouse) ? (ushort)_autoLoadNewHouseId : ushort.MaxValue,
                                RoomId = (_autoLoadNewUseWorld && _autoLoadNewUseWard && _autoLoadNewUseHouse) ? (ushort)_autoLoadNewRoomId : ushort.MaxValue,
                            }));
                        }
                    }

                    ImGuiHelpers.ScaledDummy(3.0f);
                    ImGui.Separator();
                    ImGuiHelpers.ScaledDummy(3.0f);

                    string liveKey = LiveStageHelpers.MakeLocalStageKey(_selectedLocalDefinitionFilename);
                    bool isVisible = _liveStageService.TryGetLiveStage(liveKey, out _);

                    if (isVisible)
                    {
                        if (ImGui.Button("Hide"))
                        {
                            _liveStageService.TryDestroyLiveStage(liveKey);
                            _localStageService.SetManualVisibility(_selectedLocalDefinitionFilename, false);
                        }
                    }
                    else
                    {
                        if (ImGui.Button("Show"))
                        {
                            _localStageService.SetManualVisibility(_selectedLocalDefinitionFilename, true);
                            try
                            {
                                using (FileStream stream = new FileStream(_selectedLocalDefinitionFilename, FileMode.Open, FileAccess.Read))
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
                                _logger.LogError(ex, "Exception loading {path} to instantiate!", _selectedLocalDefinitionFilename);
                            }
                        }
                    }
                }
            }
        }
    }

    private void UseCurrentLocation()
    {
        if (Location.TryGetLocation(_clientState, _playerState, out var location))
        {
            for (int i = 0; i < _allWorlds.Count; i++)
            {
                if (_allWorlds[i].Id == location.WorldId)
                {
                    _autoLoadNewWorldIndex = i;
                }
            }

            for (int i = 0; i < _allTerritories.Count; i++)
            {
                if (_allTerritories[i].Id == location.TerritoryId)
                {
                    _autoLoadNewTerritoryIndex = i;
                }
            }

            if (location.WardId != -1)
            {
                _autoLoadNewWardId = location.WardId;
            }
            else
            {
                _autoLoadNewUseWard = false;
            }

            if (location.DivisionId != -1)
            {
                _autoLoadNewDivisionIndex = location.DivisionId - 1;
            }

            if (location.HouseId != -1)
            {
                _autoLoadNewHouseId = location.HouseId;
            }
            else
            {
                _autoLoadNewUseHouse = false;
            }

            if (location.RoomId != -1)
            {
                _autoLoadNewRoomId = location.RoomId;
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

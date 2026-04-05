using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Services;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Windows;

public record class AssetType(string DisplayName, string DisplayDescription, FontAwesomeIcon Icon)
{
    public static readonly AssetType<ResourceAssetInfo> MdlResource = new("Model Resource", ".mdl", FontAwesomeIcon.Cube);
    public static readonly AssetType<ResourceAssetInfo> AvfxResource = new("VFX Resource", ".avfx", FontAwesomeIcon.WandSparkles);
    public static readonly AssetType<ResourceAssetInfo> SgbResource = new("Shared Group Resource", ".sgb", FontAwesomeIcon.Archive);
}

public record class AssetType<TAssetInfo>(string DisplayName, string DisplayDescription, FontAwesomeIcon Icon) : AssetType(DisplayName, DisplayDescription, Icon);

public record class AssetInfo(string DisplayName, AssetType Type, string ID)
{
    public virtual void DrawProperties()
    { }
}

public record class ResourceAssetInfo(string DisplayName, AssetType Type, string GamePath) : AssetInfo(DisplayName, Type, GamePath)
{
    public override void DrawProperties()
    {
        base.DrawProperties();

        ImGui.LabelText("Game Path", GamePath);
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(GamePath);
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(GamePath);
                ImGui.Separator();
                ImGui.TextDisabled("Click to copy");
            }
        }
    }
}

public interface IAssetLibraryWindow : IHostedService
{
    public const FontAwesomeIcon Icon = FontAwesomeIcon.Cubes;

    bool IsOpen { get; }
    void Show();
    void Hide();
    void SetSelectionCallback<TAssetInfo>(string objectName, string propertyName, AssetType<TAssetInfo> assetType, Func<bool> stillValidCallback, Action<TAssetInfo> selectCallback);
}

internal class AssetLibraryWindow : Window, IAssetLibraryWindow
{
    private enum HoverPreviewMode
    {
        None,
        NearPlayer,
        EditorObject,
    }

    private record class AssetLibraryTab(string DisplayName, Action DrawAction);

    private record class PathCache(List<string> MdlPaths, List<string> AvfxPaths);

    private interface ISelectionCallback
    {
        string ObjectName { get; }
        string PropertyName { get; }
        AssetType AssetType { get; }
        bool IsValid { get; }
        bool TrySelect(AssetInfo asset);
    }

    private record class SelectionCallback<TAssetInfo>(string ObjectName, string PropertyName, AssetType<TAssetInfo> AssetType, Func<bool> StillValidCallback, Action<TAssetInfo> SelectedCallback) : ISelectionCallback
    {
        public bool IsValid => StillValidCallback.Invoke();

        public bool TrySelect(AssetInfo asset)
        {
            if (asset is TAssetInfo typedAsset && IsValid)
            {
                SelectedCallback.Invoke(typedAsset);
                return true;
            }
            else
            {
                return false;
            }
        }

        AssetType ISelectionCallback.AssetType => AssetType;
    }

    private readonly ILogger _logger;
    private readonly WindowSystem _windowSystem;

    private readonly AssetLibraryTab[] _allTabs;
    private ISelectionCallback? _activeSelectionCallback;

    private readonly ResourceAssetInfo[] _gameResources;

    private HoverPreviewMode _hoverPreviewMode = HoverPreviewMode.NearPlayer;
    private AssetInfo? _selectedAssetInfo;
    private string _gameResourcesFilter = "";

    bool IAssetLibraryWindow.IsOpen => base.IsOpen;

    public AssetLibraryWindow(ILogger<AssetLibraryWindow> logger, WindowSystem windowSystem)
        : base("Stagehand Asset Library")
    {
        _logger = logger;
        _windowSystem = windowSystem;

        _allTabs =
        [
            new("Game Resources", DrawGameResourcesTab),
            new("Housing", DrawHousingTab),
        ];

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new(720, 540),
        };
        SizeCondition = ImGuiCond.FirstUseEver;

        var pathCacheBytes = Properties.Resources.Paths;
        var pathCache = JsonSerializer.Deserialize<PathCache>(pathCacheBytes);
        if (pathCache != null)
        {
            _gameResources = new ResourceAssetInfo[pathCache.MdlPaths.Count + pathCache.AvfxPaths.Count];
            for (int i = 0; i < pathCache.MdlPaths.Count; i++)
            {
                _gameResources[i] = new ResourceAssetInfo(System.IO.Path.GetFileNameWithoutExtension(pathCache.MdlPaths[i]), AssetType.MdlResource, pathCache.MdlPaths[i]);
            }
            for (int i = 0; i < pathCache.AvfxPaths.Count; i++)
            {
                _gameResources[pathCache.MdlPaths.Count + i] = new ResourceAssetInfo(System.IO.Path.GetFileNameWithoutExtension(pathCache.AvfxPaths[i]), AssetType.AvfxResource, pathCache.AvfxPaths[i]);
            }
            _gameResources.Sort((x, y) => Utils.PathSorter.CurrentCultureIgnoreCase.Compare(x.GamePath, y.GamePath));
        }
        else
        {
            _logger.LogError("Failed to parse path cache!");
            _gameResources = Array.Empty<ResourceAssetInfo>();
        }
    }

    public void Show()
    {
        IsOpen = true;
        BringToFront();
    }

    public void Hide()
    {
        IsOpen = false;
    }

    public override void Draw()
    {
        if (_activeSelectionCallback != null && !_activeSelectionCallback.IsValid)
        {
            _activeSelectionCallback = null;
        }

        using (ImRaii.TabBar("AssetSourceTabs"))
        {
            foreach (var tab in _allTabs)
            {
                using (var tabItem = ImRaii.TabItem(tab.DisplayName))
                {
                    if (tabItem.Success)
                    {
                        var defaultCellPadding = ImGui.GetStyle().CellPadding;
                        using (ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(defaultCellPadding.X, 0.0f)))
                        using (ImRaii.Table($"###AssetTab{tab.DisplayName}", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.NoBordersInBodyUntilResize, ImGui.GetContentRegionAvail()))
                        {
                            ImGui.TableSetupColumn("outliner", ImGuiTableColumnFlags.WidthFixed, 400.0f);
                            ImGui.TableSetupColumn("selected", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                            ImGui.TableNextColumn();

                            tab.DrawAction.Invoke();

                            // Bottom bar is shared
                            var addMenuWidth = ImGui.CalcTextSize("Hover Preview").X + ImGui.GetStyle().FramePadding.X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
                            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - addMenuWidth);
                            ImGui.SetCursorPosY(ImGui.GetContentRegionMax().Y - ImGui.GetFrameHeight());
                            ImGui.SetNextItemWidth(addMenuWidth);
                            using (var addMenu = Utils.ImGuiExtensions.DropdownButton("###AssetPreviewMenu", "Hover Preview"))
                            {
                                if (ImGui.IsItemHovered())
                                {
                                    using (ImRaii.Tooltip())
                                    {
                                        ImGui.TextUnformatted("Options for previewing the hovered asset");
                                    }
                                }

                                if (addMenu.Success)
                                {
                                    if (ImGui.Selectable("Off", _hoverPreviewMode == HoverPreviewMode.None))
                                    {
                                        _hoverPreviewMode = HoverPreviewMode.None;
                                    }
                                    if (ImGui.IsItemHovered())
                                    {
                                        using (ImRaii.Tooltip())
                                        {
                                            ImGui.TextUnformatted("Don't preview the hovered asset in the game");
                                        }
                                    }

                                    if (ImGui.Selectable("Near Player", _hoverPreviewMode == HoverPreviewMode.NearPlayer))
                                    {
                                        _hoverPreviewMode = HoverPreviewMode.NearPlayer;
                                    }
                                    if (ImGui.IsItemHovered())
                                    {
                                        using (ImRaii.Tooltip())
                                        {
                                            ImGui.TextUnformatted("Preview the hovered asset near the player character");
                                        }
                                    }

                                    if (ImGui.Selectable("Editor Object", _hoverPreviewMode == HoverPreviewMode.EditorObject))
                                    {
                                        _hoverPreviewMode = HoverPreviewMode.EditorObject;
                                    }
                                    if (ImGui.IsItemHovered())
                                    {
                                        using (ImRaii.Tooltip())
                                        {
                                            ImGui.TextUnformatted("Preview the hovered asset on the object\nbeing edited, if possible");
                                        }
                                    }
                                }
                            }

                            ImGui.TableNextColumn();
                            if (_selectedAssetInfo != null)
                            {
                                Utils.ImGuiExtensions.PropertiesHeader(_selectedAssetInfo.DisplayName, _selectedAssetInfo.Type.DisplayName, _selectedAssetInfo.Type.Icon, _selectedAssetInfo.Type.DisplayDescription, out bool isNameHovered);

                                if (isNameHovered)
                                {
                                    using (ImRaii.Tooltip())
                                    {
                                        ImGui.TextUnformatted(_selectedAssetInfo.ID);
                                        ImGui.Separator();
                                        ImGui.TextDisabled("Click to copy");
                                    }
                                }

                                _selectedAssetInfo.DrawProperties();

                                if (_activeSelectionCallback != null)
                                {
                                    var assignText = $"Assign to {_activeSelectionCallback.ObjectName}'s {_activeSelectionCallback.PropertyName}";
                                    var assignWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.ArrowRight, assignText);
                                    ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - assignWidth);
                                    ImGui.SetCursorPosY(ImGui.GetContentRegionMax().Y - ImGui.GetFrameHeight());
                                    ImGui.SetNextItemWidth(assignWidth);
                                    using (ImRaii.Disabled(_activeSelectionCallback.AssetType != _selectedAssetInfo.Type))
                                    {
                                        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ArrowRight, assignText))
                                        {
                                            _activeSelectionCallback.TrySelect(_selectedAssetInfo);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    public void SetSelectionCallback<TAssetInfo>(string objectName, string propertyName, AssetType<TAssetInfo> assetType, Func<bool> stillValidCallback, Action<TAssetInfo> selectCallback)
    {
        _activeSelectionCallback = new SelectionCallback<TAssetInfo>(objectName, propertyName, assetType, stillValidCallback, selectCallback);
    }

    private void DrawGameResourcesTab()
    {
        Utils.ImGuiExtensions.FilterBox("Filter"u8, ref _gameResourcesFilter);

        float bottomBarHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2.0f;
        using (var listBox = ImRaii.ListBox("###GameResources", ImGui.GetContentRegionAvail() - new Vector2(0.0f, bottomBarHeight + ImGui.GetStyle().ItemSpacing.Y)))
        {
            if (listBox.Success)
            {
                const ImGuiTreeNodeFlags commonFlags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.AllowItemOverlap /* | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick */ | ImGuiTreeNodeFlags.FramePadding;

                string directory = "";
                int directoryDepth = 0;
                bool isInCollapsedDirectory = false;
                var defaultItemSpacing = ImGui.GetStyle().ItemSpacing;
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                {
                    foreach (var resource in _gameResources)
                    {
                        if (_gameResourcesFilter.Length > 0 && !resource.GamePath.Contains(_gameResourcesFilter, StringComparison.CurrentCultureIgnoreCase))
                        {
                            continue;
                        }

                        // Leave any directories that we are currently in that we shouldn't be in
                        while (directory.Length > 1 && !resource.GamePath.StartsWith(directory + '/') && ((directoryDepth > 0 || isInCollapsedDirectory)))
                        {
                            if (!isInCollapsedDirectory)
                            {
                                ImGui.TreePop();
                                directoryDepth -= 1;
                            }
                            isInCollapsedDirectory = false;
                            var priorSeparator = directory.LastIndexOf('/', directory.Length - 2);
                            directory = priorSeparator >= 0 ? directory.Substring(0, priorSeparator) : string.Empty;
                        }

                        // Enter any directories that are not already entered
                        int nextDirectorySeparator = resource.GamePath.IndexOf('/', directory.Length + 1);
                        bool isLeafVisible = !isInCollapsedDirectory;
                        while (!isInCollapsedDirectory && nextDirectorySeparator >= 0)
                        {
                            string subdirName = resource.GamePath.Substring(directory.Length > 0 ? directory.Length + 1 : 0, directory.Length > 0 ? nextDirectorySeparator - directory.Length - 1 : nextDirectorySeparator);

                            // The directory's treenode

                            bool enteredDirectory;
                            using (ImRaii.PushFont(UiBuilder.IconFont))
                            {
                                enteredDirectory = ImGui.TreeNodeEx($"{FontAwesomeIcon.Folder.ToIconString()}###{subdirName}", commonFlags);
                            }

                            ImGui.SameLine();
                            ImGui.TextUnformatted($"  {subdirName}");

                            if (directory.Length > 0)
                            {
                                directory += '/';
                            }
                            directory += subdirName;
                            if (enteredDirectory)
                            {
                                isInCollapsedDirectory = false;
                                directoryDepth += 1;
                                nextDirectorySeparator = resource.GamePath.IndexOf('/', directory.Length + 1);
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
                            using (ImRaii.PushFont(UiBuilder.IconFont))
                            using (var fileTreeNode = ImRaii.TreeNode($"{resource.Type.Icon.ToIconString()}###{resource.GamePath}", commonFlags | ImGuiTreeNodeFlags.Leaf | (resource == _selectedAssetInfo ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None)))
                            {
                                if (ImGui.IsItemClicked())
                                {
                                    _selectedAssetInfo = resource;
                                }

                                using (ImRaii.DefaultFont())
                                {
                                    if (ImGui.IsItemHovered())
                                    {
                                        using (ImRaii.Tooltip())
                                        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, defaultItemSpacing))
                                        {
                                            ImGui.TextUnformatted(resource.GamePath);
                                            ImGui.Separator();
                                            ImGui.TextDisabled(resource.Type.DisplayName);
                                        }
                                    }

                                    ImGui.SameLine();
                                    ImGui.TextUnformatted($"  {resource.DisplayName}");
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
        }
    }

    private void DrawHousingTab()
    {
        ImGui.TextDisabled("(Not yet implemented)");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _windowSystem.AddWindow(this);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _windowSystem.RemoveWindow(this);

        return Task.CompletedTask;
    }
}

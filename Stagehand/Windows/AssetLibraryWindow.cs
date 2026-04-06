using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Common.Lua;
using Microsoft.Extensions.Hosting;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Live;
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

/// <summary>
/// A type of asset in the asset library.
/// </summary>
/// <param name="DisplayName">The user-facing name of this asset type.</param>
/// <param name="DisplayDescription">The description of this asset type.</param>
/// <param name="Icon">The icon for this asset type.</param>
public record class AssetType(string DisplayName, string DisplayDescription, FontAwesomeIcon Icon)
{
    public static readonly AssetType<MdlResourceAssetInfo> MdlResource = new("Model Resource", ".mdl", FontAwesomeIcon.Cube);
    public static readonly AssetType<AvfxResourceAssetInfo> AvfxResource = new("VFX Resource", ".avfx", FontAwesomeIcon.WandSparkles);
    public static readonly AssetType<ResourceAssetInfo> SgbResource = new("Shared Group Resource", ".sgb", FontAwesomeIcon.Archive);
}

public record class AssetType<TAssetInfo>(string DisplayName, string DisplayDescription, FontAwesomeIcon Icon) : AssetType(DisplayName, DisplayDescription, Icon);

/// <summary>
/// The base class for information about an asset in the asset library.
/// </summary>
public record class AssetInfo(string DisplayName, AssetType Type, string ID)
{
    /// <summary>
    /// Draws the properties of this asset into the selected asset pane of the asset library.
    /// </summary>
    public virtual void DrawProperties()
    { }

    /// <summary>
    /// Creates a live object at the given location and rotation to preview this asset.
    /// </summary>
    public virtual ILiveObject? CreatePreviewObject(ILiveObjectService liveObjectService, Vector3 location, Quaternion rotation)
    {
        return null;
    }

    /// <summary>
    /// Creates a new object definition for adding this asset to a Stage definition.
    /// </summary>
    public virtual ObjectDefinition? CreateObjectDefinition(Vector3 location, Quaternion rotation)
    {
        return null;
    }
}

/// <summary>
/// Asset info for a resource in the game's files.
/// </summary>
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

/// <summary>
/// Asset info for a model resource (*.mdl)
/// </summary>
public record class MdlResourceAssetInfo(string DisplayName, AssetType Type, string GamePath) : ResourceAssetInfo(DisplayName, Type, GamePath)
{
    public override ILiveObject? CreatePreviewObject(ILiveObjectService liveObjectService, Vector3 location, Quaternion rotation)
    {
        return liveObjectService.CreateBgObject(GamePath, location, rotation, Vector3.One);
    }

    public override ObjectDefinition? CreateObjectDefinition(Vector3 location, Quaternion rotation)
    {
        return new BgObjectDefinition()
        {
            DisplayName = DisplayName,
            ModelGamePath = GamePath,
            Position = location,
            RotationQuaternion = rotation,
        };
    }
}

/// <summary>
/// Asset info for a VFX resource (*.avfx)
/// </summary>
public record class AvfxResourceAssetInfo(string DisplayName, AssetType Type, string GamePath) : ResourceAssetInfo(DisplayName, Type, GamePath)
{
    public override ILiveObject? CreatePreviewObject(ILiveObjectService liveObjectService, Vector3 location, Quaternion rotation)
    {
        return liveObjectService.CreateVfx(GamePath, location, rotation, Vector3.One, Vector4.One);
    }

    public override ObjectDefinition? CreateObjectDefinition(Vector3 location, Quaternion rotation)
    {
        return new VfxObjectDefinition()
        {
            DisplayName = DisplayName,
            VfxGamePath = GamePath,
            Position = location,
            RotationQuaternion = rotation,
        };
    }
}

/// <summary>
/// A window where users can browse the various assets available to them.
/// </summary>
public interface IAssetLibraryWindow : IHostedService
{
    /// <summary>
    /// The icon that represents the asset library window.
    /// </summary>
    public const FontAwesomeIcon Icon = FontAwesomeIcon.Cubes;

    /// <summary>
    /// Whether the Asset Library window is open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Raised when the user requests to create an object definition from an asset.
    /// </summary>
    /// <remarks>
    /// Whether this event has any handlers determines whether the Create button is visible.
    /// </remarks>
    event Action<ObjectDefinition> CreateObject;

    /// <summary>
    /// Shows the Asset Library window and brings it to the front of the window order.
    /// </summary>
    void Show();

    /// <summary>
    /// Hides the Asset Library window.
    /// </summary>
    void Hide();

    /// <summary>
    /// Sets the callback to use for assigning an asset from the Asset Library window.
    /// </summary>
    /// <typeparam name="TAssetInfo">The type of asset info that can be selected.</typeparam>
    /// <param name="objectName">The name of the object whose property will be set.</param>
    /// <param name="propertyName">The name of the property that the assignment will set.</param>
    /// <param name="assetType">The type of asset to select.</param>
    /// <param name="stillValidCallback">A callback to test whether this is still valid.</param>
    /// <param name="selectCallback">The callback to invoke to assign an asset.</param>
    void SetSelectionCallback<TAssetInfo>(string objectName, string propertyName, AssetType<TAssetInfo> assetType, Func<bool> stillValidCallback, Action<TAssetInfo> selectCallback);
}

internal class AssetLibraryWindow : Window, IAssetLibraryWindow
{
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
    private readonly ILiveObjectService _liveObjectService;
    private readonly IObjectTable _objectTable;
    private readonly IOverlayService _overlayService;
    private readonly StagehandConfiguration _configuration;
    private readonly WindowSystem _windowSystem;

    private readonly AssetLibraryTab[] _allTabs;
    private ISelectionCallback? _activeSelectionCallback;

    private readonly ResourceAssetInfo[] _gameResources;

    private HoverPreviewMode HoverPreviewMode
    {
        get;
        set
        {
            field = value;

            _configuration.AssetLibraryPreviewMode = value;
            _configuration.Save();

            if (value != HoverPreviewMode.NearPlayer)
            {
                _hoverPreviewObject?.Dispose();
                _hoverPreviewObject = null;
            }
        }
    }
    private AssetInfo? _selectedAssetInfo;
    private string _gameResourcesFilter = "";

    bool IAssetLibraryWindow.IsOpen => base.IsOpen;

    public event Action<ObjectDefinition>? CreateObject;

    private ILiveObject? _hoverPreviewObject;
    private AssetInfo? HoveredAssetInfo
    {
        get;
        set
        {
            if (field != value)
            {
                _hoverPreviewObject?.Dispose();
                _hoverPreviewObject = null;

                field = value;

                var localPlayer = _objectTable.LocalPlayer;
                if (HoverPreviewMode == HoverPreviewMode.NearPlayer && value != null && localPlayer != null)
                {
                    var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, localPlayer.Rotation);
                    _hoverPreviewObject = value.CreatePreviewObject(_liveObjectService, localPlayer.Position + Vector3.Transform(Vector3.UnitZ, rotation) * 2.0f, rotation);
                }
            }
        }
    }

    public AssetLibraryWindow(ILogger<AssetLibraryWindow> logger, ILiveObjectService liveObjectService, IObjectTable objectTable, IOverlayService overlayService, StagehandConfiguration configuration, WindowSystem windowSystem)
        : base("Stagehand Asset Library")
    {
        _logger = logger;
        _liveObjectService = liveObjectService;
        _objectTable = objectTable;
        _overlayService = overlayService;
        _configuration = configuration;
        _windowSystem = windowSystem;

        HoverPreviewMode = _configuration.AssetLibraryPreviewMode;

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
                _gameResources[i] = new MdlResourceAssetInfo(System.IO.Path.GetFileNameWithoutExtension(pathCache.MdlPaths[i]), AssetType.MdlResource, pathCache.MdlPaths[i]);
            }
            for (int i = 0; i < pathCache.AvfxPaths.Count; i++)
            {
                _gameResources[pathCache.MdlPaths.Count + i] = new AvfxResourceAssetInfo(System.IO.Path.GetFileNameWithoutExtension(pathCache.AvfxPaths[i]), AssetType.AvfxResource, pathCache.AvfxPaths[i]);
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
                                    if (ImGui.Selectable("Off", HoverPreviewMode == HoverPreviewMode.None))
                                    {
                                        HoverPreviewMode = HoverPreviewMode.None;
                                    }
                                    if (ImGui.IsItemHovered())
                                    {
                                        using (ImRaii.Tooltip())
                                        {
                                            ImGui.TextUnformatted("Don't preview the hovered asset in the game");
                                        }
                                    }

                                    if (ImGui.Selectable("Near Player", HoverPreviewMode == HoverPreviewMode.NearPlayer))
                                    {
                                        HoverPreviewMode = HoverPreviewMode.NearPlayer;
                                    }
                                    if (ImGui.IsItemHovered())
                                    {
                                        using (ImRaii.Tooltip())
                                        {
                                            ImGui.TextUnformatted("Preview the hovered asset near the player character");
                                        }
                                    }

#if false // TODO: Implement editor object preview
                                    if (ImGui.Selectable("Editor Object", HoverPreviewMode == HoverPreviewMode.EditorObject))
                                    {
                                        HoverPreviewMode = HoverPreviewMode.EditorObject;
                                    }
                                    if (ImGui.IsItemHovered())
                                    {
                                        using (ImRaii.Tooltip())
                                        {
                                            ImGui.TextUnformatted("Preview the hovered asset on the object\nbeing edited, if possible");
                                        }
                                    }
#endif
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

                                var createWidth = 0.0f;
                                if (CreateObject != null)
                                {
                                    createWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Plus, "Create");
                                }

                                ImGui.SetCursorPosY(ImGui.GetContentRegionMax().Y - ImGui.GetFrameHeight());
                                if (_activeSelectionCallback != null)
                                {
                                    var assignText = "Assign";
                                    var assignWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.ArrowRight, assignText);
                                    ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - assignWidth - (createWidth > 0 ? createWidth + ImGui.GetStyle().ItemInnerSpacing.X : 0.0f));
                                    ImGui.SetNextItemWidth(assignWidth);
                                    using (ImRaii.Disabled(_activeSelectionCallback.AssetType != _selectedAssetInfo.Type))
                                    {
                                        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ArrowRight, assignText))
                                        {
                                            _activeSelectionCallback.TrySelect(_selectedAssetInfo);
                                        }
                                        if (ImGui.IsItemHovered())
                                        {
                                            using (ImRaii.Tooltip())
                                            {
                                                ImGui.TextUnformatted($"Assign to {_activeSelectionCallback.ObjectName}'s {_activeSelectionCallback.PropertyName}");
                                            }
                                        }
                                    }
                                    if (CreateObject != null)
                                    {
                                        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                                    }
                                }
                                else
                                {
                                    ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - createWidth);
                                }

                                if (CreateObject != null)
                                {
                                    ImGui.SetNextItemWidth(createWidth);
                                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Create"))
                                    {
                                        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _objectTable.LocalPlayer?.Rotation ?? 0.0f);
                                        var newObjectDefinition = _selectedAssetInfo.CreateObjectDefinition((_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.Transform(Vector3.UnitZ, rotation) * 2.0f, rotation);
                                        if (newObjectDefinition != null)
                                        {
                                            if (_hoverPreviewObject != null)
                                            {
                                                _hoverPreviewObject.Dispose();
                                                _hoverPreviewObject = null;
                                            }

                                            CreateObject.Invoke(newObjectDefinition);
                                        }
                                    }
                                    if (ImGui.IsItemHovered())
                                    {
                                        using (ImRaii.Tooltip())
                                        {
                                            ImGui.TextUnformatted("Add this to the Stage being edited");
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

        AssetInfo? hoveredAsset = null;

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
                                        hoveredAsset = resource;

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

        HoveredAssetInfo = hoveredAsset;
    }

    private void DrawHousingTab()
    {
        ImGui.TextDisabled("(Not yet implemented)");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _windowSystem.AddWindow(this);
        _overlayService.DrawOverlays += this.DrawOverlays;

        return Task.CompletedTask;
    }

    private void DrawOverlays(IOverlayDrawContext context)
    {
        if (_hoverPreviewObject != null && _hoverPreviewObject.TryGetOrientedBounds(out var orientedBounds))
        {
            context.DrawBox(orientedBounds.Transform, orientedBounds.HalfExtents, 1.0f, new Vector4(0.9f, 0.9f, 0.9f, 0.5f));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _overlayService.DrawOverlays -= DrawOverlays;
        _windowSystem.RemoveWindow(this);

        if (_hoverPreviewObject != null)
        {
            _hoverPreviewObject.Dispose();
            _hoverPreviewObject = null;
        }

        return Task.CompletedTask;
    }
}

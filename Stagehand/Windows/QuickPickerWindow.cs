using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Microsoft.Extensions.Hosting;
using Stagehand.AssetLibrary;
using Stagehand.AssetLibrary.GameResources;
using Stagehand.Editor;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Stagehand.Windows;

public interface IQuickPickerWindow : IHostedService
{
    void Show();
}

internal class QuickPickerWindow : Window, IQuickPickerWindow, IDisposable
{
    private readonly ILogger _logger;
    private readonly IObjectTable _objectTable;
    private readonly IStagehandKeybinds _stagehandKeybinds;
    private readonly IViewportPickerService _viewportPickerService;
    private readonly IAssetBookmarkService _assetBookmarkService;
    private readonly IAssetLibraryWindow _assetLibraryWindow;
    private readonly IEditorService _editorService;
    private readonly IOverlayService _overlayService;
    private readonly WindowSystem _windowSystem;

    private bool _autoRefreshNearbyObjects = true;
    private DateTimeOffset _lastNearbyObjectsRefresh = DateTimeOffset.MinValue;
    private bool _nearbyUsesCamera = false;
    private List<PickedObjectInfo> _nearbyObjects = new();
    private readonly List<PickedObjectInfo> _recentPickedObjects = new();

    public PickedObjectInfo? SelectedObjectInfo { get; private set; } = null;
    public PickedObjectInfo? HoveredObjectInfo { get; private set; } = null;

    public bool IsExpanded { get; set; } = false;
    public bool IsMini => HoveredObjectInfo == null && SelectedObjectInfo == null && !IsExpanded;
    public IFolderBookmarkItem? SelectedBookmarkFolder { get; set; } = null;

    public QuickPickerWindow(ILogger<QuickPickerWindow> logger, IObjectTable objectTable, IStagehandKeybinds stagehandKeybinds, IViewportPickerService viewportPickerService, IAssetBookmarkService assetBookmarkService, IAssetLibraryWindow assetLibraryWindow, IEditorService editorService, IOverlayService overlayService, WindowSystem windowSystem)
        : base("Stagehand Quick Picker", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize)
    {
        _logger = logger;
        _objectTable = objectTable;
        _stagehandKeybinds = stagehandKeybinds;
        _viewportPickerService = viewportPickerService;
        _assetBookmarkService = assetBookmarkService;
        _assetLibraryWindow = assetLibraryWindow;
        _editorService = editorService;
        _overlayService = overlayService;
        _windowSystem = windowSystem;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _windowSystem.AddWindow(this);

        _stagehandKeybinds.ToggleQuickPickerWindow.Pressed += Toggle;
        _stagehandKeybinds.StartQuickPicking.Pressed += StartPicking;

        _overlayService.DrawOverlays += OnDrawOverlays;

        return Task.CompletedTask;
    }

    public void Show()
    {
        IsOpen = true;
        RequestFocus = true;
    }

    public override void PreDraw()
    {
        base.PreDraw();

        if (!IsMini)
        {
            ImGui.SetNextWindowSizeConstraints(new(400.0f * ImGuiHelpers.GlobalScale, 0.0f), new(float.PositiveInfinity, float.PositiveInfinity));
        }

        if (_viewportPickerService.IsPicking)
        {
            Flags |= ImGuiWindowFlags.NoInputs;
        }
        else
        {
            Flags &= ~ImGuiWindowFlags.NoInputs;
        }
    }

    public override void Draw()
    {
        // Drag handle and window background
        ImGui.PushClipRectFullScreen(ImGui.GetWindowDrawList());
        try
        {
            if (!IsMini)
            {
                // Manually paint the background so we can exclude the drag handle portion.
                // The window and the windowsystem unhelpfully hide their tint and blur params etc. But we can grab them from the Dalamud style!
                // ...at least I'm not reflecting, right?
                var verticalSpace = new Vector2(0.0f, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y);
                var style = (StyleModel.GetConfiguredStyle() as StyleModelV1) ?? StyleModelV1.Get();
                ImGuiHelpers.PrependBlurBehind(ImGui.GetWindowDrawList(), ImGui.GetWindowPos() + verticalSpace, ImGui.GetWindowPos() + ImGui.GetWindowSize(), float.Lerp(0.005f, style.WindowBlurStrength, 1.0f) * 14.0f, ImGui.GetStyle().WindowRounding, ImGui.IsWindowFocused() ? style.WindowBlurTintActive : style.WindowBlurTint, style.WindowBlurLuminosity, float.Lerp(0.09f, 1.0f, BgAlpha ?? 1.0f) * 0.17f);
                ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetWindowPos() + verticalSpace, ImGui.GetWindowPos() + ImGui.GetWindowSize(), ImGui.GetColorU32(ImGuiCol.WindowBg), ImGui.GetStyle().WindowRounding);
            }

            bool handleHovered = false;
            Vector2 handleStart;
            Vector2 handleEnd;
            if (IsMini)
            {
                ImGui.Dummy(new(ImGui.GetFrameHeight() * 3.0f + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetStyle().ItemInnerSpacing.X, ImGui.GetFrameHeight()));
                handleHovered = ImGui.IsItemHovered();
                handleStart = ImGui.GetItemRectMin();
                handleEnd = ImGui.GetItemRectMax();
            }
            else
            {
                ImGui.SetCursorPosX(0.0f);
                handleStart = ImGui.GetCursorPos() + ImGui.GetWindowPos();
                handleEnd = handleStart + new Vector2(ImGui.GetContentRegionAvail().X + ImGui.GetStyle().WindowPadding.X, ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y);
                ImGui.Dummy(new(1.0f, handleEnd.Y - handleStart.Y));
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().WindowPadding.Y);
                handleHovered = ImGui.GetMousePos().X >= handleStart.X
                    && ImGui.GetMousePos().Y >= handleStart.Y
                    && ImGui.GetMousePos().X < handleEnd.X
                    && ImGui.GetMousePos().Y < handleEnd.Y;
            }

            if (handleHovered)
            {
                ImGui.GetWindowDrawList().AddRectFilled(handleStart, handleEnd, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), ImGui.GetStyle().FrameRounding);
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted(WindowName);
                }
            }
        }
        finally
        {
            ImGui.PopClipRect();
        }

        // Pick button
        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], _viewportPickerService.IsPicking))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.EyeDropper, new((ImGui.GetFrameHeight() * 2.0f + ImGui.GetStyle().ItemSpacing.Y) / ImGuiHelpers.GlobalScale)))
            {
                if (_viewportPickerService.IsPicking)
                {
                    _viewportPickerService.CancelPicking();
                }
                else
                {
                    StartPicking();
                }
            }
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(_viewportPickerService.IsPicking ? "Stop Picking"u8 : "Start Picking"u8);
            }
        }

        var objectInfo = HoveredObjectInfo ?? SelectedObjectInfo;

        if (objectInfo != null)
        {
            ImGui.SameLine();
            using (ImRaii.Group())
            {
                ImGui.AlignTextToFramePadding();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetStyle().FramePadding.X); // Align with first iconbutton below
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextUnformatted(objectInfo.Icon.ToIconString());
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted(objectInfo.TypeName);
                    }
                }
                ImGui.SameLine();
                if (objectInfo.PrimaryResourcePath != null)
                {
                    ImGui.TextUnformatted(objectInfo.PrimaryResourcePath);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    }
                    using (var dragSource = ImRaii.DragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
                    {
                        if (dragSource.Success)
                        {
                            ImGui.SetDragDropPayload(GameResourceDragDrop.DataTypeId, GameResourceDragDrop.MakeGameResourcePayload(objectInfo.PrimaryResourcePath));
                            ImGui.TextUnformatted(objectInfo.PrimaryResourcePath);
                        }
                    }

                    if (ImGuiComponents.IconButton(FontAwesomeIcon.ExternalLinkSquareAlt, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                    {
                        if (_assetLibraryWindow.TrySelectGameResource(objectInfo.PrimaryResourcePath))
                        {
                            _assetLibraryWindow.Show();
                        }
                    }
                    if (ImGui.IsItemHovered())
                    {
                        using (ImRaii.Tooltip())
                        {
                            ImGui.TextUnformatted("Show Resource in Asset Library");
                        }
                    }
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200.0f);
                    if (SelectedBookmarkFolder?.IsDeleted ?? false)
                    {
                        SelectedBookmarkFolder = null;
                    }
                    using (var combo = ImRaii.Combo("###BookmarkFolderCombo", SelectedBookmarkFolder?.Name ?? "(Bookmarks)"))
                    {
                        if (combo.Success)
                        {
                            if (ImGui.Selectable("(Bookmarks)", SelectedBookmarkFolder == null))
                            {
                                SelectedBookmarkFolder = null;
                            }
                            void drawItems(IReadOnlyList<IBookmarkItem> items, int level)
                            {
                                foreach (var item in items)
                                {
                                    if (item is IFolderBookmarkItem folderItem)
                                    {
                                        if (ImGui.Selectable(folderItem.Name.PadLeft(folderItem.Name.Length + level * 2) + "###" + folderItem.Guid.ToString(), SelectedBookmarkFolder == folderItem))
                                        {
                                            SelectedBookmarkFolder = folderItem;
                                        }

                                        drawItems(folderItem.ChildItems, level + 1);
                                    }
                                }
                            }
                            drawItems(_assetBookmarkService.RootItemsSorted, level: 0);
                        }
                        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            SelectedBookmarkFolder = null;
                        }
                    }
                    if (ImGui.IsItemHovered())
                    {
                        using (ImRaii.Tooltip())
                        {
                            ImGui.TextUnformatted("Destination Bookmark Folder");
                            ImGui.Separator();
                            ImGui.TextDisabled("Right click to clear selection.");
                        }
                    }
                    ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Bookmark, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                    {
                        _ = _assetBookmarkService.CreateGameResourceBookmarkAsync(objectInfo.PrimaryResourcePath, SelectedBookmarkFolder);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        using (ImRaii.Tooltip())
                        {
                            ImGui.TextUnformatted($"Add Bookmark{(SelectedBookmarkFolder != null ? $" to {SelectedBookmarkFolder.Name}" : "")}");
                        }
                    }
                    ImGui.SameLine(0.0f, ImGui.GetStyle().ItemSpacing.X);
                    using (ImRaii.Disabled(_editorService.OpenEditorWindow == null))
                    {
                        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                        {
                            if (_editorService.OpenEditorWindow != null)
                            {
                                var newDefinition = objectInfo.CreateObjectDefinition();
                                if (newDefinition != null)
                                {
                                    _editorService.OpenEditorWindow.AddObject(newDefinition);
                                }
                            }
                        }
                    }
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        using (ImRaii.Tooltip())
                        {
                            ImGui.TextUnformatted("Place in Stage");
                        }
                    }
                }
                else
                {
                    ImGui.TextDisabled("(No resources for selection)");
                }
            }
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            using (ImRaii.Group())
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Times, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                {
                    SelectedObjectInfo = null;
                    HoveredObjectInfo = null;
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted("Clear Selection"u8);
                    }
                }
                if (ImGuiComponents.IconButton(IsExpanded ? FontAwesomeIcon.CaretUp : FontAwesomeIcon.CaretDown, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                {
                    IsExpanded = !IsExpanded;
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted(IsExpanded ? "Collapse"u8 : "Expand"u8);
                    }
                }
            }
        }
        else
        {
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            if (!IsMini)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetStyle().FramePadding.X);
                ImGui.TextDisabled("(Nothing picked)");
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
            }
            using (ImRaii.Group())
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Times, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                {
                    IsOpen = false;
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted("Close Quick Picker"u8);
                    }
                }
                if (ImGuiComponents.IconButton(IsExpanded ? FontAwesomeIcon.CaretUp : FontAwesomeIcon.CaretDown, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                {
                    IsExpanded = !IsExpanded;
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted(IsExpanded ? "Collapse"u8 : "Expand"u8);
                    }
                }
            }
        }

        if (IsExpanded)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            using (var tabStrip = ImRaii.TabBar("###QuickPickerTabs"u8))
            {
                if (tabStrip.Success)
                {
                    using (var nearbyTabItem = ImRaii.TabItem("Nearby"u8))
                    {
                        if (nearbyTabItem.Success)
                        {
                            DrawNearbyTab();
                        }
                    }

                    using (var recentTabItem = ImRaii.TabItem("Recent"u8))
                    {
                        if (recentTabItem.Success)
                        {
                            DrawRecentTab();
                        }
                    }

                    using (var detailsTabItem = ImRaii.TabItem("Details"u8))
                    {
                        if (detailsTabItem.Success)
                        {
                            DrawDetailsTab(objectInfo);
                        }
                    }
                }
            }
        }
    }

    private unsafe void RefreshNearbyObjects()
    {
        _lastNearbyObjectsRefresh = DateTimeOffset.Now;
        _nearbyObjects = _viewportPickerService.GetAllObjects(onlyKnown: true);
        var playerObject = _objectTable.LocalPlayer;
        Vector3 referencePoint = (_nearbyUsesCamera || playerObject == null) ? CameraManager.Instance()->CurrentCamera->Position : playerObject.Position;
        _nearbyObjects.Sort((a, b) => MathF.Sign(Vector3.DistanceSquared(a.Position, referencePoint) - Vector3.DistanceSquared(b.Position, referencePoint)));
    }

    private void DrawNearbyTab()
    {
        ImGui.Spacing();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SyncAlt, "Refresh Objects")
            || (_autoRefreshNearbyObjects && (DateTimeOffset.Now - _lastNearbyObjectsRefresh).TotalSeconds >= 5.0f))
        {
            RefreshNearbyObjects();
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("Refresh automatically"u8, ref _autoRefreshNearbyObjects) && _autoRefreshNearbyObjects)
        {
            RefreshNearbyObjects();
        }
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight() - 1.0f);
        if (ImGuiComponents.IconButton(_nearbyUsesCamera ? FontAwesomeIcon.Camera : FontAwesomeIcon.Female, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
        {
            _nearbyUsesCamera = !_nearbyUsesCamera;
            RefreshNearbyObjects();
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(_nearbyUsesCamera ? "Measuring Distance to Camera" : "Measuring Distance to Player");
                ImGui.Separator();
                ImGui.TextDisabled(_nearbyUsesCamera ? "Click to use distance to player." : "Click to use distance to camera.");
            }
        }

        if (!_viewportPickerService.IsPicking)
        {
            HoveredObjectInfo = null;
        }
        using (var listBox = ImRaii.ListBox("###NearbyList"u8, new Vector2(-1.0f, MathF.Max(ImGui.GetContentRegionAvail().Y, 200.0f * ImGuiHelpers.GlobalScale))))
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing * 1.5f))
        {
            if (listBox.Success)
            {
                foreach (var objectInfo in _nearbyObjects)
                {
                    if (ImGui.Selectable($"{objectInfo.Icon.ToIconString()}###{objectInfo.OriginalPointer}", objectInfo.OriginalPointer == SelectedObjectInfo?.OriginalPointer))
                    {
                        SelectedObjectInfo = objectInfo;
                        _recentPickedObjects.RemoveAll(obj => obj.OriginalPointer == objectInfo.OriginalPointer);
                        _recentPickedObjects.Add(objectInfo);
                    }
                    if (!_viewportPickerService.IsPicking && ImGui.IsItemHovered())
                    {
                        HoveredObjectInfo = objectInfo;
                    }
                    ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                    using (ImRaii.DefaultFont())
                    {
                        ImGui.TextUnformatted(objectInfo.PrimaryResourcePath ?? objectInfo.TypeName);
                    }
                }
            }
        }
    }

    private void DrawRecentTab()
    {
        if (!_viewportPickerService.IsPicking)
        {
            HoveredObjectInfo = null;
        }
        using (var listBox = ImRaii.ListBox("###RecentList", new Vector2(-1.0f, MathF.Max(ImGui.GetContentRegionAvail().Y, 200.0f * ImGuiHelpers.GlobalScale))))
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing * 1.5f))
        {
            if (listBox.Success)
            {
                for (int i = _recentPickedObjects.Count - 1; i >= 0; i--)
                {
                    var objectInfo = _recentPickedObjects[i];
                    if (ImGui.Selectable($"{objectInfo.Icon.ToIconString()}###{objectInfo.OriginalPointer}", objectInfo.OriginalPointer == SelectedObjectInfo?.OriginalPointer))
                    {
                        SelectedObjectInfo = objectInfo;
                    }
                    if (!_viewportPickerService.IsPicking && ImGui.IsItemHovered())
                    {
                        HoveredObjectInfo = objectInfo;
                    }
                    ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                    using (ImRaii.DefaultFont())
                    {
                        ImGui.TextUnformatted(objectInfo.PrimaryResourcePath ?? objectInfo.TypeName);
                    }
                }
            }
        }
    }

    private void DrawDetailsTab(PickedObjectInfo? objectInfo)
    {
        if (objectInfo != null)
        {
            ImGui.Spacing();
            Utils.ImGuiExtensions.PropertiesHeader(objectInfo.PrimaryResourcePath ?? objectInfo.TypeName, additionalSelectionCount: 0, objectInfo.TypeName, objectInfo.Icon, string.Empty, out bool isDisplayNameHovered);
            if (isDisplayNameHovered)
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted(objectInfo.ObjectType.ToString());
                }
            }

            // Uncomment the scrolling container if lights have too many properties to be manageable
            //using (var propertiesPanel = ImRaii.Child("###PropertiesPanel", new Vector2(-1.0f, MathF.Max(ImGui.GetContentRegionAvail().Y, 200.0f)), border: false))
            {
                //if (propertiesPanel.Success)
                {
                    using (ImRaii.ItemWidth(-ImGui.GetContentRegionAvail().X * 0.33f))
                    {
                        Vector3 position = objectInfo.Position;
                        ImGui.InputFloat3("Position"u8, ref position);
                        Vector3 rotation = QuaternionToPitchYawRollDegrees(objectInfo.Rotation);
                        ImGui.InputFloat3("Rotation"u8, ref rotation);
                        Vector3 scale = objectInfo.Scale;
                        ImGui.InputFloat3("Scale"u8, ref scale);
                        ImGui.Spacing();

                        if (objectInfo is PickedBgObjectInfo bgInfo)
                        {
                            ImGui.LabelText("Model"u8, bgInfo.ModelGamePath);
                            if (ImGui.IsItemHovered())
                            {
                                using (ImRaii.Tooltip())
                                {
                                    ImGui.TextUnformatted(bgInfo.ModelGamePath);
                                    ImGui.Separator();
                                    ImGui.TextDisabled("Click to copy");
                                }
                            }
                            if (ImGui.IsItemClicked())
                            {
                                ImGui.SetClipboardText(bgInfo.ModelGamePath);
                            }
                            ImGui.Spacing();
                            float transparency = bgInfo.Transparency;
                            ImGui.SliderFloat("Transparency"u8, ref transparency);
                            if (bgInfo.DyeColor != null)
                            {
                                var dyeColor = new Vector4(bgInfo.DyeColor.Value.R / 255.0f, bgInfo.DyeColor.Value.G / 255.0f, bgInfo.DyeColor.Value.B / 255.0f, bgInfo.DyeColor.Value.A / 255.0f);
                                dyeColor = dyeColor * dyeColor;
                                ImGui.ColorEdit4("Dye Color"u8, ref dyeColor, ImGuiColorEditFlags.NoPicker);
                            }
                        }
                        else if (objectInfo is PickedVfxObjectInfo vfxInfo)
                        {
                            ImGui.LabelText("VFX"u8, vfxInfo.VfxGamePath);
                            if (ImGui.IsItemHovered())
                            {
                                using (ImRaii.Tooltip())
                                {
                                    ImGui.TextUnformatted(vfxInfo.VfxGamePath);
                                    ImGui.Separator();
                                    ImGui.TextDisabled("Click to copy");
                                }
                            }
                            if (ImGui.IsItemClicked())
                            {
                                ImGui.SetClipboardText(vfxInfo.VfxGamePath);
                            }
                            ImGui.Spacing();
                            Vector4 color = vfxInfo.TintColor;
                            ImGui.ColorEdit4("Color", ref color, ImGuiColorEditFlags.NoPicker);
                        }
                    }
                }
            }
        }
        else
        {
            ImGui.Spacing();
            using (ImRaii.Disabled())
            {
                ImGuiHelpers.CenteredText("(Nothing selected)"u8);
            }
            ImGui.Spacing();
        }
    }

    private unsafe void OnDrawOverlays(IOverlayDrawContext drawContext)
    {
        var objectInfo = HoveredObjectInfo ?? SelectedObjectInfo;
        if (objectInfo != null && IsOpen)
        {
            void recurse(Object* obj)
            {
                if (obj == (Object*)objectInfo.OriginalPointer)
                {
                    var color = HoveredObjectInfo != null ? new Vector4(1.0f, 0.5f, 0.25f, 1.0f) : new Vector4(1.0f, 0.35f, 0.1f, 1.0f);
                    var type = obj->GetObjectType();
                    if (type == ObjectType.BgObject || type == ObjectType.VfxObject || type == ObjectType.CharacterBase || type == ObjectType.Decal || type == ObjectType.Light)
                    {
                        var drawObject = (DrawObject*)obj;
                        FFXIVClientStructs.FFXIV.Common.Math.OrientedBounds bounds = default;
                        drawObject->ComputeOrientedBounds(&bounds);

                        drawContext.DrawBox(bounds.Transform, bounds.HalfExtents, 1.0f, color);
                    }

                    // If we've found the one object to outline, no need to iterate any further
                    return;
                }

                foreach (var child in obj->ChildObjects)
                {
                    recurse(child);
                }
            }

            var world = World.Instance();
            if (world != null)
            {
                recurse((Object*)world);
            }
        }
    }

    public void StartPicking()
    {
        if (!IsOpen)
        {
            Show();
        }

        if (!_viewportPickerService.IsPicking)
        {
            _viewportPickerService.TryStartPicking(OnObjectHovered, OnObjectClicked, null);
        }
    }

    private void OnObjectHovered(PickedObjectInfo? objectInfo)
    {
        HoveredObjectInfo = objectInfo;
    }

    private void OnObjectClicked(PickedObjectInfo? objectInfo)
    {
        SelectedObjectInfo = objectInfo;

        if (objectInfo != null)
        {
            _recentPickedObjects.RemoveAll(obj => obj.OriginalPointer == objectInfo.OriginalPointer);
            _recentPickedObjects.Add(objectInfo);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _overlayService.DrawOverlays -= OnDrawOverlays;

        _stagehandKeybinds.ToggleQuickPickerWindow.Pressed -= Toggle;
        _stagehandKeybinds.StartQuickPicking.Pressed -= StartPicking;

        _windowSystem.RemoveWindow(this);

        return Task.CompletedTask;
    }

    public void Dispose()
    { }

    private static Vector3 QuaternionToPitchYawRollDegrees(Quaternion value)
    {
        // The formula I'm using for quat -> PYR is z-up, so swizzle the dimensions around
        float x = value.Z;
        float y = value.X;
        float z = value.Y;
        float w = value.W;

        var roll = MathF.Atan2((2 * w * x) + (2 * y * z), 1 - (2 * x * x) - (2 * y * y));
        var pitch = MathF.Asin((2 * w * y) - (2 * z * x));
        var yaw = MathF.Atan2((2 * w * z) + (2 * x * y), 1 - (2 * y * y) - (2 * z * z));

        return new Vector3(RadiansToDegrees(pitch), RadiansToDegrees(yaw), RadiansToDegrees(roll));
    }

    private static float RadiansToDegrees(float radians)
    {
        return radians * 180.0f / MathF.PI;
    }
}

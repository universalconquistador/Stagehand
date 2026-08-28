using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.AssetLibrary;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.DefinitionEditors;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Stagehand.Editor.Windows;

internal class EditorWindow : Window, IDisposable
{
    private static readonly TimeSpan _autosaveInterval = TimeSpan.FromSeconds(30.0f);

    private readonly IServiceScope _serviceScope;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IFramework _framework;
    private readonly IToolManager _toolManager;
    private readonly IOutliner _outliner;
    private readonly ISelectionManager _selectionManager;
    private readonly ITransactionManager _transactionManager;
    private readonly IObjectTable _objectTable;
    private readonly IAssetLibraryWindow _assetLibraryWindow;
    private readonly IStagehandKeybinds _stagehandKeybinds;
    private readonly StagehandConfiguration _stagehandConfiguration;

    public event Action? Closed;
    public event Action? Saved;

    private string _outlinerFilter = string.Empty;
    private bool _hasUnsavedChanges = false;
    private float _splitterRatio = -1.0f;

    private readonly string _definitionFilename;
    private readonly StageDefinition _definition;
    private readonly StageDefinitionEditor _definitionEditor;
    private readonly Timer _autosaveTimer;

    public EditorWindow(IServiceScope serviceScope, string definitionFilename, StageDefinition definition)
        : base($"{Path.GetFileName(definitionFilename)} - Stagehand Editor###StagehandEditor")
    {
        _serviceScope = serviceScope;
        _framework = _serviceScope.ServiceProvider.GetRequiredService<IFramework>();
        _pluginInterface = _serviceScope.ServiceProvider.GetRequiredService<IDalamudPluginInterface>();
        _toolManager = _serviceScope.ServiceProvider.GetRequiredService<IToolManager>();
        _outliner = _serviceScope.ServiceProvider.GetRequiredService<IOutliner>();
        _selectionManager = _serviceScope.ServiceProvider.GetRequiredService<ISelectionManager>();
        _transactionManager = _serviceScope.ServiceProvider.GetRequiredService<ITransactionManager>();
        _objectTable = _serviceScope.ServiceProvider.GetRequiredService<IObjectTable>();
        _assetLibraryWindow = _serviceScope.ServiceProvider.GetRequiredService<IAssetLibraryWindow>();
        _stagehandKeybinds = _serviceScope.ServiceProvider.GetRequiredService<IStagehandKeybinds>();
        _stagehandConfiguration = _serviceScope.ServiceProvider.GetRequiredService<StagehandConfiguration>();

        _definitionFilename = definitionFilename;
        _definition = definition;
        _definitionEditor = new StageDefinitionEditor(_serviceScope.ServiceProvider, definition);
        _outliner.RootNode = _definitionEditor.OutlinerNode;
        _selectionManager.SelectedEditor = _definitionEditor;
        _transactionManager.ClearHistory();
        _transactionManager.TransactionDone += OnTransactionDoneOrUndone;
        _transactionManager.TransactionUndone += OnTransactionDoneOrUndone;
        _assetLibraryWindow.CreateObject += OnAssetLibraryCreateObject;
        _autosaveTimer = new Timer(OnAutosaveTimerElapsed, null, _autosaveInterval, _autosaveInterval);

        ShowCloseButton = false;
        RespectCloseHotkey = false;
        _splitterRatio = _stagehandConfiguration.EditorSplitterRatio;

        _stagehandKeybinds.EditorUndo.Pressed += _transactionManager.Undo;
        _stagehandKeybinds.EditorRedo.Pressed += _transactionManager.Redo;
        _stagehandKeybinds.EditorSave.Pressed += SaveDefinition;
    }

    private void OnTransactionDoneOrUndone(ITransaction transaction)
    {
        if (transaction.AffectsDataModel)
        {
            _hasUnsavedChanges = true;
        }
    }

    private void OnAssetLibraryCreateObject(ObjectDefinition newObjectDefinition)
    {
        _definitionEditor.Objects.Add(newObjectDefinition);
    }

    private void OnAutosaveTimerElapsed(object? _)
    {
        // Running on the main thread is a scuffed way of synchronization
        _framework.Run(() =>
        {
            if (_hasUnsavedChanges)
            {
                var autosaveRoot = _stagehandConfiguration.FinalAutosavePath;
                var autosavePath = Path.Combine(autosaveRoot, _definitionFilename.Substring(_stagehandConfiguration.DefinitionLibraryPath.Length + 1));

                var autosaveDirectory = Path.GetDirectoryName(autosavePath);
                if (autosaveDirectory != null)
                {
                    Directory.CreateDirectory(autosaveDirectory);
                }

                TryWriteDefinition(autosavePath);
            }
        });
    }

    private void SaveDefinition()
    {
        if (TryWriteDefinition(_definitionFilename))
        {
            _hasUnsavedChanges = false;
        }
    }

    private bool TryWriteDefinition(string filename)
    {
        try
        {
            using (var stream = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                _definition.WriteToJSONStream(stream);
            }
            if (filename == _definitionFilename)
            {
                Saved?.Invoke();
            }
            return true;
        }
        catch (Exception ex)
        {
            // TODO: Log failure!
            return false;
        }
    }

    public unsafe override void Draw()
    {
        _outliner.Update();

        int buttonSize = (int)(32.0f * ImGuiHelpers.GlobalScale);

        // Commands
        if (ImGuiComponents.IconButton("###Save", FontAwesomeIcon.Save, new Vector2(buttonSize / ImGuiHelpers.GlobalScale)))
        {
            SaveDefinition();
        }
        ImGui.SameLine();
        using (ImRaii.Disabled(_transactionManager.UndoTransactionTitle == null))
        {
            if (ImGuiComponents.IconButton("###Undo", FontAwesomeIcon.Undo, new Vector2(buttonSize / ImGuiHelpers.GlobalScale)))
            {
                _transactionManager.Undo();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            using (ImRaii.Tooltip())
            {
                if (_transactionManager.UndoTransactionTitle != null)
                {
                    ImGui.TextUnformatted($"Undo {_transactionManager.UndoTransactionTitle}");
                }
                else
                {
                    ImGui.TextUnformatted("Nothing to undo");
                }
            }
        }
        ImGui.SameLine();
        using (ImRaii.Disabled(_transactionManager.RedoTransactionTitle == null))
        {
            if (ImGuiComponents.IconButton("###Redo", FontAwesomeIcon.Redo, new Vector2(buttonSize / ImGuiHelpers.GlobalScale)))
            {
                _transactionManager.Redo();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            using (ImRaii.Tooltip())
            {
                if (_transactionManager.RedoTransactionTitle != null)
                {
                    ImGui.TextUnformatted($"Redo {_transactionManager.RedoTransactionTitle}");
                }
                else
                {
                    ImGui.TextUnformatted("Nothing to redo");
                }
            }
        }
        ImGui.SameLine();
        if (ImGui.GetCursorPosX() < ImGui.GetContentRegionMax().X - buttonSize)
        {
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - buttonSize);
        }
        using (ImRaii.Disabled(_hasUnsavedChanges && !ImGui.IsKeyDown(ImGuiKey.LeftCtrl)))
        {
            if (ImGuiComponents.IconButton("###Close", FontAwesomeIcon.Times, new Vector2(buttonSize / ImGuiHelpers.GlobalScale)))
            {
                IsOpen = false;
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            using (ImRaii.Tooltip())
            {
                if (!_hasUnsavedChanges)
                {
                    ImGui.TextUnformatted("Close editor (all saved)");
                }
                else
                {
                    ImGui.TextUnformatted("Close editor (discard unsaved changes)");
                    ImGui.Separator();
                    ImGui.TextColored(ImGuiColors.DPSRed, "Hold CTRL to enable.");
                }
            }
        }

        ImGui.Separator();

        // Tools
        var toolsPerRow = (int)(MathF.Floor((ImGui.GetContentRegionAvail().X + ImGui.GetStyle().ItemSpacing.X) / (buttonSize + ImGui.GetStyle().ItemSpacing.X)));
        if (toolsPerRow <= 0)
        {
            toolsPerRow = 1;
        }
        int toolIndex = 0;
        foreach (var tool in _toolManager.Tools)
        {
            if (toolIndex % toolsPerRow != 0 && toolIndex > 0)
            {
                ImGui.SameLine();
            }

            if (ImGuiComponents.IconButton(tool.DisplayName, tool.Icon, size: new Vector2(buttonSize / ImGuiHelpers.GlobalScale),
                defaultColor: tool.IsActive ? *ImGui.GetStyleColorVec4(ImGuiCol.ButtonActive) : null))
            {
                _toolManager.ActiveTool = tool;
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(tool.DisplayName);
                    if (tool.Description != "")
                    {
                        ImGui.Separator();
                        ImGui.TextDisabled(tool.Description);
                    }
                }
            }

            toolIndex += 1;
        }

        ImGui.Separator();

        // Object Outliner
        var clearFilterWidth = ImGui.GetFrameHeight();
        bool showClearFilter = _outlinerFilter.Length > 0;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (showClearFilter ? clearFilterWidth + ImGui.GetStyle().ItemInnerSpacing.X : 0.0f));
        if (ImGui.InputTextWithHint("###OutlinerFilter", "Filter", ref _outlinerFilter, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
        {
            _outliner.FilterText = _outlinerFilter;
        }

        if (showClearFilter)
        {
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            if (ImGuiComponents.IconButton("###OutlinerFilterClear", FontAwesomeIcon.Times, new Vector2(clearFilterWidth / ImGuiHelpers.GlobalScale)))
            {
                _outliner.FilterText = string.Empty;
                _outlinerFilter = string.Empty;
            }
        }

        float splitterRatio = _splitterRatio > 0.0f ? _splitterRatio : 0.5f;
        float splitterAvailable = ImGui.GetContentRegionAvail().Y;
        float outlinerHeight = splitterAvailable * splitterRatio;
        using (var outlinerListBox = ImRaii.ListBox("###Outliner", new Vector2(-1.0f, outlinerHeight)))
        {
            if (outlinerListBox.Success)
            {
                var itemSpacing = ImGui.GetStyle().ItemSpacing;
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                {
                    if (_outliner.RootNode != null)
                    {
                        using (ImRaii.PushId("###RootNode"))
                        {
                            DrawOutlinerNode(_outliner.RootNode, itemSpacing);
                        }
                    }
                }
            }
        }

        var addMenuWidth = 75.0f * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - addMenuWidth);
        ImGui.SetNextItemWidth(addMenuWidth);
        using (var addMenu = Utils.ImGuiExtensions.DropdownButton("###AddMenu", "Create"))
        {
            if (addMenu.Success)
            {
                DrawCreateMenuItem(EmbeddedModpackDefinitionEditor.StaticTypeInfo, EmbeddedModpackDefinitionEditor.StaticTypeInfo.DisplayName, _definitionEditor.EmbeddedModpacks, () => new EmbeddedModpackDefinition()
                {
                    DisplayName = "New Modpack",
                });
                ImGui.Separator();

                DrawCreateMenuItem(BgObjectDefinitionEditor.StaticTypeInfo, BgObjectDefinitionEditor.StaticTypeInfo.DisplayName, _definitionEditor.Objects, () => new BgObjectDefinition()
                {
                    DisplayName = $"New {BgObjectDefinitionEditor.StaticTypeInfo.DisplayName}",
                    ModelGamePath = "bgcommon/world/aet/001/bgparts/w_aet_001_04a.mdl",
                    Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY
                });
                static Quaternion GetCameraQuaternion(Matrix4x4 matrix)
                {
                    Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation);
                    return Quaternion.Inverse(rotation) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
                }
                DrawCreateMenuItem(VfxObjectDefinitionEditor.StaticTypeInfo, VfxObjectDefinitionEditor.StaticTypeInfo.DisplayName, _definitionEditor.Objects, () => new VfxObjectDefinition()
                {
                    DisplayName = $"New {VfxObjectDefinitionEditor.StaticTypeInfo.DisplayName}",
                    VfxGamePath = "bgcommon/world/common/vfx_for_event/eff/b0150_eext_y.avfx",
                    Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY
                });
                DrawCreateMenuItem(WeaponDefinitionEditor.StaticTypeInfo, WeaponDefinitionEditor.StaticTypeInfo.DisplayName, _definitionEditor.Objects, () => new WeaponDefinition()
                {
                    DisplayName = $"New {WeaponDefinitionEditor.StaticTypeInfo.DisplayName}",
                    Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY
                });
                ImGui.Separator();
                DrawCreateMenuItem(LightDefinitionEditor.StaticTypeInfo, "Ambient Light", _definitionEditor.Objects, () => new LightDefinition()
                {
                    DisplayName = $"New Ambient Light",
                    Position = (CameraManager.Instance()->CurrentCamera->Position),
                    Shape = LightShape.Ambient,
                });
                DrawCreateMenuItem(LightDefinitionEditor.StaticTypeInfo, "Point Light", _definitionEditor.Objects, () => new LightDefinition()
                {
                    DisplayName = $"New Point Light",
                    Position = (CameraManager.Instance()->CurrentCamera->Position),
                    Shape = LightShape.Point,
                });
                DrawCreateMenuItem(LightDefinitionEditor.StaticTypeInfo, "Spot Light", _definitionEditor.Objects, () => new LightDefinition()
                {
                    DisplayName = $"New Spot Light",
                    Position = (CameraManager.Instance()->CurrentCamera->Position),
                    RotationQuaternion = GetCameraQuaternion(CameraManager.Instance()->CurrentCamera->ViewMatrix),
                    Shape = LightShape.Spot,
                });
                DrawCreateMenuItem(LightDefinitionEditor.StaticTypeInfo, "Flat Light", _definitionEditor.Objects, () => new LightDefinition()
                {
                    DisplayName = $"New Flat Light",
                    Position = (CameraManager.Instance()->CurrentCamera->Position),
                    RotationQuaternion = GetCameraQuaternion(CameraManager.Instance()->CurrentCamera->ViewMatrix),
                    Shape = LightShape.Flat,
                });
                ImGui.Separator();
                DrawCreateMenuItem(SoundObjectDefinitionEditor.StaticTypeInfo, SoundObjectDefinitionEditor.StaticTypeInfo.DisplayName, _definitionEditor.Objects, () => new SoundObjectDefinition()
                {
                    DisplayName = $"New {SoundObjectDefinitionEditor.StaticTypeInfo.DisplayName}",
                    SoundGamePath = "bgcommon/sound/hou/hou_spot_fall_small_new.scd",
                    Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY
                });
            }
        }

        ImGui.InvisibleButton("###HSplitter", new Vector2(-1.0f, 8.0f));
        if (ImGui.IsItemActive())
        {
            ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int)ImGuiCol.ScrollbarGrabHovered]));
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
            var newHeight = outlinerHeight + ImGui.GetIO().MouseDelta.Y;
            _splitterRatio = newHeight / splitterAvailable;
            if (_splitterRatio < 0.25f)
            {
                _splitterRatio = 0.25f;
            }
            if (_splitterRatio > 0.75f)
            {
                _splitterRatio = 0.75f;
            }
        }
        else if (ImGui.IsItemHovered())
        {
            ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int)ImGuiCol.ScrollbarGrab]));
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        }

        ImGui.Separator();

        // Properties
        if (_selectionManager.SelectedEditor != null)
        {
            ImGuiHelpers.ScaledDummy(1);
            Utils.ImGuiExtensions.PropertiesHeader(_selectionManager.SelectedEditor.DisplayName, _selectionManager.SelectedEditor.TypeInfo.DisplayName, _selectionManager.SelectedEditor.TypeInfo.Icon, _selectionManager.SelectedEditor.TypeInfo.Description, out _);
            using (var propertiesPanel = ImRaii.Child("###PropertiesPanel", ImGui.GetContentRegionAvail(), border: false))
            {
                if (propertiesPanel.Success)
                {
                    using (ImRaii.ItemWidth(-ImGui.GetContentRegionAvail().X * 0.33f))
                    {
                        _selectionManager.SelectedEditor.DrawProperties();
                    }
                }
            }
        }
        else
        {
            const string message = "Nothing selected.";
            var textSize = ImGui.CalcTextSize(message);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X / 2.0f - textSize.X / 2.0f);
            ImGui.TextDisabled(message);
        }
    }

    private void DrawCreateMenuItem<TDefinition, TEditor>(DefinitionTypeInfo typeInfo, string typeName, DefinitionEditorDictionary<TDefinition, TEditor> collection, Func<TDefinition> newObjectFactory)
        where TEditor : class, IChildDefinitionEditor
    {
        bool selected;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            selected = ImGui.Selectable($"{typeInfo.Icon.ToIconString()}###Create{typeName}");
        }
        bool hovered = ImGui.IsItemHovered();
        ImGui.SameLine();
        ImGui.TextUnformatted($" {typeName}");

        if (hovered)
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(typeInfo.Description);
            }
        }

        if (selected)
        {
            var newObject = newObjectFactory.Invoke();
            collection.Add(newObject);
        }
    }

    private void DrawOutlinerNode(OutlinerNode node, Vector2 originalItemSpacing)
    {
        if (!node.IsVisibleWithFilter)
        {
            return;
        }

        var flags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.FramePadding;

        if (node.ParentNode == null)
        {
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        }

        if (!node.ChildNodes.Any(n => n.IsVisibleWithFilter))
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }

        if (node.IsSelected)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        bool showNodeTooltip = false;
        string tooltipPrimary = node.TooltipPrimary;
        string tooltipSecondary = node.TooltipSecondary;
        ImRaii.TreeNodeDisposable treeNode;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            treeNode = ImRaii.TreeNode($"{node.Icon.ToIconString()}###{node.DisplayName}", flags);
        }
        using (treeNode)
        {
            showNodeTooltip = ImGui.IsItemHovered();
            bool nodeLeftClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
            bool nodeRightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

            ImGui.SameLine();

            using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text] * new Vector4(1.0f, 1.0f, 1.0f, 0.7f), node.IsHiddenByParent || (node.IsVisible == false)))
            {
                ImGui.TextUnformatted($" {node.DisplayName}");
            }

            var localVisibility = node.IsVisible;
            if (localVisibility != null)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text] * new Vector4(1.0f, 1.0f, 1.0f, 0.7f), node.IsHiddenByParent))
                {
                    FontAwesomeIcon visibilityButtonIcon = localVisibility.Value ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash;
                    using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
                    using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0.0f))
                    using (ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero))
                    {
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
                        if (ImGui.Button(visibilityButtonIcon.ToIconString(), new Vector2(ImGui.GetFrameHeight())))
                        {
                            node.RaiseIsVisibleClicked();
                        }
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        {
                            nodeLeftClicked = false;
                        }
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            nodeRightClicked = false;
                        }
                        if (ImGui.IsItemHovered())
                        {
                            showNodeTooltip = true;
                            tooltipPrimary = localVisibility.Value ? "Hide" : "Show";
                            tooltipSecondary = "Hidden objects have no effect in the Stage.";
                        }
                    }
                }
            }

            if (nodeLeftClicked)
            {
                node.RaiseClicked();
            }
            if (nodeRightClicked)
            {
                node.RaiseClicked();
                if (node.ContextMenuItems != null && node.ContextMenuItems.Any())
                {
                    ImGui.OpenPopup("###ContextMenu");
                }
            }

            ImGui.SetNextWindowSizeConstraints(new Vector2(200.0f, 0.0f), new Vector2(float.MaxValue, float.MaxValue));
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(8.0f)))
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8.0f)))
            using (var contextMenu = ImRaii.Popup("###ContextMenu"))
            {
                if (contextMenu.Success)
                {
                    if (node.ContextMenuItems != null)
                    {
                        foreach (var item in node.ContextMenuItems)
                        {
                            if (ImGui.MenuItem(item.DisplayName, item.KeybindString))
                            {
                                item.RaiseClicked(node);
                            }
                        }
                    }
                }
            }

            if (treeNode.Success)
            {
                var i = 0;
                // The treenodes might have commands that add or remove children, so make a copy of the list
                var children = node.ChildNodes.ToArray();
                foreach (var child in children)
                {
                    using (ImRaii.PushId($"Child{i}-{child.DisplayName}"))
                    {
                        DrawOutlinerNode(child, originalItemSpacing);
                        i += 1;
                    }
                }
            }
        }
        if (showNodeTooltip && !string.IsNullOrEmpty(node.TooltipPrimary))
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, originalItemSpacing))
            using (ImRaii.Tooltip())
            using (ImRaii.PushFont(UiBuilder.DefaultFont))
            using (ImRaii.TextWrapPos(250.0f * ImGuiHelpers.GlobalScale))
            {
                ImGui.TextWrapped(tooltipPrimary);
                if (!string.IsNullOrEmpty(tooltipSecondary))
                {
                    ImGui.Separator();
                    using (ImRaii.Disabled())
                    {
                        ImGui.TextWrapped(tooltipSecondary);
                    }
                }
            }
        }
    }

    public override void OnClose()
    {
        base.OnClose();

        _stagehandConfiguration.EditorSplitterRatio = _splitterRatio;
        _stagehandConfiguration.Save();

        Closed?.Invoke();
    }

    public void Dispose()
    {
        _stagehandKeybinds.EditorUndo.Pressed -= _transactionManager.Undo;
        _stagehandKeybinds.EditorRedo.Pressed -= _transactionManager.Redo;
        _stagehandKeybinds.EditorSave.Pressed -= SaveDefinition;

        _autosaveTimer.Dispose();
        _transactionManager.TransactionUndone -= OnTransactionDoneOrUndone;
        _transactionManager.TransactionDone -= OnTransactionDoneOrUndone;
        _assetLibraryWindow.CreateObject -= OnAssetLibraryCreateObject;
        _definitionEditor.Dispose();
        _serviceScope.Dispose();
    }
}

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.DefinitionEditors;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Windows;
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
    private readonly StagehandConfiguration _stagehandConfiguration;

    public event Action? Closed;

    private string _outlinerFilter = string.Empty;
    private bool _hasUnsavedChanges = false;

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
        if (ImGuiComponents.IconButton("###Save", FontAwesomeIcon.Save, new Vector2(buttonSize, buttonSize)))
        {
            SaveDefinition();
        }
        ImGui.SameLine();
        using (ImRaii.Disabled(_transactionManager.UndoTransactionTitle == null))
        {
            if (ImGuiComponents.IconButton("###Undo", FontAwesomeIcon.Undo, new Vector2(buttonSize, buttonSize)))
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
            if (ImGuiComponents.IconButton("###Redo", FontAwesomeIcon.Redo, new Vector2(buttonSize, buttonSize)))
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
            if (ImGuiComponents.IconButton("###Close", FontAwesomeIcon.Times, new Vector2(buttonSize, buttonSize)))
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

            if (ImGuiComponents.IconButton(tool.DisplayName, tool.Icon, size: new Vector2(buttonSize, buttonSize),
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
            if (ImGuiComponents.IconButton("###OutlinerFilterClear", FontAwesomeIcon.Times, new Vector2(clearFilterWidth, clearFilterWidth)))
            {
                _outliner.FilterText = string.Empty;
                _outlinerFilter = string.Empty;
            }
        }

        using (var outlinerListBox = ImRaii.ListBox("###Outliner", ImGui.GetContentRegionAvail() * new Vector2(1.0f, 0.5f)))
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
                using (ImRaii.Disabled())
                {
                    if (ImGui.Selectable("Folder"))
                    {
                        // TODO: Implement!
                    }
                }
                ImGui.Separator();

                DrawCreateMenuItem(BgObjectDefinitionEditor.StaticTypeInfo, _definitionEditor.Objects, () => new BgObjectDefinition()
                {
                    DisplayName = $"New {BgObjectDefinitionEditor.StaticTypeInfo.DisplayName}",
                    ModelGamePath = "bgcommon/world/aet/001/bgparts/w_aet_001_04a.mdl",
                    Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY
                });
                DrawCreateMenuItem(LightDefinitionEditor.StaticTypeInfo, _definitionEditor.Objects, () => new LightDefinition()
                {
                    DisplayName = $"New {LightDefinitionEditor.StaticTypeInfo.DisplayName}",
                    Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY
                });
                DrawCreateMenuItem(VfxObjectDefinitionEditor.StaticTypeInfo, _definitionEditor.Objects, () => new VfxObjectDefinition()
                {
                    DisplayName = $"New {VfxObjectDefinitionEditor.StaticTypeInfo.DisplayName}",
                    VfxGamePath = "bgcommon/world/common/vfx_for_event/eff/b0150_eext_y.avfx",
                    Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY
                });
                DrawCreateMenuItem(WeaponDefinitionEditor.StaticTypeInfo, _definitionEditor.Objects, () => new WeaponDefinition()
                {
                    DisplayName = $"New {WeaponDefinitionEditor.StaticTypeInfo.DisplayName}",
                    Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY
                });
            }
        }

        ImGui.Separator();

        // Properties
        if (_selectionManager.SelectedEditor != null)
        {
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

    private void DrawCreateMenuItem<TDefinition, TEditor>(DefinitionTypeInfo typeInfo, DefinitionEditorDictionary<TDefinition, TEditor> collection, Func<TDefinition> newObjectFactory)
        where TEditor : class, IChildDefinitionEditor
    {
        bool selected;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            selected = ImGui.Selectable($"{typeInfo.Icon.ToIconString()}###Create{typeInfo.DisplayName}");
        }
        bool hovered = ImGui.IsItemHovered();
        ImGui.SameLine();
        ImGui.TextUnformatted($" {typeInfo.DisplayName}");

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

        bool hovered = false;
        ImRaii.TreeNodeDisposable treeNode;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            treeNode = ImRaii.TreeNode($"{node.Icon.ToIconString()}###{node.DisplayName}", flags);
        }
        using (treeNode)
        {
            hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                node.RaiseClicked();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                node.RaiseClicked();
                if (node.ContextMenuItems != null && node.ContextMenuItems.Any())
                {
                    ImGui.OpenPopup("###ContextMenu");
                }
            }

            ImGui.SameLine();
            ImGui.TextUnformatted($" {node.DisplayName}");

            ImGui.SetNextWindowSizeConstraints(new Vector2(200.0f, 0.0f), new Vector2(float.MaxValue, float.MaxValue));
            using (var contextMenu = ImRaii.Popup("###ContextMenu"))
            {
                if (contextMenu.Success)
                {
                    if (node.ContextMenuItems != null)
                    {
                        foreach (var item in node.ContextMenuItems)
                        {
                            if (ImGui.Selectable(item.DisplayName))
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
        if (hovered && !string.IsNullOrEmpty(node.TooltipPrimary))
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, originalItemSpacing))
            using (ImRaii.Tooltip())
            using (ImRaii.PushFont(UiBuilder.DefaultFont))
            using (ImRaii.TextWrapPos(250.0f * ImGuiHelpers.GlobalScale))
            {
                ImGui.TextWrapped(node.TooltipPrimary);
                if (!string.IsNullOrEmpty(node.TooltipSecondary))
                {
                    ImGui.Separator();
                    using (ImRaii.Disabled())
                    {
                        ImGui.TextWrapped(node.TooltipSecondary);
                    }
                }
            }
        }
    }

    public override void OnClose()
    {
        base.OnClose();

        Closed?.Invoke();
    }

    public void Dispose()
    {
        _autosaveTimer.Dispose();
        _transactionManager.TransactionUndone -= OnTransactionDoneOrUndone;
        _transactionManager.TransactionDone -= OnTransactionDoneOrUndone;
        _assetLibraryWindow.CreateObject -= OnAssetLibraryCreateObject;
        _definitionEditor.Dispose();
        _serviceScope.Dispose();
    }
}

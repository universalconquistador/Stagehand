using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.DefinitionEditors;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.Windows;

internal class EditorWindow : Window, IDisposable
{
    private readonly IServiceScope _serviceScope;
    private readonly IToolManager _toolManager;
    private readonly IOutliner _outliner;
    private readonly ISelectionManager _selectionManager;
    private readonly IObjectTable _objectTable;

    public event Action? Closed;

    private string _outlinerFilter = string.Empty;

    private readonly string _definitionFilename;
    private readonly StageDefinition _definition;
    private readonly StageDefinitionEditor _definitionEditor;

    public EditorWindow(IServiceScope serviceScope, string definitionFilename, StageDefinition definition)
        : base($"{Path.GetFileName(definitionFilename)} - Stagehand Editor###StagehandEditor")
    {
        _serviceScope = serviceScope;
        _toolManager = _serviceScope.ServiceProvider.GetRequiredService<IToolManager>();
        _outliner = _serviceScope.ServiceProvider.GetRequiredService<IOutliner>();
        _selectionManager = _serviceScope.ServiceProvider.GetRequiredService<ISelectionManager>();
        _objectTable = _serviceScope.ServiceProvider.GetRequiredService<IObjectTable>();

        _definitionFilename = definitionFilename;
        _definition = definition;
        _definitionEditor = new StageDefinitionEditor(_serviceScope.ServiceProvider, definition);
        _outliner.RootNode = _definitionEditor.OutlinerNode;
    }

    private void SaveDefinition()
    {
        try
        {
            using (var stream = new FileStream(_definitionFilename, FileMode.Create, FileAccess.Write))
            {
                _definition.WriteToJSONStream(stream);
            }
        }
        catch (Exception ex)
        {
            // TODO: Log failure!
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
        ImRaii.IEndObject? addMenu;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, ImGui.GetStyle().Colors[(int)ImGuiCol.Button]))
        using (ImRaii.PushColor(ImGuiCol.FrameBgActive, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]))
        using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]))
        {
            addMenu = ImRaii.Combo("###AddMenu", "Create");
        }
        using (addMenu)
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

                DrawCreateMenuItem(BgObjectDefinitionEditor.StaticTypeInfo, () => new BgObjectDefinition() { ModelGamePath = "bgcommon/world/aet/001/bgparts/w_aet_001_04a.mdl" });
                DrawCreateMenuItem(LightDefinitionEditor.StaticTypeInfo, () => new LightDefinition());
                DrawCreateMenuItem(VfxObjectDefinitionEditor.StaticTypeInfo, () => new VfxObjectDefinition() { VfxGamePath = "bgcommon/world/common/vfx_for_event/eff/b0150_eext_y.avfx" });
                DrawCreateMenuItem(WeaponDefinitionEditor.StaticTypeInfo, () => new WeaponDefinition());
            }
        }

        ImGui.Separator();

        // Properties
        if (_selectionManager.SelectedEditor != null)
        {
            DrawPropertiesHeader(_selectionManager.SelectedEditor.DisplayName, _selectionManager.SelectedEditor.TypeInfo.DisplayName, _selectionManager.SelectedEditor.TypeInfo.Icon, _selectionManager.SelectedEditor.TypeInfo.Description);
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

    private void DrawCreateMenuItem(DefinitionTypeInfo typeInfo, Func<ObjectDefinition> newObjectFactory)
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
            newObject.DisplayName = $"New {typeInfo.DisplayName}";
            newObject.Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.UnitY;
            var newEditor = _definitionEditor.AddObject(newObject);
            _selectionManager.SelectedEditor = newEditor;
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
        ImRaii.IEndObject? treeNode;
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
            using (ImRaii.DefaultFont())
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

    private void DrawPropertiesHeader(string displayName, string typeDisplayName, FontAwesomeIcon icon, string typeDescription, bool spaceAfter = true)
    {
        ImGui.Indent(2.0f);
        ImGuiHelpers.ScaledDummy(1);
        if (icon != FontAwesomeIcon.None)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextUnformatted(icon.ToIconString());
            }
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(displayName);
        ImGuiHelpers.ScaledDummy(0.5f);
        ImGui.TextDisabled(typeDisplayName);

        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(typeDescription))
        {
            using (ImRaii.Tooltip())
            using (ImRaii.TextWrapPos(250.0f * ImGuiHelpers.GlobalScale))
            {
                ImGui.TextWrapped(typeDescription);
            }
        }

        ImGuiHelpers.ScaledDummy(1);
        ImGui.Indent(-2);
        ImGui.Separator();
        ImGui.Indent(2);

        if (spaceAfter)
        {
            ImGuiHelpers.ScaledDummy(1.0f);
        }
        else
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
        }
    }

    public override void OnClose()
    {
        base.OnClose();

        Closed?.Invoke();
    }

    public void Dispose()
    {
        _definitionEditor.Dispose();
        _serviceScope.Dispose();
    }
}

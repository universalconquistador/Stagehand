using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.Tools;

internal class RotateTool : TransformToolBase
{
    public const string ToolDisplayName = "Rotate Tool";

    public bool SnapEnabled
    {
        get => StagehandConfiguration.RotateToolSnapEnabled;
        set
        {
            StagehandConfiguration.RotateToolSnapEnabled = value;
            StagehandConfiguration.Save();
        }
    }

    public float SnapIncrementDegrees
    {
        get => StagehandConfiguration.RotateToolSnapIncrementDegrees;
        set
        {
            StagehandConfiguration.RotateToolSnapIncrementDegrees = value;
            StagehandConfiguration.Save();
        }
    }

    private TransformOperation? _currentOperation = null;

    public RotateTool(IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<RotateTool> logger, IOverlayService overlayService, IStagehandKeybinds stagehandKeybinds, StagehandConfiguration stagehandConfiguration)
        : base(ToolDisplayName, "Adjust the rotation of objects.", FontAwesomeIcon.ArrowsSpin, sortPriority: 11.0f, stagehandKeybinds.EditorRotateTool, viewportInputService, gameGui, hitTestService, selectionManager, logger, overlayService, stagehandKeybinds, stagehandConfiguration)
    { }

    public override bool TryActivate()
    {
        base.TryActivate();

        StagehandKeybinds.EditorSnapToggle.Pressed += OnSnapToggleKeybindPressed;

        return true;
    }

    private void OnSnapToggleKeybindPressed()
    {
        SnapEnabled = !SnapEnabled;
    }

    public override void Deactivate()
    {
        StagehandKeybinds.EditorSnapToggle.Pressed -= OnSnapToggleKeybindPressed;

        base.Deactivate();
    }

    protected override void DrawOverlay(IOverlayDrawContext context)
    {
        if (SelectionManager.PrimarySelectedEditor is IObjectDefinitionEditor objectDefinitionEditor)
        {
            var translation = objectDefinitionEditor.WorldPosition;
            var rotation = objectDefinitionEditor.WorldRotationQuaternion;
            var scale = objectDefinitionEditor.WorldScale;
            var mode = CoordinateSpace switch { TransformCoordinateSpace.Local => ImGuizmoMode.Local, TransformCoordinateSpace.World => ImGuizmoMode.World, _ => ImGuizmoMode.Local };
            if (context.DrawGizmo("###RotateToolGizmo", ref translation, ref rotation, ref scale, ImGuizmoOperation.Rotate, mode, SnapEnabled ? SnapIncrementDegrees : 0.0f))
            {
                if (_currentOperation == null)
                {
                    _currentOperation = new(SelectionManager.SelectedEditors, objectDefinitionEditor);
                }
                _currentOperation.SetNewRotation(rotation);
            }
            else
            {
                _currentOperation = null;
            }
        }

        base.DrawOverlay(context);
    }

    public override bool DrawOptionGutter()
    {
        base.DrawOptionGutter();

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], condition: SnapEnabled))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Magnet, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                SnapEnabled = !SnapEnabled;
            }
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(SnapEnabled ? "Snap Increment Enabled"u8 : "Snap Increment Disabled"u8);
                ImGui.Separator();
                ImGui.TextDisabled(SnapEnabled ? "Click to disable."u8 : "Click to enable."u8);
            }
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        float snapIncrement = SnapIncrementDegrees;
        ImGui.SetNextItemWidth(ImGui.GetFrameHeight() * 5.0f);
        using (ImRaii.Disabled(!SnapEnabled))
        {
            if (ImGui.InputFloat("###RotateToolSnapIncrement"u8, ref snapIncrement, step: 5.0f, stepFast: 15.0f, format: "%0.0f"u8, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                SnapIncrementDegrees = MathF.Max(0.01f, snapIncrement);
            }
        }

        return true;
    }
}

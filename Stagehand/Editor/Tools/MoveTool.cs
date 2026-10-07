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
using System.Text;

namespace Stagehand.Editor.Tools;

internal class MoveTool : TransformToolBase
{
    public bool SnapEnabled
    {
        get => StagehandConfiguration.MoveToolSnapEnabled;
        set
        {
            StagehandConfiguration.MoveToolSnapEnabled = value;
            StagehandConfiguration.Save();
        }
    }

    public float SnapIncrement
    {
        get => StagehandConfiguration.MoveToolSnapIncrement;
        set
        {
            StagehandConfiguration.MoveToolSnapIncrement = value;
            StagehandConfiguration.Save();
        }
    }

    private TransformOperation? _currentOperation = null;

    public MoveTool(IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<MoveTool> logger, IOverlayService overlayService, StagehandConfiguration stagehandConfiguration)
        : base("Move Tool", "Move objects.", FontAwesomeIcon.ArrowsUpDownLeftRight, sortPriority: 10.0f, viewportInputService, gameGui, hitTestService, selectionManager, logger, overlayService, stagehandConfiguration)
    { }

    protected override void DrawOverlay(IOverlayDrawContext context)
    {
        if (SelectionManager.PrimarySelectedEditor is IObjectDefinitionEditor objectDefinitionEditor)
        {
            var translation = objectDefinitionEditor.WorldPosition;
            var rotation = objectDefinitionEditor.WorldRotationQuaternion;
            var scale = objectDefinitionEditor.WorldScale;
            var mode = CoordinateSpace switch { TransformCoordinateSpace.Local => ImGuizmoMode.Local, TransformCoordinateSpace.World => ImGuizmoMode.World, _ => ImGuizmoMode.Local };
            if (context.DrawGizmo("###MoveToolGizmo", ref translation, ref rotation, ref scale, ImGuizmoOperation.Translate, mode, SnapEnabled ? SnapIncrement : 0.0f))
            {
                if (_currentOperation == null)
                {
                    _currentOperation = new(SelectionManager.SelectedEditors, objectDefinitionEditor);
                }
                _currentOperation.SetNewTranslation(translation);
            }
            else
            {
                _currentOperation = null;
            }
        }
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
        float snapIncrement = SnapIncrement;
        ImGui.SetNextItemWidth(ImGui.GetFrameHeight() * 5.0f);
        using (ImRaii.Disabled(!SnapEnabled))
        {
            if (ImGui.InputFloat("###MoveToolSnapIncrement"u8, ref snapIncrement, step: 0.01f, stepFast: 0.1f, format: "%0.2f"u8, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                SnapIncrement = MathF.Max(0.01f, snapIncrement);
            }
        }

        return true;
    }
}

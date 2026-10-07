using Dalamud.Bindings.ImGuizmo;
using Dalamud.Interface;
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
    private TransformOperation? _currentOperation = null;

    public RotateTool(IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<RotateTool> logger, IOverlayService overlayService, StagehandConfiguration stagehandConfiguration)
        : base("Rotate Tool", "Adjust the rotation of objects.", FontAwesomeIcon.ArrowsSpin, sortPriority: 11.0f, viewportInputService, gameGui, hitTestService, selectionManager, logger, overlayService, stagehandConfiguration)
    { }

    protected override void DrawOverlay(IOverlayDrawContext context)
    {
        if (SelectionManager.PrimarySelectedEditor is IObjectDefinitionEditor objectDefinitionEditor)
        {
            var translation = objectDefinitionEditor.WorldPosition;
            var rotation = objectDefinitionEditor.WorldRotationQuaternion;
            var scale = objectDefinitionEditor.WorldScale;
            var mode = CoordinateSpace switch { TransformCoordinateSpace.Local => ImGuizmoMode.Local, TransformCoordinateSpace.World => ImGuizmoMode.World, _ => ImGuizmoMode.Local };
            if (context.DrawGizmo("###RotateToolGizmo", ref translation, ref rotation, ref scale, ImGuizmoOperation.Rotate, mode))
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
    }
}

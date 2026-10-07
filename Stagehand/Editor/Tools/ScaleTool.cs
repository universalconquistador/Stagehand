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

internal class ScaleTool : TransformToolBase
{
    private TransformOperation? _currentOperation = null;

    public ScaleTool(IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<ScaleTool> logger, IOverlayService overlayService, StagehandConfiguration stagehandConfiguration)
        : base("Scale Tool", "Adjust the size of objects.", FontAwesomeIcon.ExpandAlt, sortPriority: 12.0f, viewportInputService, gameGui, hitTestService, selectionManager, logger, overlayService, stagehandConfiguration)
    { }

    protected override void DrawOverlay(IOverlayDrawContext context)
    {
        if (SelectionManager.PrimarySelectedEditor is IObjectDefinitionEditor objectDefinitionEditor)
        {
            var translation = objectDefinitionEditor.WorldPosition;
            var rotation = objectDefinitionEditor.WorldRotationQuaternion;
            var scale = objectDefinitionEditor.WorldScale;
            var mode = CoordinateSpace switch { TransformCoordinateSpace.Local => ImGuizmoMode.Local, TransformCoordinateSpace.World => ImGuizmoMode.World, _ => ImGuizmoMode.Local };
            if (context.DrawGizmo("###ScaleToolGizmo", ref translation, ref rotation, ref scale, ImGuizmoOperation.Scale, mode, snap: 0.0f))
            {
                if (_currentOperation == null)
                {
                    _currentOperation = new(SelectionManager.SelectedEditors, objectDefinitionEditor);
                }
                _currentOperation.SetNewScale(scale);
            }
            else
            {
                _currentOperation = null;
            }
        }
    }
}

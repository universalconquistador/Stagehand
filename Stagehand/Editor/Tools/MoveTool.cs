using Dalamud.Bindings.ImGuizmo;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Text;

namespace Stagehand.Editor.Tools;

internal class MoveTool : TransformToolBase
{
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
            if (context.DrawGizmo("###MoveToolGizmo", ref translation, ref rotation, ref scale, ImGuizmoOperation.Translate, mode))
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
}

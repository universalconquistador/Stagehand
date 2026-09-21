using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Text;

namespace Stagehand.Editor.Tools;

internal class MoveTool : SelectToolBase
{
    private readonly IOverlayService _overlayService;

    private TransformOperation? _currentOperation = null;

    public MoveTool(IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<MoveTool> logger, IOverlayService overlayService)
        : base("Move Tool", "Move objects.", FontAwesomeIcon.ArrowsUpDownLeftRight, sortPriority: 10.0f, viewportInputService, gameGui, hitTestService, selectionManager, logger)
    {
        _overlayService = overlayService;
    }

    public override bool TryActivate()
    {
        _overlayService.DrawOverlays += DrawOverlay;

        return base.TryActivate();
    }

    private void DrawOverlay(IOverlayDrawContext context)
    {
        if (SelectionManager.PrimarySelectedEditor is IObjectDefinitionEditor objectDefinitionEditor)
        {
            var translation = objectDefinitionEditor.WorldPosition;
            var rotation = objectDefinitionEditor.WorldRotationQuaternion;
            var scale = objectDefinitionEditor.WorldScale;
            if (context.DrawGizmo("###MoveToolGizmo", ref translation, ref rotation, ref scale, Dalamud.Bindings.ImGuizmo.ImGuizmoOperation.Translate, Dalamud.Bindings.ImGuizmo.ImGuizmoMode.Local))
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

    public override void Deactivate()
    {
        _overlayService.DrawOverlays -= DrawOverlay;
        base.Deactivate();
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}

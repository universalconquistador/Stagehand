using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Stagehand.Editor.DefinitionEditors;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.Tools;

internal class RotateTool : SelectToolBase
{
    private readonly IOverlayService _overlayService;

    private TransformOperation? _currentOperation = null;

    public RotateTool(IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<RotateTool> logger, IOverlayService overlayService)
        : base("Rotate Tool", "Adjust the rotation of objects.", FontAwesomeIcon.ArrowsSpin, sortPriority: 11.0f, viewportInputService, gameGui, hitTestService, selectionManager, logger)
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
            if (context.DrawGizmo("###RotateToolGizmo", ref translation, ref rotation, ref scale, Dalamud.Bindings.ImGuizmo.ImGuizmoOperation.Rotate, Dalamud.Bindings.ImGuizmo.ImGuizmoMode.Local))
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

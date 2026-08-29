using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
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
    private readonly ISelectionManager _selectionManager;

    public RotateTool(IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<RotateTool> logger, IOverlayService overlayService)
        : base("Rotate Tool", "Adjust the rotation of objects.", FontAwesomeIcon.ArrowsSpin, sortPriority: 11.0f, viewportInputService, gameGui, hitTestService, selectionManager, logger)
    {
        _overlayService = overlayService;
        _selectionManager = selectionManager;
    }
    public override bool TryActivate()
    {
        _overlayService.DrawOverlays += DrawOverlay;

        return base.TryActivate();
    }

    private void DrawOverlay(IOverlayDrawContext context)
    {
        if (_selectionManager.SelectedEditor is IObjectDefinitionEditor objectDefinitionEditor)
        {
            var translation = objectDefinitionEditor.WorldPosition;
            var rotation = objectDefinitionEditor.WorldRotationQuaternion;
            var scale = objectDefinitionEditor.WorldScale;
            if (context.DrawGizmo("###RotateToolGizmo", ref translation, ref rotation, ref scale, Dalamud.Bindings.ImGuizmo.ImGuizmoOperation.Rotate, Dalamud.Bindings.ImGuizmo.ImGuizmoMode.Local))
            {
                objectDefinitionEditor.WorldRotationQuaternion = rotation;
            }
        }
        else if (_selectionManager.SelectedEditor is StageDefinitionEditor stageDefinitionEditor)
        {
            var translation = stageDefinitionEditor.EditTranslation;
            var rotation = stageDefinitionEditor.EditRotation;
            var scale = new Vector3(stageDefinitionEditor.EditUniformScale);
            if (context.DrawGizmo("###RotateToolGizmo", ref translation, ref rotation, ref scale, Dalamud.Bindings.ImGuizmo.ImGuizmoOperation.Rotate, Dalamud.Bindings.ImGuizmo.ImGuizmoMode.Local))
            {
                stageDefinitionEditor.EditRotation = rotation;
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

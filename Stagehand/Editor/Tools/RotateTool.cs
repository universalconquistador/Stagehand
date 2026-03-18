using Dalamud.Interface;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Editor.Tools;

internal class RotateTool : EditorToolBase
{
    private readonly IOverlayService _overlayService;
    private readonly ISelectionManager _selectionManager;

    public RotateTool(IOverlayService overlayService, ISelectionManager selectionManager)
        : base("Rotate Tool", "Adjust the rotation of objects.", FontAwesomeIcon.ArrowsSpin, sortPriority: 11.0f)
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
            var translation = objectDefinitionEditor.Position;
            var rotation = objectDefinitionEditor.RotationQuaternion;
            var scale = objectDefinitionEditor.Scale;
            if (context.DrawGizmo("###RotateToolGizmo", ref translation, ref rotation, ref scale, Dalamud.Bindings.ImGuizmo.ImGuizmoOperation.Rotate, Dalamud.Bindings.ImGuizmo.ImGuizmoMode.Local))
            {
                objectDefinitionEditor.RotationQuaternion = rotation;
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

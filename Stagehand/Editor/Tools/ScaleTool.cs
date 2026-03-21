using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Editor.Tools;

internal class ScaleTool : SelectToolBase
{
    private readonly IOverlayService _overlayService;
    private readonly ISelectionManager _selectionManager;

    public ScaleTool(IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<ScaleTool> logger, IOverlayService overlayService)
        : base("Scale Tool", "Adjust the size of objects.", FontAwesomeIcon.ExpandAlt, sortPriority: 12.0f, viewportInputService, gameGui, hitTestService, selectionManager, logger)
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
            if (context.DrawGizmo("###ScaleToolGizmo", ref translation, ref rotation, ref scale, Dalamud.Bindings.ImGuizmo.ImGuizmoOperation.Scale, Dalamud.Bindings.ImGuizmo.ImGuizmoMode.Local))
            {
                objectDefinitionEditor.Scale = scale;
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

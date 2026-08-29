using Dalamud.Bindings.ImGuizmo;
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

internal class ScaleTool : SelectToolBase
{
    private readonly IOverlayService _overlayService;
    private readonly ISelectionManager _selectionManager;

    private Vector3? _startScale = null;

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
            var translation = objectDefinitionEditor.WorldPosition;
            var rotation = objectDefinitionEditor.WorldRotationQuaternion;
            var scale = objectDefinitionEditor.WorldScale;
            if (context.DrawGizmo("###ScaleToolGizmo", ref translation, ref rotation, ref scale, Dalamud.Bindings.ImGuizmo.ImGuizmoOperation.Scale, Dalamud.Bindings.ImGuizmo.ImGuizmoMode.Local))
            {
                objectDefinitionEditor.WorldScale = scale;
            }
        }
        else if (_selectionManager.SelectedEditor is StageDefinitionEditor stageDefinitionEditor)
        {
            var translation = stageDefinitionEditor.EditTranslation;
            var rotation = stageDefinitionEditor.EditRotation;
            var scale = new Vector3(stageDefinitionEditor.EditUniformScale);
            if (context.DrawGizmo("###ScaleToolGizmo", ref translation, ref rotation, ref scale, Dalamud.Bindings.ImGuizmo.ImGuizmoOperation.Scale, Dalamud.Bindings.ImGuizmo.ImGuizmoMode.Local))
            {
                if (_startScale == null)
                {
                    _startScale = new Vector3(stageDefinitionEditor.EditUniformScale);
                }
                float delta = Vector3.Dot(scale - _startScale.Value, Vector3.One);
                stageDefinitionEditor.EditUniformScale = _startScale.Value.X + delta;
            }
            else if (!ImGuizmo.IsUsing())
            {
                if (_startScale != null)
                {
                    _startScale = null;
                }
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

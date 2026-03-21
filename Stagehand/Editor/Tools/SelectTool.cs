using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using Microsoft.Extensions.Logging;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.Tools;

/// <summary>
/// The base class for tools that can select object editors by clicking on them in the viewport.
/// </summary>
internal class SelectToolBase : EditorToolBase
{
    private readonly IGameGui _gameGui;
    private readonly IEditorHitTestService _hitTestService;
    private readonly ISelectionManager _selectionManager;
    private readonly ILogger _logger;

    private bool _isMouseCaptured = false;
    private Vector2 _dragDelta = Vector2.Zero;
    private IEditorHitTestShape? _dragShape = null;

    public SelectToolBase(string displayName, string description, FontAwesomeIcon icon, float sortPriority, IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<SelectToolBase> logger)
        : base(displayName, description, icon, sortPriority, viewportInputService)
    {
        _gameGui = gameGui;
        _hitTestService = hitTestService;
        _selectionManager = selectionManager;
        _logger = logger;
    }

    public unsafe override bool HandleMouseInput(ref readonly UIInputData inputData)
    {
        if (_isMouseCaptured)
        {
            var isClick = _dragDelta.Length() < 7.0f;

            if (!inputData.UIFilteredCursorInputs.MouseButtonHeldFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.LBUTTON)
                && !inputData.UIFilteredCursorInputs.MouseButtonHeldFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.RBUTTON))
            {
                _logger.LogDebug("Mouse is captured but not holding button. Ending capture.");

                if (isClick)
                {
                    _selectionManager.SelectedEditor = _dragShape?.Editor;
                }

                _isMouseCaptured = false;
                return true;
            }
            else
            {
                _dragDelta += new Vector2(inputData.UIFilteredCursorInputs.DeltaX, inputData.UIFilteredCursorInputs.DeltaY);
                return base.HandleMouseInput(in inputData);
            }
        }
        else
        {
            // If we don't have the mouse captured but it's already mid-drag, don't mess with it.
            if ((!inputData.UIFilteredCursorInputs.MouseButtonPressedFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.LBUTTON)
                && inputData.UIFilteredCursorInputs.MouseButtonHeldFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.LBUTTON))
                || (!inputData.UIFilteredCursorInputs.MouseButtonPressedFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.RBUTTON)
                && inputData.UIFilteredCursorInputs.MouseButtonHeldFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.RBUTTON)))
            {
                // Was already dragging on something else
                return base.HandleMouseInput(in inputData);
            }
            else if (inputData.UIFilteredCursorInputs.MouseButtonPressedFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.LBUTTON)
                || inputData.UIFilteredCursorInputs.MouseButtonPressedFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.RBUTTON))
            {
                var cameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();
                var activeCamera = cameraManager->CurrentCamera;
                var mouseRay = activeCamera->ScreenPointToRay(ImGui.GetMousePos());

                bool bgCollisionHit = FFXIVClientStructs.FFXIV.Common.Component.BGCollision.BGCollisionModule.RaycastMaterialFilter(mouseRay.Origin, mouseRay.Direction, out var bgCollisionHitInfo);

                if (_hitTestService.HitTestShapes(mouseRay.Origin, mouseRay.Direction, out var hitShape, out var hitPosition, out var hitNormal)
                    && (!bgCollisionHit || (bgCollisionHitInfo.Point - (Vector3)mouseRay.Origin).LengthSquared() > (hitPosition - (Vector3)mouseRay.Origin).LengthSquared()))
                {
                    _logger.LogDebug("Mouse was not captured but button was pressed over bgobject. Starting capture.");
                    _isMouseCaptured = true;
                    _dragDelta = Vector2.Zero;
                    _dragShape = hitShape;
                }

                // Want to let right mouse camera control go through
                return base.HandleMouseInput(in inputData);
            }
            else
            {
                var cameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();
                var activeCamera = cameraManager->CurrentCamera;
                var mouseRay = activeCamera->ScreenPointToRay(ImGui.GetMousePos());

                bool bgCollisionHit = FFXIVClientStructs.FFXIV.Common.Component.BGCollision.BGCollisionModule.RaycastMaterialFilter(mouseRay.Origin, mouseRay.Direction, out var bgCollisionHitInfo);

                if (_hitTestService.HitTestShapes(mouseRay.Origin, mouseRay.Direction, out var hitShape, out var hitPosition, out var hitNormal)
                    && (!bgCollisionHit || (bgCollisionHitInfo.Point - (Vector3)mouseRay.Origin).LengthSquared() > (hitPosition - (Vector3)mouseRay.Origin).LengthSquared()))
                {
                    // Hovering on an object editor
                    return base.HandleMouseInput(in inputData);
                }
                else
                {
                    // Clicked on something else, deselect all editors
                    if (inputData.UIFilteredCursorInputs.MouseButtonPressedFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.LBUTTON))
                    {
                        _selectionManager.SelectedEditor = null;
                    }

                    // Missed all the object editors
                    return base.HandleMouseInput(in inputData);
                }
            }
        }
    }
}

internal class SelectTool : SelectToolBase
{
    public SelectTool(IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger<SelectTool> logger)
        : base("Select Tool", "Select objects in the game by clicking on them.", FontAwesomeIcon.MousePointer, sortPriority: 0.0f, viewportInputService, gameGui, hitTestService, selectionManager, logger)
    { }
}

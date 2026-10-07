using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Editor.Tools;

public static class TransformCoordinateSpaceExtensions
{
    public static FontAwesomeIcon GetIcon(this TransformCoordinateSpace space)
    {
        return space switch
        {
            TransformCoordinateSpace.Local => FontAwesomeIcon.LocationArrow,
            TransformCoordinateSpace.World => FontAwesomeIcon.GlobeAmericas,
            _ => FontAwesomeIcon.None,
        };
    }

    public static string GetDisplayName(this TransformCoordinateSpace space)
    {
        return space switch
        {
            TransformCoordinateSpace.Local => "Local Space",
            TransformCoordinateSpace.World => "World Space",
            _ => "",
        };
    }
}

internal class TransformToolBase : SelectToolBase
{
    protected readonly StagehandConfiguration StagehandConfiguration;

    private readonly IOverlayService _overlayService;

    public TransformCoordinateSpace CoordinateSpace
    {
        get => StagehandConfiguration.TransformGizmoCoordinateSpace;
        set
        {
            StagehandConfiguration.TransformGizmoCoordinateSpace = value;
            StagehandConfiguration.Save();
        }
    }

    public TransformToolBase(string displayName, string description, FontAwesomeIcon icon, float sortPriority, IViewportInputService viewportInputService, IGameGui gameGui, IEditorHitTestService hitTestService, ISelectionManager selectionManager, ILogger logger, IOverlayService overlayService, StagehandConfiguration stagehandConfiguration)
        : base(displayName, description, icon, sortPriority, viewportInputService, gameGui, hitTestService, selectionManager, logger)
    {
        _overlayService = overlayService;
        StagehandConfiguration = stagehandConfiguration;
    }

    public override bool TryActivate()
    {
        _overlayService.DrawOverlays += DrawOverlay;

        return base.TryActivate();
    }

    protected virtual void DrawOverlay(IOverlayDrawContext context)
    { }

    public override bool DrawOptionGutter()
    {
        var nextSpace = (TransformCoordinateSpace)(((int)CoordinateSpace + 1) % 2);
        if (ImGuiComponents.IconButton(CoordinateSpace.GetIcon(), new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
        {
            CoordinateSpace = nextSpace;
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted($"Gizmo Orientation: {CoordinateSpace.GetDisplayName()}");
                ImGui.Separator();
                ImGui.TextDisabled($"Click to switch the gizmo orientation to {nextSpace.GetDisplayName()}.");
            }
        }

        return true;
    }

    public override void Deactivate()
    {
        _overlayService.DrawOverlays -= DrawOverlay;
        base.Deactivate();
    }
}

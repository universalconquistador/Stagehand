using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.UI;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Editor.Tools;

/// <summary>
/// A service that can be selected and used in the 3D viewport.
/// </summary>
public interface IEditorTool
{
    /// <summary>
    /// The user-facing name of the tool.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// A user-facing description of the tool.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// The icon to represent the tool.
    /// </summary>
    FontAwesomeIcon Icon { get; }

    /// <summary>
    /// A value used to sort tools from lowest to hightest.
    /// </summary>
    /// <remarks>
    /// The Select tool has a priority of zero.
    /// </remarks>
    float SortPriority { get; }

    /// <summary>
    /// Whether the tool is the active tool.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Attempts to activate the tool.
    /// </summary>
    /// <returns>Whether the tool successfully activated.</returns>
    bool TryActivate();

    /// <summary>
    /// Deactivates the tool.
    /// </summary>
    void Deactivate();
}

internal abstract class EditorToolBase : IEditorTool, IViewportInputHandler, IDisposable
{
    private readonly IViewportInputService _viewportInputService;

    public string DisplayName { get; }
    public string Description { get; }
    public FontAwesomeIcon Icon { get; }
    public float SortPriority { get; }

    public bool IsActive { get; private set; } = false;

    public EditorToolBase(string displayName, string description, FontAwesomeIcon icon, float sortPriority, IViewportInputService viewportInputService)
    {
        _viewportInputService = viewportInputService;

        DisplayName = displayName;
        Description = description;
        Icon = icon;
        SortPriority = sortPriority;
    }

    public virtual void Deactivate()
    {
        _viewportInputService.RemoveInputHandler(this);
        IsActive = false;
    }

    public virtual bool TryActivate()
    {
        IsActive = true;
        _viewportInputService.AddInputHandler(this, 0.0f);
        return true;
    }

    public virtual void Dispose()
    {
        if (IsActive)
        {
            Deactivate();
        }
    }

    public virtual bool HandleMouseInput(ref readonly UIInputData inputData)
    {
        return false;
    }
}

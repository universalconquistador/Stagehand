using Stagehand.Editor.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Stagehand.Editor.Services;

public interface IToolManager
{
    /// <summary>
    /// The tools available to the user, sorted by <see cref="IEditorTool.SortPriority"/>.
    /// </summary>
    IReadOnlyList<IEditorTool> Tools { get; }

    /// <summary>
    /// The tool that the user has currently active.
    /// </summary>
    IEditorTool? ActiveTool { get; set; }
}

internal class ToolManager : IToolManager
{
    private readonly IEditorTool[] _tools;
    public IReadOnlyList<IEditorTool> Tools => _tools;

    private IEditorTool? _activeTool;
    public IEditorTool? ActiveTool
    {
        get => _activeTool;
        set
        {
            if (_activeTool != value && (value?.TryActivate() ?? true))
            {
                _activeTool?.Deactivate();
                _activeTool = value;
            }
        }
    }

    public ToolManager(IEnumerable<IEditorTool> tools)
    {
        _tools = tools.OrderBy(tool => tool.SortPriority).ToArray();
        var initialTool = Tools[0];
        if (initialTool.TryActivate())
        {
            _activeTool = initialTool;
        }
    }
}

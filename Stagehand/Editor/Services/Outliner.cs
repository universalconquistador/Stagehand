using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Editor.Services;

public class OutlinerContextMenuItem
{
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public event Action<OutlinerNode> Clicked;

    public OutlinerContextMenuItem(string displayName, string description, Action<OutlinerNode> clicked)
    {
        DisplayName = displayName;
        Description = description;
        Clicked = clicked;
    }

    public void RaiseClicked(OutlinerNode node)
    {
        Clicked.Invoke(node);
    }
}

public class OutlinerNode
{
    private string _displayName;
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName != value && ParentNode != null)
            {
                ParentNode._recomputeChildOrder = true;
            }
            _displayName = value;
        }
    }
    public FontAwesomeIcon Icon { get; set; }
    public string TooltipPrimary { get; set; } = string.Empty;
    public string TooltipSecondary { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = false;
    public IEnumerable<OutlinerContextMenuItem>? ContextMenuItems { get; set; }

    public OutlinerNode? ParentNode { get; private set; } = null;
    public bool IsVisibleWithFilter { get; private set; } = true;
    public event Action<OutlinerNode>? Clicked;

    private List<OutlinerNode> _childNodes = new List<OutlinerNode>();
    public IReadOnlyList<OutlinerNode> ChildNodes => _childNodes;

    private bool _recomputeChildOrder = false;

    public OutlinerNode(string displayName, FontAwesomeIcon icon, string tooltipPrimary = "", string tooltipSecondary = "")
    {
        _displayName = displayName;
        Icon = icon;
        TooltipPrimary = tooltipPrimary;
        TooltipSecondary = tooltipSecondary;
    }

    public void AddChild(OutlinerNode child)
    {
        if (child.ParentNode != null)
        {
            child.ParentNode.RemoveChild(child);
        }

        _childNodes.Add(child);
        child.ParentNode = this;
        _recomputeChildOrder = true;
    }

    public void RemoveChild(OutlinerNode child)
    {
        child.ParentNode = null;
        _childNodes.Remove(child);
    }

    public void Update(string filter)
    {
        if (_recomputeChildOrder)
        {
            _childNodes.Sort((a, b) => a.DisplayName.CompareTo(b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        }

        bool anyChildrenVisibleWithFilter = false;
        foreach (var child in _childNodes)
        {
            child.Update(filter);
            if (child.IsVisibleWithFilter)
            {
                anyChildrenVisibleWithFilter = true;
            }
        }

        IsVisibleWithFilter = string.IsNullOrEmpty(filter) || anyChildrenVisibleWithFilter || DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
    }

    public void RaiseClicked()
    {
        Clicked?.Invoke(this);
    }
}

/// <summary>
/// A hierarchy of collapsible nodes.
/// </summary>
public interface IOutliner
{
    /// <summary>
    /// The root Outliner node.
    /// </summary>
    OutlinerNode? RootNode { get; set; }

    /// <summary>
    /// The text the user is filtering the nodes with.
    /// </summary>
    string FilterText { get; set; }

    /// <summary>
    /// Updates the outliner once per frame, including sorting and filtering.
    /// </summary>
    void Update();
}

internal class Outliner : IOutliner
{
    public OutlinerNode? RootNode { get; set; }

    public string FilterText { get; set; }

    public void Update()
    {
        RootNode?.Update(FilterText);
    }
}

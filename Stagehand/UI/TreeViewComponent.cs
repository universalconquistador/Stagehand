using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Stagehand.UI;

public abstract class TreeViewComponent<TItem>
    where TItem : class
{
    // This is kinda convoluted, but here's what's happening:
    // 
    // - We want common tree UI/UX to be implemented just once, including state like filtering, renaming, and selection.
    // - We want the data to come from trees of items from various systems (bookmarks, assets) without making those items implement
    //   treeview-specific functionality on their items.
    // - We need to support heterogeneous items in trees that are handled in their own ways, e.g. both folders and assets
    //   in the same tree.
    // - We want to be fast and lean, without having to create a parallel tree of view-layer nodes and keep it in sync, and
    //   with minimal casting (which is a symptom of poor object-oriented design).
    //
    // The solution implemented here is for a tree to have a single ITreeItemOperations for each kind of tree item it supports,
    // which it chooses and binds the node to in GetItemOperations. This operations object then contains all the item-specific
    // behavior, with 100% type safety. No casting or type checks!
    protected interface ITreeItemOperations<out TSpecificItem>
        where TSpecificItem : TItem
    {
        TItem? GetParent();
        IReadOnlyList<TItem> GetChildren();
        FontAwesomeIcon GetIcon();
        string GetText();
        string GetUniqueId();
        void SetText(string newText);
        string? GetDescription();
        string? GetTypeDescription();
        void DrawContextMenu(string id);

        bool CanSelect();
        bool IsLeafNode();
        bool IsVisible();
        bool CanRename();

        bool CanDrag();
        bool TryDrag(out ReadOnlySpan<byte> typeId, out byte[] payload);
        bool CanAcceptDrop(ReadOnlySpan<byte> typeId);
        bool TryAcceptDrop(ReadOnlySpan<byte> typeId, ReadOnlySpan<byte> payload);

        void PopItem();

        void HandleClicked();
        void HandleDoubleClicked();
    }

    protected abstract class TreeItemOperationsBase<TSpecificItem, TTreeView> : ITreeItemOperations<TSpecificItem>
        where TSpecificItem : class, TItem
        where TTreeView : TreeViewComponent<TItem>
    {
        public TTreeView TreeView { get; }
        public TSpecificItem Item => _itemStack.Peek();

        private readonly Stack<TSpecificItem> _itemStack = new();

        public TreeItemOperationsBase(TTreeView treeView)
        {
            TreeView = treeView;
        }

        public virtual bool CanRename() => false;
        public virtual bool CanSelect() => true;
        public virtual bool CanDrag() => false;

        public virtual void DrawContextMenu(string id)
        { }

        public abstract TItem? GetParent();
        public virtual IReadOnlyList<TItem> GetChildren() => Array.Empty<TItem>();
        public virtual string? GetDescription() => null;
        public abstract FontAwesomeIcon GetIcon();
        public abstract string GetText();
        public virtual string GetUniqueId() => GetText();
        public virtual string? GetTypeDescription() => null;

        public virtual bool IsLeafNode() => GetChildren().Count == 0;
        public virtual bool IsVisible() => GetText().Contains(TreeView.FilterText, StringComparison.CurrentCultureIgnoreCase);

        public virtual void SetText(string newText)
        {
            TreeView.InvalidateFilter(Item);
        }

        public virtual bool TryDrag(out ReadOnlySpan<byte> typeId, out byte[] payload)
        {
            typeId = ReadOnlySpan<byte>.Empty;
            payload = Array.Empty<byte>();
            return false;
        }

        public virtual bool CanAcceptDrop(ReadOnlySpan<byte> typeId) => false;
        public virtual bool TryAcceptDrop(ReadOnlySpan<byte> typeId, ReadOnlySpan<byte> payload) => false;

        public virtual void HandleClicked()
        { }
        public virtual void HandleDoubleClicked()
        { }

        public void PushItem(TSpecificItem item)
        {
            _itemStack.Push(item);
        }

        public void PopItem()
        {
            _itemStack.Pop();
        }
    }

    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set
        {
            _filterText = value;
            InvalidateFilter();
        }
    }

    public virtual TItem? RenamingItem { get; set; }
    public virtual TItem? HoveredItem { get; private set; }
    public virtual TItem? SelectedItem { get; set; }

    protected abstract IReadOnlyList<TItem> RootItems { get; }

    protected virtual bool HasContextMenu => false;

    private readonly HashSet<TItem> _itemsToExpand = new();
    private readonly Dictionary<TItem, bool> _isVisibleCache = new();

    public void InvalidateFilter()
    {
        _isVisibleCache.Clear();
    }

    public void InvalidateFilter(TItem item)
    {
        _isVisibleCache.Remove(item);
        TItem? ancestor = item;
        while (ancestor != null)
        {
            _isVisibleCache.Remove(ancestor);
            var operations = GetItemOperations(ancestor);
            ancestor = operations.GetParent();
            operations.PopItem();
        }
    }

    public void Draw(Vector2 size)
    {
        var startY = ImGui.GetCursorPosY();
        string filter = _filterText;
        Utils.ImGuiExtensions.FilterBox("Filter"u8, ref filter, HasFilterPopup ? ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X : -1.0f);
        if (filter != _filterText)
        {
            FilterText = filter;
        }

        if (HasFilterPopup)
        {
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], IsFiltering))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Filter, new Vector2(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                {
                    ImGui.OpenPopup("###BookmarkFiltersList");
                }
            }
            ImGui.SetNextWindowSizeConstraints(new Vector2(200.0f * ImGuiHelpers.GlobalScale, 0.0f), new Vector2(400.0f * ImGuiHelpers.GlobalScale, float.MaxValue));
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(8.0f)))
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8.0f)))
            using (var filtersList = ImRaii.Popup("###BookmarkFiltersList"))
            {
                if (filtersList.Success)
                {
                    DrawFilterPopup();
                }
            }
        }

        TItem? hoveredNode = null;
        using (var listBox = ImRaii.ListBox("###TreeItems", new Vector2(size.X, size.Y - (ImGui.GetCursorPosY() - startY))))
        {
            if (listBox.Success)
            {
                var defaultItemSpacing = ImGui.GetStyle().ItemSpacing;
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                {
                    var children = RootItems;
                    for (int i = 0; i < children.Count; i++)
                    {
                        DrawTreeNode(children[i], defaultItemSpacing, ref hoveredNode);
                    }
                }

                ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, MathF.Max(ImGui.GetContentRegionAvail().Y, ImGui.GetDragDropPayload().IsNull ? 0.0f : ImGui.GetFrameHeight())));
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    SelectedItem = null;
                }
                if (HasContextMenu && ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    ImGui.OpenPopup("###TreeEmptySpaceContextMenu");
                }
                using (var dropTarget = ImRaii.DragDropTarget())
                {
                    if (dropTarget.Success)
                    {
                        // while holding over the empty space dummy
                        var draggingPayload = ImGui.GetDragDropPayload();
                        if (!draggingPayload.IsNull && CanAcceptDrop(TrimAtFirstNull(draggingPayload.DataType)))
                        {
                            var payload = ImGui.AcceptDragDropPayload(draggingPayload.DataType, ImGuiDragDropFlags.None);
                            if (!payload.IsNull)
                            {
                                // dropped onto the dummy
                                unsafe
                                {
                                    TryAcceptDrop(TrimAtFirstNull(payload.DataType), new ReadOnlySpan<byte>(payload.Data, payload.DataSize));
                                }
                            }
                        }
                    }
                }

                if (HasContextMenu)
                {
                    DrawContextMenu("###TreeEmptySpaceContextMenu");
                }
            }
        }

        HoveredItem = hoveredNode;
    }

    private void DrawTreeNode(TItem item, Vector2 defaultItemSpacing, ref TItem? hoveredItem)
    {
        var operations = GetItemOperations(item);

        if (!IsVisible(item, operations))
        {
            return;
        }

        const ImGuiTreeNodeFlags commonFlags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.FramePadding;

        var flags = commonFlags;
        if (operations.IsLeafNode())
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }

        if (operations.CanSelect())
        {
            flags |= ImGuiTreeNodeFlags.OpenOnArrow; // | ImGuiTreeNodeFlags.OpenOnDoubleClick; // OpenOnDoubleClick is problematic with renaming
        }

        if (item == SelectedItem)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        ImRaii.TreeNodeDisposable treeNode;
        using (ImRaii.PushId(operations.GetUniqueId()))
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                bool expand = _itemsToExpand.Contains(item);
                if (expand)
                {
                    ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                    _itemsToExpand.Remove(item);
                }
                treeNode = ImRaii.TreeNode($"{operations.GetIcon().ToIconString()}###{operations.GetUniqueId()}", flags);
                if (expand)
                {
                    ImGui.SetScrollHereY(0.5f);
                }
            }
            using (treeNode)
            {
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        HandleDoubleClicked(item, operations);
                    }
                    else
                    {
                        HandleClicked(item, operations);
                    }
                }

                string ContextMenuId = $"###ContextMenu{operations.GetUniqueId()}";
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    HandleClicked(item, operations);
                    ImGui.OpenPopup(ContextMenuId);
                }

                if (ImGui.IsItemHovered())
                {
                    hoveredItem = item;

                    var description = operations.GetDescription();
                    if (description != null)
                    {
                        using (ImRaii.Tooltip())
                        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, defaultItemSpacing))
                        {
                            ImGui.TextUnformatted(description);
                            var typeDescription = operations.GetTypeDescription();
                            if (typeDescription != null)
                            {
                                ImGui.Separator();
                                ImGui.TextDisabled(typeDescription);
                            }
                        }
                    }
                }

                bool isDoubleClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left) && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);

                if (operations.CanDrag())
                {
                    using (var dragSource = ImRaii.DragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
                    {
                        if (dragSource.Success && operations.TryDrag(out var typeId, out var payload))
                        {
                            ImGui.SetDragDropPayload(typeId, payload);
                            ImGui.TextUnformatted(operations.GetDescription() ?? operations.GetText());
                        }
                    }
                }

                using (var dropTarget = ImRaii.DragDropTarget())
                {
                    if (dropTarget.Success)
                    {
                        // holding a drag over this tree node
                        var draggingPayload = ImGui.GetDragDropPayload();
                        if (!draggingPayload.IsNull && operations.CanAcceptDrop(TrimAtFirstNull(draggingPayload.DataType)))
                        {
                            var payload = ImGui.AcceptDragDropPayload(draggingPayload.DataType, ImGuiDragDropFlags.None);
                            if (!payload.IsNull)
                            {
                                // dropped onto this tree node
                                unsafe
                                {
                                    operations.TryAcceptDrop(TrimAtFirstNull(payload.DataType), new ReadOnlySpan<byte>(payload.Data, payload.DataSize));
                                }
                            }
                        }
                    }
                }

                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                if (item == RenamingItem)
                {
                    string name = operations.GetText();
                    unsafe
                    {
                        using (ImRaii.PushColor(ImGuiCol.FrameBg, *ImGui.GetStyleColorVec4(ImGuiCol.WindowBg)))
                        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0.0f))
                        {
                            ImGui.SetKeyboardFocusHere();
                            ImGui.SetNextItemWidth(-1.0f);
                            if (ImGui.InputText("###RenameTextBox", ref name, flags: ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll)
                                || ImGui.IsItemDeactivated() || (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsItemClicked(ImGuiMouseButton.Left)))
                            {
                                if (name != operations.GetText())
                                {
                                    operations.SetText(name);
                                }
                                RenamingItem = null;
                            }
                        }
                    }
                }
                else
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetStyle().FramePadding.X);
                    ImGui.TextUnformatted($"{operations.GetText()}");
                    if (isDoubleClicked)
                    {
                        HandleTextDoubleClicked(item, operations);
                    }
                }

                // Context menu
                operations.DrawContextMenu(ContextMenuId);

                var children = operations.GetChildren();
                operations.PopItem();

                // Children
                if (treeNode.Success)
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        DrawTreeNode(children[i], defaultItemSpacing, ref hoveredItem);
                    }
                }
            }
        }
    }

    private static ReadOnlySpan<byte> TrimAtFirstNull(ReadOnlySpan<byte> span)
    {
        var firstNull = span.IndexOf((byte)0);
        if (firstNull >= 0)
        {
            return span.Slice(0, firstNull);
        }
        else
        {
            return span;
        }
    }

    protected virtual bool HasFilterPopup => false;
    protected virtual bool IsFiltering => false;

    protected virtual void DrawFilterPopup()
    { }

    protected virtual void DrawContextMenu(string id)
    { }

    public void ExpandItem(TItem? item)
    {
        while (item != null)
        {
            _itemsToExpand.Add(item);
            var operations = GetItemOperations(item);
            item = operations.GetParent();
            operations.PopItem();
        }
    }

    protected bool IsVisible(TItem item)
    {
        if (_isVisibleCache.TryGetValue(item, out bool cachedIsVisible))
        {
            return cachedIsVisible;
        }
        ITreeItemOperations<TItem> operations = GetItemOperations(item);
        var result = operations.IsVisible();
        operations.PopItem();
        _isVisibleCache[item] = result;
        return result;
    }

    private bool IsVisible(TItem item, ITreeItemOperations<TItem> operations)
    {
        if (_isVisibleCache.TryGetValue(item, out bool cachedIsVisible))
        {
            return cachedIsVisible;
        }
        var result = operations.IsVisible();
        _isVisibleCache[item] = result;
        return result;
    }

    protected abstract ITreeItemOperations<TItem> GetItemOperations(TItem item);

    protected virtual void HandleClicked(TItem item, ITreeItemOperations<TItem> itemOperations)
    {
        if (itemOperations.CanSelect())
        {
            SelectedItem = item;
        }
        itemOperations.HandleClicked();
    }

    protected virtual void HandleTextDoubleClicked(TItem item, ITreeItemOperations<TItem> itemOperations)
    {
        if (itemOperations.CanRename())
        {
            RenamingItem = item;
        }
    }

    protected virtual void HandleDoubleClicked(TItem item, ITreeItemOperations<TItem> itemOperations)
    {
        itemOperations.HandleDoubleClicked();
    }

    protected virtual bool CanAcceptDrop(ReadOnlySpan<byte> typeId) => false;
    protected virtual bool TryAcceptDrop(ReadOnlySpan<byte> typeId, ReadOnlySpan<byte> payload) => false;
}

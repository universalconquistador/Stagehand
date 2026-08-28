using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Stagehand.AssetLibrary.Bookmarks;

public partial class BookmarkTreeViewComponent
{
    private class FolderItemOperations : TreeItemOperationsBase<IFolderBookmarkItem, BookmarkTreeViewComponent>
    {
        public override IBookmarkItem? GetParent() => Item.ParentItem;
        public override IReadOnlyList<IBookmarkItem> GetChildren() => Item.ChildItems;
        public override FontAwesomeIcon GetIcon() => FontAwesomeIcon.Folder;
        public override string GetText() => Item.Name;
        public override string GetUniqueId() => Item.Guid.ToString();

        // WARNING: Recursing here is not great complexity-wise, we should cache visibility per item
        public override bool IsVisible() => Item.Name.Contains(TreeView.FilterText, StringComparison.CurrentCultureIgnoreCase)
            || Item.ChildItems.Any(TreeView.IsVisible);

        public override bool CanRename() => true;
        public override bool CanDrag() => true;

        public FolderItemOperations(BookmarkTreeViewComponent bookmarkTreeViewComponent)
            : base(bookmarkTreeViewComponent)
        { }

        public override void SetText(string newText)
        {
            _ = TreeView.RenameBookmarkFolderAsync(Item, newText);
        }

        public override bool CanAcceptDrop(IBookmarkItem item)
        {
            return true;
        }

        public override bool TryAcceptDrop(IBookmarkItem item)
        {
            _ = TreeView.ReparentBookmarkItemAsync(item, Item);
            return true;
        }

        public override void DrawContextMenu(string id)
        {
            ImGui.SetNextWindowSizeConstraints(new Vector2(200.0f, 0.0f), new Vector2(float.MaxValue, float.MaxValue));
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(8.0f)))
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8.0f)))
            using (var contextMenu = ImRaii.Popup(id))
            {
                if (contextMenu.Success)
                {
                    if (ImGui.MenuItem("Rename"))
                    {
                        TreeView.RenamingItem = Item;
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Delete"u8))
                    {
                        _ = TreeView.DeleteBookmarkItemAsync(Item);
                    }
                }
            }
        }
    }
}

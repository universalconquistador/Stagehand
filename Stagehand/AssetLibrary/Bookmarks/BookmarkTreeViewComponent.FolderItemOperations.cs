using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Stagehand.AssetLibrary.GameResources;
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

        public override bool IsVisible() => (TreeView.FilterText.Length > 0 && Item.Name.Contains(TreeView.FilterText, StringComparison.CurrentCultureIgnoreCase))
            || (TreeView.FilterText.Length == 0 && TreeView.HiddenAssetTypes.Count == 0)
            || Item.ChildItems.Any(TreeView.IsVisible);

        public override bool CanRename() => true;
        public override bool CanDrag() => true;

        public FolderItemOperations(BookmarkTreeViewComponent bookmarkTreeViewComponent)
            : base(bookmarkTreeViewComponent)
        { }

        public override void SetText(string newText)
        {
            base.SetText(newText);
            _ = TreeView.RenameBookmarkFolderAsync(Item, newText);
        }

        public override bool TryDrag(out ReadOnlySpan<byte> typeId, out byte[] payload)
        {
            typeId = BookmarkDragDrop.DataTypeId;
            payload = BookmarkDragDrop.MakeDragPayload(Item);
            return true;
        }

        public override bool CanAcceptDrop(ReadOnlySpan<byte> typeId)
        {
            return BookmarkDragDrop.IsBookmarkPayload(typeId)
                || GameResourceDragDrop.IsGameResourcePayload(typeId)
                || GameFolderDragDrop.IsGameFolderPayload(typeId);
        }

        public override bool TryAcceptDrop(ReadOnlySpan<byte> typeId, ReadOnlySpan<byte> payload)
        {
            if (BookmarkDragDrop.IsBookmarkPayload(typeId) && BookmarkDragDrop.TryParsePayload(payload, TreeView._assetBookmarkService, out var bookmarkItem))
            {
                _ = TreeView.ReparentBookmarkItemAsync(bookmarkItem, Item);

                return true;
            }
            else if (GameResourceDragDrop.IsGameResourcePayload(typeId) && GameResourceDragDrop.TryParsePayload(payload, out var resourceGamePath))
            {
                _ = TreeView.CreateAndSelectResourceBookmarkAsync(Item, resourceGamePath);

                return true;
            }
            else if (GameFolderDragDrop.IsGameFolderPayload(typeId) && GameFolderDragDrop.TryParsePayload(payload, out var folderGamePath))
            {
                _ = TreeView.CreateAndSelectFolderBookmarkAsync(Item, folderGamePath);

                return true;
            }
            else
            {
                return false;
            }
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
                    if (ImGui.MenuItem("New Folder"))
                    {
                        TreeView.ExpandItem(Item);
                        _ = TreeView.CreateAndSelectFolderAsync(Item, "New Folder");
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Cut"))
                    {
                        _ = TreeView.CutItemAsync(Item);
                    }
                    if (ImGui.MenuItem("Copy"))
                    {
                        _ = TreeView.CopyItemAsync(Item);
                    }
                    if (ImGui.MenuItem("Paste"))
                    {
                        _ = TreeView.PasteItemsAsync(Item);
                    }
                    ImGui.Separator();
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

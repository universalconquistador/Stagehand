using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;

namespace Stagehand.AssetLibrary.Bookmarks;

public partial class BookmarkTreeViewComponent
{
    private class GameFolderItemOperations : TreeItemOperationsBase<IGameFolderBookmarkItem, BookmarkTreeViewComponent>
    {
        public override IBookmarkItem? GetParent() => Item.ParentItem;
        public override string? GetDescription() => Item.FolderGamePath;
        public override FontAwesomeIcon GetIcon() => FontAwesomeIcon.ExternalLinkSquareAlt;
        public override string GetText() => Item.FolderName;
        public override string GetUniqueId() => Item.Guid.ToString();
        public override string? GetTypeDescription() => "Resource Folder Link";

        public override bool IsLeafNode() => true;
        public override bool IsVisible() => Item.FolderGamePath.Contains(TreeView.FilterText, StringComparison.CurrentCultureIgnoreCase);
        
        public override bool CanDrag() => true;

        public GameFolderItemOperations(BookmarkTreeViewComponent bookmarkTreeViewComponent)
            : base(bookmarkTreeViewComponent)
        { }

        public override void HandleDoubleClicked()
        {
            TreeView.GameFolderDoubleClicked?.Invoke(Item);
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
                    if (ImGui.MenuItem("Delete"u8))
                    {
                        _ = TreeView.DeleteBookmarkItemAsync(Item);
                    }
                }
            }
        }
    }
}

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Stagehand.AssetLibrary.GameResources;

public partial class GameResourceTreeViewComponent
{
    private class FolderItemOperations : TreeItemOperationsBase<IGameFilesystemFolder, GameResourceTreeViewComponent>
    {
        public override IGameFilesystemItem? GetParent() => Item.ParentItem;
        public override IReadOnlyList<IGameFilesystemItem> GetChildren() => Item.ChildItems;
        public override FontAwesomeIcon GetIcon() => FontAwesomeIcon.Folder;
        public override string GetText() => Item.Name;

        public override bool IsVisible() => Item.FullGamePath.Contains(TreeView.FilterText, StringComparison.CurrentCultureIgnoreCase)
            || Item.ChildItems.Any(TreeView.IsVisible);

        private readonly IAssetBookmarkService _assetBookmarkService;

        public FolderItemOperations(GameResourceTreeViewComponent treeView, IAssetBookmarkService assetBookmarkService)
            : base(treeView)
        {
            _assetBookmarkService = assetBookmarkService;
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
                    void DrawBookmarksFolder(IFolderBookmarkItem folderItem)
                    {
                        using (var submenu = ImRaii.Menu(folderItem.Name))
                        {
                            if (submenu.Success)
                            {
                                foreach (var childItem in folderItem.ChildItems)
                                {
                                    if (childItem is IFolderBookmarkItem folderChildItem)
                                    {
                                        using (ImRaii.PushId($"ChildFolder{childItem.Guid}"))
                                        {
                                            DrawBookmarksFolder(folderChildItem);
                                        }
                                    }
                                }

                                if (ImGui.MenuItem($"Add Bookmark to {folderItem.Name}"))
                                {
                                    _ = CreateGameFolderBookmarkAsync(folderItem, Item.FullGamePath);
                                }
                            }
                        }
                    }

                    using (var bookmarksMenu = ImRaii.Menu("Bookmarks"u8))
                    {
                        if (bookmarksMenu.Success)
                        {
                            foreach (var rootItem in _assetBookmarkService.RootItemsSorted)
                            {
                                if (rootItem is IFolderBookmarkItem folderItem)
                                {
                                    using (ImRaii.PushId($"ChildFolder{rootItem.Guid}"))
                                    {
                                        DrawBookmarksFolder(folderItem);
                                    }
                                }
                            }

                            if (ImGui.MenuItem($"Add Bookmark"))
                            {
                                _ = CreateGameFolderBookmarkAsync(null, Item.FullGamePath);
                            }
                        }
                    }
                }
            }
        }

        private async Task CreateGameFolderBookmarkAsync(IFolderBookmarkItem? parentFolder, string gamePath)
        {
            var newItem = await _assetBookmarkService.CreateGameFolderBookmarkAsync(gamePath, parentFolder).ConfigureAwait(false);
            await _assetBookmarkService.SaveBookmarksAsync().ConfigureAwait(false);
        }
    }
}

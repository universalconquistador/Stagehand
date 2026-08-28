using Stagehand.AssetLibrary.GameResources;
using Stagehand.UI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Stagehand.AssetLibrary.Bookmarks;

/// <summary>
/// A tree view component that shows the user's bookmarks.
/// </summary>
public partial class BookmarkTreeViewComponent : TreeViewComponent<IBookmarkItem>
{
    private readonly IAssetBookmarkService _assetBookmarkService;
    private readonly IGameResourceAssetService _gameResourceAssetService;

    private readonly FolderItemOperations _folderItemOperations;
    private readonly GameFolderItemOperations _gameFolderItemOperations;
    private readonly GameResourceItemOperations _gameResourceItemOperations;

    public override IBookmarkItem? SelectedItem
    {
        get => base.SelectedItem;
        set
        {
            if (base.SelectedItem != null)
            {
                base.SelectedItem.Deleted -= OnSelectedBookmarkItemDeleted;
            }
            base.SelectedItem = value;
            if (value != null)
            {
                value.Deleted += OnSelectedBookmarkItemDeleted;
            }
        }
    }

    public event Action<IGameFolderBookmarkItem>? GameFolderDoubleClicked;
    public event Action<IGameResourceBookmarkItem>? GameResourceDoubleClicked;

    protected override IReadOnlyList<IBookmarkItem> RootItems => _assetBookmarkService.RootItemsSorted;

    public BookmarkTreeViewComponent(IAssetBookmarkService assetBookmarkService, IGameResourceAssetService gameResourceAssetService)
    {
        _assetBookmarkService = assetBookmarkService;
        _gameResourceAssetService = gameResourceAssetService;

        _folderItemOperations = new(this);
        _gameFolderItemOperations = new(this);
        _gameResourceItemOperations = new(this, _gameResourceAssetService);
    }

    protected override bool CanAcceptDrop(IBookmarkItem item)
    {
        return true;
    }

    protected override bool TryAcceptDrop(IBookmarkItem item)
    {
        _ = ReparentBookmarkItemAsync(item, null);
        return true;
    }

    private void OnSelectedBookmarkItemDeleted(IBookmarkItem obj)
    {
        SelectedItem = null;
    }

    public async Task CreateAndSelectFolderAsync(IFolderBookmarkItem? parentFolder, string name)
    {
        var newFolder = await _assetBookmarkService.CreateFolderAsync(parentFolder, name).ConfigureAwait(false);
        SelectedItem = newFolder;
        RenamingItem = newFolder;
        await _assetBookmarkService.SaveBookmarksAsync().ConfigureAwait(false);
    }

    public async Task DeleteBookmarkItemAsync(IBookmarkItem bookmarkItem)
    {
        await _assetBookmarkService.DeleteAsync(bookmarkItem).ConfigureAwait(false);
        await _assetBookmarkService.SaveBookmarksAsync().ConfigureAwait(false);
    }

    public async Task RenameBookmarkFolderAsync(IFolderBookmarkItem folder, string newName)
    {
        await _assetBookmarkService.SetFolderBookmarkNameAsync(folder, newName).ConfigureAwait(false);
        await _assetBookmarkService.SaveBookmarksAsync().ConfigureAwait(false);
    }

    public async Task ReparentBookmarkItemAsync(IBookmarkItem item, IFolderBookmarkItem? newParent)
    {
        await _assetBookmarkService.MoveAsync(item, newParent).ConfigureAwait(false);
        if (newParent != null)
        {
            ExpandItem(newParent);
        }
        await _assetBookmarkService.SaveBookmarksAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Binds bookmark items to the specific type of tree item operations to use with them
    /// </summary>
    private class BookmarkItemOperationsVisitor : IBookmarkItemVisitor<BookmarkTreeViewComponent, ITreeItemOperations<IBookmarkItem>>
    {
        public static ITreeItemOperations<IBookmarkItem> VisitFolderBookmarkItem(IFolderBookmarkItem folderBookmarkItem, ref BookmarkTreeViewComponent param)
        {
            param._folderItemOperations.PushItem(folderBookmarkItem);
            return param._folderItemOperations;
        }

        public static ITreeItemOperations<IBookmarkItem> VisitGameFolderBookmarkItem(IGameFolderBookmarkItem gameFolderBookmarkItem, ref BookmarkTreeViewComponent param)
        {
            param._gameFolderItemOperations.PushItem(gameFolderBookmarkItem);
            return param._gameFolderItemOperations;
        }

        public static ITreeItemOperations<IBookmarkItem> VisitGameResourceBookmarkItem(IGameResourceBookmarkItem gameResourceBookmarkItem, ref BookmarkTreeViewComponent param)
        {
            param._gameResourceItemOperations.PushItem(gameResourceBookmarkItem);
            return param._gameResourceItemOperations;
        }
    }

    protected override ITreeItemOperations<IBookmarkItem> GetItemOperations(IBookmarkItem item)
    {
        BookmarkTreeViewComponent treeView = this;
        return item.Visit<BookmarkItemOperationsVisitor, BookmarkTreeViewComponent, ITreeItemOperations<IBookmarkItem>>(ref treeView);
    }
}

using Stagehand.UI;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.AssetLibrary.GameResources;

/// <summary>
/// A tree view component that shows the resources in the game's data files.
/// </summary>
public partial class GameResourceTreeViewComponent : TreeViewComponent<IGameFilesystemItem>
{
    private readonly IGameResourceAssetService _gameResourceAssetService;

    private readonly FolderItemOperations _folderItemOperations;
    private readonly ResourceItemOperations _resourceItemOperations;

    protected override IReadOnlyList<IGameFilesystemItem> RootItems => _gameResourceAssetService.RootItems;

    public GameResourceTreeViewComponent(IGameResourceAssetService gameResourceAssetService, IAssetBookmarkService assetBookmarkService)
    {
        _gameResourceAssetService = gameResourceAssetService;

        _folderItemOperations = new(this, assetBookmarkService);
        _resourceItemOperations = new(this, gameResourceAssetService, assetBookmarkService);
    }

    private class FilesystemItemOperationsVisitor : IGameFilesystemItemVisitor<GameResourceTreeViewComponent, ITreeItemOperations<IGameFilesystemItem>>
    {
        public static ITreeItemOperations<IGameFilesystemItem> VisitFolder(IGameFilesystemFolder folder, ref GameResourceTreeViewComponent param)
        {
            param._folderItemOperations.PushItem(folder);
            return param._folderItemOperations;
        }

        public static ITreeItemOperations<IGameFilesystemItem> VisitResource(IGameFilesystemResource resource, ref GameResourceTreeViewComponent param)
        {
            param._resourceItemOperations.PushItem(resource);
            return param._resourceItemOperations;
        }
    }

    protected override ITreeItemOperations<IGameFilesystemItem> GetItemOperations(IGameFilesystemItem item)
    {
        GameResourceTreeViewComponent treeView = this;
        return item.Visit<FilesystemItemOperationsVisitor, GameResourceTreeViewComponent, ITreeItemOperations<IGameFilesystemItem>>(ref treeView);
    }
}

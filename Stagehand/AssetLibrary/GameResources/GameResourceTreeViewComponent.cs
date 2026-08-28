using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Stagehand.AssetLibrary.Assets;
using Stagehand.UI;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.AssetLibrary.GameResources;

/// <summary>
/// A tree view component that shows the resources in the game's data files.
/// </summary>
public partial class GameResourceTreeViewComponent : TreeViewComponent<IGameFilesystemItem>
{
    public HashSet<AssetType> HiddenAssetTypes { get; } = new();

    private readonly IGameResourceAssetService _gameResourceAssetService;

    private readonly FolderItemOperations _folderItemOperations;
    private readonly ResourceItemOperations _resourceItemOperations;

    protected override IReadOnlyList<IGameFilesystemItem> RootItems => _gameResourceAssetService.RootItems;
    protected override bool HasFilterPopup => true;
    protected override bool IsFiltering => HiddenAssetTypes.Count != 0;

    public GameResourceTreeViewComponent(IGameResourceAssetService gameResourceAssetService, IAssetBookmarkService assetBookmarkService)
    {
        _gameResourceAssetService = gameResourceAssetService;

        _folderItemOperations = new(this, assetBookmarkService);
        _resourceItemOperations = new(this, gameResourceAssetService, assetBookmarkService);
    }

    protected override void DrawFilterPopup()
    {
        foreach (var assetType in AssetType.AllAssetTypes)
        {
            bool visible = !HiddenAssetTypes.Contains(assetType);
            using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], visible))
            {
                if (ImGuiComponents.IconButtonWithText(visible ? assetType.Icon : FontAwesomeIcon.Ban, assetType.DisplayName, new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()) / ImGuiHelpers.GlobalScale))
                {
                    if (visible)
                    {
                        HiddenAssetTypes.Add(assetType);
                        InvalidateFilter();
                    }
                    else
                    {
                        HiddenAssetTypes.Remove(assetType);
                        InvalidateFilter();
                    }
                }
            }
        }
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

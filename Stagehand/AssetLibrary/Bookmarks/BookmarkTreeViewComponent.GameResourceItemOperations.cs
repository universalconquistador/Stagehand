using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Stagehand.AssetLibrary.GameResources;
using System;
using System.Numerics;

namespace Stagehand.AssetLibrary.Bookmarks;

public partial class BookmarkTreeViewComponent
{
    private class GameResourceItemOperations : TreeItemOperationsBase<IGameResourceBookmarkItem, BookmarkTreeViewComponent>
    {
        public override IBookmarkItem? GetParent() => Item.ParentItem;
        public override FontAwesomeIcon GetIcon() => _gameResourceAssetService.TryGetResource(Item.ResourceGamePath, out var resource) ? resource.AssetInfo.Type.Icon : FontAwesomeIcon.FileCircleXmark;
        public override string GetText() => Item.ResourceName;
        public override string GetUniqueId() => Item.Guid.ToString();
        public override string? GetDescription() => Item.ResourceGamePath;
        public override string? GetTypeDescription() => _gameResourceAssetService.TryGetResource(Item.ResourceGamePath, out var resource) ? resource.AssetInfo.Type.DisplayName : "(Resource not found)";
        
        public override bool IsLeafNode() => true;
        public override bool IsVisible() => Item.ResourceGamePath.Contains(TreeView.FilterText, StringComparison.CurrentCultureIgnoreCase) && (!_gameResourceAssetService.TryGetResource(Item.ResourceGamePath, out var resource) || !TreeView.HiddenAssetTypes.Contains(resource.AssetInfo.Type));

        public override bool CanDrag() => true;

        private readonly IGameResourceAssetService _gameResourceAssetService;

        public GameResourceItemOperations(BookmarkTreeViewComponent bookmarkTreeViewComponent, IGameResourceAssetService gameResourceAssetService)
            : base(bookmarkTreeViewComponent)
        {
            _gameResourceAssetService = gameResourceAssetService;
        }

        public override void HandleDoubleClicked()
        {
            TreeView.GameResourceDoubleClicked?.Invoke(Item);
        }

        public override bool TryDrag(out ReadOnlySpan<byte> typeId, out byte[] payload)
        {
            typeId = BookmarkDragDrop.DataTypeId;
            payload = BookmarkDragDrop.MakeDragPayload(Item);
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
                    if (ImGui.MenuItem("Cut"))
                    {
                        _ = TreeView.CutItemAsync(Item);
                    }
                    if (ImGui.MenuItem("Copy"))
                    {
                        _ = TreeView.CopyItemAsync(Item);
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

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Stagehand.AssetLibrary.Assets;
using Stagehand.AssetLibrary.GameResources;
using Stagehand.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Stagehand.AssetLibrary;

partial class AssetLibraryWindow
{
    private class HousingGroupOperations : TreeViewComponent<IHousingNode>.TreeItemOperationsBase<HousingGroupNode, HousingTreeViewComponent>
    {
        public HousingGroupOperations(HousingTreeViewComponent treeView) : base(treeView)
        { }

        public override FontAwesomeIcon GetIcon() => FontAwesomeIcon.Folder;
        public override string GetText() => Item.DisplayName;
        public override IReadOnlyList<IHousingNode> GetChildren() => Item.ChildItems;

        public override bool IsVisible() => (TreeView.FilterText.Length > 0 && Item.DisplayName.Contains(TreeView.FilterText, StringComparison.CurrentCultureIgnoreCase))
            || (TreeView.FilterText.Length == 0)
            || Item.ChildItems.Any(TreeView.IsVisible);

        public override IHousingNode? GetParent()
        {
            return Item.ParentNode;
        }
    }

    private class HousingItemOperations : TreeViewComponent<IHousingNode>.TreeItemOperationsBase<HousingItemNode, HousingTreeViewComponent>
    {
        private readonly ITextureProvider _textureProvider;
        public HousingItemOperations(HousingTreeViewComponent treeView, ITextureProvider textureProvider) : base(treeView)
        {
            _textureProvider = textureProvider;
        }

        public override FontAwesomeIcon GetIcon() => FontAwesomeIcon.BoxOpen;
        public override string GetText() => Item.DisplayName;
        public override IReadOnlyList<IHousingNode> GetChildren() => Item.ChildItems;
        public override bool IsVisible() => (TreeView.FilterText.Length > 0 && Item.DisplayName.Contains(TreeView.FilterText, StringComparison.CurrentCultureIgnoreCase))
            || (TreeView.FilterText.Length == 0);
            //|| Item.ChildItems.Any(TreeView.IsVisible); // Resources are always visible if the item is

        public override IHousingNode? GetParent()
        {
            return Item.ParentNode;
        }

        public override void DrawToolTip(Vector2 defaultItemSpacing)
        {
            using (ImRaii.Tooltip())
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, defaultItemSpacing))
            {
                var iconTexture = _textureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(Item.Icon));
                ImGui.Image(iconTexture.GetWrapOrEmpty().Handle, new Vector2(ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y));
                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemSpacing.X);
                using (ImRaii.Group())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(Item.DisplayName);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled(Item.Category);
                }
                ImGui.Separator();
                using (ImRaii.TextWrapPos(300.0f * ImGuiHelpers.GlobalScale))
                {
                    ImGui.TextWrapped(Item.Description);
                }
            }
        }
    }

    private class HousingResourceOperations : TreeViewComponent<IHousingNode>.TreeItemOperationsBase<HousingResourceNode, HousingTreeViewComponent>
    {
        public HousingResourceOperations(HousingTreeViewComponent treeView) : base(treeView)
        { }

        public override FontAwesomeIcon GetIcon() => Item.AssetInfo.Type.Icon;
        public override string GetText() => Item.DisplayName;
        public override string? GetDescription() => Item.GamePath;
        public override string? GetTypeDescription() => Item.AssetInfo.Type.DisplayName;
        public override bool IsVisible() => true; // Don't filter resource names; we want to be able to search for the name of an item and have all its resources be visible.

        public override bool CanDrag() => true;

        public override bool TryDrag(out ReadOnlySpan<byte> typeId, out byte[] payload)
        {
            typeId = GameResourceDragDrop.DataTypeId;
            payload = GameResourceDragDrop.MakeGameResourcePayload(Item.GamePath);
            return true;
        }

        public override IHousingNode? GetParent()
        {
            return Item.ParentNode;
        }
    }

    private interface IHousingNode
    {
        IHousingNode? ParentNode { get; set; }
        string DisplayName { get; }
        void InitChildren();
    }

    private record class HousingGroupNode(string DisplayName, List<IHousingNode> ChildItems) : IHousingNode
    {
        public IHousingNode? ParentNode { get; set; }

        public void InitChildren()
        {
            foreach (var child in ChildItems)
            {
                child.ParentNode = this;
                child.InitChildren();
            }
        }
    }

    private record class HousingItemNode(string DisplayName, string ModelKey, ushort Icon, string Description, string Category, HousingResourceNode[] ChildItems) : IHousingNode
    {
        public IHousingNode? ParentNode { get; set; }

        public void InitChildren()
        {
            foreach (var child in ChildItems)
            {
                child.ParentNode = this;
            }
        }
    }

    private record class HousingResourceNode(string DisplayName, string GamePath, AssetInfo AssetInfo) : IHousingNode
    {
        public IHousingNode? ParentNode { get; set; }

        public void InitChildren()
        { }
    }

    private class HousingTreeViewComponent : TreeViewComponent<IHousingNode>
    {
        protected override IReadOnlyList<IHousingNode> RootItems { get; }

        private readonly HousingGroupOperations _groupOperations;
        private readonly HousingItemOperations _itemOperations;
        private readonly HousingResourceOperations _resourceOperations;

        public HousingTreeViewComponent(IDataManager dataManager, IGameResourceAssetService gameResourceAssetService, ITextureProvider textureProvider)
        {
            // Make indoor items
            List<HousingGroupNode> catalogCategoryNodes = new();
            string[] categoryNames = ["Indoor Furnishings", "Tables", "Tabletop", "Wall-Mounted", "Rugs"];
            for (int category = 12; category <= 16; category++)
            {
                catalogCategoryNodes.Add(new HousingGroupNode(categoryNames[category - 12], new()));
            }

            var indoorSubcategorySheet = dataManager.GetExcelSheet<FurnitureCatalogCategory>();
            HousingGroupNode[] allSubcategoryNodes = new HousingGroupNode[indoorSubcategorySheet.Count];
            foreach (var subcategory in indoorSubcategorySheet.OrderBy(subcat => subcat.Unknown1))
            {
                var subcategoryNode = new HousingGroupNode(subcategory.Category.ToString(), new());
                allSubcategoryNodes[subcategory.RowId] = subcategoryNode;
                catalogCategoryNodes[subcategory.Unknown0 - 12].ChildItems.Add(subcategoryNode);
            }

            Dictionary<uint, uint> itemIdToSubcategoryIndex = new();
            var indoorItemSubcategorySheet = dataManager.GetExcelSheet<FurnitureCatalogItemList>();
            foreach (var item in indoorItemSubcategorySheet)
            {
                if (item.Item.IsValid && item.Category.IsValid)
                {
                    itemIdToSubcategoryIndex[item.Item.RowId] = item.Category.RowId;
                }
            }

            var indoorItemSheet = dataManager.GetExcelSheet<HousingFurniture>();
            var otherIndoorItemsNode = new HousingGroupNode("(Other)", new());
            foreach (var row in indoorItemSheet)
            {
                if (row.Item.TryGetValue(out var item) && !item.Name.IsEmpty)
                {
                    string modelKey = row.ModelKey.ToString("D4");
                    List<HousingResourceNode> resourceItems = new();
                    if (gameResourceAssetService.TryGetFolder($"bgcommon/hou/indoor/general/{modelKey}/bgparts", out var modelFolder))
                    {
                        foreach (var folderChild in modelFolder.ChildItems)
                        {
                            if (folderChild is IGameFilesystemResource resourceChild)
                            {
                                resourceItems.Add(new HousingResourceNode(resourceChild.Name, resourceChild.FullGamePath, resourceChild.AssetInfo));
                            }
                        }
                    }
                    var itemNode = new HousingItemNode(item.Name.ToString(), modelKey, item.Icon, item.Description.ToString(), item.ItemUICategory.Value.Name.ToString(), resourceItems.Count > 0 ? resourceItems.ToArray() : Array.Empty<HousingResourceNode>());
                    if (itemIdToSubcategoryIndex.TryGetValue(item.RowId, out var subcategoryIndex))
                    {
                        allSubcategoryNodes[subcategoryIndex].ChildItems.Add(itemNode);
                    }
                    else
                    {
                        otherIndoorItemsNode.ChildItems.Add(itemNode);
                    }
                }
            }
            otherIndoorItemsNode.ChildItems.Sort((nodeA, nodeB) => nodeA.DisplayName.CompareTo(nodeB.DisplayName));
            foreach (var subcategoryNode in allSubcategoryNodes)
            {
                subcategoryNode.ChildItems.Sort((nodeA, nodeB) => nodeA.DisplayName.CompareTo(nodeB.DisplayName));
            }
            var indoorGroup = new HousingGroupNode("Indoor Items", ((IReadOnlyList<IHousingNode>)catalogCategoryNodes).Append(otherIndoorItemsNode).ToList());

            // Make outdoor items
            var outdoorItemSheet = dataManager.GetExcelSheet<HousingYardObject>();
            var outdoorNodes = new List<IHousingNode>();
            foreach (var row in outdoorItemSheet)
            {
                if (row.Item.TryGetValue(out var item) && !item.Name.IsEmpty)
                {
                    string modelKey = row.ModelKey.ToString("D4");
                    List<HousingResourceNode> resourceItems = new();
                    if (gameResourceAssetService.TryGetFolder($"bgcommon/hou/outdoor/general/{modelKey}/bgparts", out var modelFolder))
                    {
                        foreach (var folderChild in modelFolder.ChildItems)
                        {
                            if (folderChild is IGameFilesystemResource resourceChild)
                            {
                                resourceItems.Add(new HousingResourceNode(resourceChild.Name, resourceChild.FullGamePath, resourceChild.AssetInfo));
                            }
                        }
                    }
                    var itemNode = new HousingItemNode(item.Name.ToString(), modelKey, item.Icon, item.Description.ToString(), item.ItemUICategory.Value.Name.ToString(), resourceItems.Count > 0 ? resourceItems.ToArray() : Array.Empty<HousingResourceNode>());
                    outdoorNodes.Add(itemNode);
                }
            }
            outdoorNodes.Sort((nodeA, nodeB) => nodeA.DisplayName.CompareTo(nodeB.DisplayName));
            var outdoorGroup = new HousingGroupNode("Outdoor Items", outdoorNodes);

            RootItems = [indoorGroup, outdoorGroup];
            foreach (var item in RootItems)
            {
                item.InitChildren();
            }

            _groupOperations = new(this);
            _itemOperations = new(this, textureProvider);
            _resourceOperations = new(this);
        }

        protected override ITreeItemOperations<IHousingNode> GetItemOperations(IHousingNode item)
        {
            if (item is HousingItemNode itemNode)
            {
                _itemOperations.PushItem(itemNode);
                return _itemOperations;
            }
            else if (item is HousingResourceNode resourceNode)
            {
                _resourceOperations.PushItem(resourceNode);
                return _resourceOperations;
            }
            else if (item is HousingGroupNode groupNode)
            {
                _groupOperations.PushItem(groupNode);
                return _groupOperations;
            }
            else
            {
                throw new InvalidOperationException();
            }
        }
    }

    private HousingTreeViewComponent _housingTreeView;

    private void LoadHousingNodes()
    {
        _housingTreeView = new(_dataManager, _gameResourceAssetService, _textureProvider);
    }

    private void DrawHousingTab()
    {
        float bottomBarHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2.0f;
        var treeComponentSize = ImGui.GetContentRegionAvail() - new Vector2(0.0f, bottomBarHeight + ImGui.GetStyle().ItemSpacing.Y);
        var priorSelection = _housingTreeView.SelectedItem;
        var priorHover = _housingTreeView.HoveredItem;
        _housingTreeView.Draw(treeComponentSize);

        if (priorSelection != _housingTreeView.SelectedItem)
        {
            if (_housingTreeView.SelectedItem is HousingResourceNode selectedGameResource)
            {
                _selectedAssetInfo = selectedGameResource.AssetInfo;
            }
            else
            {
                _selectedAssetInfo = null;
                HoveredAssetInfo = null;
            }
        }

        if (priorHover != _housingTreeView.HoveredItem)
        {
            HoveredAssetInfo = (_housingTreeView.HoveredItem as HousingResourceNode)?.AssetInfo ?? _selectedAssetInfo;
        }
    }
}

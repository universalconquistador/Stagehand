using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Stagehand.AssetLibrary;

/// <summary>
/// A user-created folder that can contain child bookmark items.
/// </summary>
public interface IFolderBookmarkItem : IBookmarkItem
{
    /// <summary>
    /// The name of this folder.
    /// </summary>
    /// <remarks>
    /// This can be changed by calling <see cref="IAssetBookmarkService.SetFolderBookmarkNameAsync(IFolderBookmarkItem?, string)"/>.
    /// </remarks>
    string Name { get; }
}

internal partial class AssetBookmarkService
{
    public class FolderBookmarkItem : BookmarkItem, IFolderBookmarkItem
    {
        [JsonIgnore]
        public override int TypeSortOrder => 10;

        [JsonIgnore]
        public override bool IsDeleted
        {
            get => base.IsDeleted;
            set
            {
                base.IsDeleted = value;
                if (value)
                {
                    foreach (var child in ChildItems)
                    {
                        child.IsDeleted = true;
                    }
                }
            }
        }

        [JsonInclude]
        public string Name { get; set; }

        [JsonInclude]
        public List<BookmarkItem> ChildItems { get; }

        protected override IReadOnlyList<IBookmarkItem> GetChildItems()
        {
            return ChildItems;
        }

        IFolderBookmarkItem? IBookmarkItem.ParentItem => ParentItem;

        protected override string DisplayName => Name;

        [JsonConstructor]
        public FolderBookmarkItem(string name, List<BookmarkItem> childItems, Guid guid)
            : base(guid)
        {
            Name = name;
            ChildItems = childItems;

            foreach (var child in ChildItems)
            {
                child.ParentItem = this;
            }
        }

        public FolderBookmarkItem(string name)
            : this(name, new(), Guid.NewGuid())
        { }

        public override void SortChildItems()
        {
            ChildItems.Sort();

            foreach (var childItem in ChildItems)
            {
                childItem.SortChildItems();
            }
        }

        public override void RaiseDeleted()
        {
            foreach (var child in ChildItems)
            {
                child.RaiseDeleted();
            }

            RaiseDeleted(this);
        }

        public override BookmarkItem DeepClone(bool newGuid)
        {
            return new FolderBookmarkItem(Name, ChildItems.Select(item => item.DeepClone(newGuid)).ToList(), newGuid ? Guid.NewGuid() : Guid);
        }

        public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        {
            return TVisitor.VisitFolderBookmarkItem(this, ref param);
        }
    }
}

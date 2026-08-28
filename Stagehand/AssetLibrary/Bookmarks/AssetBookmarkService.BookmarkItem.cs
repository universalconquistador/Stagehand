using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stagehand.AssetLibrary;

public interface IBookmarkItemVisitor<TParam, TResult>
{
    static abstract TResult VisitFolderBookmarkItem(IFolderBookmarkItem folderBookmarkItem, ref TParam param);
    static abstract TResult VisitGameResourceBookmarkItem(IGameResourceBookmarkItem gameResourceBookmarkItem, ref TParam param);
    static abstract TResult VisitGameFolderBookmarkItem(IGameFolderBookmarkItem gameFolderBookmarkItem, ref TParam param);
}

/// <summary>
/// An item in the bookmark tree.
/// </summary>
public interface IBookmarkItem
{
    /// <summary>
    /// The folder item that contains this item, or null if it is a root-level item.
    /// </summary>
    IFolderBookmarkItem? ParentItem { get; }

    /// <summary>
    /// The child items of this item, sorted alphabetically.
    /// </summary>
    IReadOnlyList<IBookmarkItem> ChildItems { get; }

    /// <summary>
    /// The unique ID of this bookmark item.
    /// </summary>
    public Guid Guid { get; }

    /// <summary>
    /// Whether this item has been deleted through a call to <see cref="IAssetBookmarkService.DeleteAsync(IBookmarkItem)"/>
    /// or by loading a new bookmark library from disk.
    /// </summary>
    /// <remarks>
    /// Deletions are permanent, and cnce this item is deleted it will never be un-deleted.
    /// </remarks>
    bool IsDeleted { get; }

    /// <summary>
    /// Raised after this item has been deleted.
    /// </summary>
    event Action<IBookmarkItem> Deleted;

    /// <summary>
    /// Invokes the <c>Visit[...]</c> method on the given visitor type that corresponds to the concrete type of this bookmark item.
    /// </summary>
    /// <typeparam name="TVisitor">The visitor type.</typeparam>
    /// <typeparam name="TParam">The type of parameter that the visitor type accepts.</typeparam>
    /// <typeparam name="TResult">The type of result that the visitor type returns.</typeparam>
    /// <param name="param">The parameter to pass to the visitor.</param>
    /// <returns>The result returned by the visitor.</returns>
    TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        where TVisitor : IBookmarkItemVisitor<TParam, TResult>;
}

internal partial class AssetBookmarkService
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
    [JsonDerivedType(typeof(FolderBookmarkItem), typeDiscriminator: "Folder")]
    [JsonDerivedType(typeof(GameResourceBookmarkItem), typeDiscriminator: "GameResource")]
    [JsonDerivedType(typeof(GameFolderBookmarkItem), typeDiscriminator: "GameFolder")]
    public abstract class BookmarkItem : IBookmarkItem, IComparable<BookmarkItem>
    {
        [JsonIgnore]
        public abstract int TypeSortOrder { get; }

        [JsonIgnore]
        public virtual bool IsDeleted { get; set; } = false;
        public event Action<IBookmarkItem>? Deleted;

        [JsonIgnore]
        public FolderBookmarkItem? ParentItem { get; set; }

        [JsonIgnore]
        protected abstract string DisplayName { get; }

        [JsonIgnore]
        IFolderBookmarkItem? IBookmarkItem.ParentItem => ParentItem;

        [JsonInclude]
        public Guid Guid { get; }

        // Some chicanery here because we want folders to have their own non-readonly ChildItems list of the non-interface type
        // but we need to implement IBookmarkItem in an overridable way.
        IReadOnlyList<IBookmarkItem> IBookmarkItem.ChildItems => GetChildItems();
        protected virtual IReadOnlyList<IBookmarkItem> GetChildItems() => Array.Empty<IBookmarkItem>();

        public BookmarkItem(Guid guid)
        {
            Guid = guid;
        }

        public int CompareTo(BookmarkItem? other)
        {
            if (other == null)
            {
                return 1;
            }

            var typeDelta = TypeSortOrder - other.TypeSortOrder;
            if (typeDelta != 0)
            {
                return -typeDelta;
            }

            var nameDelta = DisplayName.CompareTo(other?.DisplayName);
            if (nameDelta != 0)
            {
                return nameDelta;
            }

            return Guid.CompareTo(other?.Guid);
        }

        protected void RaiseDeleted(IBookmarkItem thisItem)
        {
            Deleted?.Invoke(thisItem);
        }

        public abstract BookmarkItem DeepClone(bool newGuid);

        public abstract void RaiseDeleted();

        public abstract TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
            where TVisitor : IBookmarkItemVisitor<TParam, TResult>;

        public virtual void SortChildItems()
        { }
    }
}

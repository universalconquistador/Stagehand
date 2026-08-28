using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace Stagehand.AssetLibrary;

/// <summary>
/// A bookmark item that links to a game resource.
/// </summary>
public interface IGameResourceBookmarkItem : IBookmarkItem
{
    /// <summary>
    /// The full path of the game resource to link to.
    /// </summary>
    string ResourceGamePath { get; }

    /// <summary>
    /// The name and extension of the game resource.
    /// </summary>
    string ResourceName { get; }
}

internal partial class AssetBookmarkService
{
    private class GameResourceBookmarkItem : BookmarkItem, IGameResourceBookmarkItem
    {
        [JsonIgnore]
        public override int TypeSortOrder => 1;

        public string ResourceGamePath { get; }

        [JsonIgnore]
        public string ResourceName { get; }

        protected override string DisplayName => ResourceName;

        IFolderBookmarkItem? IBookmarkItem.ParentItem => ParentItem;

        [JsonConstructor]
        public GameResourceBookmarkItem(string resourceGamePath, Guid guid)
            : base(guid)
        {
            ResourceGamePath = resourceGamePath;
            ResourceName = Path.GetFileNameWithoutExtension(ResourceGamePath);
        }

        public GameResourceBookmarkItem(string resourceGamePath)
            : this(resourceGamePath, Guid.NewGuid())
        { }

        public override void RaiseDeleted()
        {
            RaiseDeleted(this);
        }

        public override BookmarkItem DeepClone(bool newGuid)
        {
            return new GameResourceBookmarkItem(ResourceGamePath, newGuid ? Guid.NewGuid() : Guid);
        }

        public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        {
            return TVisitor.VisitGameResourceBookmarkItem(this, ref param);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace Stagehand.AssetLibrary;

/// <summary>
/// A bookmark item that links to a folder in the game's resources.
/// </summary>
public interface IGameFolderBookmarkItem : IBookmarkItem
{
    /// <summary>
    /// The full game path of the folder in the game's resources to link to.
    /// </summary>
    string FolderGamePath { get; }

    /// <summary>
    /// The name of the folder, without any containing folders.
    /// </summary>
    string FolderName { get; }
}

internal partial class AssetBookmarkService
{
    private class GameFolderBookmarkItem : BookmarkItem, IGameFolderBookmarkItem
    {
        [JsonIgnore]
        public override int TypeSortOrder => 2;

        public string FolderGamePath { get; }

        [JsonIgnore]
        public string FolderName { get; }

        protected override string DisplayName => FolderName;

        IFolderBookmarkItem? IBookmarkItem.ParentItem => ParentItem;

        [JsonConstructor]
        public GameFolderBookmarkItem(string folderGamePath, Guid guid)
            : base(guid)
        {
            FolderGamePath = folderGamePath;
            FolderName = Path.GetFileName(FolderGamePath);
        }

        public GameFolderBookmarkItem(string folderGamePath)
            : this(folderGamePath, Guid.NewGuid())
        { }

        public override void RaiseDeleted()
        {
            RaiseDeleted(this);
        }

        public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        {
            return TVisitor.VisitGameFolderBookmarkItem(this, ref param);
        }
    }
}

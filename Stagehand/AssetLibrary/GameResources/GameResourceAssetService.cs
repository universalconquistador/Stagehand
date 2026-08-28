using Stagehand.AssetLibrary.Assets;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Stagehand.AssetLibrary.GameResources;

/// <summary>
/// A type that can process folder and resource items from the game's filesystem.
/// </summary>
/// <typeparam name="TParam">The type of parameter this visitor takes.</typeparam>
/// <typeparam name="TResult">The type of result this visitor produces.</typeparam>
public interface IGameFilesystemItemVisitor<TParam, TResult>
{
    static abstract TResult VisitFolder(IGameFilesystemFolder folder, ref TParam param);
    static abstract TResult VisitResource(IGameFilesystemResource resource, ref TParam param);
}

/// <summary>
/// A resource or folder in the game data.
/// </summary>
public interface IGameFilesystemItem
{
    /// <summary>
    /// The game filesystem item that contains this one, or null if this filesystem item is in the root. 
    /// </summary>
    IGameFilesystemItem? ParentItem { get; }

    /// <summary>
    /// The full game path of this item.
    /// </summary>
    string FullGamePath { get; }

    /// <summary>
    /// The name of this item, without any containing folders or extension.
    /// </summary>
    string Name { get; }

    TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        where TVisitor : IGameFilesystemItemVisitor<TParam, TResult>;
}

/// <summary>
/// A resource in the game data.
/// </summary>
public interface IGameFilesystemResource : IGameFilesystemItem
{
    /// <summary>
    /// Information about the resource.
    /// </summary>
    AssetInfo AssetInfo { get; }
}

/// <summary>
/// A folder in the game data.
/// </summary>
public interface IGameFilesystemFolder : IGameFilesystemItem
{
    /// <summary>
    /// The game filesystem items directly inside this folder item.
    /// </summary>
    IReadOnlyList<IGameFilesystemItem> ChildItems { get; }
}

/// <summary>
/// Provides information about the resources in the game's data files.
/// </summary>
public interface IGameResourceAssetService
{
    /// <summary>
    /// The outermost items in the game filesystem.
    /// </summary>
    IReadOnlyList<IGameFilesystemItem> RootItems { get; }

    /// <summary>
    /// Attempts to find the folder in the game's filesystem with the given game path.
    /// </summary>
    /// <param name="folderGamePath">The game path of the folder to look for.</param>
    /// <param name="folder">The folder that was found, if any.</param>
    /// <returns>Whether the folder was found.</returns>
    bool TryGetFolder(string folderGamePath, [NotNullWhen(true)] out IGameFilesystemFolder? folder);

    /// <summary>
    /// Attempts to find the resource in the game's filesystem with the given game path.
    /// </summary>
    /// <param name="resourceGamePath">The game path of the resource to look for.</param>
    /// <param name="resource">The resource that was found, if any.</param>
    /// <returns>Whether the resource was found.</returns>
    bool TryGetResource(string resourceGamePath, [NotNullWhen(true)] out IGameFilesystemResource? resource);
}

internal class GameResourceAssetService : IGameResourceAssetService
{
    private abstract record class GameFilesystemItem(IGameFilesystemItem? ParentItem, string FullGamePath) : IGameFilesystemItem, IComparable<GameFilesystemItem>
    {
        public string Name { get; } = Path.GetFileNameWithoutExtension(FullGamePath);

        protected abstract int NodeTypeSortOrder { get; }

        public int CompareTo(GameFilesystemItem? other)
        {
            if (other == null)
            {
                return 1;
            }

            var nodeTypeDelta = NodeTypeSortOrder - other.NodeTypeSortOrder;
            if (nodeTypeDelta != 0)
            {
                return -nodeTypeDelta;
            }

            return Name.CompareTo(other.Name);
        }

        public virtual void SortChildNodes()
        { }

        public abstract TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
            where TVisitor : IGameFilesystemItemVisitor<TParam, TResult>;
    }

    private record class GameFilesystemFolder(IGameFilesystemItem? ParentItem, string FullPath) : GameFilesystemItem(ParentItem, FullPath), IGameFilesystemFolder
    {
        protected override int NodeTypeSortOrder => 10;

        public List<GameFilesystemItem> ChildItems { get; } = new();

        IReadOnlyList<IGameFilesystemItem> IGameFilesystemFolder.ChildItems => ChildItems;

        public override void SortChildNodes()
        {
            ChildItems.Sort();

            foreach (var childItem in ChildItems)
            {
                childItem.SortChildNodes();
            }
        }

        public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        {
            return TVisitor.VisitFolder(this, ref param);
        }
    }

    private record class GameFilesystemResource(IGameFilesystemItem? ParentItem, string FullPath, AssetInfo AssetInfo) : GameFilesystemItem(ParentItem, FullPath), IGameFilesystemResource
    {
        protected override int NodeTypeSortOrder => 1;

        public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        {
            return TVisitor.VisitResource(this, ref param);
        }
    }

    /// <summary>
    /// Metadata about the resources in the game data, loaded from the <c>paths.json</c> file embedded in <see cref="Properties.Resources.Paths"/>.
    /// </summary>
    /// <param name="MdlPaths">The full game path of each of the <c>.mdl</c> resources in the game data.</param>
    /// <param name="AvfxPaths">The full game path of each of the <c>.avfx</c> resources in the game data.</param>
    /// <param name="ScdPaths">The full game path of each of the <c>.scd</c> resources in the game data.</param>
    private record class PathCache(List<string> MdlPaths, List<string> AvfxPaths, List<string> ScdPaths);

    private readonly ILogger _logger;

    private readonly Dictionary<string, GameFilesystemFolder> _folderItems = new();
    private readonly Dictionary<string, GameFilesystemResource> _resourceItems = new();

    public IReadOnlyList<IGameFilesystemItem> RootItems { get; }

    public GameResourceAssetService(ILogger<GameResourceAssetService> logger)
    {
        _logger = logger;

        var pathCacheBytes = Properties.Resources.Paths;
        var pathCache = JsonSerializer.Deserialize<PathCache>(pathCacheBytes);
        if (pathCache != null)
        {
            List<GameFilesystemItem> rootItems = new();

            Func<string, GameFilesystemFolder> addOrGetFolderNode = null!;
            addOrGetFolderNode = path =>
            {
                if (_folderItems.TryGetValue(path, out var existingNode))
                {
                    return existingNode;
                }
                else
                {
                    var lastSlash = path.LastIndexOf('/');
                    GameFilesystemFolder? parentFolder = null;
                    if (lastSlash > 0)
                    {
                        parentFolder = addOrGetFolderNode(path.Substring(0, lastSlash));
                    }
                    var newFolder = new GameFilesystemFolder(parentFolder, path);

                    if (parentFolder != null)
                    {
                        parentFolder.ChildItems.Add(newFolder);
                    }
                    else
                    {
                        rootItems.Add(newFolder);
                    }

                    _folderItems[path] = newFolder;

                    return newFolder;
                }
            };

            foreach (var mdlPath in pathCache.MdlPaths)
            {
                var lastSlash = mdlPath.LastIndexOf('/');
                var parentFolder = addOrGetFolderNode(mdlPath.Substring(0, lastSlash));

                var assetInfo = new MdlResourceAssetInfo(Path.GetFileNameWithoutExtension(mdlPath), mdlPath);

                var mdlResource = new GameFilesystemResource(parentFolder, mdlPath, assetInfo);
                _resourceItems[mdlPath] = mdlResource;
                if (parentFolder != null)
                {
                    parentFolder.ChildItems.Add(mdlResource);
                }
                else
                {
                    rootItems.Add(mdlResource);
                }
            }

            foreach (var avfxPath in pathCache.AvfxPaths)
            {
                var lastSlash = avfxPath.LastIndexOf('/');
                var parentFolder = addOrGetFolderNode(avfxPath.Substring(0, lastSlash));
                
                var assetInfo = new AvfxResourceAssetInfo(Path.GetFileNameWithoutExtension(avfxPath), avfxPath);

                var avfxResource = new GameFilesystemResource(parentFolder, avfxPath, assetInfo);
                _resourceItems[avfxPath] = avfxResource;
                if (parentFolder != null)
                {
                    parentFolder.ChildItems.Add(avfxResource);
                }
                else
                {
                    rootItems.Add(avfxResource);
                }
            }

            foreach (var scdPath in pathCache.ScdPaths)
            {
                var lastSlash = scdPath.LastIndexOf('/');
                var parentFolder = addOrGetFolderNode(scdPath.Substring(0, lastSlash));
                
                var assetInfo = new ScdResourceAssetInfo(Path.GetFileNameWithoutExtension(scdPath), scdPath);

                var scdResource = new GameFilesystemResource(parentFolder, scdPath, assetInfo);
                _resourceItems[scdPath] = scdResource;
                if (parentFolder != null)
                {
                    parentFolder.ChildItems.Add(scdResource);
                }
                else
                {
                    rootItems.Add(scdResource);
                }
            }

            foreach (var rootItem in rootItems)
            {
                rootItem.SortChildNodes();
            }

            rootItems.Sort();
            RootItems = rootItems;
        }
        else
        {
            _logger.LogError("Failed to parse path cache!");
            RootItems = Array.Empty<IGameFilesystemItem>();
        }
    }

    public bool TryGetFolder(string folderGamePath, [NotNullWhen(true)] out IGameFilesystemFolder? folder)
    {
        var result = _folderItems.TryGetValue(folderGamePath, out var folderItem);
        folder = folderItem;
        return result;
    }

    public bool TryGetResource(string resourceGamePath, [NotNullWhen(true)] out IGameFilesystemResource? resource)
    {
        var result = _resourceItems.TryGetValue(resourceGamePath, out var resourceItem);
        resource = resourceItem;
        return result;
    }
}

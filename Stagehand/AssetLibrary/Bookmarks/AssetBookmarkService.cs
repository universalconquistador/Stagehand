using Dalamud.Plugin;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.AssetLibrary;

/// <summary>
/// Maintains a collection of asset bookmarks organized into folders.
/// </summary>
public interface IAssetBookmarkService
{
    /// <summary>
    /// Whether bookmarks are currently being loaded from disk.
    /// </summary>
    /// <remarks>
    /// You should probably not make changes to bookmarks while this is <see langword="true"/>,
    /// as they will be deleted and replaced with the loaded data.
    /// </remarks>
    bool IsLoadingBookmarks { get; }

    /// <summary>
    /// The root bookmark items, sorted by folder vs non-folder and then by name.
    /// </summary>
    IReadOnlyList<IBookmarkItem> RootItemsSorted { get; }

    /// <summary>
    /// Creates a new folder with the given name, optionally within a parent folder.
    /// </summary>
    /// <param name="name">The name of the folder to create.</param>
    /// <param name="parent">The parent folder to create the new folder within, or <see langword="null"/> to create the new folder at the outermost level.</param>
    /// <returns>The new folder item.</returns>
    Task<IFolderBookmarkItem> CreateFolderAsync(string name, IFolderBookmarkItem? parent);

    /// <summary>
    /// Creates a new bookmark for the resource at the given game path, optionally within a parent folder.
    /// </summary>
    /// <param name="resourceGamePath">The game path of the resource to create a bookmark to.</param>
    /// <param name="parent">The parent folder to create the new bookmark within, or <see langword="null"/> to create the new folder at the outermost level.</param>
    /// <returns>The new bookmark item.</returns>
    Task<IGameResourceBookmarkItem> CreateGameResourceBookmarkAsync(string resourceGamePath, IFolderBookmarkItem? parent);

    /// <summary>
    /// Creates a new bookmark for the folder at the given game path, optionally within a parent folder.
    /// </summary>
    /// <param name="folderGamePath">The game path of the folder to create a bookmark to.</param>
    /// <param name="parent">The parent folder to create the new bookmark within, or <see langword="null"/> to create the new folder at the outermost level.</param>
    /// <returns>The new bookmark item.</returns>
    Task<IGameFolderBookmarkItem> CreateGameFolderBookmarkAsync(string folderGamePath, IFolderBookmarkItem? parent);

    /// <summary>
    /// Creates bookmark items according to the given data transfer fragment from a previous call to <see cref="SaveToFragment(IReadOnlyList{IBookmarkItem})"/>.
    /// </summary>
    /// <param name="fragment">The fragment containing the items to create.</param>
    /// <param name="parent">The parent to place the created items in, or null.</param>
    /// <returns>The items that were created.</returns>
    Task<IReadOnlyList<IBookmarkItem>> CreateFromFragment(DataTransferFragment fragment, IFolderBookmarkItem? parent);

    /// <summary>
    /// Saves the given bookmark items to a data transfer fragment that can be serialized and then passed back to
    /// <see cref="CreateFromFragment(DataTransferFragment, IFolderBookmarkItem?)"/>.
    /// </summary>
    /// <param name="items">The items to create the fragment with.</param>
    /// <returns>A data transfer fragment with the bookmark items.</returns>
    Task<DataTransferFragment> SaveToFragment(IReadOnlyList<IBookmarkItem> items);

    /// <summary>
    /// Deletes the given bookmark item.
    /// </summary>
    /// <remarks>
    /// Once a bookmark item has been deleted, it can never be un-deleted.
    /// </remarks>
    /// <param name="item">The bookmark to delete.</param>
    Task DeleteAsync(IBookmarkItem item);

    /// <summary>
    /// Moves the given bookmark item to a new containing folder.
    /// </summary>
    /// <param name="item">The bookmark item to move.</param>
    /// <param name="newParent">The folder to move the bookmark item to.</param>
    Task MoveAsync(IBookmarkItem item, IFolderBookmarkItem? newParent);

    /// <summary>
    /// Sets the name of the given folder.
    /// </summary>
    /// <param name="folderBookmark">The folder to set the name of.</param>
    /// <param name="newName">The new name for the folder.</param>
    Task SetFolderBookmarkNameAsync(IFolderBookmarkItem folderBookmark, string newName);

    /// <summary>
    /// Saves the current bookmark hierarchy to disk.
    /// </summary>
    Task SaveBookmarksAsync();
}

internal partial class AssetBookmarkService : IAssetBookmarkService, IDisposable
{
    private readonly ILogger _logger;
    private readonly IDalamudPluginInterface _dalamudPluginInterface;

    private readonly string _bookmarkLibraryFilename;
    private readonly SemaphoreSlim _bookmarkLibraryLock = new(1, 1); // Held during short-running mutations to the in-memory bookmarks
    private readonly SemaphoreSlim _diskOperationLock = new(1, 1); // Held during long-running I/O operations, so we don't try to write to or read from disk multiple times concurrently
    private CancellationTokenSource _bookmarkLibraryToken = new();
    private int _loadBookmarksTasks = 0;

    public bool IsLoadingBookmarks => Volatile.Read(ref _loadBookmarksTasks) > 0;

    private List<BookmarkItem> _rootItemsSorted = new();
    public IReadOnlyList<IBookmarkItem> RootItemsSorted => (IReadOnlyList<IBookmarkItem>)(IReadOnlyList<BookmarkItem>)_rootItemsSorted;

    public AssetBookmarkService(ILogger<AssetBookmarkService> logger, IDalamudPluginInterface dalamudPluginInterface)
    {
        _logger = logger;
        _dalamudPluginInterface = dalamudPluginInterface;

        _bookmarkLibraryFilename = Path.Combine(_dalamudPluginInterface.GetPluginConfigDirectory(), "bookmarks.json");

        _ = LoadBookmarksAsync();
    }

    private record class BookmarksFile(List<BookmarkItem> Items);

    private async Task LoadBookmarksAsync()
    {
        Interlocked.Increment(ref _loadBookmarksTasks);

        try
        {
            // Cancel any in-flight operations on the bookmark library file
            var newCancellationToken = new CancellationTokenSource();
            var previousToken = Interlocked.Exchange(ref _bookmarkLibraryToken, newCancellationToken);
            await previousToken.CancelAsync().ConfigureAwait(false);

            // Wait for any previous in-flight operations to finish
            List<BookmarkItem>? loadedItems = null;
            await _diskOperationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using (var fileStream = new FileStream(_bookmarkLibraryFilename, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    loadedItems = (await JsonSerializer.DeserializeAsync<BookmarksFile>(fileStream, cancellationToken: newCancellationToken.Token).ConfigureAwait(false))?.Items;
                }

                newCancellationToken.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Bookmark load cancelled.");
            }
            catch (FileNotFoundException)
            {
                _logger.LogInformation("No bookmarks file exists.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception loading bookmarks!");
            }
            finally
            {
                _diskOperationLock.Release();
            }

            if (loadedItems != null && !newCancellationToken.IsCancellationRequested)
            {
                // Sort all the items just to make extra sure they are in the correct order
                foreach (var newRootItem in loadedItems)
                {
                    newRootItem.SortChildItems();
                }
                loadedItems.Sort();

                // Mark all the old bookmarks as deleted, and swap in the new list
                await _bookmarkLibraryLock.WaitAsync().ConfigureAwait(false);
                List<BookmarkItem> oldRootItems;
                try
                {
                    // Really doesn't have to be interlocked as we're in the library lock but eh, it makes me feel cool
                    oldRootItems = Interlocked.Exchange(ref _rootItemsSorted, loadedItems);
                    
                    foreach (var oldRootItem in oldRootItems)
                    {
                        oldRootItem.IsDeleted = true;
                    }
                }
                finally
                {
                    _bookmarkLibraryLock.Release();
                }

                // Raise delete events outside the locks
                foreach (var oldRootItem in oldRootItems)
                {
                    oldRootItem.RaiseDeleted();
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _loadBookmarksTasks);
        }
    }

    public async Task SaveBookmarksAsync()
    {
        // Cancel any in-flight operations on the bookmark library file
        var newCancellationToken = new CancellationTokenSource();
        var previousToken = Interlocked.Exchange(ref _bookmarkLibraryToken, newCancellationToken);
        await previousToken.CancelAsync().ConfigureAwait(false);

        List<BookmarkItem> rootItemClones;
        await _bookmarkLibraryLock.WaitAsync().ConfigureAwait(false);
        try
        {
            rootItemClones = _rootItemsSorted.Select(item => item.DeepClone(newGuid: false)).ToList();
        }
        finally
        {
            _bookmarkLibraryLock.Release();
        }

        // Wait for any previous in-flight operations to finish
        await _diskOperationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Serialize to temp file
            var tempFilename = Path.GetTempFileName();
            using (var fileStream = new FileStream(tempFilename, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fileStream, new BookmarksFile(rootItemClones), cancellationToken: newCancellationToken.Token).ConfigureAwait(false);
            }

            newCancellationToken.Token.ThrowIfCancellationRequested();

            // Overwrite final file with temp file
            File.Move(tempFilename, _bookmarkLibraryFilename, overwrite: true);

            File.Delete(tempFilename);

            _logger.LogDebug("Saved bookmarks to {file}.", Path.GetFileName(_bookmarkLibraryFilename));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Bookmark save cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception saving bookmarks!");
        }
        finally
        {
            _diskOperationLock.Release();
        }
    }

    public async Task<IFolderBookmarkItem> CreateFolderAsync(string name, IFolderBookmarkItem? parent)
    {
        var newItem = new FolderBookmarkItem(name);
        await AddNewItemAsync(parent, newItem);
        return newItem;
    }

    public async Task<IGameResourceBookmarkItem> CreateGameResourceBookmarkAsync(string resourceGamePath, IFolderBookmarkItem? parent)
    {
        var newItem = new GameResourceBookmarkItem(resourceGamePath);
        await AddNewItemAsync(parent, newItem).ConfigureAwait(false);
        return newItem;
    }

    public async Task<IGameFolderBookmarkItem> CreateGameFolderBookmarkAsync(string folderGamePath, IFolderBookmarkItem? parent)
    {
        var newItem = new GameFolderBookmarkItem(folderGamePath);
        await AddNewItemAsync(parent, newItem).ConfigureAwait(false);
        return newItem;
    }

    public record class BookmarkDataTransferFragment(List<BookmarkItem> BookmarkItems) : DataTransferFragment()
    { }

    public async Task<IReadOnlyList<IBookmarkItem>> CreateFromFragment(DataTransferFragment fragment, IFolderBookmarkItem? parent)
    {
        List<IBookmarkItem> results = new();

        if (fragment is BookmarkDataTransferFragment bookmarkFragment)
        {
            foreach (var item in bookmarkFragment.BookmarkItems)
            {
                var itemClone = item.DeepClone(newGuid: true);
                await AddNewItemAsync(parent, itemClone);
                results.Add(itemClone);
            }
        }
        else
        {
            _logger.LogWarning("Tried to create bookmark item(s) from a non-bookmark fragment!");
        }

        return results;
    }

    private async Task AddNewItemAsync(IFolderBookmarkItem? parent, BookmarkItem newItem)
    {
        await _bookmarkLibraryLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (parent is FolderBookmarkItem parentFolder)
            {
                newItem.ParentItem = parentFolder;
                AddSorted(parentFolder.ChildItems, newItem);
            }
            else
            {
                AddSorted(_rootItemsSorted, newItem);
            }
        }
        finally
        {
            _bookmarkLibraryLock.Release();
        }
    }

    public async Task<DataTransferFragment> SaveToFragment(IReadOnlyList<IBookmarkItem> items)
    {
        List<BookmarkItem> itemClones = new();

        await _bookmarkLibraryLock.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var item in items)
            {
                if (item is BookmarkItem bookmarkItem)
                {
                    itemClones.Add(bookmarkItem.DeepClone(newGuid: false));
                }
            }
        }
        finally
        {
            _bookmarkLibraryLock.Release();
        }

        return new BookmarkDataTransferFragment(itemClones);
    }

    public async Task DeleteAsync(IBookmarkItem bookmark)
    {
        if (bookmark is BookmarkItem bookmarkItem)
        {
            await _bookmarkLibraryLock.WaitAsync().ConfigureAwait(false);
            try
            {
                bool removed = TryRemoveSorted(bookmarkItem.ParentItem?.ChildItems ?? _rootItemsSorted, bookmarkItem);
                Debug.Assert(removed);
                bookmarkItem.ParentItem = null;
                bookmarkItem.IsDeleted = true;
            }
            finally
            {
                _bookmarkLibraryLock.Release();
            }

            // Delete is raised outside the bookmark library lock, so that handlers can perform operations in response that take the lock themselves
            bookmarkItem.RaiseDeleted();
        }
    }

    public async Task MoveAsync(IBookmarkItem bookmark, IFolderBookmarkItem? newParent)
    {
        // If the new parent is a child of the bookmark, that doesn't work (would result in an orphaned infinite loop)
        var parentToCheck = newParent;
        while (parentToCheck != null)
        {
            if (parentToCheck == bookmark)
            {
                _logger.LogWarning("User tried to move a bookmark into one of its own child folders!");
                return;
            }
            parentToCheck = parentToCheck.ParentItem;
        }

        if (bookmark is BookmarkItem bookmarkItem)
        {
            await _bookmarkLibraryLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (newParent != bookmark.ParentItem)
                {
                    bool removed = TryRemoveSorted(bookmarkItem.ParentItem?.ChildItems ?? _rootItemsSorted, bookmarkItem);
                    Debug.Assert(removed);
                    if (newParent != null && newParent is FolderBookmarkItem newParentFolder)
                    {
                        AddSorted(newParentFolder.ChildItems, bookmarkItem);
                        bookmarkItem.ParentItem = newParentFolder;
                    }
                    else
                    {
                        AddSorted(_rootItemsSorted, bookmarkItem);
                        bookmarkItem.ParentItem = null;
                    }
                }
            }
            finally
            {
                _bookmarkLibraryLock.Release();
            }
        }
    }

    public async Task SetFolderBookmarkNameAsync(IFolderBookmarkItem bookmark, string newName)
    {
        if (bookmark is FolderBookmarkItem folderBookmark)
        {
            await _bookmarkLibraryLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (newName != folderBookmark.Name)
                {
                    var siblingList = folderBookmark.ParentItem?.ChildItems ?? _rootItemsSorted;
                    bool found = TryRemoveSorted(siblingList, folderBookmark);
                    Debug.Assert(found);
                    folderBookmark.Name = newName;
                    AddSorted(siblingList, folderBookmark);
                }
            }
            finally
            {
                _bookmarkLibraryLock.Release();
            }
        }
    }

    public void Dispose()
    {
        _bookmarkLibraryLock.Dispose();
    }

    private static void AddSorted<T>(List<T> sortedList, T child)
        where T : IComparable<T>
    {
        var index = sortedList.BinarySearch(child);

        // If there are multiple matches, move past them to the end
        if (index >= 0)
        {
            while (sortedList[index].CompareTo(child) == 0)
            {
                index += 1;
            }
        }

        sortedList.Insert(index >= 0 ? index : ~index, child);
    }

    private static bool TryRemoveSorted<T>(List<T> sortedList, T child)
        where T : class, IComparable<T>
    {
        var index = sortedList.BinarySearch(child);
        if (index >= 0)
        {
            // There is at least one item with a matching sort string
            // It's likely that there's only one match and this is it
            if (sortedList[index] == child)
            {
                sortedList.RemoveAt(index);
                return true;
            }

            var startIndex = index;

            // First, walk backwards to look for a match
            while (index > 0 && sortedList[index - 1].CompareTo(child) == 0)
            {
                index -= 1;
                if (sortedList[index] == child)
                {
                    sortedList.RemoveAt(index);
                    return true;
                }
            }

            // Then, walk forwards to look for a match
            index = startIndex;
            while (index < sortedList.Count - 1 && sortedList[index + 1].CompareTo(child) == 0)
            {
                index += 1;
                if (sortedList[index] == child)
                {
                    sortedList.RemoveAt(index);
                    return true;
                }
            }

            // None of the matches were the item in question
            return false;
        }
        else
        {
            return false;
        }
    }
}

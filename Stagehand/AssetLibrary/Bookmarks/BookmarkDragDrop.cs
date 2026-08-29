using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Stagehand.AssetLibrary.Bookmarks;

public static class BookmarkDragDrop
{
    public static ReadOnlySpan<byte> DataTypeId => "BOOKMARK"u8;

    public static bool IsBookmarkPayload(ReadOnlySpan<byte> typeId)
    {
        return typeId.SequenceEqual(DataTypeId);
    }

    public static byte[] MakeDragPayload(IBookmarkItem bookmarkItem)
    {
        return Encoding.Unicode.GetBytes(bookmarkItem.ToBookmarkPath());
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, IAssetBookmarkService assetBookmarkService, [NotNullWhen(true)] out IBookmarkItem? bookmarkItem)
    {
        return assetBookmarkService.TryFindBookmarkItemFromPath(Encoding.Unicode.GetString(payload), out bookmarkItem);
    }
}

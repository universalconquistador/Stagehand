using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Stagehand.AssetLibrary.GameResources;

public static class GameFolderDragDrop
{
    public static ReadOnlySpan<byte> DataTypeId => "GAMEFOLDER"u8;

    public static bool IsGameFolderPayload(ReadOnlySpan<byte> typeId)
    {
        return typeId.SequenceEqual(DataTypeId);
    }

    public static byte[] MakeGameFolderPayload(string folderGamePath)
    {
        return Encoding.Unicode.GetBytes(folderGamePath);
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, [NotNullWhen(true)] out string? folderGamePath)
    {
        folderGamePath = Encoding.Unicode.GetString(payload);
        return true;
    }
}

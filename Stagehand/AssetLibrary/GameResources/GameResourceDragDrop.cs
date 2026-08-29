using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Stagehand.AssetLibrary.GameResources;

public static class GameResourceDragDrop
{
    public static ReadOnlySpan<byte> DataTypeId => "GAMERESOURCE"u8;

    public static bool IsGameResourcePayload(ReadOnlySpan<byte> typeId)
    {
        return typeId.SequenceEqual(DataTypeId);
    }

    public static byte[] MakeGameResourcePayload(string resourceGamePath)
    {
        return Encoding.Unicode.GetBytes(resourceGamePath);
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, [NotNullWhen(true)] out string? resourceGamePath)
    {
        resourceGamePath = Encoding.Unicode.GetString(payload);
        return true;
    }
}

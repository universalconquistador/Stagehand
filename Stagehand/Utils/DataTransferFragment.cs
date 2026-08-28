using Stagehand.AssetLibrary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Utils;

/// <summary>
/// The base class for objects serialized to & from JSON blobs for clipboard and disk transfer.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(AssetBookmarkService.BookmarkDataTransferFragment), typeDiscriminator: "BookmarkDataTransferFragment")]
public abstract record class DataTransferFragment()
{
    public string ToDataString()
    {
        return JsonSerializer.Serialize(this);
    }

    public async Task WriteToStream(Stream utf8JsonStream, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(utf8JsonStream, cancellationToken).ConfigureAwait(false);
    }

    public static DataTransferFragment? FromDataString(string dataString)
    {
        return JsonSerializer.Deserialize<DataTransferFragment>(dataString);
    }

    public static async Task<DataTransferFragment?> FromStream(Stream utf8JsonStream, CancellationToken cancellationToken)
    {
        return await JsonSerializer.DeserializeAsync<DataTransferFragment>(utf8JsonStream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

using Stagehand.AssetLibrary;
using Stagehand.Definitions.Serialization;
using Stagehand.Editor.DefinitionEditors.Objects;
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
[JsonDerivedType(typeof(ObjectDefinitionDataTransferFragment), typeDiscriminator: "ObjectDefinitionDataTransferFragment")]
public abstract record class DataTransferFragment()
{
    private static readonly JsonSerializerOptions _dataTransferFramentJsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new Vector2JsonConverter(),
            new Vector3JsonConverter(),
            new Vector4JsonConverter(),
        },
    };

    public string ToDataString()
    {
        return JsonSerializer.Serialize(this, _dataTransferFramentJsonOptions);
    }

    public async Task WriteToStream(Stream utf8JsonStream, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(utf8JsonStream, this, _dataTransferFramentJsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public static DataTransferFragment? FromDataString(string dataString)
    {
        try
        {
            return JsonSerializer.Deserialize<DataTransferFragment>(dataString, _dataTransferFramentJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<DataTransferFragment?> FromStream(Stream utf8JsonStream, CancellationToken cancellationToken)
    {
        return await JsonSerializer.DeserializeAsync<DataTransferFragment>(utf8JsonStream, _dataTransferFramentJsonOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

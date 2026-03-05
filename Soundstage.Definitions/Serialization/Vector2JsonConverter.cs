using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundstage.Definitions.Serialization;

/// <summary>
/// A <see cref="JsonConverter{T}"/> that serializes <see cref="Vector2"/> values as JSON objects with numeric X and Y properties.
/// </summary>
public class Vector2JsonConverter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected JSON object for System.Numerics.Vector2 value!");
        }
        reader.Read();

        Vector2 result = new Vector2();

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name in System.Numerics.Vector2 value!");
            }
            var propertyName = reader.GetString();
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException("Expected number value for System.Numerics.Vector2 property!");
            }
            var propertyValue = reader.GetSingle();
            reader.Read();

            switch (propertyName)
            {
                case nameof(Vector2.X):
                    result.X = propertyValue;
                    break;
                case nameof(Vector2.Y):
                    result.Y = propertyValue;
                    break;
                default:
                    throw new JsonException($"'{propertyName}' is not a valid property of System.Numberics.Vector2!");
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber(nameof(Vector2.X), value.X);
        writer.WriteNumber(nameof(Vector2.Y), value.Y);

        writer.WriteEndObject();
    }
}

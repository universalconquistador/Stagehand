using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stagehand.Definitions.Serialization;

/// <summary>
/// A <see cref="JsonConverter{T}"/> that serializes <see cref="Vector3"/> values as JSON objects with numeric X, Y, and Z properties.
/// </summary>
public class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected JSON object for System.Numerics.Vector3 value!");
        }
        reader.Read();

        Vector3 result = new Vector3();

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name in System.Numerics.Vector3 value!");
            }
            var propertyName = reader.GetString();
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException("Expected number value for System.Numerics.Vector3 property!");
            }
            var propertyValue = reader.GetSingle();
            reader.Read();

            switch (propertyName)
            {
                case nameof(Vector3.X):
                    result.X = propertyValue;
                    break;
                case nameof(Vector3.Y):
                    result.Y = propertyValue;
                    break;
                case nameof(Vector3.Z):
                    result.Z = propertyValue;
                    break;
                default:
                    throw new JsonException($"'{propertyName}' is not a valid property of System.Numberics.Vector3!");
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber(nameof(Vector3.X), value.X);
        writer.WriteNumber(nameof(Vector3.Y), value.Y);
        writer.WriteNumber(nameof(Vector3.Z), value.Z);

        writer.WriteEndObject();
    }
}

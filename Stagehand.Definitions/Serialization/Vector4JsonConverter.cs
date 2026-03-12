using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stagehand.Definitions.Serialization;

/// <summary>
/// A <see cref="JsonConverter{T}"/> that serializes <see cref="Vector4"/> values as JSON objects with numeric X, Y, Z, and W properties.
/// </summary>
public class Vector4JsonConverter : JsonConverter<Vector4>
{
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected JSON object for System.Numerics.Vector4 value!");
        }
        reader.Read();

        Vector4 result = new Vector4();

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name in System.Numerics.Vector4 value!");
            }
            var propertyName = reader.GetString();
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException("Expected number value for System.Numerics.Vector4 property!");
            }
            var propertyValue = reader.GetSingle();
            reader.Read();

            switch (propertyName)
            {
                case nameof(Vector4.X):
                    result.X = propertyValue;
                    break;
                case nameof(Vector4.Y):
                    result.Y = propertyValue;
                    break;
                case nameof(Vector4.Z):
                    result.Z = propertyValue;
                    break;
                case nameof(Vector4.W):
                    result.W = propertyValue;
                    break;
                default:
                    throw new JsonException($"'{propertyName}' is not a valid property of System.Numberics.Vector4!");
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber(nameof(Vector4.X), value.X);
        writer.WriteNumber(nameof(Vector4.Y), value.Y);
        writer.WriteNumber(nameof(Vector4.Z), value.Z);
        writer.WriteNumber(nameof(Vector4.W), value.W);

        writer.WriteEndObject();
    }
}

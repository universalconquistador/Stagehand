using Stagehand.Definitions.Objects;
using Stagehand.Definitions.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stagehand.Definitions;

/// <summary>
/// Informational metadata about a Stage definition.
/// </summary>
public struct StageInfo
{
    /// <summary>
    /// The display name of the Stage definition.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The name of the author(s) of the Stage definition.
    /// </summary>
    public string AuthorName { get; set; } = "";

    /// <summary>
    /// A user-facing string describing the version of the Stage definition.
    /// </summary>
    public string VersionString { get; set; } = "";

    /// <summary>
    /// A description of the Stage definition.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// The territory type (see the <c>TerritoryType</c> Excel sheet) that the Stage definition
    /// is intended to be shown in.
    /// </summary>
    public int IntendedTerritoryType { get; set; }

    public StageInfo()
    { }
}

/// <summary>
/// Defines a collection of objects that can be loaded and unloaded from the FFXIV scene as a unit by the Stagehand plugin.
/// </summary>
/// <remarks>
/// These are loaded from and saved to <c>.json</c> files using the <see cref="StandardSerializerOptions"/>,
/// and from and to IPC API compatible strings using <see cref="ToDefinitionString"/> and <see cref="TryParseDefinitionString(string, out StageDefinition?)"/>.
/// </remarks>
public class StageDefinition
{
    /// <summary>
    /// The serialization options to be used when serializing and deserializing Stage definitions.
    /// </summary>
    public static readonly JsonSerializerOptions StandardSerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 4,
        Converters =
        {
            new JsonStringEnumConverter(),
            new Vector2JsonConverter(),
            new Vector3JsonConverter(),
            new Vector4JsonConverter(),
        },
    };

    /// <summary>
    /// The serialization options to be used when serializing and deserializing Stage definitions
    /// for use with the IPC API.
    /// </summary>
    public static readonly JsonSerializerOptions DefinitionStringSerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(),
            new Vector2JsonConverter(),
            new Vector3JsonConverter(),
            new Vector4JsonConverter(),
        },
    };

    /// <summary>
    /// The metadata about this Stage definition.
    /// </summary>
    public StageInfo Info { get; set; } = new StageInfo();

    /// <summary>
    /// The objects in this Stage definition, identified by unique string identifiers.
    /// </summary>
    public Dictionary<string, ObjectDefinition> Objects { get; set; } = new Dictionary<string, ObjectDefinition>();

    /// <summary>
    /// Writes this Stage definition to the given stream in JSON format.
    /// </summary>
    public void WriteToJSONStream(Stream destination)
    {
        JsonSerializer.Serialize(destination, this, StandardSerializerOptions);
    }

    /// <summary>
    /// Attempts to load a Stage definition from the given stream in JSON format.
    /// </summary>
    /// <param name="source">The stream to read the definition from.</param>
    /// <param name="definition">The definition that was parsed, or null if parsing failed.</param>
    /// <returns>Whether a Stage definition was successfully parsed from the given stream.</returns>
    public static bool TryParseJSONStream(Stream source, [NotNullWhen(true)] out StageDefinition? definition)
    {
        try
        {
            definition = JsonSerializer.Deserialize<StageDefinition>(source, DefinitionStringSerializerOptions);
            return definition != null;
        }
        catch (JsonException)
        {
            definition = null;
            return false;
        }
    }

    /// <summary>
    /// Creates a definition string from this Stage definition suitable for use with the Stagehand IPC API.
    /// </summary>
    public string ToDefinitionString()
    {
        return JsonSerializer.Serialize(this, DefinitionStringSerializerOptions);
    }

    /// <summary>
    /// Attempts to load the given Stage definition string from the IPC API format.
    /// </summary>
    /// <param name="definitionString">The definition string in IPC API format, as created by <see cref="ToDefinitionString"/>.</param>
    /// <param name="definition">The definition that was parsed, or null if parsing failed.</param>
    /// <returns>Whether a Stage definition was successfully parsed from the given string.</returns>
    public static bool TryParseDefinitionString(string definitionString, [NotNullWhen(true)] out StageDefinition? definition)
    {
        try
        {
            definition = JsonSerializer.Deserialize<StageDefinition>(definitionString, DefinitionStringSerializerOptions);
            return definition != null;
        }
        catch (JsonException)
        {
            definition = null;
            return false;
        }
    }
}

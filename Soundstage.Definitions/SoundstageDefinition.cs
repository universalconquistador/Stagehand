using Soundstage.Definitions.Objects;
using Soundstage.Definitions.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundstage.Definitions;

/// <summary>
/// Informational metadata about a Soundstage definition.
/// </summary>
public struct SoundstageInfo
{
    /// <summary>
    /// The display name of the Soundstage definition.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The name of the author(s) of the Soundstage definition.
    /// </summary>
    public string AuthorName { get; set; } = "";

    /// <summary>
    /// A user-facing string describing the version of the Soundstage definition.
    /// </summary>
    public string VersionString { get; set; } = "";

    /// <summary>
    /// A description of the Soundstage definition.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// The territory type (see the <c>TerritoryType</c> Excel sheet) that the Soundstage definition
    /// is intended to be shown in.
    /// </summary>
    public int IntendedTerritoryType { get; set; }

    public SoundstageInfo()
    { }
}

/// <summary>
/// Defines a collection of objects that can be loaded and unloaded from the FFXIV scene as a unit by the Soundstage plugin.
/// </summary>
/// <remarks>
/// These are loaded from and saved to <c>.json</c> files using the <see cref="StandardSerializerOptions"/>,
/// and from and to IPC API compatible strings using <see cref="ToDefinitionString"/> and <see cref="TryParseDefinitionString(string, out SoundstageDefinition?)"/>.
/// </remarks>
public class SoundstageDefinition
{
    /// <summary>
    /// The serialization options to be used when serializing and deserializing Soundstage definitions.
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
    /// The serialization options to be used when serializing and deserializing Soundstage definitions
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
    /// The metadata about this Soundstage definition.
    /// </summary>
    public SoundstageInfo Info { get; set; } = new SoundstageInfo();

    /// <summary>
    /// The objects in this Soundstage definition, identified by unique string identifiers.
    /// </summary>
    public Dictionary<string, ObjectDefinition> Objects { get; set; } = new Dictionary<string, ObjectDefinition>();

    /// <summary>
    /// Creates a definition string from this Soundstage definition suitable for use with the Soundstage IPC API.
    /// </summary>
    public string ToDefinitionString()
    {
        return JsonSerializer.Serialize(this, StandardSerializerOptions);
    }

    /// <summary>
    /// Attempts to load the given Soundstage definition string from the IPC API format.
    /// </summary>
    /// <param name="definitionString">The definition string in IPC API format, as created by <see cref="ToDefinitionString"/>.</param>
    /// <param name="definition">The definition that was parsed, or null if parsing failed.</param>
    /// <returns>Whether a Soundstage definition was successfully parsed from the given string.</returns>
    public static bool TryParseDefinitionString(string definitionString, [NotNullWhen(true)] out SoundstageDefinition? definition)
    {
        try
        {
            definition = JsonSerializer.Deserialize<SoundstageDefinition>(definitionString);
            return definition != null;
        }
        catch (JsonException parseException)
        {
            definition = null;
            return false;
        }
    }
}

&nbsp;
![Stagehand Logo](Stagehand%20Logo%202x.png)

# `Stagehand.Definitions`

The `Stagehand.Definitions` package contains the definition data used for storing and transferring Stagehand definitions.


## Definitions

Stagehand definitions (`StagehandDefinition`) primarily consist of `StagehandInfo`, which contains informational metadata for the player about the definition, and `ObjectDefinition` instances that define the objects to create.


## JSON Loading & Saving

To read and write Stagehand definitions from/to .json files, use the `TryParseJSONStream` and `WriteToJSONStream` methods. These methods use standardized serialization options including support for serializing vector values as JSON objects and enum values as strings.

To load a Stagehand definition from a stream of JSON data, use `StagehandDefinition.TryParseJSONStream()`, like so:
```csharp
using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
{
    if (StagehandDefinition.TryParseJSONStream(stream, out var definition))
    {
        // Use `definition`
    }
    else
    {
        // Failed to parse
    }
}
```

To write a Stagehand definition to a JSON stream, use `StagehandDefinition.WriteToJSONStream()`, like so:
```csharp
using (var stream = new FileStream(filename, FileMode.Create, FileAccess.Write))
{
    definition.WriteToJSONStream(filename);
}
```


## IPC String Loading & Saving

To serialize and deserialize Stagehand definitions to/from IPC-compatible strings, use the `ToDefinitionString` and `TryParseDefinitionString`. At the moment these are just non-prettified JSON strings, but in the future they will become Penumbra-style base64-encoded Gzipped JSON strings.

Example:

```csharp
// Serialize to IPC string
var ipcString = definition.ToDefinitionString();

// Deserialize back to definition
if (StagehandDefinition.TryParseDefinitionString(ipcString, out var newDefinition))
{
    // Use `newDefinition`
}
else
{
    // Failed to parse
}
```


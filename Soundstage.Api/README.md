&nbsp;
![Soundstage Logo](Soundstage%20Logo%202x.png)

# `Soundstage.Api`

The `Soundstage.Api` package contains the interface for interacting with the [Soundstage](https://github.com/universalconquistador/Soundstage) FFXIV plugin via Dalamud IPC, and helper functions in `Soundstage.Api.SoundstageApi` to initialize the IPC connection.


## Overview

There are four easy steps to using the Soundstage API:

1. Add a reference to this package to your plugin project:
   ```xml
   <PackageReference Include="Soundstage.Api" Version="<current version>" />
   ```
2. Call `SoundstageApi.CreateIpcClient()`, passing in the `IDalamudPluginInterface` provided to your plugin by Dalamud:
   ```csharp
   var soundstageApi = Soundstage.Api.SoundstageApi.CreateIpcClient(myDalamudPluginInterface);
   ```
3. Use the returned value to interact with the Soundstage plugin:
   ```csharp
   var visibleSoundstageIds = soundstageApi.GetVisibleSoundstageIds(includeLocal: true, includeTemporary: true, includeEditing: true);
   ```
4. Dispose the IPC client when done:
   ```csharp
   soundstageApi.Dispose();
   ```


## API Details

The Soundstage API includes methods for querying the local soundstage definitions, creating and destroying temporary soundstages, and showing and hiding both local and temporary soundstages.

For more specific details, see [ISoundstageApi.cs](ISoundstageApi.cs).

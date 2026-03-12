&nbsp;
![Stagehand Logo](Stagehand%20Logo%202x.png)

# `Stagehand.Api`

The `Stagehand.Api` package contains the interface for interacting with the [Stagehand](https://github.com/universalconquistador/Stagehand) FFXIV plugin via Dalamud IPC, and helper functions in `Stagehand.Api.StagehandApi` to initialize the IPC connection.


## Overview

There are four easy steps to using the Stagehand API:

1. Add a reference to this package to your plugin project:
   ```xml
   <PackageReference Include="Stagehand.Api" Version="<current version>" />
   ```
2. Call `StagehandApi.CreateIpcClient()`, passing in the `IDalamudPluginInterface` provided to your plugin by Dalamud:
   ```csharp
   var StagehandApi = Stagehand.Api.StagehandApi.CreateIpcClient(myDalamudPluginInterface);
   ```
3. Use the returned value to interact with the Stagehand plugin:
   ```csharp
   var visibleStagehandIds = StagehandApi.GetVisibleStagehandIds(includeLocal: true, includeTemporary: true, includeEditing: true);
   ```
4. Dispose the IPC client when done:
   ```csharp
   StagehandApi.Dispose();
   ```


## API Details

The Stagehand API includes methods for querying the local Stagehand definitions, creating and destroying temporary Stagehands, and showing and hiding both local and temporary Stagehands.

For more specific details, see [IStagehandApi.cs](IStagehandApi.cs).

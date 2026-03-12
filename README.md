&nbsp;
![Stagehand Logo](Stagehand%20Logo%202x.png)

Stagehand is an FFXIV plugin that lets the player place extra visual objects into the game world and manage them in sets called Stagehands.


## Installing

(TODO: Add repo URL)

## Usage

Use the `/stagehand` command to bring up the main UI.

The left side of the main window lists the Stages in your Stages folder (by default in `C:\Users\<username>\Documents\Stages\`).
The right side of the main window shows info about the selected Stagehand and lets you edit it or assign it to automatically load for specific locations.

So far there is no editor for Stage definitions, so you have to edit the `.json` files by hand to match up with the `StageDefinition` class. Work on an editor is underway.


## Developers

### Repo Layout

Stagehand is made up of several C# projects:

**`Stagehand.Definitions`**: The object model for Stage `.json` files and serialized strings used in IPC.  
[![NuGet Version](https://img.shields.io/nuget/v/Stagehand.Api)](https://www.nuget.org/packages/Stagehand.Api/)

**`Stagehand.Api`**: A library for other plugins to interact with the Stagehand plugin via Dalamud IPC.  
[![NuGet Version](https://img.shields.io/nuget/v/Stagehand.Definitions)](https://www.nuget.org/packages/Stagehand.Definitions/)


**`Stagehand`**: The Stagehand FFXIV plugin itself, including (but not limited to):
 - The `Stagehand.Live` namespace, containing classes for creating objects ingame from Stage definitions
 - The `Stagehand.Windows` namespace, containing the Dear ImGui windows that make up the plugin's user interface
 - The `Stagehand.Editor` namespace, containing all the code for the definition editor


### IPC (NOT YET IMPLEMENTED)

The Stagehand IPC API is defined in the `Stagehand.Api.IStagehandApi` interface.
It lets plugins query the local Stage definitions, create and destroy temporary Stagehands, and show and hide both local and temporary Stagehands.

To get started, reference the `Stagehand.Api` NuGet package and call `StagehandApi.CreateIpcClient()`.
For more information, see the the [README](Stagehand.Api/README.md).


## Definitions

To inspect, modify, or generate Stage definitions, use the `Stagehand.Definitions` package.
For more information, see the [README](Stagehand.Definitions/README.md).


## Human Project

This whole thing is designed and typed by humans, so far just myself (UniversalConquistador), for you, for the joy of it.

&nbsp;
![Soundstage Logo](Soundstage%20Logo%202x.png)

Soundstage is an FFXIV plugin that lets the player place extra visual objects into the game world and manage them in sets called soundstages.


## Installing

(TODO: Add repo URL)

## Usage

Use the `/soundstage` command to bring up the main UI.

The left side of the main window lists the soundstages in your Soundstage folder (by default in `C:\Users\<username>\Documents\Soundstages\`).
The right side of the main window shows info about the selected soundstage and lets you edit it or assign it to automatically load for specific locations.

So far there is no editor for soundstage definitions, so you have to edit the `.json` files by hand to match up with the `SoundstageDefinition` class. Work on an editor is underway.


## Developers

### Repo Layout

Soundstage is made up of several C# projects:

**`Soundstage.Definitions`**: The object model for soundstage `.json` files and serialized strings used in IPC. Available as a NuGet package.

**`Soundstage.Api`**: A library for other plugins to interact with the Soundstage plugin via Dalamud IPC. Available as a NuGet package.

**`Soundstage`**: The Soundstage FFXIV plugin itself, including (but not limited to):
 - The `Soundstage.Live` namespace, containing classes for creating objects ingame from soundstage definitions
 - The `Soundstage.Windows` namespace, containing the Dear ImGui windows that make up the plugin's user interface
 - The `Soundstage.Editor` namespace, containing all the code for the definition editor


### IPC (NOT YET IMPLEMENTED)

The Soundstage IPC API is defined in the `Soundstage.Api.ISoundstageApi` interface.
It lets plugins query the local soundstage definitions, create and destroy temporary soundstages, and show and hide both local and temporary soundstages.

To get started, reference the `Soundstage.Api` NuGet package and call `SoundstageApi.CreateIpcClient()`.
For more information, see the `ISoundstageApi` interface itself.


## Human Project

This whole thing is designed and typed by humans, so far just myself (UniversalConquistador), for you, for the joy of it.

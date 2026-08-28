# Stagehand Release Notes

## 0.3.0

This is the first testing release of Stagehand! A lot of planned stuff is not yet implemented, but I would love to hear as much feedback as you can give regardless!

NOTE: The existence of Stagehand is not top secret, but I would appreciate you not sharing it around yet as I want to make a good first impression which will involve finishing up the unfinished spots. I don't want people writing it off because it's as of yet unfinished. Thanks! =D

Quickstart guide:
1. Open the Stagehand library with `/stagehand` and click the `New` button and give it a name for your Stage.
2. Select your new Stage and click the `Open Editor` button.
3. Click the `Create` dropdown in the editor and select `Background Object`.
4. Paste in a model path, for example `bg/ex4/04_uvs_u5/fld/u5f1/bgparts/u5f1_v1_mir01.mdl` (if you have Endwalker) or `bgcommon/world/aet/001/bgparts/w_aet_001_04a.mdl`.
5. Select the Move, Rotate, and Scale tools and use them to position the object.
6. Save and exit the editor.
7. Manually show and hide your stage with the `Show` and `Hide` buttons, or expand the `Add Auto Load Location` section and specify a zone (and server and optionally housing info) and then click `Add`.
8. Click the `My Stages` heading to open your Stage folder. Share your stages with your fellow Stagehand friends!

Notes:
 - Models that are part of housing items support dyeing! (Could this be because of the .shpk the models use?)
 - You can use any static model in the game, not just housing items. Go crazy! I particularly look forward to seeing void builds with rolling countrysides, naval battles, coliseums, etc.
 - To find VFX or model paths, either use the Penumbra resource logger or the `/stagehanddebug` command which will let you inspect the objects onscreen and click to copy model and vfx paths.

Known issues:
 - Settings button does not open the Settings
 - Settings page is very unfinished (press Enter to save, in the meantime)

Not yet implemented: (in no particular order)
 - Better stage library tools (delete, rename, make folder, etc), although if you put your stages into folders via the file explorer, those will be reflected in the plugin
 - ~~Click-to-select~~
 - ~~Undo + redo~~
 - ~~3D light widgets to show spot light cone angle, etc~~
 - Better resource browsing for model & VFX resources
 - Public website for documentation
 - ~~IPC API to facilitate e.g. sync services~~

Later down the road:
 - Support for modded models, weapons, and VFX
 - Housing object import (and .sgb layouts in general)
 - Object folders for organization
 - Hotkey support in editor
 - Interactive placement tools
 - Saveable prefabs
 - Looping animations


 Thank you for helping me with my plugin! Please leave any feedback, bugs, suggestions, thoughts, cool builds, etc! (Keeping in mind the known issues & not yet implemented features above)

## 0.3.1

 - Adds a 'Duplicate' command to the right click menu of objects in the Stage editor.
 - Prevents users from accidentally showing a Stage while editing it.

## 0.3.2

 - Lots of behind the scenes code that makes selecting objects with the debug tool much nicer! When using the 'Start Picking' button, the old coarse box-based selection has been replaced with pixel-perfect selection. It also now ignores VFX and lights and characters, as well as any model with 'lightshaft' in the name (because light shaft meshes were very commonly getting in the way when trying to select objects indoors). Furthermore, this sets me up to be able to add click selection in the editor very soon!

## 0.3.3

 - Mouse selection is here! The Select, Move, Rotate, and Scale tools now let you select objects in your Stage by clicking on them! The code involved to convince the game to do this is somewhat tricky and right now there is a known quirk where a game object (e.g. a character) cannot be selected with the mouse if they are in front (intended) or behind (not intended) a Stage object. I will continue to refine this in the future, but if you experience any other quirks or UX feelsbads please do let me know.

## 0.3.4

 - Added light widgets for all four shapes of lights and tweaked selection color and thickness. The flat light skew angle visualization is not *quite* correct but it's going to have to suffice for now.

## 0.3.5

 - Prevented the Stagehand library and editor windows from hiding during gpose.
 - Updated to for compatability with FFXIV version `2026.03.17.0000.0000`.

## 0.3.6

 - Added undo & redo system to the editor! No keybinds for it yet though, sorry. I had to write a little bit of jank spaghetti code to handle dragging property sliders as one action, so please do let me know if you manage to break it.
 - Fixed the visual size of a selected weapon's bounding box.

## 0.3.7

 - Added click support to weapons in the editor, and adjusted how they compute their selected outline to be more accurate.
 - Fixed click-drag edits showing the old value in the undo/redo tooltip rather than the new value.
 - Fixed choosing a weapon becoming many partial undo steps.

## 0.3.8

 - Adds the Asset Library window for browsing game assets. Currently it shows the `.mdl` and `.avfx` assets referenced by the game's environments. You can show the Asset Library window from the button in the main Stagehand window, or from the button next to the Model and VFX properties in the editor.
 - You can optionally preview assets ingame when you hover over them in the Asset Library, and if you are editing a Stage you can easily add them with one click.
 - When adding a new auto load location, the 'Use current location' button will now enable the world, ward, and house filters as appropriate.
 - Small polish items on the auto load condition UI.
 - The Stage is now selected by default when opening the editor.
 - Fixed a bug displaying folders in the Stage library.

## 0.3.9

 - Updates to Dalamud API 15
 - Minor bugfixes and refactoring

## 0.3.10

 - Adds periodic autosave while the editor is open with unsaved changes (every 30 seconds). By default these are saved to `AppData\Roaming\XIVLauncher\pluginConfigs\Stagehand\autosave\`, but you can select any folder in the Stagehand settings.
 - Reworks the editor's close button so that you don't accidentally discard your changes without saving.
 
## 0.4.0

 - Adds initial modding support! Background objects should work great, I haven't tested VFX yet.
 - Modded files on disk are not fully implemented yet--use the embedded option instead.
 - Needs some UX around reusing modpacks from one Stage to another.
 - Modding support is by far the most tricky and fragile part of Stagehand (so far), so please PLEASE say something if you run into Stagehand crashes!

## 0.4.1

 - The initial IPC API is here! Developers can now use the [Stagehand.Api package](https://www.nuget.org/packages/Stagehand.Api/) and the [Stagehand.Definitions package](https://www.nuget.org/packages/Stagehand.Definitions/) to do awesome stuff programmatically with Stagehand! There is a demo plugin showing how to use each part of the API in the [Stagehand GitHub repository](https://github.com/universalconquistador/Stagehand).
 - Adds a draggable horizontal splitter to the editor so you can see more objects or more properties!
 - Adds an option in the Asset Library to preview the hovered asset using the transform of the targeted game object.

## 0.4.2 & 0.4.3

 - Fixes an issue I caused where lights and object dyes were not correctly being applied when a stage was shown.

## 0.4.4

 - Adds sounds! You can place looping instances of sounds from .scd resources and configure their volume, playback speed, and fade in time.
 - Additionally, you can choose for a sound to be positional so that it sounds like it's coming from where you place the object.
 - Note that each `.scd` file can have multiple sounds in it, not just one! Use the Sound Index property on your sounds to specify which sound in the `.scd` resource to play. You can use the VfxEditor plugin's Sounds tab to load up an `.scd` resource and view the sounds within it.
 - The Asset Library has been updated with all the `.scd` resources in the vanilla game *that are placed in zones*. I want to expand this to all the other sounds used in the game, but until then you can use the VfxEditor plugin to explore the rest of the game's `.scd` resources.

## 0.4.5

 - Adds the ability to hide objects in the editor. Hidden objects have no effect on the scene, which is useful for many things like WIP stuff you don't want to get rid of or keeping a palette of base objects to duplicate.

## 0.4.6

 - Fixes UI issues with DPI scaling, mostly in the editor.

## 0.4.7

 - Adds keybinds! These are completely customizable in the Stagehand settings.
    - Undo: Ctrl + Z
    - Redo: Ctrl + Shift + Z
    - Save: Ctrl + S
    - Duplicate Selected Object: Ctrl + D
    - Delete Selected Object: Delete
    - Hide Selected Object: Ctrl + H
    - Unhide Selected Object: Ctrl + Shift + H

## 0.4.8

 - Fixes bug getting the current location
 - Adds IPC event for when the user saves a stage in the editor

## 0.4.9

 - Adds bookmarks to the Asset Library! You can find them in the aptly-named Bookmarks tab.
   - Create folders using the New Folder button up top (double click on a folder's name or right click on it to rename it)
   - Add bookmarks to game resources and game folders by right clicking them in the Game Resources tab! You can bookmark something as many times as you like, so you can organize them however you please.
   - Copy & paste via right clicking! You can even paste and send the text to someone over chat (although it's not particularly pretty)
   - Drag and drop to move items between folders
 - Adds asset type filtering to the Asset Library's Game Resources tab and the Bookmarks tab! This should make it much easier to browse sounds and VFX.
 - The preview objects created by the asset library now stick around when you select an asset. To make them go away you can click in the empty space, select a folder, or click the square X button at the top right.
 - Adds distinct creation options for each kind of light.
 - Adjusts light creation to place new lights at the camera's position and orient them along your view.
 - Tweaks a few of the default values for lights to be more practical.
 - Fixes autosave triggering an editor saved IPC event.

## 0.4.10

 - Adds visibility caching to the Game Resources tree to address performance issues with very large numbers of visible items.

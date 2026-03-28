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
 - IPC API to facilitate e.g. sync services

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

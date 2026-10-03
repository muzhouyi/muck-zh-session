![BepInEx logo](https://avatars2.githubusercontent.com/u/39589027?s=256)

# BepInExPack for Muck

This is [BepInEx 5.4.11](https://github.com/BepInEx/BepInEx) pack for Muck.

BepInEx is a general purpose framework for Unity modding.
BepInEx includes tools and libraries to

* load custom code (hereafter *plugins*) into the game on launch;
* patch in-game methods, classes and even entire assemblies without touching original game files;
* configure plugins and log game to desired outputs like console or file;
* manage plugin dependencies.

BepInEx is currently [one of the most popular modding tools for Unity on GitHub](https://github.com/topics/modding?o=desc&s=stars).

## This pack's contents

This pack is preconfigured and usable for Muck modding.  
In particular, this pack comes with preconfigured `BepInEx.cfg` that enables the BepInEx console and more extensive logging.

## Installation (game, automated)

This is the recommended way to install BepInEx on the game.

1. Download and install [Thunderstore Mod Manager](https://www.overwolf.com/app/Thunderstore-Thunderstore_Mod_Manager) or [r2modman](https://Muck.thunderstore.io/package/ebkr/r2modman/)
2. Click **Install with Mod Manager** button on top of the page
3. Run the game via the mod manager

## Installation (manual)

If you are installing this manually, do the following

1. Extract the archive into a folder. **Do not extract into the game folder.**
2. Move the contents of `BepInExPack_Muck` folder into the game folder (where the game executable is located).
3. Run the game. If everything runs correctly, you will see BepInEx console pop up on your desktop.
4. Follow either Windows or Linux game running instructions below:

### Configuration (Windows)

No need to configure. Simply run the game. If everything is correct, you will see a console pop up.

### Configuration (Linux)

1. Make `start_game_bepinex.sh` executable with `chmod u+x start_game_bepinex.sh`.
2. In Steam, go to game's properties and set game's launch arguments to
    
	```
	./start_game_bepinex.sh %command%
	```
	
	Note to advanced users: You can change `./start_game_bepinex.sh` to full path to the script. This way you can install this pack entirely outside of game folder.
3. Run the game via Steam.

At this moment you will not see any clear indication if BepInEx works.
It is suggested to test by installing a simple plugin such as [MessageCenter from BepInEx.Utility](https://github.com/BepInEx/BepInEx.Utility).

## Useful links

* [BepInEx: writing basic plugin walkthrough](https://bepinex.github.io/bepinex_docs/v5.4.4/articles/dev_guide/plugin_tutorial/index.html)
* [BepInEx: useful plugins for modding](https://bepinex.github.io/bepinex_docs/v5.4.4/articles/dev_guide/dev_tools.html)
* [BepInEx: patching game methods at runtime](https://bepinex.github.io/bepinex_docs/v5.4.4/articles/dev_guide/runtime_patching.html)

## Issues, questions, etc.

At this moment, you can use the following channels to ask for help

* [Muck Modding Community](https://discord.gg/M27wjFAfTQ)
* [BepInEx Discord](https://discord.gg/MpFEDAg) -- **Only technical support for THIS PACKAGE. No support for plugins.**

## Changelog

#### 5.4.1101

* Added components to run BepInEx on *nix systems

#### 5.4.1100

* Initial release with BepInEx 5.4.11
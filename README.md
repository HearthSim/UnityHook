# UnityHook

UnityHook is a simple platform for hooking managed function calls, targeting
specifically assemblies compiled for [Unity3d](http://unity3d.com/) games.

Installed hooks allow overriding hooked functions' return values, essentially
granting complete control over managed code execution in a targeted game.

## Build

1. Clone the repo
2. Open UnityHook solution file with Visual Studio
3. Build project Hooker

## Usage

The compiled files and dependancies can be found at `$REPO_PATH/Hooker/bin/{DEBUG|RELEASE}` and the instructions suppose you have a terminal open at that directory.
* Execute: `Hooker.exe $GAME_LIBRARY_FOLDERPATH $HOOKS_FILEPATH`
    * $GAME_LIBRARY_FOLDERPATH is the parent folder of the assembly files to patch;
    * $HOOKS_FILEPATH is the file which declares the methods to hook. See `$REPO_PATH/Hooker/example_hooks` for examples.
    

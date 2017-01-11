# UnityHook

UnityHook is a simple platform for hooking managed function calls, targeting
specifically assemblies compiled for [Unity3d](http://unity3d.com/) games.

Installed hooks allow overriding hooked functions' return values, essentially
granting complete control over managed code execution in a targeted game.

## Build

1. Clone the repo;
2. Open UnityHook solution file with Visual Studio;
3. Build project Hooker;
4. Build project HookRegistry;
5. All binary files can be found inside the `bin` folder of each project.

## Usage

**Hooker** is the project that actually injects code into original game assemblies/libraries (== .dll files).
It can be used to 'hook' and 'restore' assemblies. Run `hooker.exe help` for information about the options to pass.

**HookRegistry** is the project that contains code to be executed when a hooked method/function has been called
while the game is running. The project compiles to 1 binary file that must be passed to **Hooker**.

>~Hooks file~ is the file which contains all methods to be hooked. See `{REPOPATH}\Hooker\example_hooks` for more information
it's about syntax. The example_hooks file is needed for the example at the next section.
**NOTE:** The hooker will hook all methods entered in this file. Undefined behaviour will occur if **HooksRegistry** contains no 
matching functionality for each method!   

## Example

**Create a connection to the server without SSL**

What you need:

    - The (compiled) binaries from **Hooker**. Refered to as {HOOKERPATH};
    - The (compiled) binary from **HookRegistry**. Referred to as {REGISTRY};
    - The location of the game folder. Referred to as {GAMEDIR};
    - A hooks file, as mentioned above. Referred to as {HOOKS}.
    
Steps:

    1. Call Hooker.exe, `{HOOKERPATH}\Hooker.exe -d "{GAMEDIR"} -h "{HOOKS}" -l "{REGISTRY}"`
    2. Verify that that Hooker did not encounter a problem;
        - Requested methods are hooked
        - Original assemblies are duplicated next to the original as {name}.original.dll
        - Patched assemblies are written to the game directory as {name}.out.dll
        - Patched assemblies replace the original assemblies
        - **HookRegistry** assembly is copied next to the game assemblies
    3. Run the Game - Watch the game log for lines starting with [HOOKER].
# UnityHook

UnityHook is a simple platform for hooking managed function calls, targeting
specifically assemblies compiled for [Unity3d](http://unity3d.com/) games.

Installed hooks allow overriding hooked functions' return values, essentially
granting complete control over managed code execution in a targeted game.

# Code formatting

This project uses [AStyle](http://astyle.sourceforge.net/) to keep it's contents formatted.
Take the following steps to format all source code:

1. Download [AStyle](http://astyle.sourceforge.net/)
2. If needed, compile, and add the binary to your PATH variable
3. Run the formatter on all \*.cs files with the formatting options file found in the root of the repo.
When using the recursive option, don't let your terminal expand the wildcard. 
AStyle is capable of handling the wildcard itself.
eg; ```astyle.exe --options=hearthsim_codestyle.ini --recursive "./*.cs"```

# Minimal dependancies

This branch tries to keep the amount of compile-time dependancies as small as possible. Minimal compile-time dependancies 
make it easier to distribute the **HooksRegistry** binary (and independant of **Hooker**). All compiled binaries are more robust 
and have a higher chance of working when new game versions are released. This also has a big downside; every method call 
has to be done in a dynamic way and complex operations are tedious because of the dynamic overhead.

>Because of the tedious work it also forces us to come up with more 'outside of the box' solutions. This alone makes the projects
interesting and challenging to work on!

## Build

1. Clone the repo;
2. Open UnityHook solution file with Visual Studio;
3. Build project **Hooker**;
4. Build project **HookRegistry**;
5. All binary files can be found inside the `bin` folder of each project.

## Hooker

**Hooker** is the project that actually injects code into original game assemblies/libraries (= .dll files).
It can be used to 'hook' and 'restore' assemblies. Run `hooker.exe help` for information about the options to pass.
To hook game assemblies you need to tell it the location of the game folder and the path to a compiled **HookRegistry**.
The **Hooker** currently has support for following games

- Hearthstone

## HookRegistry

**HookRegistry** is the project that contains code to be executed when a hooked method/function has been called
while the game is running. The project compiles to 1 binary file that must be passed to **Hooker**.
Currently implemented hooks are following

- Hearthstone
    - Disable SSL connection between client/server;
    - Duplicate packets transferred between client/server to other TCP streams. These streams try to attach to the
    [HearthStone packet dumper tool](https://github.com/HearthSim/Hearthstone-Packet-Dumps/tree/master/tools). 
- General
    - Hooking into the Unity logger.

## Hooks file
The file which contains all methods to be hooked. See `{REPOPATH}\Hooker\example_hooks` for more information
about it's syntax. The example_hooks file is needed for the example at the next section.

**NOTE:** The hooker will always hook all methods entered in the Hooks file, if found. 
Hooking a method which is not expected by **HooksRegistry** has NO side effect on the game!   

## Usage Example
> The example expects the example_hooks file to be used as of the latest commit, including the **HookRegistry** compiled binary.

Effects of the example
- The game creates a non secure connection to the server (NOT over TLS);
- All transferred network packets are being duplicated to other TCP streams.

What you need

- The (compiled) binaries **PATH** from **Hooker**. Refered to as {HOOKERPATH};
- The (compiled) binary **FILE** from **HookRegistry**. Referred to as {REGISTRY};
- The **PATH** to the game folder. Referred to as {GAMEDIR};
- A hooks **FILE**, as mentioned above. Referred to as {HOOKS}.
    
Steps

1. Call Hooker.exe;
```
{HOOKERPATH}\Hooker.exe hook -d "{GAMEDIR}" -h "{HOOKS}" -l "{REGISTRY}"
```
2. Verify that that Hooker did not encounter a problem;
    - Requested methods are hooked;
    - Original assemblies are duplicated next to the original as {name}.original.dll;
    - Patched assemblies are written to the game directory as {name}.out.dll;
    - Patched assemblies replace the original assemblies as {name}.dll;
    - **HookRegistry** assembly is copied next to the game assemblies;
3. Run the Game - Watch the game log for lines starting with [HOOKER].

> To restore, run the command ```{HOOKERPATH}\Hooker.exe restore -d "{GAMEDIR}"```
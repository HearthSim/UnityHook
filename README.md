# UnityHook

UnityHook is a simple platform for hooking managed function calls, targeting
specifically assemblies compiled for [Unity3d](http://unity3d.com/) games.

Installed hooks allow overriding hooked functions' return values, essentially
granting complete control over managed code execution in a targeted game.

## Required 3rd-party Binaries

To the lib/ directory, you must add the following 3rd-party binaries; these can
be found in the {GameName}_Data/Managed folder of the game in question.

- UnityEngine.dll
- Assembly-CSharp.dll
- Assembly-CSharp-firstpass.dll
- System.dll

## EntityLogger

Open solution, build Hooker, plop Assembly-CSharp.dll in the bin folder
containing Hooker, run `Hooker .../Hearthstone_Data example_hooks`. Hopefully,
see results in entity.log in Hearthstone's root folder.

[Example entity.log](https://gist.github.com/Mischanix/b172f83767b352009fc1)

Of note, Newtonsoft.JSON is compiled specially for Unity, and involves removing
anything dependent on System.Data.

using CommandLine;
using CommandLine.Text;
using Mono.Cecil;

namespace Hooker.util
{
	// General options for any operation
	class Options
	{
		/* Command line operations */
		[VerbOption(Program.OPERATION_HOOK,
					HelpText = "Hook the methods from the libraries found at the given path.")]
		public HookSubOptions HookVerb
		{
			get;
			set;
		}
		[VerbOption(Program.OPERATION_RESTORE,
					HelpText =
						"Restore the library backup files created by this program before the HOOK operation.")]
		public RestoreSubOptions RestoreVerb
		{
			get;
			set;
		}

		[HelpVerbOption]
		public string GetUsage(string verb)
		{
			var help = HelpText.AutoBuild(this, verb);

			help.AddPreOptionsLine("Usage: app.exe ACTION [OPTIONS]");
			help.AddPostOptionsLine("Use the verb HELP for option details.");
			return help;
		}
	}

	abstract class GeneralOptions
	{
		/* Command line options */
		[Option('d', "gamedir", Required = true,
				HelpText = "The path to the game installation folder.")]
		public string GamePath
		{
			get;
			set;
		}

		// Set this to false on release
		[Option("debug", Required = false, DefaultValue = true,
				HelpText = "This switch allows debug print statements to work.")]
		public bool DebugMode
		{
			get;
			set;
		}

		[Option("log", Required = false, DefaultValue = "",
				HelpText = "The path to the file where all output will be redirected to.")]
		public string LogFile
		{
			get;
			set;
		}
	}

	// Specifically to Hook operations
	class HookSubOptions : GeneralOptions
	{
		[Option('h', "hooksfile", Required = true,
				HelpText = "The path to the file which defines which functions to hook.")]
		public string HooksFilePath
		{
			get;
			set;
		}

		[Option('l', "libfile", Required = true, DefaultValue = "HookRegistry.dll",
				HelpText =
					"The library that contains the functionality that gets executed when a hooked method triggers.")]
		public string HooksRegistryFilePath
		{
			get;
			set;
		}

		[Option("overwrite", Required = false, DefaultValue = true,
				HelpText = "Dependancies of libfile will be copied to the game library folder, overwriting existing files.")]
		// Careful! Setting this to true might result in unexpected game behaviour, since game libraries itself are
		// most likely to be a dependancy themselves!
		public bool OverwriteDependancies
		{
			get;
			set;
		}

		// Assembly blueprint of the HooksRegistry assembly.
		public AssemblyDefinition HooksRegistryAssemblyBlueprint
		{
			get;
			set;
		}

		// Type where all hooks are registered. This is also the type that gets called by
		// the hooking code and delegates to the hooked methods
		public TypeDefinition HookRegistryTypeBlueprint
		{
			get;
			set;
		}
	}

	// Specifically to Restore operations
	class RestoreSubOptions : GeneralOptions
	{

	}
}

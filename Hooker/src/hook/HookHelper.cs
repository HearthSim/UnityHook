﻿using GameKnowledgeBase;
using Hooker.util;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Reflection;

namespace Hooker
{
	class HookHelper
	{
		public const string ALREADY_PATCHED =
			"The file {0} is already patched and will be skipped.";
		public const string PARSED_HOOKSFILE = "Parsed {0} entries from hooks file `{1}`.";
		public const string CHECKING_ASSEMBLY = "Checking contents of assembly `{0}`.";
		public const string ASSEMBLY_ALREADY_PATCHED =
			"The assembly file `{0}` is already patched and will be skipped. " +
			"If this message is is unexpected, restore the original assembly file and run this program again!";
		public const string ASSEMBLY_NOT_PATCHED =
			"The assembly {0} is not patched because no function to hook was found.";
		public const string ERR_WRITE_BACKUP = "Creating backup for assembly `{0}` failed!";
		public const string ERR_WRITE_FILE = "Could not write data to file `{0}`!";
		public const string ERR_COPY_HLIB =
			"The HooksRegistry library could not be copied to {0}, try it manually after exiting this program!";

		// This structure represents one line of text in our hooks file.
		// It basically boils down to what function we target in which class.
		public struct HOOK_ENTRY
		{
			public string TypeName;
			public string MethodName;

			public string FullMethodName
			{
				get
				{
					return TypeName + METHOD_SPLIT + MethodName;
				}
			}
		}

		// The string used to split TypeName from FunctionName; see ReadHooksFile(..)
		public const string METHOD_SPLIT = "::";

		// Collection of all options
		private HookSubOptions _options
		{
			get;
		}

		// Array of all methods that match hook classes from HookRegistry
		private string[] ExpectedMethods;

		public HookHelper(HookSubOptions options)
		{
			_options = options;
		}

		List<HOOK_ENTRY> ReadHooksFile(string hooksFilePath)
		{
			var hookEntries = new List<HOOK_ENTRY>();

			// Open and parse our hooks file.
			// File.ReadLines needs at least framework 4.0
			foreach (string line in File.ReadLines(hooksFilePath))
			{
				// Remove all unnecessary whitespace
				var lineTrimmed = line.Trim();
				// Skip empty or comment lines
				if (lineTrimmed.Length == 0 || lineTrimmed.IndexOf("//") == 0)
				{
					continue;
				}
				// In our hooks file we use C++ style syntax to avoid parsing problems
				// regarding full names of Types and Methods. (namespaces!)
				// Hook calls are now registered as FULL_TYPE_NAME::METHOD_NAME
				// There are no methods registered without type, so this always works!
				var breakIdx = lineTrimmed.IndexOf(METHOD_SPLIT);
				// This is not a super robuust test, but it filters out the gross of
				// impossible values.
				if (breakIdx != -1)
				{
					// Create and store a new entry object
					hookEntries.Add(new HOOK_ENTRY
					{
						// From start to "::"
						TypeName = lineTrimmed.Substring(0, breakIdx),
						// After (exclusive) "::" to end
						MethodName = lineTrimmed.Substring(breakIdx + METHOD_SPLIT.Length),
					});
				}

			}

			Program.Log.Info(PARSED_HOOKSFILE, hookEntries.Count, hooksFilePath);
			return hookEntries;
		}


		public void CheckOptions()
		{
			// Gamedir is general option and is checked by Program!

			var hooksfile = Path.GetFullPath(_options.HooksFilePath);
			_options.HooksFilePath = hooksfile;
			if (!File.Exists(hooksfile))
			{
				throw new FileNotFoundException("Exe option `hooksfile` is invalid!");
			}

			var libfile = Path.GetFullPath(_options.HooksRegistryFilePath);
			_options.HooksRegistryFilePath = libfile;
			if (!File.Exists(libfile))
			{
				throw new FileNotFoundException("Exe option `libfile` is invalid!");
			}

			// Save the definition of the assembly containing our HOOKS.
			var hooksAssembly = AssemblyDefinition.ReadAssembly(_options.HooksRegistryFilePath);
			_options.HooksRegistryAssemblyBlueprint = hooksAssembly;
			// Check if the Hooks.HookRegistry type is present, this is the entrypoint for all
			// hooked methods.
			ModuleDefinition assModule = hooksAssembly.MainModule;
			TypeDefinition hRegType = assModule.Types.FirstOrDefault(t => t.FullName.Equals("Hooks.HookRegistry"));
			// Store the HooksRegistry type reference.
			_options.HookRegistryTypeBlueprint = hRegType;
			if (hRegType == null)
			{
				throw new InvalidDataException("The HooksRegistry library does not contain `Hooks.HookRegistry`!");
			}
		}

		void ProcessHookRegistry()
		{
			// We load the HookRegistry dll in a seperate app domain, which allows for dynamic unloading
			// when needed.
			// HookRegistry COULD lock referenced DLL's which we want to overwrite, so unloading releases
			// the locks HookRegistry held.

			
			// Isolated domain where the library will be loaded into.
			var hookRegistryDomain = AppDomain.CreateDomain("HookRegistryDomain");

			try
			{
				// Create an instance of our own assembly in a new appdomain.
				object instance = hookRegistryDomain
					.CreateInstanceAndUnwrap(Assembly.GetExecutingAssembly().FullName,"Hooker.HookRegistryTester");

				// All methods executed here are actually executed in the hookregistry domain!
				var hrTester = (HookRegistryTester)instance;			
				ExpectedMethods = hrTester.FetchExpectedMethods(_options.HooksRegistryFilePath);						
			}
			finally
			{
				AppDomain.Unload(hookRegistryDomain);
			}
		}

		void CopyHooksLibrary(GameKB knowledgeBase)
		{
			var libTargetPath = Path.Combine(knowledgeBase.LibraryPath,
											 Path.GetFileName(_options.HooksRegistryFilePath));
			try
			{
				// Copy the HookRegistry library next to the libraries of the game!
				File.Copy(_options.HooksRegistryFilePath, libTargetPath, true);
				// The HookRegistry library file is copied next to the game libraries because
				// hooking a library will add a dependancy towards HookRegistry.
				// HookRegistry is designed to have NO DEPENDANCIES. If you built it with
				// additional dependancies, they will need to be copied manually to the library
				// directory!

				Program.Log.Info("HooksRegistry binary copied to `{0}`", libTargetPath);

				// Update registry file path to be around the targetted libraries.
				_options.HooksRegistryFilePath = libTargetPath;

				// And reload assembly definition
				_options.HooksRegistryAssemblyBlueprint = AssemblyDefinition.ReadAssembly(
													 _options.HooksRegistryFilePath);
			}
			catch (Exception)
			{
				Program.Log.Warn(ERR_COPY_HLIB, libTargetPath);
			}
		}

		public void TryHook(GameKB gameKnowledge)
		{
			// Validate all command line options.
			CheckOptions();
			// Copy our injected library to the location of the 'to hook' assemblies.
			CopyHooksLibrary(gameKnowledge);
			// Test HookRegistry library.
			ProcessHookRegistry();

			List<HOOK_ENTRY> hookEntries = ReadHooksFile(_options.HooksFilePath);

			// Iterate all libraries known for the provided game.
			// An assembly blueprint will be created from the yielded filenames.
			// The blueprints will be edited, saved and eventually replaces the original assembly.
			foreach (string libraryFilePath in gameKnowledge.LibraryFilePaths)
			{
				if (!File.Exists(libraryFilePath)) continue;

				var libBackupPath = AssemblyHelper.GetPathBackup(libraryFilePath);
				var libPatchedPath = AssemblyHelper.GetPathOut(libraryFilePath);

				// Load the assembly file
				AssemblyDefinition assembly = AssemblyHelper.LoadAssembly(libraryFilePath,
																		  gameKnowledge.LibraryPath);
				if (assembly.HasPatchMark())
				{
					Program.Log.Warn(ASSEMBLY_ALREADY_PATCHED, libraryFilePath);
					continue;
				}

				// Construct a hooker wrapper around the main Module of the assembly.
				// The wrapper facilitates hooking into method calls.
				ModuleDefinition mainModule = assembly.MainModule;
				Hooker wrapper = Hooker.New(mainModule, _options);
				Program.Log.Info(CHECKING_ASSEMBLY, libraryFilePath);

				// Keep track of hooked methods
				bool isHooked = false;
				// Loop each hook entry looking for registered types and methods
				foreach (HOOK_ENTRY hookEntry in hookEntries)
				{
					try
					{
						wrapper.AddHookBySuffix(hookEntry.TypeName, hookEntry.MethodName, ExpectedMethods);
						isHooked = true;
					}
					catch (MissingMethodException)
					{
						// The method is not found in the current assembly.
						// This is no error because we run all hook entries against all libraries!
					}
				}

				try
				{
					// Only save if the file actually changed!
					if (isHooked)
					{
						// Generate backup from original file
						try
						{
							// This throws if the file already exists.
							File.Copy(libraryFilePath, libBackupPath, false);
						}
						catch (Exception)
						{
							// Do nothing
						}

						// Save the manipulated assembly.
						assembly.Save(libPatchedPath);

						// Overwrite the original with the hooked one
						File.Copy(libPatchedPath, libraryFilePath, true);
					}
					else
					{
						Program.Log.Debug(ASSEMBLY_NOT_PATCHED, libraryFilePath);
					}
				}
				catch (IOException e)
				{
					// The file could be locked! Notify user.
					// .. or certain libraries could not be resolved..
					// Try to find the path throwing an exception.. but this method is not foolproof!
					object path = typeof(IOException).GetField("_maybeFullPath",
															BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(e);
					Program.Log.Warn(ERR_WRITE_FILE, path);

					throw e;
				}
			}
		}
	}
}

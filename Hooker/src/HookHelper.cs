using GameKnowledgeBase;
using Hooker.util;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Reflection;
using Hooker.Hook;
using static Hooker.Hook.HooksFileParser;

namespace Hooker
{
	class HookHelper
	{
		public const string ALREADY_PATCHED =
			"The file {0} is already patched and will be skipped.";
		public const string ASSEMBLY_ALREADY_PATCHED =
			"The assembly file is already patched and will be skipped. " +
			"If this message is is unexpected, restore the original assembly file and run this program again!";
		public const string ASSEMBLY_NOT_PATCHED =
			"The assembly is not patched because no function to hook was found.";
		public const string ERR_COPY_HLIB =
			"The HooksRegistry library could not be copied to {0}, try it manually after exiting this program!";

		// Collection of all options
		private HookSubOptions _options
		{
			get;
		}

		// Array of all methods that match hook classes from HookRegistry.
		private string[] ExpectedMethods;
		// Array of all files which are referenced by the HookRegistry.
		private string[] ReferencedLibraryPaths;

		public HookHelper(HookSubOptions options)
		{
			_options = options;
		}

		public void CheckOptions()
		{
			// Gamedir is general option and is checked by Program!

			string hooksfile = Path.GetFullPath(_options.HooksFilePath);
			_options.HooksFilePath = hooksfile;
			if (!File.Exists(hooksfile))
			{
				throw new FileNotFoundException("Option `hooksfile` does not point to existing file!");
			}

			string libfile = Path.GetFullPath(_options.HooksRegistryFilePath);
			_options.HooksRegistryFilePath = libfile;
			if (!File.Exists(libfile))
			{
				throw new FileNotFoundException("Option `libfile` does not point to existing file!");
			}

			// Save the definition of the assembly containing our HOOKS.
			AssemblyDefinition hooksAssembly = AssemblyHelper.LoadAssembly(_options.HooksRegistryFilePath);
			_options.HooksRegistryAssemblyBlueprint = hooksAssembly;
			// Check if the Hooks.HookRegistry type is present, this is the entrypoint for all
			// hooked methods.
			ModuleDefinition assModule = hooksAssembly.MainModule;
			TypeDefinition hRegType = assModule.Types.FirstOrDefault(t => t.FullName.Equals("Hooks.HookRegistry"));
			// Store the HooksRegistry type reference.
			_options.HookRegistryTypeBlueprint = hRegType;
			if (hRegType == null)
			{
				throw new InvalidDataException("The HooksRegistry library does not contain type `Hooks.HookRegistry`!");
			}
		}

		void ProcessHookRegistry(GameKB gameKnowledge)
		{
			// We load the HookRegistry dll in a seperate app domain, which allows for dynamic unloading
			// when needed.
			// HookRegistry COULD lock referenced DLL's which we want to overwrite, so unloading releases
			// the locks HookRegistry held.

			// Isolated domain where the library will be loaded into.
			var testingDomain = AppDomain.CreateDomain("HR_Testing");

			using (Program.Log.OpenBlock("Testing Hookregistry library"))
			{
				try
				{
					// Create an instance of our own assembly in a new appdomain.
					object instance = testingDomain
						.CreateInstanceAndUnwrap(typeof(HookRegistryTester).Assembly.FullName, typeof(HookRegistryTester).FullName);

					/* All methods used are actually executed in the testing domain! */
					var hrTester = (HookRegistryTester)instance;
					hrTester.Analyze(_options.HooksRegistryFilePath, gameKnowledge.LibraryPath);

					// Load all data from the tester.
					ExpectedMethods = hrTester.ExpectedMethods;
					ReferencedLibraryPaths = hrTester.ReferencedAssemblyFiles.ToArray();
				}
				// Exceptions will flow back into our own AppDomain when unhandled.
				catch (Exception)
				{
					Program.Log.Warn("FAIL Testing");
					throw;
				}
				finally
				{
					AppDomain.Unload(testingDomain);
				}
			}
		}

		void CopyLibraries(GameKB knowledgeBase)
		{
			// The original folder containing HookRegistry library.
			string origHRFolderPath = Path.GetDirectoryName(_options.HooksRegistryFilePath);
			// The library folder of our game.
			string gameLibFolder = knowledgeBase.LibraryPath;

			// List of all assemblies to copy to the game library folder.
			IEnumerable<string> assembliesToCopy = new List<string>(ReferencedLibraryPaths)
			{
				_options.HooksRegistryFilePath
			};
			// Only keep unique entries.
			assembliesToCopy = assembliesToCopy.Distinct();

			// If TRUE, existing files in gameLibFolder will be overwritten if a dependancy has the
			// same name.
			bool overwriteDependancies = _options.OverwriteDependancies;

			using (Program.Log.OpenBlock("Copying HookRegistry dependancies"))
			{
				Program.Log.Info("Source directory `{0}`", origHRFolderPath);
				Program.Log.Info("Target directory `{0}`", gameLibFolder);

				foreach (string referencedLibPath in ReferencedLibraryPaths)
				{
					// Only copy the libraries which come from the same path as HookRegistry originally.
					string libFolderPath = Path.GetDirectoryName(referencedLibPath);
					if (!libFolderPath.Equals(origHRFolderPath)) continue;

					// Construct name for library file under the game library folder.
					string libFileName = Path.GetFileName(referencedLibPath);
					string targetLibPath = Path.Combine(gameLibFolder, libFileName);

					try
					{
						if (!overwriteDependancies && File.Exists(targetLibPath))
						{
							Program.Log.Info("Skipped `{0}`", libFileName);
						}
						else
						{
							File.Copy(referencedLibPath, targetLibPath, true);
							Program.Log.Info("SUCCESS Copied binary `{0}`", libFileName);

							if (referencedLibPath == _options.HooksRegistryFilePath)
							{
								// Update the options object to reflect the copied library.
								_options.HooksRegistryFilePath = targetLibPath;
								_options.HooksRegistryAssemblyBlueprint = AssemblyHelper.LoadAssembly(targetLibPath, gameLibFolder);
							}
						}
					}
					catch (Exception)
					{
						Program.Log.Warn("FAIL Error copying `{0}`. Manual copy is needed!", libFileName);
					}
				}
			}
		}

		public void TryHook(GameKB gameKnowledge)
		{
			// Validate all command line options.
			CheckOptions();
			// Test HookRegistry library.
			ProcessHookRegistry(gameKnowledge);
			// Copy our injected library to the location of the 'to hook' assemblies.
			CopyLibraries(gameKnowledge);

			List<HOOK_ENTRY> hookEntries = ReadHooksFile(_options.HooksFilePath);

			using (Program.Log.OpenBlock("Parsing libary files"))
			{
				// Iterate all libraries known for the provided game.
				// An assembly blueprint will be created from the yielded filenames.
				// The blueprints will be edited, saved and eventually replaces the original assembly.
				foreach (string libraryFilePath in gameKnowledge.LibraryFilePaths)
				{
					using (Program.Log.OpenBlock(libraryFilePath))
					{
						if (!File.Exists(libraryFilePath))
						{
							Program.Log.Warn("File does not exist!");
							continue;
						}

						string libBackupPath = AssemblyHelper.GetPathBackup(libraryFilePath);
						string libPatchedPath = AssemblyHelper.GetPathOut(libraryFilePath);

						AssemblyDefinition assembly = null;
						try
						{
							// Load the assembly file
							assembly = AssemblyHelper.LoadAssembly(libraryFilePath, gameKnowledge.LibraryPath);
						}
						catch (BadImageFormatException e)
						{
							Program.Log.Warn("Library file is possibly encrypted!");
							Program.Log.Info("Library skipped because it cannot be read.");
							Program.Log.Debug("Full exception: {0}", e.Message);
							continue;
						}

						if (assembly.HasPatchMark())
						{
							Program.Log.Warn(ASSEMBLY_ALREADY_PATCHED);
							continue;
						}

						// Construct a hooker wrapper around the main Module of the assembly.
						// The wrapper facilitates hooking into method calls.
						ModuleDefinition mainModule = assembly.MainModule;
						var wrapper = HookLogic.New(mainModule, _options);

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
								Program.Log.Warn(ASSEMBLY_NOT_PATCHED, libraryFilePath);
							}
						}
						catch (IOException e)
						{
							// The file could be locked! Notify user.
							// .. or certain libraries could not be resolved..
							// Try to find the path throwing an exception.. but this method is not foolproof!
							object path = typeof(IOException).GetField("_maybeFullPath",
																	BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(e);
							Program.Log.Warn("Could not write patched data to file `{0}`!", path);

							throw e;
						}
					}
				}
			}
		}
	}
}

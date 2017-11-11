using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Hooker.Hook
{
	/*
	 * Special code for loading and testing the Hookregistry library seperated from the hooking application domain.
	 * Using a separate application domain allows the HookRegistry to perform nasty stuff without (let's hope) affecting
	 * the hooking code.
	 * Unloading the entire application domain also releases locks on possible libraries we want to overwrite!
	 */
	class HookRegistryTester : MarshalByRefObject
	{
		// Holds the virtual space which is used to load the HookRegistry assembly.
		AppDomain _currentDomain => AppDomain.CurrentDomain;

		// HookRegistry assembly definition.
		Assembly _hrAssembly;

		// Path to the library folder of the game we are hooking.
		// This variable is used as fallback if an assembly failed to load.
		string _gameLibraryPath;
		// Path to the folder containing the HookRegistry library.
		// This variable is also used as fallback for assembly loading.
		string _hrLibraryPath;

		// Contains a list of all assembly files which are referenced by the HookRegistry library.
		List<string> _referencedAssemblyFiles;
		public IList<string> ReferencedAssemblyFiles => _referencedAssemblyFiles;

		// A list off all types which contain HOOK logic.
		// Exposing Type objects causes the leaks to the app domain of the reader!
		// If this is a calculated risk, change the property accessibility to public.
		IEnumerable<Type> _hookTypes;
		private IEnumerable<Type> HookTypes => _hookTypes;

		// Contains a list of all methods expected by the hooks defined in HookRegistry.
		string[] _expectedMethods;
		public string[] ExpectedMethods => _expectedMethods;

		public HookRegistryTester()
		{
			_referencedAssemblyFiles = new List<string>();
		}

		// Loads the HookRegistry library and performs all analyzations.
		public void Analyze(string hrAssemblyPath, string gameLibraryPath)
		{
			// Store folder paths for fallback library loading.
			_hrLibraryPath = Path.GetDirectoryName(hrAssemblyPath);
			_gameLibraryPath = gameLibraryPath;

			try
			{
				// Hook into event of succesfully loading new assemblies.
				_currentDomain.AssemblyLoad += CollectReferencedAssemblies;
				// Hook into event of failure when loading new assemblies.
				// This callback attempts to load assembly files from the HookRegistry library folder.
				_currentDomain.AssemblyResolve += FallbackAssemblyHRReference;
				// This callback attempts to load assembly files from the game library folder.
				_currentDomain.AssemblyResolve += FallbackAssemblyLoadGameLibrary;

				try
				{
					// Load HookRegistry assembly
					var assemblyName = AssemblyName.GetAssemblyName(hrAssemblyPath);
					// Leak assembly type from load domain into this one.
					_hrAssembly = _currentDomain.Load(assemblyName);

					// Initialise the Hookregistery class by calling it's singleton method.
					Type hrType = _hrAssembly.GetType("Hooks.HookRegistry", true);
					MethodInfo singletonMethod = hrType.GetMethod("Get", BindingFlags.Static | BindingFlags.Public);
					if (singletonMethod == null)
					{
						throw new InvalidDataException("Hookregistry class doesn't appear to have a Get() method!");
					}
					singletonMethod.Invoke(null, new object[] { });
				}
				catch (TargetInvocationException e)
				{
					// Unpack actual exception which caused the issue and raise it.
					throw e.InnerException;
				}

				// Locate all hooks.
				FindHooks();
				// Fetch all expected method names.
				FetchExpectedMethods();
			}
			finally
			{
				// Remove listeners to be good memory citizens.
				_currentDomain.AssemblyResolve -= FallbackAssemblyLoadGameLibrary;
				_currentDomain.AssemblyResolve -= FallbackAssemblyHRReference;
				_currentDomain.AssemblyLoad -= CollectReferencedAssemblies;
			}
		}

		// When an assembly fails to load, this method is called.
		// The name of the assembly is combined with the path to the folder containing HookRegistry, which might have 
		// the missing library dll file.
		private Assembly FallbackAssemblyHRReference(object sender, ResolveEventArgs args)
		{
			var referenceName = new AssemblyName(args.Name);
			// Filename of the assembly, minus the extension.
			string libFileName = referenceName.Name;
			// Check if the file exists under game lib folder.
			// TODO: .dll is hardcoded, but it could also be .exe or whatnot..
			string fullLibPath = Path.Combine(_hrLibraryPath, libFileName + ".dll");
			if (File.Exists(fullLibPath))
			{
				// Load this assembly instead!
				var assemblyName = new AssemblyName(libFileName)
				{
					CodeBase = fullLibPath
				};
				var assembly = Assembly.Load(assemblyName);
				return assembly;
			}

			// When null is returned, no fallback has been found.
			return null;
		}

		// When an assembly fails to load, this method is called.
		// The name of the assembly is combined with the known path to the game libraries, which might have 
		// the missing library dll file.
		private Assembly FallbackAssemblyLoadGameLibrary(object sender, ResolveEventArgs args)
		{
			var referenceName = new AssemblyName(args.Name);
			// Filename of the assembly, minus the extension.
			string libFileName = referenceName.Name;
			// Check if the file exists under game lib folder.
			// TODO: .dll is hardcoded, but it could also be .exe or whatnot..
			string fullLibPath = Path.Combine(_gameLibraryPath, libFileName + ".dll");
			if (File.Exists(fullLibPath))
			{
				// Load this assembly instead!
				var assemblyName = new AssemblyName(libFileName)
				{
					CodeBase = fullLibPath
				};
				var assembly = Assembly.Load(assemblyName);
				return assembly;
			}

			// When null is returned, no fallback has been found.
			return null;
		}

		// Collects the file paths of all assemblies that were loaded in our app domain because of a
		// reference to HookRegistry.
		private void CollectReferencedAssemblies(object sender, AssemblyLoadEventArgs args)
		{
			// Get the full path of the assembly that was loaded into this appdomain.
			string libraryFilePath = args.LoadedAssembly.Location;
			// .. and keep track of this path.
			_referencedAssemblyFiles.Add(libraryFilePath);
		}

		// Loads the hookregistry library into our AppDomain and collect all hooking classes.
		private void FindHooks()
		{
			var hookTypes = new List<Type>();

			Type hrType = _hrAssembly.GetType("Hooks.HookRegistry", true);
			// Initialise the Hookregistery class while defering initialisation of dynamic types.
			// Dynamic loading of types write-locks the library files which need to be overwritten after the hooking process!
			hrType.GetMethod("Get", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { });

			// Locate all Hook classes through the RuntimeHook attribute.
			Type runtimeHookAttr = _hrAssembly.GetType("Hooks.RuntimeHookAttribute", true);
			foreach (Type type in _hrAssembly.GetTypes())
			{
				// Match each type against the attribute
				object[] hooks = type.GetCustomAttributes(runtimeHookAttr, false);
				if (hooks != null && hooks.Length > 0)
				{
					// The types that match are Hook classes.
					hookTypes.Add(type);
				}
			}

			_hookTypes = hookTypes;
		}

		// Calls GetExpectedMethods from each hooking class contained in HookRegistry.
		// The purpose is to track all methods which are expected by all loaded hooks.
		// Cross referencing this list with all 'to-hook' methods allows us to display
		// a warning for potentially unexpected behaviour.
		private void FetchExpectedMethods()
		{
			var temp = new List<string>();
			foreach (Type hook in _hookTypes)
			{
				MethodInfo method = hook.GetMethod("GetExpectedMethods");
				if (method != null)
				{
					object methods = method.Invoke(null, new object[] { });
					temp.AddRange((string[])methods);
				}
			}

			_expectedMethods = temp.ToArray();
		}
	}
}

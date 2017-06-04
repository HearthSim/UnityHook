using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Hooker
{
	/*
	 * Special code for loading and testing the Hookregistry library seperated from the hooking application domain.
	 * Using a seperate application domain allows the HookRegistry to perform nasty stuff without (let's hope) affecting
	 * the hooking code.
	 * Unloading the entire application domain also releases locks on possible libraries we want to overwrite!
	 */
	class HookRegistryTester : MarshalByRefObject
	{
		public HookRegistryTester() { }

		public string[] FetchExpectedMethods(string assemblyPath)
		{
			// AND ANOTHER ONE - AppDomain
			var setup = new AppDomainSetup()
			{
				// Used for probing assemblies, defaults to the directory which contains the HookRegistry 
				// library.
				ApplicationBase = Path.GetDirectoryName(assemblyPath)
			};

			var newAppDomain = AppDomain.CreateDomain("temp", null, setup);

			try
			{
				Assembly hrAssembly = null;

				try
				{
					// Retrieve name and load assembly.
					var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
					hrAssembly = AppDomain.CurrentDomain.Load(assemblyName);

					Type hrType = hrAssembly.GetType("Hooks.HookRegistry", true);
					// Initialise the Hookregistery class while defering initialisation of dynamic types.
					// Dynamic loading of types write-locks the library files which need to be overwritten after the hooking process!
					hrType.GetMethod("Get", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { });
				}
				catch (Exception e)
				{
					if (e is TargetInvocationException)
					{
						throw new FileNotFoundException("Hooksregistry or a dependancy was not found!", e.InnerException);
					}
					else
					{
						throw;
					}
				}
				// Locate all hooks.
				IEnumerable<Type> hooks = FindHooks(hrAssembly);
				// Fetch all expected method names.
				return FetchExpectedMethods(hooks);
			}
			finally
			{
				AppDomain.Unload(newAppDomain);
			}
		}

		// Loads the hookregistry library into our AppDomain and collect all hooking classes.
		private IEnumerable<Type> FindHooks(Assembly assembly)
		{
			var hookTypes = new List<Type>();

			Type hrType = assembly.GetType("Hooks.HookRegistry", true);
			// Initialise the Hookregistery class while defering initialisation of dynamic types.
			// Dynamic loading of types write-locks the library files which need to be overwritten after the hooking process!
			hrType.GetMethod("Get", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { });

			// Locate all Hook classes through the RuntimeHook attribute.
			Type runtimeHookAttr = assembly.GetType("Hooks.RuntimeHookAttribute", true);
			foreach (Type type in assembly.GetTypes())
			{
				// Match each type against the attribute
				object[] hooks = type.GetCustomAttributes(runtimeHookAttr, false);
				if (hooks != null && hooks.Length > 0)
				{
					// The types that match are Hook classes.
					hookTypes.Add(type);
				}
			}

			return hookTypes;
		}

		// Calls GetExpectedMethods from each hooking class contained in HookRegistry.
		// The purpose is to track all methods which are expected by all loaded hooks.
		// Cross referencing this list with all 'to-hook' methods allows us to display
		// a warning for potentially unexpected behaviour.
		private string[] FetchExpectedMethods(IEnumerable<Type> hookTypes)
		{
			var temp = new List<string>();
			foreach (Type hook in hookTypes)
			{
				MethodInfo method = hook.GetMethod("GetExpectedMethods");
				if (method != null)
				{
					var methods = method.Invoke(null, new object[] { });
					temp.AddRange((string[])methods);
				}
			}

			return temp.ToArray();
		}
	}
}

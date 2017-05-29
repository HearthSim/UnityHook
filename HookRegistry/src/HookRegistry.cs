// Assembly wide entry point for hooked methods (within game libraries).
// The purpose of this project is to have NO DEPENDANCIES which facilitates
// it's distribution and installment.
// If there are introduced dependancies, make sure to copy them to the directory
// containing all game libraries!
// The HookRegistry library file, compilated unit of this project, will be copied
// next to the game libraries by the Hooker project.

using GameKnowledgeBase;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security;

[assembly: AssemblyTitle("HookRegistry")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
namespace Hooks
{
	public class HookRegistry
	{
		// TRUE if the current execution context is invoked by Unity.
		// FALSE if the current execution context is external to Unity.
		private bool _IsWithinUnity = false;

		// Method signature definition.
		// This thing defines the needed structure for a certain function call.
		// In our case it's the signature of a onCall(..) function defined in each hook class.
		public delegate object Callback(string typeName, string methodName, object thisObj,
										object[] args);

		// All callback functions registered to with this object
		private List<Callback> callbacks = new List<Callback>();

		// All types which have a generic parameter that are expected to be hooked.
		// The declaring type is used as structural blueprint for the generic instantiated type.
		// Basically: Generic instantiations are seperate types, different from the actual type
		// which holds the generic parameters.
		private List<RuntimeTypeHandle> declaringTypes = new List<RuntimeTypeHandle>();

		// Objects initiated and initialised of all discovered hook classes.
		private List<object> activeHooks = new List<object>();

		private static HookRegistry _instance;

		public static HookRegistry Get()
		{
			if (_instance == null)
			{
				_instance = new HookRegistry();
				// Initialise the assembly store.
				_instance.Init();

				// Setup all hook information
				_instance.LoadRuntimeHooks();

				try
				{
					// Test if this code is running within the Unity Engine
					_instance.TestInGame();
				}
				catch (SecurityException)
				{
					// Do nothing
				}

			}
			return _instance;
		}

		private void Init()
		{
			// All necessary libraries should be next to this assembly file.
			// GetExecutingAssembly does not always give the desired effect. In case of dynamic invocation
			// it will return the location of the Assembly DOING the incovation!
			var assemblyPath = Assembly.GetExecutingAssembly().Location;
			var assemblyDirPath = Path.GetDirectoryName(assemblyPath);
			// Initialise the game knowledge database with the discovered path.
			HSKB.Get(assemblyDirPath);
		}

		// Load necessary types for dynamic method calls
		private void PrepareDynamicCalls()
		{

		}

		// Method that tests the execution context for the presence of an initialized Unity framework.
		private void TestInGame()
		{
			_IsWithinUnity = UnityEngine.Application.isPlaying;
			Log("Running inside Unity player, ALLOWING hooks");
		}

		// Wrapper around the log method from unity.
		// This method writes to the log file of the unity game
		public void Log(string message)
		{
			if (_IsWithinUnity)
			{
				// Create a nice format before printing to log
				var logmessage = string.Format("[HOOKER]\t{0}", message);
				UnityEngine.Debug.Log(logmessage);
			}
		}

		// This function implements behaviour to force a crash in the game.
		// This is to make sure we don't break anything.
		public static void Panic(string message = "")
		{
			var msg = string.Format("Forced crash because of error: `{0}` !", message);
			// Push the message to the game log
			Get().Log(msg);

			// Make the game crash!
			throw new Exception("[HOOKER] Forced crash because of an error!");
		}

		// First function called by modified libraries.
		// Return the response coming from the hook, because it's needed by the original library code   ! important
		public static object OnCall(RuntimeMethodHandle rmh, object thisObj, object[] args)
		{
			return HookRegistry.Get().Internal_OnCall(rmh, thisObj, args);
		}

		// Add a hook listener
		public static void Register(Callback cb)
		{
			HookRegistry.Get().callbacks.Add(cb);
		}

		public static void RegisterDeclaringType(RuntimeTypeHandle typeHandle)
		{
			HookRegistry.Get().declaringTypes.Add(typeHandle);
		}

		// Remove a hook listener
		public static void Unregister(Callback cb)
		{
			HookRegistry.Get().callbacks.Remove(cb);
		}

		// Discover and store all HOOK classes, which have the [RuntimeHook] attribute.
		void LoadRuntimeHooks()
		{
			foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
			{
				object[] hooks = type.GetCustomAttributes(typeof(RuntimeHookAttribute), false);
				if (hooks != null && hooks.Length > 0)
				{
					// Get the default constructor.
					activeHooks.Add(type.GetConstructor(new Type[] { }).Invoke(new object[] { }));
				}
			}
		}

		// Call each hook object and return it's response.
		object Internal_OnCall(RuntimeMethodHandle rmh, object thisObj, object[] args)
		{
			// Without Unity Engine as context, we don't execute the hook.
			// This is to prevent accidentally calling unity functions without the proper
			// initialisation.
			if (!_IsWithinUnity)
			{
				return null;
			}

			MethodBase method = null;
			try
			{
				// Try to directly resolve the method definition.
				method = MethodBase.GetMethodFromHandle(rmh);
			}
			catch (ArgumentException)
			{
				// Direct resolution of method definition doesn't work, probably because of generic parameters.
				// We use the blueprints that were registered to try and decode this generic instantiated type.
				foreach (RuntimeTypeHandle declHandle in declaringTypes)
				{
					try
					{
						method = MethodBase.GetMethodFromHandle(rmh, declHandle);
					}
					catch (ArgumentException)
					{
						// Do nothing, continue loop..
					}
				}
			}

			// Panic if the method variable is still not set.
			if (method == null)
			{
				Panic("Could not resolve the method handle!");
			}

			// Fetch usefull information from the method definition.
			// Use that information to log the call.
			var typeName = method.DeclaringType.FullName;
			var methodName = method.Name;
			// TODO: replace with parameters of function.
			var paramString = "..";
			var message = string.Format("Called by `{0}.{1}({2})`", typeName, methodName, paramString);

			// Coming from UnityEngine.dll - UnityEngine.Debug.Log(..)
			// This method prints into the game's debug log
			Log(message);

			// Execute each hook, because we don't know which one to actually target
			foreach (Callback cb in callbacks)
			{
				var o = cb(typeName, methodName, thisObj, args);
				// If the hook did not return null, return it's response.
				// This test explicitly ends the enclosing FOR loop, so hooks that were
				// not executed yet will not run.
				if (o != null)
				{
					return o;
				}
			}
			return null;
		}
	}

	[AttributeUsage(AttributeTargets.Class, Inherited = false)]
	public class RuntimeHookAttribute : Attribute
	{
	}
}

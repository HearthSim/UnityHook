// Assembly wide entry point for hooked methods (within game libraries).
// The purpose of this project is to have NO DEPENDANCIES which facilitates
// it's distribution and installment.
// If there are introduced dependancies, make sure to copy them to the directory
// containing all game libraries!
// The HookRegistry library file, compilated unit of this project, will be copied
// next to the game libraries by the Hooker project.

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
		private List<Callback> _callbacks = new List<Callback>();

		// All types which have a generic parameter that are expected to be hooked.
		// The declaring type is used as structural blueprint for the generic instantiated type.
		// Basically: Generic instantiations are seperate types, different from the actual type
		// which holds the generic parameters.
		private List<RuntimeTypeHandle> _declaringTypes = new List<RuntimeTypeHandle>();

		// All class types that implement hooking functionality
		private List<Type> _knownHooks = new List<Type>();
		public Type[] KnownHooks => _knownHooks.ToArray();

		#region INIT
		private static HookRegistry _instance;
		private static object _initLock = new object();
		private bool _isInitialised = false;

		public static HookRegistry Get()
		{
			if (_instance == null)
			{
				lock (_initLock)
				{
					// We need a double check because hooking async methods can cause race issues.
					if (_instance == null)
					{
						_instance = new HookRegistry
						{
							// Test if we're running inside the unity player.
							_IsWithinUnity = IsWithinUnity()
						};

						_instance.Init();
					}
				}
			}

			return _instance;
		}

		private void Init()
		{
			if (!_isInitialised)
			{
				// All necessary libraries should be next to this assembly file.
				// GetExecutingAssembly does not always give the desired effect. In case of dynamic invocation
				// it will return the location of the Assembly DOING the incovation!
				string assemblyPath = Assembly.GetExecutingAssembly().Location;
				string assemblyDirPath = Path.GetDirectoryName(assemblyPath);
				// TODO: see if something needs to be done with the current directory.

				if (_IsWithinUnity)
				{
					Internal_Log("Running inside Unity player, ALLOWING hooks");

					// Pre-load necessary library files.
					ReferenceLoader.Load();

					// Setup all hook information.
					LoadRuntimeHooks();
				}
			}

			_isInitialised = true;
		}

		// Method that tests the execution context for the presence of an initialized Unity framework.
		private static bool TestInGame()
		{
			return UnityEngine.Application.isPlaying;
		}

		// Hackish way to detect if we're running in an Unity appdomain.
		public static bool IsWithinUnity()
		{
			try
			{
				// Test if this code is running within the Unity Engine
				return TestInGame();
			}
			catch (SecurityException)
			{
				// Do nothing
				return false;
			}
		}

		// Discover and store all HOOK classes, which have the [RuntimeHook] attribute.
		void LoadRuntimeHooks()
		{
			Internal_Log("Enumerating all found hook types:");
			foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
			{
				object[] hooks = type.GetCustomAttributes(typeof(RuntimeHookAttribute), false);
				if (hooks != null && hooks.Length > 0)
				{
					// Store type.
					_knownHooks.Add(type);
					// Initialise hook through default constructor.
					type.GetConstructor(new Type[] { }).Invoke(new object[] { });
					Internal_Log(type.Name);
				}
			}
		}

		// Add a hook listener
		public static void Register(Callback cb)
		{
			Get()._callbacks.Add(cb);
			string message = String.Format("Registered callback; {0}", cb.Method.Name);
			Get().Internal_Log(message);
		}

		// Remove a hook listener
		public static void Unregister(Callback cb)
		{
			Get()._callbacks.Remove(cb);
		}

		// Register a type which holds generic parameters.
		// These types must be known since it's information is lost during runtime
		// when generic arguments were provided.
		public static void RegisterDeclaringType(RuntimeTypeHandle typeHandle)
		{
			Get()._declaringTypes.Add(typeHandle);
			string message = String.Format("Registered parent generic type `{0}`", typeHandle.ToString());
			Get().Internal_Log(message);
		}

		#endregion

		#region LOGGING

		public static void Log(string message)
		{
			Get().Internal_Log(message);
		}

		// Wrapper around the log method from unity.
		// This method writes to the log file of the unity game.
		public void Internal_Log(string message)
		{
			if (_IsWithinUnity)
			{
				// Create a nice format before printing to log
				string logmessage = String.Format("[HOOKER]\t{0}", message);
				UnityEngine.Debug.Log(logmessage);
			}
		}

		// This function implements behaviour to force a crash in the game.
		// This is to make sure we don't break anything.
		public static void Panic(string message = "")
		{
			string msg = String.Format("Forced crash because of error: `{0}` !", message);
			// Push the message to the game log
			Get().Internal_Log(msg);

			// Make the game crash!
			throw new Exception("[HOOKER] Forced crash because of an error!");
		}

		#endregion

		#region RUNTIME

		// First function called by modified libraries.
		// Return the response coming from the hook, because it's needed by the original library code   ! important
		public static object OnCall(RuntimeMethodHandle rmh, object thisObj, object[] args)
		{
			return Get().Internal_OnCall(rmh, thisObj, args);
		}

		// Call each hook object and return it's response.
		private object Internal_OnCall(RuntimeMethodHandle rmh, object thisObj, object[] args)
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
				foreach (RuntimeTypeHandle declHandle in _declaringTypes)
				{

					method = MethodBase.GetMethodFromHandle(rmh, declHandle);
					if (method != null) break;
				}
			}

			// Panic if the method variable is still not set.
			if (method == null)
			{
				Panic("Could not resolve the method handle!");
			}

			// Fetch usefull information from the method definition.
			// Use that information to log the call.
			string typeName = method.DeclaringType.FullName;
			string methodName = method.Name;
			// TODO: replace with parameters of function.
			string paramString = "..";
			string message = String.Format("Called by `{0}.{1}({2})`", typeName, methodName, paramString);

			// Coming from UnityEngine.dll - UnityEngine.Debug.Log(..)
			// This method prints into the game's debug log
			Internal_Log(message);

			// Execute each hook, because we don't know which one to actually target
			foreach (Callback cb in _callbacks)
			{
				object o = cb(typeName, methodName, thisObj, args);
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

		#endregion
	}

	[AttributeUsage(AttributeTargets.Class, Inherited = false)]
	public class RuntimeHookAttribute : Attribute
	{
	}
}

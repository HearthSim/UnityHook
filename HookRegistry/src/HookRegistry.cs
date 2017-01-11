﻿using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

[assembly: AssemblyTitle("HookRegistry")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
namespace Hooks
{
    public class HookRegistry
    {
        // Represents UnityEngine.Debug.Log(string).
        // Usage: _LogMethod.Invoke(null, new[]{messagestring})
        MethodInfo _LogMethod;

        // Method signature definition.
        // This thing defines the needed structure for a certain function call.
        // In our case it's the signature of a onCall(..) function defined in each hook class.
        public delegate object Callback(string typeName, string methodName, object thisObj, object[] args);

        // The path where the currently executing library can be found.
        public static string LibLocation { get; private set; }
        public const string LIB_UNITY_NAME = "UnityEngine.dll";
        public const string LIB_CSHARP_NAME = "Assembly-CSharp.dll";
        public const string LIB_CSHARP_FIRSTP_NAME = "Assembly-CSharp-firstpass.dll";

        // All callback functions registered to with this object
        List<Callback> callbacks = new List<Callback>();

        // Objects initialised from all discovered hook classes
        List<object> activeHooks = new List<object>();

        static HookRegistry _instance;

        public static HookRegistry Get()
        {
            if (_instance == null)
            {
                _instance = new HookRegistry();
                // Initialise the assembly store.
                _instance.InitAssemblies();
                // Load assembly data for dynamic calls
                _instance.PrepareDynamicCalls();
                // Setup all hook information
                _instance.LoadRuntimeHooks();

            }
            return _instance;
        }

        // The assembly store will assist in providing assemblies for dynamic
        // method calls
        private void InitAssemblies()
        {
            // All necessary libraries should be next to this assembly file.
            // GetExecutingAssembly does not always give the desired effect. In case of dynamic invocation
            // it will return the location of the Assembly DOING the incovation!
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var assemblyDirPath = Path.GetDirectoryName(assemblyPath);
            LibLocation = assemblyDirPath;
        }

        // Load necessary types for dynamic method calls
        private void PrepareDynamicCalls()
        {
            // We must design this function around failing, because our locationstring is not 100% thrustworthy
            // nor is there any guarantee that de requested libraries are in the same folder!
            // EG When dynamically invoking Get(..) from the Hooker project, this fails.
            // When hooking, it's not necessary to have the game libraries be next to this assembly.
            try
            {
                // Prepare dynamic call to Unity
                Assembly unityAssembly = Assembly.LoadFrom(Path.Combine(LibLocation, LIB_UNITY_NAME));
                var unityType = unityAssembly.GetType("UnityEngine.Debug");
                _LogMethod = unityType.GetMethod("Log", BindingFlags.Static | BindingFlags.Public, Type.DefaultBinder, new Type[] { typeof(string) }, null);
            } catch (Exception)
            {
                // Do nothing
            }
        }

        // Wrapper around the log method from unity.
        // This method writes to the log file of the unity game
        public void Log(string message)
        {
            if (_LogMethod != null)
            {
                // Create a nice format before printing to log
                var logmessage = String.Format("[HOOKER]\t{0}", message);
                _LogMethod.Invoke(null, new[] { logmessage });
            }
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

        // Remove a hook listener
        public static void Unregister(Callback cb)
        {
            HookRegistry.Get().callbacks.Remove(cb);
        }

        // Install hook listeners by classes that have the [RuntimeHook] attribute
        void LoadRuntimeHooks()
        {
            // Loop all types defined in this assembly file; read - the HookRegistry project
            // The game might be provided with a custom built core library.. 
            // And Reflection.Assembly might as well be the first type it won't contain!
            // foreach (var type in Assembly.GetExecutingAssembly().GetTypes())

            // Install hook listeners by classes that have the [RuntimeHook] attribute
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                var hooks = type.GetCustomAttributes(typeof(RuntimeHookAttribute), false);
                if (hooks != null && hooks.Length > 0)
                {
                    activeHooks.Add(type.GetConstructor(Type.EmptyTypes).Invoke(new object[0]));
                }
            }
        }

        // Call each hook object and return it's response.
        object Internal_OnCall(RuntimeMethodHandle rmh, object thisObj, object[] args)
        {
            var method = MethodBase.GetMethodFromHandle(rmh);
            var typeName = method.DeclaringType.FullName;
            var methodName = method.Name;

            // Coming from UnityEngine.dll - UnityEngine.Debug.Log(..)
            // This method prints into the game log
            var message = String.Format("Called by `{0}.{1}(...)`", typeName, methodName);
            Log(message);

            // Execute each hook, because we don't know which one to actually target
            foreach (var cb in callbacks)
            {
                var o = cb(typeName, methodName, thisObj, args);
                // If the hook did not return null, return it's response.
                // This test explicitly ends the enclosing FOR loop, so hooks that were 
                // not executed will be skipped.
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

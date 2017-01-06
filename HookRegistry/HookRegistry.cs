using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Reflection;

[assembly: AssemblyTitle("HookRegistry")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
namespace Hooks
{
    public class HookRegistry
    {
        // Method signature definition.
        // This thing defines the needed structure for a certain function call.
        // In our case it's the signature of a onCall(..) function defined in each hook class.
        public delegate object Callback(string typeName, string methodName, object thisObj, object[] args);

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
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var assemblyDirPath = System.IO.Path.GetDirectoryName(assemblyPath);
            // No exception occurs if the store was already initialised
            AssemblyStore.Get(assemblyDirPath);
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

            //// We bring our own booze.. dependancies! By using Mono.Cecil
            //AssemblyDefinition thisAssembly;
            //AssemblyHelper.LoadAssembly(typeof(HookRegistry).Module.FullyQualifiedName, out thisAssembly);

            //// Infer reference type of Hook attribute class
            //TypeReference hookTypeRef = thisAssembly.MainModule.Import(typeof(RuntimeHookAttribute));
            //// Loop all types in our assembly
            //foreach (TypeDefinition typeDefinition in thisAssembly.MainModule.Types)
            //{
            //    // Loop all attributes of this type
            //    foreach (CustomAttribute attr in typeDefinition.CustomAttributes)
            //    {
            //        if (attr.AttributeType.Equals(hookTypeRef))
            //        { // Attribute match! This contains hook code.
            //            // Create an instance of this type through the empty param constructor
            //            object instance = typeDefinition.GetType().GetConstructor(Type.EmptyTypes).Invoke(new object[] { });
            //            // Store the instance
            //            activeHooks.Add(instance);
            //        }
            //    }
            //}
        }

        // Call each hook object and return it's response.
        object Internal_OnCall(RuntimeMethodHandle rmh, object thisObj, object[] args)
        {
            var method = MethodBase.GetMethodFromHandle(rmh);
            var typeName = method.DeclaringType.FullName;
            var methodName = method.Name;

            // Coming from UnityEngine.dll - UnityEngine.Debug.Log(..)
            // This method prints into the game log
            //Debug.Log(String.Format("{0}.{1}(...)", typeName, methodName));

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

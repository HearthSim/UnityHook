﻿// This hook causes Hearthstone to communicate in plaintext, without using TLS/SSL.
// NOTE: This is designed for use with 3rd party servers that don't support TLS connections
// and will cause the Hearthstone client to fail to connect to an official server, therefore
// it is disabled by default.
// To enable this hook, add "BattleNetCSharp.Init" to example_hooks


using System;
using System.IO;
using System.Reflection;

namespace Hooks
{
    [RuntimeHook]
    class SSLDisable
    {
        // Represents bgs.SslParameters
        Type TypeSslParams;
        // Represents bgs.BattleNetCSharp
        Type TypeBattleNetC;

        // This variable is used to control the interception of the hooked method.
        // When TRUE, we return null to allow normal execution of the function.
        // When FALSE, we hook into the call.
        // This switch allows us to call the original method from within this hook class.
        private bool reentrant;

        public SSLDisable(bool initDynamicCalls)
        {
            HookRegistry.Register(OnCall);
            reentrant = false;
            if (initDynamicCalls)
            {
                PrepareDynamicCalls();
            }
        }

        private void PrepareDynamicCalls()
        {
            // Prepare dynamic call to CSharp-firstpass library
            // Load from assembly file at currently executing path
            var loc = Path.Combine(HookRegistry.LibLocation, HookRegistry.LIB_CSHARP_FIRSTP_NAME);
            Assembly libAssembly = Assembly.LoadFrom(loc);
            TypeSslParams = libAssembly.GetType("bgs.SslParameters");
            TypeBattleNetC = libAssembly.GetType("bgs.BattleNetCSharp");
        }

        private void DisableSSL(ref object sslparamobject)
        {
            // Use the type definition to set the correct bits to false
            TypeSslParams.GetField("useSsl").SetValue(sslparamobject, (object)false);
        }

        private object ProxyBNetInit(ref object bnetobject, object[] args)
        {
            // Dynamically invoke the Init method as defined by the type
            var initMethod = TypeBattleNetC.GetMethod("Init");
            return initMethod.Invoke(bnetobject, args);
        }

        // Returns a list of methods (full names) that this hook expects. 
        // The Hooker will cross reference all returned methods with the requested methods.
        public static string[] GetExpectedMethods()
        {
            return new string[] { "bgs.BattleNetCSharp::Init" };
        }

        object OnCall(string typeName, string methodName, object thisObj, object[] args)
        {
            if (typeName != "bgs.BattleNetCSharp" || methodName != "Init")
            {
                return null;
            }
            
            // This is a call from ourselves, so return null to prevent calling ourselves in an infinite loop.
            if (reentrant)
            {
                return null;
            }

            try
            {
                // public bool Init(bool internalMode, string userEmailAddress, string targetServer, 
                //                  int port, SslParameters sslParams);

                DisableSSL(ref args[4]);
                // We actually call OURSELVES here, hence the reentrant
                reentrant = true;
                return ProxyBNetInit(ref thisObj, args);
            }
            catch (Exception e)
            {
                // Write meaningful information to the game output
                var message = String.Format("BattleNetCSharp.Init(..) failed for the following reason: {0}\n{1}", e.Message, e.StackTrace);
                HookRegistry.Panic(message);
            }

            // Never return null after typeName check!
            return (object)true;
        }
    }
}

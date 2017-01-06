// This hook causes Hearthstone to communicate in plaintext, without using TLS/SSL.
// NOTE: This is designed for use with 3rd party servers that don't support TLS connections
// and will cause the Hearthstone client to fail to connect to an official server, therefore
// it is disabled by default.
// To enable this hook, add "BattleNetCSharp.Init" to example_hooks


using System;
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

        private bool reentrant = false;

        public SSLDisable()
        {
            HookRegistry.Register(OnCall);
            // Load necessary calls
            PrepareDynamicCalls();
        }

        private void PrepareDynamicCalls()
        {
            // Prepare dynamic call to Unity
            Assembly libAssembly = Assembly.LoadFrom(AssemblyStore.GetAssemblyPath(AssemblyStore.LIB_TYPE.LIB_CSHARP_FIRSTPASS));
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

        object OnCall(string typeName, string methodName, object thisObj, object[] args)
        {
            if (typeName != "bgs.BattleNetCSharp" || methodName != "Init")
            {
                return null;
            }

            if (reentrant)
                return null;

            reentrant = true;
            try
            {
                // public bool Init(bool internalMode, string userEmailAddress, string targetServer, 
                //                  int port, SslParameters sslParams);

                DisableSSL(ref args[4]);
                return ProxyBNetInit(ref thisObj, args);
            }
            catch (Exception e)
            {
                // Write meaningful information to the game output
                var message = String.Format("BattleNetCSharp.Init(..) failed for the following reason\n{0}\n{1}", e.Message, e.StackTrace);
                HookRegistry.Get().Log(message);

                // Make the game crash!
                throw new Exception("Forced crash because of error!");
            }

            // return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Hooks.PacketDumper
{
    [RuntimeHook]
    class IncomingPackets
    {
        // Represents bgs.BattleNetPacket
        Type TypeBattleNetPacket;
        // Represents PegasusPacket
        Type TypePegasusPacket;

        public IncomingPackets()
        {
            HookRegistry.Register(OnCall);
            // Load necessary calls
            PrepareDynamicCalls();
        }

        private void PrepareDynamicCalls()
        {
            // Prepare dynamic call to CSharp-firstpass library
            // Load from assembly file at currently executing path
            var locFirstPass = Path.Combine(HookRegistry.LibLocation, HookRegistry.LIB_CSHARP_FIRSTP_NAME);
            Assembly libFirstPass = Assembly.LoadFrom(locFirstPass);
            TypeBattleNetPacket = libFirstPass.GetType("bgs.BattleNetPacket");

            var loc = Path.Combine(HookRegistry.LibLocation, HookRegistry.LIB_CSHARP_NAME);
            Assembly lib = Assembly.LoadFrom(loc);
            TypePegasusPacket = lib.GetType("PegasusPacket");
        }

        // Returns a list of methods (full names) that this hook expects. 
        // The Hooker will cross reference all returned methods with the requested methods.
        public static string[] GetExpectedMethods()
        {
            return new string[] { "bgs.BattleNetPacket.IsLoaded", "PegasusPacket.IsLoaded" };
        }

        private object ProxyIsLoaded(string typeName, object thisObj)
        {
            switch(typeName)
            {
                case "bgs.BattleNetPacket":
                    return TypeBattleNetPacket.GetMethod("IsLoaded").Invoke(thisObj, new object[] { });
                    break;
                case "PegasusPacket":
                    return TypePegasusPacket.GetMethod("IsLoaded").Invoke(thisObj, new object[] { });
                    break;
                default:
                    // Returning false here would just introduce undefined behaviour
                    HookRegistry.Panic("Unknown typename!");
                    break;
            }

            return false;
        }

        private void DumpPacket(string typeName, object thisObj)
        {
            // We only have to trigger the encode method, because that one should be hooked as well!
            // We can't call encode and dump the returned value, because that leads to multiple dumps of
            // the same data.
            switch (typeName)
            {
                case "bgs.BattleNetPacket":
                    TypeBattleNetPacket.GetMethod("Encode").Invoke(thisObj, new object[] { });
                    break;
                case "PegasusPacket":
                    TypePegasusPacket.GetMethod("Encode").Invoke(thisObj, new object[] { });
                    break;
                default:
                    // Returning false here would just introduce undefined behaviour
                    HookRegistry.Panic("Unknown typename!");
                    break;
            }
        }


        object OnCall(string typeName, string methodName, object thisObj, object[] args)
        {
            if (typeName != "bgs.BattleNetPacket" || typeName != "PegasusPacket" || methodName != "IsLoaded")
            {
                return null;
            }

            // Call the real method
            object isLoaded = ProxyIsLoaded(typeName, thisObj);


            if((bool)isLoaded == true)
            {
                // If the packet is complete, we copy it to our own stream
                DumpPacket(typeName, thisObj);
            }

            return isLoaded;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Hooks.PacketDumper
{
    [RuntimeHook]
    class OutgoingPackets
    {

        // Represents bgs.BattleNetPacket
        Type TypeBattleNetPacket;
        // Represents PegasusPacket
        Type TypePegasusPacket;

        public OutgoingPackets()
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
            return new string[] { "bgs.BattleNetPacket.Encode", "PegasusPacket.Encode" };
        }

        private object ProxyEncode(string typeName, object thisObj)
        {
            switch (typeName)
            {
                case "bgs.BattleNetPacket":
                    return TypeBattleNetPacket.GetMethod("Encode").Invoke(thisObj, new object[] { });
                    break;
                case "PegasusPacket":
                    return TypePegasusPacket.GetMethod("Encode").Invoke(thisObj, new object[] { });
                    break;
                default:
                    // Returning null here would just introduce undefined behaviour
                    HookRegistry.Panic("Unknown typename!");
                    break;
            }

            return null;
        }

        private void DumpPacket(string typeName, byte[] data)
        {
            // Maybe do some kind of double write protection here?

            var tee = TeeStream.Get();
            switch (typeName)
            {
                case "bgs.BattleNetPacket":
                    tee.WriteBattlePacket(data);
                    break;
                case "PegasusPacket":
                    tee.WritePegasusPacket(data);
                    break;
                default:
                    // Returning null here would just introduce undefined behaviour
                    HookRegistry.Panic("Unknown typename!");
                    break;
            }
        }

        object OnCall(string typeName, string methodName, object thisObj, object[] args)
        {
            if (typeName != "bgs.BattleNetPacket" || typeName != "PegasusPacket" || methodName != "Encode")
            {
                return null;
            }
            // Proxy to the real call
            object data = ProxyEncode(typeName, thisObj);
            // Dump the packet data
            DumpPacket(typeName, (byte[])data);
            // Return the actual data
            return data;
        }
    }
}

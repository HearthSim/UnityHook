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
        object[] EMPTY_ARGS = { };

        // Represents bgs.BattleNetPacket
        Type TypeBattleNetPacket;
        // Represents bnet.protocol.Header -> the header of battle.net packet
        Type TypeBattleNetHeader;

        // Represents PegasusPacket
        Type TypePegasusPacket;

        // This variable is used to control the interception of the hooked method.
        // When TRUE, we return null to allow normal execution of the function.
        // When FALSE, we hook into the call.
        // This switch allows us to call the original method from within this hook class.
        private bool reentrant;


        public IncomingPackets(bool initDynamicCalls)
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
            var locFirstPass = Path.Combine(HookRegistry.LibLocation, HookRegistry.LIB_CSHARP_FIRSTP_NAME);
            Assembly libFirstPass = Assembly.LoadFrom(locFirstPass);
            TypeBattleNetPacket = libFirstPass.GetType("bgs.BattleNetPacket");
            TypeBattleNetHeader = libFirstPass.GetType("bnet.protocol.Header");

            var loc = Path.Combine(HookRegistry.LibLocation, HookRegistry.LIB_CSHARP_NAME);
            Assembly lib = Assembly.LoadFrom(loc);
            TypePegasusPacket = lib.GetType("PegasusPacket");
        }

        // Returns a list of methods (full names) that this hook expects. 
        // The Hooker will cross reference all returned methods with the requested methods.
        public static string[] GetExpectedMethods()
        {
            return new string[] { "bgs.BattleNetPacket::IsLoaded", "PegasusPacket::IsLoaded" };
        }

        private object ProxyIsLoaded(string typeName, object thisObj)
        {
            switch (typeName)
            {
                case "bgs.BattleNetPacket":
                    return TypeBattleNetPacket.GetMethod("IsLoaded").Invoke(thisObj, EMPTY_ARGS);
                case "PegasusPacket":
                    return TypePegasusPacket.GetMethod("IsLoaded").Invoke(thisObj, EMPTY_ARGS);
                default:
                    // Returning false here would just introduce undefined behaviour
                    HookRegistry.Panic("Unknown typename!");
                    break;
            }

            return false;
        }

        // Dumps the current packet onto the tee stream.
        // The packet has to be reconstructed according to the rules found in the respective
        // encoding(..) method.
        private void DumpPacket(string typeName, object thisObj)
        {
            TeeStream tee = TeeStream.Get();
            // Container for our dumped packet.
            MemoryStream dataStream = new MemoryStream();

            switch (typeName)
            {
                case "bgs.BattleNetPacket":
                    {
                        // Get data.
                        object header = TypeBattleNetPacket.GetMethod("GetHeader").Invoke(thisObj, EMPTY_ARGS); // bnet.protocol.Header
                        object data = TypeBattleNetPacket.GetMethod("GetBody").Invoke(thisObj, EMPTY_ARGS); // byte[]

                        // Get sizes of packet parts.
                        uint headerSize = (uint)TypeBattleNetHeader.GetMethod("GetSerializedSize").Invoke(header, EMPTY_ARGS); // uint
                        int bodySize = ((byte[])data).Length;

                        // Write sizes to buffer.
                        int shiftedHeaderSize = ((int)headerSize >> 8);
                        dataStream.WriteByte((byte)(shiftedHeaderSize & 0xff));
                        dataStream.WriteByte((byte)(headerSize & 0xff));

                        // Write header to buffer.
                        TypeBattleNetHeader.GetMethod("Serialize", BindingFlags.Instance | BindingFlags.Public)
                            .Invoke(header, new object[] { dataStream });

                        // Copy body to buffer.
                        dataStream.Write((byte[])data, 0, bodySize);

                        var packetData = dataStream.ToArray();
                        // Write data to tee stream.
                        tee.WriteBattlePacket(packetData, true);
                    }
                    break;
                case "PegasusPacket":
                    {
                        // Get data.
                        object data = TypePegasusPacket.GetMethod("GetBody").Invoke(thisObj, EMPTY_ARGS); // byte[]
                        object type = TypePegasusPacket.GetField("Type", BindingFlags.GetField).GetValue(thisObj); // int

                        // Get size of body.
                        int bodySize = ((byte[])data).Length;

                        // Write sizes to buffer.
                        byte[] typeBytes = BitConverter.GetBytes((int)type); // 4 bytes
                        byte[] sizeBytes = BitConverter.GetBytes(bodySize); // 4 bytes

                        dataStream.Write(typeBytes, 0, 4);
                        dataStream.Write(sizeBytes, 0, 4);

                        // Write body to the stream.
                        dataStream.Write((byte[])data, 0, bodySize);

                        var packetData = dataStream.ToArray();
                        // Write to tee stream.
                        tee.WritePegasusPacket(packetData, true);
                    }
                    break;
                default:
                    // Returning false here would just introduce undefined behaviour
                    HookRegistry.Panic("Unknown typename!");
                    break;
            }
        }


        object OnCall(string typeName, string methodName, object thisObj, object[] args)
        {

            if ((typeName != "bgs.BattleNetPacket" && typeName != "PegasusPacket") || methodName != "IsLoaded")
            {
                return null;
            }

            if (reentrant == true)
            {
                return null;
            }

            // Setting this variable makes sure we don't end up in an infinite loop.
            // Because the hooker calls the hooked method again to fetch the returned data.
            reentrant = true;

            // Call the real method
            object isLoaded = ProxyIsLoaded(typeName, thisObj);

            if ((bool)isLoaded == true)
            {
                // If the packet is complete, we copy it to our own stream
                DumpPacket(typeName, thisObj);
            }

            // Reset state.
            reentrant = false;

            return isLoaded;
        }
    }
}

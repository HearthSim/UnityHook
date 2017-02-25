using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Hooks.PacketDumper
{
    [RuntimeHook]
    class OutgoingPackets
    {
        object[] EMPTY_ARGS = { };

        // Represents bgs.BattleNetPacket
        Type TypeBattleNetPacket;
        // Represents PegasusPacket
        Type TypePegasusPacket;

        // Connection over ssl.
        Type TypeSslConnection;
        // Connection without ssl.
        Type TypeClientConnection;
        // Constructed types with generic params filled in.
        Type TypePegasusConnection;
        Type TypeBattleNetConnection;

        private bool reentrant;

        public OutgoingPackets(bool initDynamicCalls)
        {
            HookRegistry.Register(OnCall);
            reentrant = false;

            if (initDynamicCalls)
            {
                PrepareDynamicCalls();
                RegisterGenericDeclaringTypes();
            }
        }

        private void PrepareDynamicCalls()
        {
            // Prepare dynamic call to CSharp-firstpass library
            // Load from assembly file at currently executing path
            var locFirstPass = Path.Combine(HookRegistry.LibLocation, HookRegistry.LIB_CSHARP_FIRSTP_NAME);
            Assembly libFirstPass = Assembly.LoadFrom(locFirstPass);
            TypeBattleNetPacket = libFirstPass.GetType("bgs.BattleNetPacket");

            TypeSslConnection = libFirstPass.GetType("bgs.SslClientConnection");
            TypeClientConnection = libFirstPass.GetType("bgs.ClientConnection`1");

            var loc = Path.Combine(HookRegistry.LibLocation, HookRegistry.LIB_CSHARP_NAME);
            Assembly lib = Assembly.LoadFrom(loc);
            TypePegasusPacket = lib.GetType("PegasusPacket");

            // Construct generic substituted types
            TypePegasusConnection = TypeClientConnection.MakeGenericType(new Type[] { TypePegasusPacket });
            TypeBattleNetConnection = TypeClientConnection.MakeGenericType(new Type[] { TypeBattleNetPacket });
        }

        private void RegisterGenericDeclaringTypes()
        {
            // We hook into a method from a generic class, so we want to register
            // that generic class in order to resolve the generic instantiation of that 
            // method.
            HookRegistry.RegisterDeclaringType(TypeClientConnection.TypeHandle);
        }

        // Returns a list of methods (full names) that this hook expects. 
        // The Hooker will cross reference all returned methods with the requested methods.
        public static string[] GetExpectedMethods()
        {
            return new string[] { "bgs.SslClientConnection::SendPacket", "bgs.ClientConnection`1::SendPacket" };
        }

        private void ProxySendPacket(string typeName, object thisObj, object[] args)
        {
            switch (typeName)
            {
                case "bgs.SslClientConnection":
                    TypeSslConnection.GetMethod("SendPacket").Invoke(thisObj, args);
                    break;

                case "bgs.ClientConnection`1":
                    // Check type of first (packet) argument to correctly delegate call.
                    Type argType = args[0].GetType();

                    if (argType.Equals(TypeBattleNetPacket))
                    {
                        var method = TypeBattleNetConnection.GetMethod("SendPacket");
                        method.Invoke(thisObj, args);
                    }
                    else if (argType.Equals(TypePegasusPacket))
                    {
                        var method = TypePegasusConnection.GetMethod("SendPacket");
                        method.Invoke(thisObj, args);
                    }
                    else
                    {
                        HookRegistry.Panic("Unknown packet type!");
                    }

                    break;

                default:
                    // Returning null here would just introduce undefined behaviour
                    var msg = string.Format("Unknown typename: {0}!", typeName);
                    HookRegistry.Panic(msg);
                    break;
            }
        }

        // Dump data just as we receive it.
        private void DumpPacket(string typeName, object[] args)
        {
            // Maybe do some kind of double write protection here?
            var tee = TeeStream.Get();
            object packet = args[0];

            switch (typeName)
            {
                case "bgs.SslClientConnection":
                    // The packet is always battle.net packet
                    var packetData = (byte[])TypeBattleNetPacket.GetMethod("Encode").Invoke(packet, EMPTY_ARGS);
                    tee.WriteBattlePacket(packetData, false);
                    break;

                case "bgs.ClientConnection`1":
                    // Test type of the packet.
                    Type argType = packet.GetType();

                    if (argType.Equals(TypeBattleNetPacket))
                    {
                        byte[] data = (byte[])TypeBattleNetPacket.GetMethod("Encode").Invoke(packet, EMPTY_ARGS);
                        tee.WriteBattlePacket(data, false);
                    }
                    else if (argType.Equals(TypePegasusPacket))
                    {
                        byte[] data = (byte[])TypePegasusPacket.GetMethod("Encode").Invoke(packet, EMPTY_ARGS);
                        tee.WritePegasusPacket(data, false);
                    }
                    else
                    {
                        HookRegistry.Panic("Unknown packet type!");
                    }
                    
                    break;

                default:
                    // Returning null here would just introduce undefined behaviour
                    var msg = string.Format("Unknown typename: {0}!", typeName);
                    HookRegistry.Panic(msg);
                    break;
            }
        }

        object OnCall(string typeName, string methodName, object thisObj, object[] args)
        {
            if ((typeName != "bgs.SslClientConnection" && typeName != "bgs.ClientConnection`1") || methodName != "SendPacket")
            {
                return null;
            }

            if (reentrant == true)
            {
                return null;
            }

            reentrant = true;

            // Dump the packet..
            DumpPacket(typeName, args);

            // // Don't proxy, keep going with this method!
            // ProxySendPacket(typeName, thisObj, args);

            reentrant = false;

            // Return something to proceed normal execution.
            return null;
        }
    }
}

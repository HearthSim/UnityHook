// This hook introduces additional code to dump each RECEIVED packet.
// The packet is caught from the communication stream between client and server.

using bgs;
using HackstoneAnalyzer.PayloadFormat;
using System;
using Hooks.PacketDumper;

namespace Hooks
{
	[RuntimeHook]
	class IncomingPackets
	{
		private const string RECEIVED_PACKET_NOTIFY = "RECEIVED Packet type `{0}` - SID: {1} - MID: {2}";

		// This variable is used to control the interception of the hooked method.
		// When TRUE, we return null to allow normal execution of the function.
		// When FALSE, we hook into the call.
		// This switch allows us to call the original method from within this hook class.
		private bool reentrant;

		public IncomingPackets()
		{
			HookRegistry.Register(OnCall);
			reentrant = false;
		}

		// Returns a list of methods (full names) that this hook expects.
		// The Hooker will cross reference all returned methods with the requested methods.
		public static string[] GetExpectedMethods()
		{
			return new string[] { "bgs.BattleNetPacket::IsLoaded", "PegasusPacket::IsLoaded" };
		}

		private object ProxyIsLoaded(string typeName, object thisObj)
		{
			Type packetType = thisObj.GetType();

			if (packetType.Equals(typeof(BattleNetPacket)))
			{
				return ((BattleNetPacket)thisObj).IsLoaded();
			}
			else if (packetType.Equals(typeof(PegasusPacket)))
			{
				return ((PegasusPacket)thisObj).IsLoaded();
			}
			else
			{
				// Returning false here would just introduce undefined behaviour
				HookRegistry.Panic("Unknown typename!");
			}

			return false;
		}

		// Dumps the current packet onto the tee stream.
		// The packet has to be reconstructed according to the rules found in the respective
		// encoding(..) method.
		private void DumpPacket(string typeName, object thisObj)
		{
			// Object that does the duplication of packets.
			var dumper = DumpServer.Get();

			// the name of the packet is retrievable in generalized form.
			Type packetType = thisObj.GetType();
			// More packet specific details.
			string packetTypeString = packetType.Name;
			int methodID = -1;
			int serviceID = -1;

			HookRegistry.Get().Log("Incoming Dump MARK 1");

			if (packetType.Equals(typeof(BattleNetPacket)))
			{
				var packet = ((BattleNetPacket)thisObj);

				// Debug information
				bnet.protocol.Header header = packet.GetHeader();
				methodID = (int)header.MethodId;
				serviceID = (int)header.ServiceId;

				byte[] packetData = Serializer.SerializePacket(packet);
				dumper.SendPacket(PacketType.Battlenetpacket, PacketDirection.Incoming, 0, packetData);
			}
			else if (packetType.Equals(typeof(PegasusPacket)))
			{
				var packet = ((PegasusPacket)thisObj);

				// Debug information
				serviceID = packet.Type;

				byte[] packetData = Serializer.SerializePacket(packet);
				dumper.SendPacket(PacketType.Pegasuspacket, PacketDirection.Incoming, 0, packetData);
			}
			else
			{
				// Returning false here would just introduce undefined behaviour
				HookRegistry.Panic("Unknown packet type!");
			}

			string message = string.Format(RECEIVED_PACKET_NOTIFY, packetTypeString, serviceID, methodID);
			HookRegistry.Get().Log(message);
		}

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{

			if ((typeName != "bgs.BattleNetPacket" && typeName != "PegasusPacket") ||
				methodName != "IsLoaded")
			{
				return null;
			}

			if (reentrant == true)
			{
				return null;
			}

			// Setting this variable makes sure we don't end up in an infinite loop.
			// Because the we call the hooked method again to fetch the actual data.
			reentrant = true;

			// Call the method again in reentrant mode.
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

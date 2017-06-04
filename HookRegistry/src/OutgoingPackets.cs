// This hook introduces additional code to dump each SENT packet.
// The packet is caught from the communication stream between client and server.

using bgs;
using HackstoneAnalyzer.PayloadFormat;
using System;
using Hooks.PacketDumper;
using bnet.protocol.authentication;

namespace Hooks
{
	[RuntimeHook]
	class OutgoingPackets
	{
		private const string SENT_PACKET_NOTIFY = "SENT Packet type `{0}` - SID: {1} - MID: {2} - PayloadType `{3}`";

		// This variable is used to control the interception of the hooked method.
		// When TRUE, we return null to allow normal execution of the function.
		// When FALSE, we hook into the call.
		// This switch allows us to call the original method from within this hook class.
		private bool reentrant;

		public OutgoingPackets()
		{
			HookRegistry.Register(OnCall);
			RegisterGenericDeclaringTypes();
			reentrant = false;
		}

		private void RegisterGenericDeclaringTypes()
		{
			// We hook into a method from a generic class, so we want to register
			// that generic class in order to resolve the generic instantiation of that
			// method.
			HookRegistry.RegisterDeclaringType(typeof(ClientConnection<>).TypeHandle);
		}

		// Returns a list of methods (full names) that this hook expects.
		// The Hooker will cross reference all returned methods with the requested methods.
		public static string[] GetExpectedMethods()
		{
			return new string[] { "bgs.SslClientConnection::SendPacket", "bgs.ClientConnection`1::SendPacket" };
		}

		// Dump data just as we receive it.
		private void DumpPacket(string typeName, object[] args)
		{
			// Object that does the duplication of packets.
			var dumper = DumpServer.Get();

			object packetObj = args[0];
			Type packetType = packetObj.GetType();

			string packetTypeString = packetType.Name;
			int methodID = -1;
			int serviceID = -1;
			object body = null;

			if (packetType.Equals(typeof(BattleNetPacket)))
			{
				var packet = ((BattleNetPacket)packetObj);

				// Debug information
				bnet.protocol.Header header = packet.GetHeader();
				methodID = (int)header.MethodId;
				serviceID = (int)header.ServiceId;
				body = packet.GetBody();

				// Calculate the hash of the body, which is passed to analyzers.
				uint bodyHash = Util.GenerateHashFromObjectType(body);

				byte[] packetData = Serializer.SerializePacket(packet);
				dumper.SendPacket(PacketType.Battlenetpacket, PacketDirection.Outgoing, bodyHash, packetData);

				// Test for LogonRequest body packet, since that one contains the version string
				var logonRequest = body as LogonRequest;
				if(logonRequest != null)
				{
					dumper.InitialiseHandshake(logonRequest.Version);
				}
			}
			else if (packetType.Equals(typeof(PegasusPacket)))
			{
				var packet = ((PegasusPacket)packetObj);

				// Debug information
				serviceID = packet.Type;
				body = packet.GetBody();

				// Calculate the hash of the body, which is passed to analyzers.
				uint bodyHash = Util.GenerateHashFromObjectType(body);

				byte[] packetData = Serializer.SerializePacket(packet);
				dumper.SendPacket(PacketType.Pegasuspacket, PacketDirection.Outgoing, bodyHash, packetData);
			}
			else
			{
				// Returning false here would just introduce undefined behaviour
				HookRegistry.Panic("Unknown packet type!");
			}

			string message = string.Format(SENT_PACKET_NOTIFY, packetTypeString, serviceID, methodID,
										body.GetType().FullName);
			HookRegistry.Get().Log(message);
		}

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{
			if ((typeName != "bgs.SslClientConnection" && typeName != "bgs.ClientConnection`1") ||
				methodName != "SendPacket")
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

			// Don't proxy, because we don't intend to change the methods behaviour.
			// By returning null we effectively have appended functionality at the start of the targetted
			// method.

			reentrant = false;

			// Return something to proceed normal execution.
			return null;
		}
	}
}

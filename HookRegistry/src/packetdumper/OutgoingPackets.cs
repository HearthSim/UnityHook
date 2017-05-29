using bgs;
using System;

namespace Hooks.PacketDumper
{
    [RuntimeHook]
	class OutgoingPackets
	{
		private static object[] EMPTY_ARGS = { };

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
			// Maybe do some kind of double write protection here?
			var tee = TeeStream.Get();
			object packet = args[0];

            string packetTypeString = "unknown";
            int methodID = -1;
            int serviceID = -1;
            object body = null;

            switch (typeName)
			{
				case "bgs.SslClientConnection":
                    {
                        // The packet is always battle.net packet
                        var packetData = ((BattleNetPacket)packet).Encode();

                        // Debug information
                        var header = ((BattleNetPacket)packet).GetHeader();
                        packetTypeString = typeof(BattleNetPacket).Name;
                        methodID = (int)header.MethodId;
                        serviceID = (int)header.ServiceId;
                        body = ((BattleNetPacket)packet).GetBody();


                        tee.WriteBattlePacket(packetData, false);
                    }
					break;

				case "bgs.ClientConnection`1":
					// Test type of the packet.
					Type argType = packet.GetType();

					if (argType.Equals(typeof(BattleNetPacket)))
					{
						byte[] data = ((BattleNetPacket)packet).Encode();

                        // Debug information
                        var header = ((BattleNetPacket)packet).GetHeader();
                        packetTypeString = typeof(BattleNetPacket).Name;
                        methodID = (int)header.MethodId;
                        serviceID = (int)header.ServiceId;
                        body = ((BattleNetPacket)packet).GetBody();

                        tee.WriteBattlePacket(data, false);
					}
					else if (argType.Equals(typeof(PegasusPacket)))
					{
                        byte[] data = ((PegasusPacket)packet).Encode();

                        // Debug information
                        serviceID = ((PegasusPacket)packet).Type;
                        body = ((PegasusPacket)packet).GetBody();

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

            var raw = "Packet type `{0}` - SID: {1} - MID: {2} - PayloadType `{3}`";
            var message = string.Format(raw, packetTypeString, serviceID, methodID, body.GetType().FullName);
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

			// // Don't proxy, keep going with this method!
			// ProxySendPacket(typeName, thisObj, args);

			reentrant = false;

			// Return something to proceed normal execution.
			return null;
		}
	}
}

using bgs;
using System;
using System.IO;

namespace Hooks.PacketDumper
{
	[RuntimeHook]
	class IncomingPackets
	{
		object[] EMPTY_ARGS = { };

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
			TeeStream tee = TeeStream.Get();
			// Container for our dumped packet.
			MemoryStream dataStream = new MemoryStream();

			Type packetType = thisObj.GetType();

			if (packetType.Equals(typeof(BattleNetPacket)))
			{
				BattleNetPacket packet = ((BattleNetPacket)thisObj);
				var header = packet.GetHeader();
				var body = packet.GetBody();

				uint headerSize = header.GetSerializedSize();
				// Body is byte buffer because packet is incoming/serialised!
				int bodySize = ((byte[])body).Length;

				int shiftedHeaderSize = ((int)headerSize >> 8);
				dataStream.WriteByte((byte)(shiftedHeaderSize & 0xff));
				dataStream.WriteByte((byte)(headerSize & 0xff));

				// Write header to buffer.
				header.Serialize(dataStream);

				// Copy body to buffer.
				dataStream.Write((byte[])body, 0, bodySize);

				var packetData = dataStream.ToArray();
				// Write data to tee stream.
				tee.WriteBattlePacket(packetData, true);
			}
			else if (packetType.Equals(typeof(PegasusPacket)))
			{
				PegasusPacket packet = ((PegasusPacket)thisObj);
				var body = packet.GetBody();
				int type = packet.Type;

				int bodySize = ((byte[])body).Length;
				// Write sizes to buffer.
				byte[] typeBytes = BitConverter.GetBytes(type); // 4 bytes
				byte[] sizeBytes = BitConverter.GetBytes(bodySize); // 4 bytes

				dataStream.Write(typeBytes, 0, 4);
				dataStream.Write(sizeBytes, 0, 4);

				// Write body to the stream.
				dataStream.Write((byte[])body, 0, bodySize);

				var packetData = dataStream.ToArray();
				// Write to tee stream.
				tee.WritePegasusPacket(packetData, true);
			}
			else
			{
				// Returning false here would just introduce undefined behaviour
				HookRegistry.Panic("Unknown typename!");
			}
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

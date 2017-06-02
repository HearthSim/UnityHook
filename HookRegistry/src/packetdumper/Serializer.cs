using bgs;
using bnet.protocol;
using System;
using System.IO;

namespace Hooks.PacketDumper
{
	abstract class Serializer
	{
		// Manually serializes the contents of a PegasusPacket.
		public static byte[] SerializePacket(PegasusPacket packet)
		{
			int type = packet.Type;
			object body = packet.GetBody();
			var bodyProto = body as IProtoBuf;
			if (bodyProto != null)
			{
				// There is an encode routine defined for a body which is a protobuffer
				// instance.
				return packet.Encode();
			}

			/* Manual encoding */

			var bodyBuffer = body as byte[];
			if (bodyBuffer == null)
			{
				string message = string.Format("Body of this packet (`{0}`) is not a byte buffer!", body.GetType().Name);
				HookRegistry.Panic(message);
			}

			var dataStream = new MemoryStream();
			int bodySize = bodyBuffer.Length;
			// Write sizes to buffer.
			byte[] typeBytes = BitConverter.GetBytes(type); // 4 bytes
			byte[] sizeBytes = BitConverter.GetBytes(bodySize); // 4 bytes

			dataStream.Write(typeBytes, 0, 4);
			dataStream.Write(sizeBytes, 0, 4);

			// Write body to the stream.
			dataStream.Write(bodyBuffer, 0, bodySize);

			byte[] packetData = dataStream.ToArray();
			return packetData;
		}

		public static byte[] SerializePacket(BattleNetPacket packet)
		{
			Header header = packet.GetHeader();
			object body = packet.GetBody();
			var bodyProto = body as IProtoBuf;
			if (bodyProto != null)
			{
				// There is an encode routine defined for a body which is a protobuffer
				// instance.
				return packet.Encode();
			}

			/* Manual encoding */

			var bodyBuffer = body as byte[];
			if (bodyBuffer == null)
			{
				string message = string.Format("Body of this packet (`{0}`) is not a byte buffer!", body.GetType().Name);
				HookRegistry.Panic(message);
			}

			var dataStream = new MemoryStream();
			uint headerSize = header.GetSerializedSize();
			int shiftedHeaderSize = ((int)headerSize >> 8);

			// Body is byte buffer because packet is incoming/serialised!
			int bodySize = bodyBuffer.Length;

			dataStream.WriteByte((byte)(shiftedHeaderSize & 0xff));
			dataStream.WriteByte((byte)(headerSize & 0xff));

			// Write header to buffer.
			header.Serialize(dataStream);

			// Copy body to buffer.
			dataStream.Write(bodyBuffer, 0, bodySize);

			byte[] packetData = dataStream.ToArray();
			return packetData;
		}
	}
}

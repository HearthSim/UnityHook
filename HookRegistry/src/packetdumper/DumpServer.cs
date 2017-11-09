using bgs;
using bnet.protocol;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using HackstoneAnalyzer.PayloadFormat;
using Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace Hooks.PacketDumper
{
	class DumpServer
	{
		// Analyzers can connect to this port to receive a stream of packet data.
		private const int ANALYZER_LISTENER_PORT = 6666;

		// Timer to generate timespan between start and the moment a packet is sent to the analyzers.
		private Stopwatch _timeWatch;

		private TcpListener _connectionListener;
		private List<Socket> _connections;

		// First payload sent to the listening analyzers.
		private byte[] _handshakePayload;
		// Stream of all packets sent by this dumper.
		// This does NOT contain the handshake payload!
		private MemoryStream _replayBuffer;
		private object _bufferLock;

		// Holds information about all Pegasus Packet type integers.
		private PacketDecoderManager _pegPacketDecoder;

		// Contains the buffers holding partial data during transmissions.
		// The buffers are mapped by their socket instance.
		// Ints are used to prevent cleanup of Socket objects.
		private Map<int, StreamPartialData> _partialStreamBuffers;

		private Stream _dbgOut;

		private class StreamPartialData
		{
			public readonly MemoryStream RecvPartialBuffer;
			public readonly MemoryStream SendPartialBuffer;

			public readonly object RecvLock;
			public readonly object SendLock;

			/* Streams are mutually exclusive for ONE kind of packets ONLY! */
			public bool IsTypeDecided
			{
				get => _isTypeDecided;
				set => _isTypeDecided = true;
			}
			private bool _isTypeDecided;

			public bool HasDecodedBNET
			{
				get => _hasDecodedBNET;
				set
				{
					if (_isTypeDecided)
					{
						HookRegistry.Panic("Second packet type registered on stream!");
					}
					else
					{
						_hasDecodedBNET = true;
						_isTypeDecided = true;
					}
				}
			}
			private bool _hasDecodedBNET;

			public bool HasDecodedPEG
			{
				get => _hasDecodedPEG;
				set
				{
					if (_isTypeDecided)
					{
						HookRegistry.Panic("Second packet type registered on stream!");
					}
					else
					{
						_hasDecodedPEG = true;
						_isTypeDecided = true;
					}
				}
			}
			private bool _hasDecodedPEG;

			public StreamPartialData()
			{
				RecvPartialBuffer = new MemoryStream(0);
				SendPartialBuffer = new MemoryStream(0);

				RecvLock = new object();
				SendLock = new object();

				_isTypeDecided = false;
				_hasDecodedBNET = false;
				_hasDecodedPEG = false;
			}
		}

		protected DumpServer()
		{
			_timeWatch = new Stopwatch();
			_timeWatch.Start();

			_connectionListener = null;
			_connections = new List<Socket>();

			_replayBuffer = new MemoryStream();
			_bufferLock = new object();

			_pegPacketDecoder = new PacketDecoderManager(false);

			_partialStreamBuffers = new Map<int, StreamPartialData>();

			_dbgOut = File.OpenWrite("dbg_packets.hexdump");
			// Truncate as well.
			_dbgOut.SetLength(0);
			_dbgOut.Flush();

			Setup();
		}

		#region INIT

		private void Setup()
		{
			for (int i = 0; i < 5; ++i)
			{
				IPAddress listenAddress = IPAddress.Loopback;
				// Increase the listening port each iteration because multiple dumpservers
				// could be active at the same time.
				int listenPort = ANALYZER_LISTENER_PORT + i;

				try
				{
					_connectionListener = new TcpListener(listenAddress, listenPort);

					string logMsg = String.Format("DumpServer - Listening on {0}:{1}", listenAddress, listenPort);
					HookRegistry.Log(logMsg);
					break;
				}
				catch (Exception) // TODO; Change to explicit exception
				{
					// Do nothing
				}
			}

			if (_connectionListener == null)
			{
				HookRegistry.Log("DumpServer - Ran out of available ports to listen on!");
			}
			else
			{
				try
				{
					InitialiseHandshake();
				}
				catch (Exception e)
				{
					string message = String.Format("Dumpserver - Failed initialising handshake; {0}", e.ToString());
					HookRegistry.Panic(message);
				}
			}
		}

		// Constructs the handshake payload.
		// After the handshake payload has been set, analyzers will be accepted.
		private void InitialiseHandshake()
		{
			string hsVersion = BattleNet.Client().GetApplicationVersion().ToString();

			HookRegistry.Log(String.Format("DumpServer - Initialising handshake with HSVER {0}", hsVersion));
			var handshake = new Handshake()
			{
				Magic = Util.MAGIC_V,
				// HSVersion is unknown at this point.
				HsVersion = hsVersion
			};

			// Write payload with prefixed length to the buffer.
			// Prefixing with length is important since the protobuf doesn't delimit itself!
			var tempBuffer = new MemoryStream();
			handshake.WriteDelimitedTo(tempBuffer);
			_handshakePayload = tempBuffer.ToArray();

			InitialiseStream();
		}

		private static DumpServer _thisObj;

		public static DumpServer Get()
		{
			if (_thisObj == null)
			{
				_thisObj = new DumpServer();
			}

			return _thisObj;
		}

		#endregion

		#region CONN_SETUP

		// Start accepting analyzer connections.
		private void InitialiseStream()
		{
			_connectionListener.Start();
			_connectionListener.BeginAcceptSocket(AcceptAnalyzer, null);
		}

		private void AcceptAnalyzer(IAsyncResult result)
		{
			// Fetch socket to new client.
			// This blocks if no new analyzer has tried to connect.
			Socket client = null;
			try
			{
				client = _connectionListener.EndAcceptSocket(result);

				// Set state of socket as write_only.
				// This will trigger an error on the analyzers IF they try to send data.
				client.Shutdown(SocketShutdown.Receive);
			}
			catch (Exception e)
			{
				string message = String.Format("Connecting analyzer failed for following reason: {0}", e.Message);
				HookRegistry.Log(message);
			}

			if (client != null)
			{
				/*
				 * BeginSend will copy the provided data into the send buffer.
				 * After this copy, the callback will be invoked on a separate thread.
				 * The EndSend(..) method on the callback thread will block until the send operation 
				 * has been completed.
				 * 
				 * Sequential BeginSend operations without EndSend inbetween are allowed, but could hog up 
				 * resources really quickly!
				 */

				try
				{
					lock (_bufferLock)
					{
						// Send handshake payload.
						client.BeginSend(_handshakePayload, 0, _handshakePayload.Length, SocketFlags.None, FinishSocketSend, client);

						// Follow up with all buffered packets.
						// ToArray omits non used buffer space. Don't use ToBuffer()!
						byte[] packetBacklog = _replayBuffer.ToArray();
						client.BeginSend(packetBacklog, 0, packetBacklog.Length, SocketFlags.None, FinishSocketSend, client);

						// Store the client so it's possible to send new data after the backlog.
						// We suppose sending data to the connected analyzer won't fail.
						_connections.Add(client);
					}
				}
				catch (Exception e)
				{
					string message = String.Format("Sending BACKLOG to newly attached analyzer failed for following reason: {0}", e.Message);
					HookRegistry.Log(message);
				}
			}

			// Accept the next analyzer.
			_connectionListener.BeginAcceptSocket(AcceptAnalyzer, null);
		}

		// Call this method when a socket generated an exception and needs to be cleaned up.
		private void CleanSocket(Socket socket)
		{
			try
			{
				socket.Shutdown(SocketShutdown.Both);
				socket.Close();
			}
			catch (Exception)
			{
				// Do nothing.
			}
			finally
			{
				_connections.Remove(socket);
			}
		}

		#endregion

		#region COMMS

		// Store packet to be sent to all attached analyzers.
		// Packets are only sent IF the InitialiseHandshake(..) has been called.
		// It's allowed to 'send' packets before the handshake is initialised.
		public void SendPacket(PacketType type, PacketDirection direction, uint bodyTypeHash, byte[] data, int offset = 0, int count = -1)
		{
			// Disable the dump mechanism when the server is not running.
			if (_connectionListener == null) return;

			// If count was not provided, set it to the length of the entire buffer.
			if (count < 0)
			{
				count = data.Length;
			}

			// Construct new payload to send.
			var packet = new CapturedPacket()
			{
				Type = type,
				Direction = direction,
				Data = ByteString.CopyFrom(data, offset, count),
				BodyTypeHash = bodyTypeHash,
				PassedSeconds = Duration.FromTimeSpan(_timeWatch.Elapsed)
			};

			// Write length delimited payload.
			var tempBuffer = new MemoryStream();
			packet.WriteDelimitedTo(tempBuffer);
			byte[] packetBytes = tempBuffer.ToArray();


			Socket[] connectionsCopy;
			lock (_bufferLock)
			{
				// Append data to the replay buffer.
				_replayBuffer.Write(packetBytes, 0, packetBytes.Length);
				// Race conditions occur when a copy isn't made.
				connectionsCopy = _connections.ToArray();
			}

			// Trigger a send of new data on all analyzer connections.
			foreach (Socket connection in connectionsCopy)
			{
				try
				{
					connection.BeginSend(packetBytes, 0, packetBytes.Length, SocketFlags.None, FinishSocketSend, connection);
				}
				catch (Exception e)
				{
					string message = String.Format("Sending data to analyzer caused exception `{0}`, connection is closed!", e.Message);
					HookRegistry.Log(message);
					CleanSocket(connection);
				}
			}
		}

		// Clean up socket send resources.
		private void FinishSocketSend(IAsyncResult result)
		{
			var client = (Socket)result.AsyncState;
			bool finished = result.IsCompleted;
			try
			{
				client.EndSend(result);
			}
			catch (Exception e)
			{
				string message = String.Format("Sending data to analyzer caused exception `{0}`, connection is closed!", e.Message);
				HookRegistry.Log(message);
				CleanSocket(client);
			}
		}

		#endregion

		#region DECOMPOSITION

		public void SendPartialData(Socket socket, bool isIncomingData, byte[] buffer, int offset, int count)
		{
			if (_connectionListener == null) return;

			byte[] seperator = new byte[] { 0xCC, 0x00, 0x00, 0x00, 0xCC };
			_dbgOut.Write(buffer, offset, count);
			_dbgOut.Write(seperator, 0, seperator.Length);
			_dbgOut.Flush();

			int streamKey = socket.GetHashCode();
			StreamPartialData meta;
			_partialStreamBuffers.TryGetValue(streamKey, out meta);
			if (meta == null)
			{
				meta = new StreamPartialData();
				_partialStreamBuffers[streamKey] = meta;
			}

			PacketDirection dataDirection = (isIncomingData) ? PacketDirection.Incoming : PacketDirection.Outgoing;

			// Store new data into correct buffer.
			object bufferLock = (isIncomingData) ? meta.RecvLock : meta.SendLock;
			MemoryStream correctStream = (isIncomingData) ? meta.RecvPartialBuffer : meta.SendPartialBuffer;

			bool typeNewlyDecided = false;
			lock (bufferLock)
			{
				HookRegistry.Log(String.Format("{0} - Storing {1} bytes into buffer", socket.GetHashCode(), count));
				correctStream.Write(buffer, offset, count);
				// We suppose connection handshakes are always done by SENDING exactly ONE packet on the stream.
				if (!meta.IsTypeDecided && isIncomingData == false)
				{
					DecideStreamPacketType(socket, meta);
					typeNewlyDecided = true;
				}

				if (meta.IsTypeDecided)
				{
					if (meta.HasDecodedBNET)
					{
						LoopDeserializeBNET(correctStream, dataDirection);
					}
					else if (meta.HasDecodedPEG)
					{
						LoopDeserializePEG(correctStream, dataDirection);
					}
				}
			}

			// If the type was newly decided it's possible that packets in the opposite direction are waiting to
			// be serialized.
			if (meta.IsTypeDecided && typeNewlyDecided)
			{
				SendPartialData(socket, !isIncomingData, new byte[] { }, 0, 0);
			}
		}

		private void DecideStreamPacketType(Socket socket, StreamPartialData meta, PacketDirection dataDirection = PacketDirection.Outgoing)
		{
			int usedBytes;
			uint bodyHash;

			MemoryStream correctStream = (dataDirection == PacketDirection.Incoming) ? meta.RecvPartialBuffer : meta.SendPartialBuffer;
			byte[] buffer = correctStream.GetBuffer();
			int availableBytes = (int)correctStream.Length;
			int offset = 0;

			if (buffer.Length < availableBytes)
			{
				HookRegistry.Panic("Buffer is not big enough to satisfy availablebytes!");
			}

			/* 
			 * Try decoding as BNET packet 
			 * 
			 * Decode 1 packet from the provided data.
			 * The type is succesfully decided when there is NO data left after reconstructing one packet in this buffer!
			 */
			try
			{
				var currentPacket = new BattleNetPacket();
				usedBytes = currentPacket.Decode(buffer, offset, availableBytes);

				if (currentPacket.IsLoaded())
				{
					if (usedBytes == availableBytes)
					{
						meta.HasDecodedBNET = true;
						return;
					}
					else
					{
						HookRegistry.Log(String.Format("BNET decoded, but bytes left in buffer! {0} <=> {1}", usedBytes, availableBytes));
					}
				}
			}
			catch (Exception)
			{
				// Do nothing.
			}

			/* 
			 * Try decoding as PEG packet
			 * 
			 */
			PegasusPacket pegPacket = DeserializePEG(buffer, offset, availableBytes, out usedBytes, out bodyHash);
			if (pegPacket != null)
			{
				if (usedBytes == availableBytes)
				{
					meta.HasDecodedPEG = true;
					return;
				}
				else
				{
					HookRegistry.Log("PEG decoded, but bytes left in buffer!");
				}
			}

			HookRegistry.Panic("Couldn't find out which packets are transmitted on this socket!");
		}

		/// <summary>
		/// Remove all bytes skipped, because these were already processed.
		/// The unprocessed bytes will be block copied to the front of the stream.
		/// </summary>
		/// <param name="stream"></param>
		/// <param name="skippedBytes"></param>
		private void TrimMemoryStream(MemoryStream stream, int skippedBytes)
		{
			HookRegistry.Log(String.Format("Asked to trim off {0} bytes", skippedBytes));

			if (skippedBytes > stream.Length || skippedBytes < 1)
			{
				HookRegistry.Panic("Cannot skip more bytes than available inside the memory stream!");
			}

			int remainingBytes = (int)(stream.Length - skippedBytes);
			if (remainingBytes > 0)
			{
				byte[] directBuffer = stream.GetBuffer();
				Buffer.BlockCopy(directBuffer, skippedBytes, directBuffer, 0, remainingBytes);
				stream.SetLength(remainingBytes);
			}
			else
			{
				// Reset buffer to empty
				stream.SetLength(0);
			}
		}

		#endregion

		#region DECOM_BNET

		private void LoopDeserializeBNET(MemoryStream stream, PacketDirection direction)
		{
			int endComposedOffset = 0;
			int packetsComposed = 0;

			byte[] dataBuffer = stream.GetBuffer();
			int dataOffset = 0;
			int bufferSize = (int)stream.Length;

			while (dataOffset < bufferSize)
			{
				int availableData = bufferSize - dataOffset;

				int usedBytes;
				BattleNetPacket bnetPacket = DeserializeBNET(dataBuffer, dataOffset, availableData, out usedBytes);
				if (bnetPacket != null)
				{
					SendPacket(PacketType.Battlenetpacket, direction, 0, dataBuffer, dataOffset, usedBytes);
					dataOffset += usedBytes;

					endComposedOffset = dataOffset;
					packetsComposed++;
				}
				else
				{
					break;
				}
			}

			if (packetsComposed > 0 && endComposedOffset > 0)
			{
				TrimMemoryStream(stream, endComposedOffset);
			}

			HookRegistry.Log(String.Format("DumpServer - Deserialized {0} BNET packets. {1} bytes left in buffer", packetsComposed, stream.Length));
		}

		private BattleNetPacket DeserializeBNET(byte[] buffer, int offset, int count, out int usedBytes)
		{
			usedBytes = 0;

			int availableBytes = count;
			try
			{
				var currentPacket = new BattleNetPacket();
				usedBytes = currentPacket.Decode(buffer, offset, availableBytes);

				if (currentPacket.IsLoaded())
				{
					// Currently there are no checks possible to verify decoding was successful!
					// **Reported header size VS header serialized size DOES NOT work, the deserialize method 
					// **from SilentOrbit is broken.
					return currentPacket;
				}
			}
#pragma warning disable CS0168 // Variable is declared but never used
			catch (Exception e)
			{
				// HookRegistry.Log(String.Format("Exception while decoding BNET packet:\n{0}", e.ToString()));
			}
#pragma warning restore CS0168 // Variable is declared but never used

			return null;
		}

		#endregion

		#region DECOMP_PEG

		private void LoopDeserializePEG(MemoryStream stream, PacketDirection direction)
		{
			int endComposedOffset = 0;
			int packetsComposed = 0;

			byte[] dataBuffer = stream.GetBuffer();
			int dataOffset = 0;
			int bufferSize = (int)stream.Length;

			while (dataOffset < bufferSize)
			{
				int availableData = bufferSize - dataOffset;

				int usedBytes;
				uint bodyHash;
				PegasusPacket pegPacket = DeserializePEG(dataBuffer, dataOffset, availableData, out usedBytes, out bodyHash);
				if (pegPacket != null)
				{
					SendPacket(PacketType.Pegasuspacket, direction, bodyHash, dataBuffer, dataOffset, usedBytes);
					dataOffset += usedBytes;

					endComposedOffset = dataOffset;
					packetsComposed++;
				}
				else
				{
					break;
				}
			}

			if (packetsComposed > 0 && endComposedOffset > 0)
			{
				TrimMemoryStream(stream, endComposedOffset);
			}

			HookRegistry.Log(String.Format("DumpServer - Deserialized {0} PEG packets. {1} bytes left in buffer", packetsComposed, stream.Length));
		}

		private PegasusPacket DeserializePEG(byte[] buffer, int offset, int count, out int usedBytes, out uint bodyHash)
		{
			usedBytes = 0;
			bodyHash = 0;

			try
			{
				var currentPacket = new PegasusPacket();

				int availableBytes = count;
				usedBytes = currentPacket.Decode(buffer, offset, availableBytes);

				if (currentPacket.IsLoaded())
				{
					if (!_pegPacketDecoder.CanDecodePacket(currentPacket.Type))
					{
						// Deserialize byte buffer (body) back into a ProtoBuf object.
						// This is an additional false positive prevention barrier and 
						// allows it's hash to be formulated.
						PegasusPacket decodedPacket = _pegPacketDecoder.DecodePacket(currentPacket);
						if (decodedPacket == null)
						{
							// Error occurred during reconstruction of the pegasus packet.
							return null;
						}
						else
						{
							// If succesfully deserialized we can generate a hash from the packet contents.
							bodyHash = Util.GenerateHashFromObjectType(decodedPacket.GetBody());
							return currentPacket;
						}
					}
					else
					{
						HookRegistry.Log("PEG decoded, but bytes left in buffer!");
					}
				}
			}
#pragma warning disable CS0168 // Variable is declared but never used
			catch (Exception e)
			{
				// HookRegistry.Log(String.Format("Exception while decoding PEG packet:\n{0}", e.ToString()));
			}
#pragma warning restore CS0168 // Variable is declared but never used

			return null;
		}

		#endregion

		#region SERIALIZATION

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
				string message = String.Format("Body of this packet (`{0}`) is not a byte buffer!", body.GetType().Name);
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
				string message = String.Format("Body of this packet (`{0}`) is not a byte buffer!", body.GetType().Name);
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

		#endregion
	}
}

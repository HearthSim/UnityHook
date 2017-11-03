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
		private PegasusPacketDecoder _pegPacketDecoder;

		// Contains the buffers holding partial data during transmissions.
		// The buffers are mapped by their socket instance.
		// Ints are used to prevent cleanup of Socket objects.
		private Map<int, StreamPartialData> _partialStreamBuffers;

		// private Stream _dbgOut;

		private class StreamPartialData
		{
			// Buffer for holding (partial) data received by the game.
			public readonly MemoryStream RecvPartialBuffer;
			// Buffer for holding (partial) data transmitted by the game.
			public readonly MemoryStream SendPartialBuffer;

			// Lock which must be aqcuired before operating on RecvPartialBuffer.
			public readonly object RecvLock;
			// Lock which must be aqcuired before operating on SendPartialBuffer.
			public readonly object SendLock;

			// True if we gave up on dumping data from this stream, false otherwise.
			public bool DoIgnore
			{
				get => _doIgnore;
				set
				{
					_doIgnore = true;
				}
			}

			private bool _doIgnore;
			private int _detectionTries;

			/* Streams are mutually exclusive for ONE kind of packets ONLY! */
			// True if the packet type transmitted on this stream is known, false otherwise.
			public bool IsTypeDecided
			{
				get => _isTypeDecided;
				set => _isTypeDecided = true;
			}
			private bool _isTypeDecided;

			// True if BNET packets were detected on this stream, false otherwise.
			public bool HasDecodedBNET
			{
				get => _hasDecodedBNET;
				set
				{
					if (_isTypeDecided)
					{
						HookRegistry.Panic("DumpServer - Second packet type registered on stream!");
					}
					else
					{
						_hasDecodedBNET = true;
						_isTypeDecided = true;
					}
				}
			}
			private bool _hasDecodedBNET;

			// True if PEG packets were detected on this stream, false otherwise.
			public bool HasDecodedPEG
			{
				get => _hasDecodedPEG;
				set
				{
					if (_isTypeDecided)
					{
						HookRegistry.Panic("DumpServer - Second packet type registered on stream!");
					}
					else
					{
						_hasDecodedPEG = true;
						_isTypeDecided = true;
					}
				}
			}
			private bool _hasDecodedPEG;

			// True if the representing connection (read: socket) got wrapped by an abstracting object,
			// false otherwise.
			// eg: Socket is hooked, which dumps all raw bytes. SslStream is hooked, which dumps the 
			// bytes BEFORE they got encrypted. => We want to dump data from the wrapping object (SslStream)
			// because Socket would only give us the encrypted data.
			public bool ConnectionIsWrapped => _connectionIsWrapped;
			private bool _connectionIsWrapped;

			public StreamPartialData(bool connectionIsWrapped)
			{
				RecvPartialBuffer = new MemoryStream(0);
				SendPartialBuffer = new MemoryStream(0);

				_doIgnore = false;
				_detectionTries = 0;

				RecvLock = new object();
				SendLock = new object();

				_isTypeDecided = false;
				_hasDecodedBNET = false;
				_hasDecodedPEG = false;

				_connectionIsWrapped = connectionIsWrapped;
			}

			// Transition this object to hold the data of the wrapped stream only!
			public void MoveToWrappedConnection()
			{
				if (!_connectionIsWrapped)
				{
					_connectionIsWrapped = true;

					// Remove all data from the buffers because the stream moves from unwrapped
					// to wrapped state.
					// This indicates that we're holding garbage data because the unwrapped data might
					// have been obfuscated.
					lock (RecvLock)
					{
						RecvPartialBuffer.SetLength(0);
					}
					lock (SendLock)
					{
						SendPartialBuffer.SetLength(0);
					}

					// Also reset the ignore status for this stream.
					_doIgnore = false;
				}
			}

			// Increase the amount of tries in order to detect the type of packet transmitted on this stream.
			// This object will set DoIgnore to true after a predefined amount of tries is exceeded.
			public void IncreasePacketDetectionTries()
			{
				_detectionTries++;
				if (_detectionTries > 3)
				{
					// At 3 retries, ignore the stream!
					_doIgnore = true;
					// We CANNOT dispose of the buffers because we don't know if they should live
					// because of stream wrapping or not!
					//lock (RecvLock)
					//{
					//	RecvPartialBuffer.Dispose();
					//}
					//lock (SendLock)
					//{
					//	SendPartialBuffer.Dispose();
					//}
				}
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

			_pegPacketDecoder = new PegasusPacketDecoder();

			_partialStreamBuffers = new Map<int, StreamPartialData>();

			//_dbgOut = File.OpenWrite("dbg_packets.hexdump");
			//// Truncate as well.
			//_dbgOut.SetLength(0);
			//_dbgOut.Flush();

			Setup();
		}

		#region INIT

		private void Setup()
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

			IPAddress listenAddress = IPAddress.Loopback;
			int listenPort = 0;
			for (int i = 0; i < 5; ++i)
			{
				// Increase the listening port each iteration because multiple dumpservers
				// could be active at the same time.
				listenPort = ANALYZER_LISTENER_PORT + i;

				// Becomes _connectionListener
				var tempListener = new TcpListener(listenAddress, listenPort);

				try
				{
					tempListener.Start();
				}
				catch (SocketException)
				{
					// Do nothing, just skip to the next port
					continue;
				}

				// Succesfully bound to a port.
				_connectionListener = tempListener;
				// Start asynchronously accepting analyzers.
				_connectionListener.BeginAcceptSocket(AcceptAnalyzer, null);
				// Continue setup process.
				break;
			}

			if (_connectionListener == null)
			{
				HookRegistry.Log("DumpServer - Ran out of available ports to listen on!");
			}
			else
			{
				HookRegistry.Log("DumpServer - Listening on {0}:{1}", listenAddress, listenPort);
			}


		}

		// Constructs the handshake payload.
		// After the handshake payload has been set, analyzers will be accepted.
		private void InitialiseHandshake()
		{
			string hsVersion = BattleNet.Client().GetApplicationVersion().ToString();

			HookRegistry.Log("DumpServer - Initialising handshake with HSVER {0}", hsVersion);
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
				HookRegistry.Log("DumpServer - Connecting analyzer failed for following reason: {0}", e.Message);
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
					HookRegistry.Log("DumpServer - Sending BACKLOG to newly attached analyzer failed for following reason: {0}", e.Message);
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
					HookRegistry.Log("DumpServer - Sending data to analyzer caused exception `{0}`, connection is closed!", e.Message);
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
				HookRegistry.Log("DumpServer - Sending data to analyzer caused exception `{0}`, connection is closed!", e.Message);
				CleanSocket(client);
			}
		}

		#endregion

		#region DECOMPOSITION

		// Data is only dumped if the data operation suceeded, which means the data comes in
		// depth first.
		// First we'll see raw socket data, afterwards (possibly) unobfuscated data from wrapping streams.
		// To prevent recording garbage we prepare the memory object for each socket and tell it if we're 
		// a wrapper or not.
		public void PreparePartialBuffers(Socket socket, bool isWrapping)
		{
			int streamKey = socket.GetHashCode();
			StreamPartialData meta;
			_partialStreamBuffers.TryGetValue(streamKey, out meta);
			if (meta == null)
			{
				meta = new StreamPartialData(isWrapping);
				_partialStreamBuffers[streamKey] = meta;

				if (isWrapping)
				{
					HookRegistry.Debug("DumpServer - Wrapping stream registered - {0}", streamKey);
				}
				else
				{
					HookRegistry.Debug("DumpServer - Non-Wrapping stream registered - {0}", streamKey);
				}
			}
			else
			{
				// Clear buffers if suddenly a wrapping stream is registered over the unwrapped one.
				if (isWrapping && !meta.ConnectionIsWrapped)
				{
					HookRegistry.Log("DumpServer - {0} - Moving from unwrapped to wrapped state!", streamKey);
					meta.MoveToWrappedConnection();
				}
			}
		}

		// Copies the provided data into owned buffers.
		// The buffers are inspected for game packets and transmitted to the analyzers (if listening).
		// singleDecode is used to instruct the decoder loop to stop after succesfully decoding ONE packet, instead
		// of trying to decode more packets afterwards.
		public void PartialData(Socket socket, bool isIncomingData, byte[] buffer, int offset, int count, bool isWrapping, bool singleDecode = false)
		{
			if (_connectionListener == null) return;

			//byte[] seperator = new byte[] { 0xCC, 0x00, 0x00, 0x00, 0xCC };
			//_dbgOut.Write(buffer, offset, count);
			//_dbgOut.Write(seperator, 0, seperator.Length);
			//_dbgOut.Flush();

			int streamKey = socket.GetHashCode();
			StreamPartialData meta;
			_partialStreamBuffers.TryGetValue(streamKey, out meta);
			if (meta == null)
			{
				HookRegistry.Panic("DumpServer - Provided key is not registered!");
				return;
			}

			if (meta.DoIgnore) return;

			// Skip unwrapped data because it might be obfuscated.
			if (!isWrapping && meta.ConnectionIsWrapped)
			{
				// HookRegistry.Debug("DumpServer - {0} - ignoring data because of wrapping", streamKey);
				return;
			}

			PacketDirection dataDirection = (isIncomingData) ? PacketDirection.Incoming : PacketDirection.Outgoing;

			// Store new data into correct buffer.
			object bufferLock = (isIncomingData) ? meta.RecvLock : meta.SendLock;
			MemoryStream correctStream = (isIncomingData) ? meta.RecvPartialBuffer : meta.SendPartialBuffer;

			bool typeNewlyDecided = false;
			lock (bufferLock)
			{
				HookRegistry.Debug("DumpServer - {0} - Storing {1}/{2} bytes into buffer", socket.GetHashCode(), count, buffer.Length);
				try
				{
					correctStream.Write(buffer, offset, count);
				}
				catch (ArgumentOutOfRangeException)
				{
					string message = String.Format("Offset: {0}, count: {1}, buffSize: {2}", offset, count, buffer.Length);
					HookRegistry.Panic(message);

					throw;
				}

				// We suppose connection handshakes are always done by SENDING exactly ONE packet on the stream.
				if (!meta.IsTypeDecided && isIncomingData == false)
				{
					DecideStreamPacketType(meta);
					typeNewlyDecided = true;
				}

				if (meta.IsTypeDecided)
				{
					if (meta.HasDecodedBNET)
					{
						LoopDeserializeBNET(correctStream, dataDirection, false | singleDecode);
					}
					else if (meta.HasDecodedPEG)
					{
						LoopDeserializePEG(correctStream, dataDirection, false | singleDecode);
					}
				}
			}

			// If the type was newly decided it's possible that packets in the opposite direction are waiting to
			// be serialized.
			if (meta.IsTypeDecided && typeNewlyDecided)
			{
				PartialData(socket, !isIncomingData, new byte[] { }, 0, 0, isWrapping, singleDecode);
			}
		}

		// Tries to decode the first packet transmitted on the provided stream.
		private void DecideStreamPacketType(StreamPartialData meta, PacketDirection dataDirection = PacketDirection.Outgoing)
		{
			int usedBytes;
			uint bodyHash;

			MemoryStream correctStream = (dataDirection == PacketDirection.Incoming) ? meta.RecvPartialBuffer : meta.SendPartialBuffer;
			byte[] buffer = correctStream.GetBuffer();
			int availableBytes = (int)correctStream.Length;
			int offset = 0;

			if (buffer.Length < availableBytes)
			{
				HookRegistry.Panic("DumpServer - Buffer is not big enough to satisfy availablebytes!");
			}

			// Increase detection tries.
			meta.IncreasePacketDetectionTries();

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
						HookRegistry.Log("BNET packets were detected on stream!");
						return;
					}
					else
					{
						HookRegistry.Log("DumpServer - BNET decoded, but bytes left in buffer! {0} <=> {1}", usedBytes, availableBytes);
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
					HookRegistry.Log("PEG packets were detected on stream!");
					return;
				}
				else
				{
					HookRegistry.Log("DumpServer - PEG decoded, but bytes left in buffer!");
				}
			}

			byte[] buffSlice = buffer.Slice(0, availableBytes);
			HookRegistry.Log("DumpServer - Failed to detect packet type on stream:\n{0}", buffSlice.ToHexString());
		}

		/// <summary>
		/// Remove all bytes skipped, because these were already processed.
		/// The unprocessed bytes will be block copied to the front of the stream.
		/// </summary>
		/// <param name="stream"></param>
		/// <param name="skippedBytes"></param>
		private void TrimMemoryStream(MemoryStream stream, int skippedBytes)
		{
			HookRegistry.Debug("DumpServer - Asked to trim off {0} bytes", skippedBytes);

			if (skippedBytes > stream.Length || skippedBytes < 1)
			{
				HookRegistry.Panic("DumpServer - Cannot skip more bytes than available inside the memory stream!");
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
				// Indicate the stream is empty.
				stream.SetLength(0);
			}
		}

		#endregion

		#region DECOM_BNET

		// Tries to deserialise as much as possible BNET packets from the provided stream.
		private void LoopDeserializeBNET(MemoryStream stream, PacketDirection direction, bool singleRun)
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

					if (singleRun == true) break;
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

			HookRegistry.Debug("DumpServer - Deserialized {0} BNET packets. {1} bytes left in buffer", packetsComposed, stream.Length);
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

		// Tries to deserialise as much as possible PEG packets from the provided stream.
		private void LoopDeserializePEG(MemoryStream stream, PacketDirection direction, bool singleRun)
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

					if (singleRun) break;
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

			HookRegistry.Debug("DumpServer - Deserialized {0} PEG packets. {1} bytes left in buffer", packetsComposed, stream.Length);
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
					if (_pegPacketDecoder.CanDecodePacket(currentPacket.Type))
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
							bodyHash = _pegPacketDecoder.GetPegTypeHash(currentPacket.Type);
							return currentPacket;
						}
					}
					else
					{
						HookRegistry.Debug("DumpServer - Unknown PEG Type {0}", currentPacket.Type);
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

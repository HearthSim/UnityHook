using bgs;
using bnet.protocol;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using HackstoneAnalyzer.PayloadFormat;
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

		// Contains the buffers holding partial data during transmissions.
		// The buffers are mapped by their stream instance.
		private Map<object, StreamPartialData> _partialStreamBuffers;

		private class StreamPartialData
		{
			public readonly MemoryStream RecvPartialBuffer;
			public readonly MemoryStream SendPartialBuffer;
			public bool IgnoreRead;
			public bool IgnoreWrite;

			public readonly object ReadLock;
			public readonly object WriteLock;

			//public BattleNetPacket RecvBNETPacket { 
			//	get => _recvBNETPacket ?? (_recvBNETPacket = new BattleNetPacket()); 
			//	set => _recvBNETPacket = null;
			//}
			//public BattleNetPacket SendBNETPacket {
			//	get => _sendBNETPacket ?? (_sendBNETPacket = new BattleNetPacket());
			//	set => _sendBNETPacket = null;
			//}
			//public PegasusPacket RecvPEGPacket {
			//	get => _recvPEGPacket ?? (_recvPEGPacket = new PegasusPacket());
			//	set => _recvPEGPacket = null;
			//}
			//public PegasusPacket SendPEGPacket {
			//	get => _sendPEGPacket ?? (_sendPEGPacket = new PegasusPacket());
			//	set => _sendPEGPacket = null;
			//}

			//private BattleNetPacket _recvBNETPacket;
			//private BattleNetPacket _sendBNETPacket;

			//private PegasusPacket _recvPEGPacket;
			//private PegasusPacket _sendPEGPacket;

			public StreamPartialData()
			{
				RecvPartialBuffer = new MemoryStream();
				SendPartialBuffer = new MemoryStream();

				IgnoreRead = false;
				IgnoreWrite = false;

				ReadLock = new object();
				WriteLock = new object();

				//_recvBNETPacket = null;
				//_sendBNETPacket = null;
				//_recvPEGPacket = null;
				//_sendPEGPacket = null;
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

			_partialStreamBuffers = new Map<object, StreamPartialData>();

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
		public void SendPacket(PacketType type, PacketDirection direction, uint bodyTypeHash, byte[] data)
		{
			// Disable the dump mechanism when the server is not running.
			if (_connectionListener == null) return;

			HookRegistry.Log("Packet HIT!");

			// Construct new payload to send.
			var packet = new CapturedPacket()
			{
				Type = type,
				Direction = direction,
				Data = ByteString.CopyFrom(data, 0, data.Length),
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

		public void SendPartialData(object streamKey, bool incomingData, byte[] buffer, int offset, int count)
		{
			HookRegistry.Log(String.Format("offset: {0}, count: {1}", offset, count));
			//byte[] pickedSlice = new byte[count];
			//Buffer.BlockCopy(buffer, offset, pickedSlice, 0, count);
			//HookRegistry.Log(pickedSlice.ToHexString());

			StreamPartialData meta;
			_partialStreamBuffers.TryGetValue(streamKey, out meta);
			if (meta == null)
			{
				meta = new StreamPartialData();
				_partialStreamBuffers[streamKey] = meta;
			}

			object bufferLock = (incomingData) ? meta.ReadLock : meta.WriteLock;
			PacketDirection packetDirection = (incomingData) ? PacketDirection.Incoming : PacketDirection.Outgoing;
			int packetsComposed = 0;

			lock (bufferLock)
			{
				MemoryStream activeBuffer = (incomingData) ? meta.RecvPartialBuffer : meta.SendPartialBuffer;
				// Push new contents into the buffer.
				activeBuffer.Write(buffer, offset, count);

				/* Re-evaluate buffer for completed packets, which will be distributed by the dumpserver. */
				byte[] bufferSnap = activeBuffer.GetBuffer();

				int snapLength = (int)activeBuffer.Length;
				int snapOffset = 0;
				int lastComposedOffset = 0;
				bool redo = true;

				while (true)
				{
					while (snapOffset < snapLength)
					{
						// BNET
						try
						{
							var currentPacket = new BattleNetPacket();
							int usedBytes = currentPacket.Decode(bufferSnap, snapOffset, (snapLength - snapOffset));

							// We are only interested in complete packets, because we don't know much about the data
							// being processed.
							if (!currentPacket.IsLoaded())
							{
								break;
							}

							// BodyHash can't be determined because we only have access to the encoded version
							// of the object.
							// uint bodyHash = Util.GenerateHashFromObjectType(currentPacket.GetBody());
							byte[] packetBuffer = bufferSnap.Slice(snapOffset, snapOffset + usedBytes);
							lastComposedOffset = snapOffset + usedBytes;
							snapOffset += usedBytes;

							SendPacket(PacketType.Battlenetpacket, packetDirection, 0, packetBuffer);
							packetsComposed++;
						}
						catch (Exception e)
						{
							if (e is NotImplementedException || e is ProtocolBufferException)
							{
								// Exception thrown by the proto decode method.
								// Just ignore it and continue with the next packet.
							}
							else
							{
								// There is an additional check; `header == null` which would throw an Exception object
								// but that condition will never be met since the deserialization technique is in-object
								// from stream.
								// If they change the deserialization to `DeserializeLengthDelimited` that check could pass
								// and throw an Exception object.
								throw;
							}
						}
					}

					while (snapOffset < snapLength)
					{
						// PEG
						try
						{
							var currentPacket = new PegasusPacket();
							int usedBytes = currentPacket.Decode(bufferSnap, snapOffset, (snapLength - snapOffset));

							// We are only interested in complete packets, because we don't know much about the data
							// being processed
							if (!currentPacket.IsLoaded())
							{
								break;
							}

							// BodyHash can't be determined because we only have access to the encoded version
							// of the object.
							// uint bodyHash = Util.GenerateHashFromObjectType(currentPacket.GetBody());
							byte[] packetBuffer = bufferSnap.Slice(snapOffset, snapOffset + usedBytes);
							lastComposedOffset = snapOffset + usedBytes;
							snapOffset += usedBytes;

							SendPacket(PacketType.Pegasuspacket, packetDirection, 0, packetBuffer);
							packetsComposed++;
						}
						catch (Exception e)
						{
							if (e is NotImplementedException || e is ProtocolBufferException)
							{
								// Exception thrown by the proto decode method.
								// Just ignore it and continue with the next packet.
							}
							else if (e is OverflowException)
							{
								// https://msdn.microsoft.com/en-us/library/system.overflowexception(v=vs.110).aspx
								// This exception is probably triggered by `newarr` operation.

								// DEBUG
								//int amount = (snapLength - snapOffset);
								//byte[] pickedSlice = new byte[amount];
								//Buffer.BlockCopy(bufferSnap, snapOffset, pickedSlice, 0, amount);
								//HookRegistry.Log(pickedSlice.ToHexString());

								HookRegistry.Log(String.Format("OVERFLOW; size={0}", activeBuffer.Length));

								//throw;
							}
							else
							{
								throw;
							}
						}
					}

					if (packetsComposed == 0)
					{
						/* 
						 * 2 situations are possible;
						 * Either the stream object has inserted padding for whatever reason (TLS handshaking).
						 * The activebuffer holds no COMPLETE packet (fragmented body).
						 */
						if (!redo) break;
						redo = false;

						HookRegistry.Log(String.Format("DumpServer - SKIP 1 FOR RECONSTRUCTION {{{0}}}", bufferSnap[snapOffset].ToString("X2")));
						// The naïve solution to both problems is to increase the offset until we find a new packet.
						// With more knowledge this solution could be updated to be less CPU-hungry.
						snapOffset++;

					}
					else
					{
						break;
					}
				}

				if (lastComposedOffset > 0)
				{
					HookRegistry.Log(String.Format("DumpServer - Constructed {0} packets", packetsComposed));
					// Remove all bytes skipped by the offset acquired from composing packets. 
					// This is done by an inplace block copy.

					// lastComposedOffset <= snapLength <= MS.Length!
					int skippedBytes = lastComposedOffset;
					int remainingBytes = (int)(activeBuffer.Length - lastComposedOffset);

					if (remainingBytes > 0)
					{
						byte[] directBuffer = activeBuffer.GetBuffer();
						Buffer.BlockCopy(directBuffer, skippedBytes, directBuffer, 0, remainingBytes);
						activeBuffer.SetLength(activeBuffer.Length - snapOffset);
					}
					else
					{
						// Reset buffer
						activeBuffer.SetLength(0);
					}

					HookRegistry.Log(String.Format("DumpServer - ActiveBuffer size: {0}", activeBuffer.Length));
				}
			}
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

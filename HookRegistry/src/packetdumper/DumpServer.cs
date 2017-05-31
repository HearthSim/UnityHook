using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using HackstoneAnalyzer.PayloadFormat;

namespace Hooks.src.packetdumper
{
	class DumpServer
	{
		// Magic header for our handshake packet.
		// This value indicates that the stream contains packet data.
		private const long MAGIC_V = 0x2E485344554D502E;

		// Analyzers can connect to this port to receive a stream of packet data.
		private const int ANALYZER_LISTENER_PORT = 6666;

		// Timer to generate timespan between start and the moment a packet is sent to the analyzers.
		private Stopwatch _timeWatch;

		private TcpListener _connectionListener;
		private List<Socket> _connections;
		// Keeps track of how many bytes of the replay buffer were sent to the analyzers.
		private Map<Socket, int> _sentBytes;

		// First payload sent to the listening analyzers.
		private byte[] _handshakePayload;
		// Stream of all packets sent by this dumper.
		// This does NOT contain the handshake payload!
		private MemoryStream _replayBuffer;
		private object _bufferLock;

		protected DumpServer()
		{
			_timeWatch = new Stopwatch();
			_timeWatch.Start();

			_connectionListener = new TcpListener(IPAddress.Loopback, ANALYZER_LISTENER_PORT);
			_sentBytes = new Map<Socket, int>();

			_replayBuffer = new MemoryStream();
			_bufferLock = new object();
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

		// Constructs the handshake payload.
		// After the handshake payload has been set, analyzers will be accepted.
		public void InitialiseHandshake(int hsVersion)
		{
			if (_handshakePayload == null)
			{
				var handshake = new Handshake()
				{
					Magic = MAGIC_V,
					// HSVersion is unknown at this point.
					HsVersion = hsVersion
				};

				_handshakePayload = handshake.ToByteArray();

				InitialiseStream();
			}
		}

		// Start accepting analyzer connections.
		private void InitialiseStream()
		{
			_connectionListener.BeginAcceptSocket(AcceptAnalyzer, null);
		}

		private void AcceptAnalyzer(IAsyncResult result)
		{
			// Fetch socket to new client.
			// This blocks if no new analyzer has tried to connect.
			try
			{
				Socket client = _connectionListener.EndAcceptSocket(result);

				// Set state of socket as write_only.
				// This will trigger an error on the analyzers IF they try to send data.
				client.Shutdown(SocketShutdown.Receive);

				/*
				 * BeginSend will copy the provided data into the send buffer.
				 * After this copy, the callback will be invoked on a separate thread.
				 * The EndSend(..) method on the callback thread will block until the send operation 
				 * has been completed.
				 * 
				 * Sequential BeginSend operations without EndSend inbetween are allowed, but could hog up 
				 * resources really quickly!
				 */

				lock (_bufferLock)
				{
					// Send handshake payload.
					client.BeginSend(_handshakePayload, 0, _handshakePayload.Length, SocketFlags.None, FinishSocketSend, client);

					// Follow up with all buffered packets.
					byte[] packetBacklog = _replayBuffer.GetBuffer();
					client.BeginSend(packetBacklog, 0, packetBacklog.Length, SocketFlags.None, FinishSocketSend, client);

					// Store the client so it's possible to send new data after the backlog.
					_connections.Add(client);
				}
			}
			catch (Exception)
			{
				// Do nothing, failed to attach analyzer.
			}

			// Accept the next analyzer.
			_connectionListener.BeginAcceptSocket(AcceptAnalyzer, null);
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
			catch (Exception)
			{
				CleanSocket(client);
			}
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

		// Store packet to be sent to all attached analyzers.
		// Packets are only sent IF the InitialiseHandshake(..) has been called.
		// It's allowed to 'send' packets before the handshake is initialised.
		public void SendPacket(PacketType type, PacketDirection direction, int bodyTypeHash, ByteString data)
		{
			// Construct new payload to send.
			var packet = new CapturedPacket()
			{
				Type = type,
				Direction = direction,
				DataLength = data.Length,
				Data = data,
				BodyTypeHash = bodyTypeHash,
				PassedSeconds = Duration.FromTimeSpan(_timeWatch.Elapsed)
			};
			byte[] packetBytes = packet.ToByteArray();

			lock (_bufferLock)
			{
				// Append data to the replay buffer.
				_replayBuffer.Write(packetBytes, 0, packetBytes.Length);
			}

			// Trigger a send of new data on all analyzer connections.
			Socket[] connectionsCopy = _connections.ToArray();
			foreach (Socket connection in connectionsCopy)
			{
				connection.BeginSend(packetBytes, 0, packetBytes.Length, SocketFlags.None, FinishSocketSend, connection);
			}
		}
	}
}

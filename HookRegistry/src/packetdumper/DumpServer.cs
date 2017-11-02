using HackstoneAnalyzer.PayloadFormat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

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

		protected DumpServer()
		{
			_timeWatch = new Stopwatch();
			_timeWatch.Start();

			_connectionListener = null;
			_connections = new List<Socket>();

			_replayBuffer = new MemoryStream();
			_bufferLock = new object();
		}

		private void Setup()
		{
			var listeningPort = ANALYZER_LISTENER_PORT;

			for(int i = 0; i < 5; ++i)
			{
				try {
					_connectionListener = new TcpListener(IPAddress.Loopback, listeningPort+i);


				} catch(Exception) // TODO; Change to explicit exception
				{
					// Do nothing
				}
			}
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
		public void InitialiseHandshake(string hsVersion)
		{
			if (_handshakePayload == null)
			{
				HookRegistry.Get().Internal_Log("DumpServer - Initialising handshake");
				var handshake = new Handshake()
				{
					Magic = Util.MAGIC_V,
					// HSVersion is unknown at this point.
					HsVersion = hsVersion
				};

				// Write payload with prefixed length to the buffer.
				// Prefixing with length is important since the protobuf doesn't delimit itself!
				MemoryStream tempBuffer = new MemoryStream();
				handshake.WriteDelimitedTo(tempBuffer);
				_handshakePayload = tempBuffer.ToArray();

				InitialiseStream();
			}
		}

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
				string message = string.Format("Connecting analyzer failed for following reason: {0}", e.Message);
				HookRegistry.Get().Internal_Log(message);
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
					string message = string.Format("Sending BACKLOG to newly attached analyzer failed for following reason: {0}", e.Message);
					HookRegistry.Get().Internal_Log(message);
				}
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
			catch (Exception e)
			{
				string message = string.Format("Sending data to analyzer caused exception `{0}`, connection is closed!", e.Message);
				HookRegistry.Get().Internal_Log(message);
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
		public void SendPacket(PacketType type, PacketDirection direction, uint bodyTypeHash, byte[] data)
		{
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
					string message = string.Format("Sending data to analyzer caused exception `{0}`, connection is closed!", e.Message);
					HookRegistry.Get().Internal_Log(message);
					CleanSocket(connection);
				}
			}
		}
	}
}

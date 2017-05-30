using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Hooks.src.packetdumper
{
	class DumpServer
	{
		// Magic header for our handshake packet.
		// This value indicates that the stream contains packet data.
		private const long MAGIC_V = 0x2E485344554D502E;

		// Analyzers can connect to this port to receive a stream of packet data.
		private const int ANALYZER_LISTENER_PORT = 6666;

		private TcpListener _connectionListener;
		private List<Socket> _connections;

		// First payload sent to the listening analyzers.
		private Handshake _handshakePayload;
		// Stream of all packets sent by this dumper.
		// This does NOT contain the handshake payload!
		private MemoryStream _replayBuffer;

		protected DumpServer()
		{
			_connectionListener = new TcpListener(IPAddress.Loopback, ANALYZER_LISTENER_PORT);
			_replayBuffer = new MemoryStream();

			_handshakePayload = new Handshake()
			{
				Magic = MAGIC_V,
				// HSVersion is unknown at this point.
				HsVersion = 0
			};
		}

		private static DumpServer _thisObj;

		public static DumpServer Get()
		{
			if(_thisObj == null)
			{
				_thisObj = new DumpServer();
				_thisObj.Initialise();
			}

			return _thisObj;
		}

		private void Initialise()
		{
			_connectionListener.BeginAcceptSocket(AcceptAnalyzer, null);
		}

		private void AcceptAnalyzer(IAsyncResult state)
		{
			// Fetch socket to new client.
			Socket client = _connectionListener.EndAcceptSocket(state);
			_connections.Add(client);
			// TODO
		}
	}
}

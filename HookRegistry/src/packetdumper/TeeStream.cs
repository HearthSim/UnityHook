using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Timers;

namespace Hooks.PacketDumper
{
    class TeeStream
	{
		private static object[] EMPTY_ARGS = { };

		public const int BNET_RECV_PORT = 6666;
		public const int BNET_SEND_PORT = 6667;
		public const int PEGASUS_RECV_PORT = 6668;
		public const int PEGASUS_SEND_PORT = 6669;

		// Stream where all battle.net packets are being written to
		private TcpClient TcpBattleNetIn;
		private TcpClient TcpBattleNetOut;
		// Stream where all Pegasus packets are being written to
		private TcpClient TcpPegasusIn;
		private TcpClient TcpPegasusOut;
		// Timer used to check if the tcp connection is still alive
		private Timer PollingAnalyzer;

		private Stream BIN;
		private Stream BOUT;
		private Stream PIN;
		private Stream POUT;

		private TeeStream()
		{
			TcpBattleNetIn = new TcpClient(); // "localhost", 30123
			TcpBattleNetOut = new TcpClient();
			TcpPegasusIn = new TcpClient(); // "localhost", 30124
			TcpPegasusOut = new TcpClient();

			PollingAnalyzer = new Timer();

			BIN = null;
			BOUT = null;
			PIN = null;
			POUT = null;
		}

		private void Initialise()
		{
			// Set a timer to test every few seconds if the analyzer accepts incoming data

			// 20 seconds.
			PollingAnalyzer.Interval = 20 * 1000;
			// Reset timer after event.
			PollingAnalyzer.AutoReset = true;
			// Try to setup the streams on event.
			PollingAnalyzer.Elapsed += SetupStreams;
			// Start the timer
			PollingAnalyzer.Start();

			// Run the first setup manually
			SetupStreams(null, null);
		}

		private void SetupStreams(Object source, ElapsedEventArgs e)
		{
			// Bind to localhost so the packets use the loopback interface to reach the listener.
			// This prevents sending packets to other devices on the connected network.
			bool connected = false;

			HookRegistry.Get().Log("Checking for analyzer!");

			try
			{
				var one = TcpBattleNetOut.BeginConnect(IPAddress.Loopback, BNET_SEND_PORT, null, null);
				var two = TcpBattleNetIn.BeginConnect(IPAddress.Loopback, BNET_RECV_PORT, null, null);
				var three = TcpPegasusOut.BeginConnect(IPAddress.Loopback, PEGASUS_SEND_PORT, null, null);
				var four = TcpPegasusIn.BeginConnect(IPAddress.Loopback, PEGASUS_RECV_PORT, null, null);

				// Wait 2 seconds for connections.
				var waitHandle = one.AsyncWaitHandle;
				using (waitHandle)
				{
					waitHandle.WaitOne(TimeSpan.FromSeconds(2));
				}

				TcpBattleNetOut.EndConnect(one);
				TcpBattleNetIn.EndConnect(two);
				TcpPegasusOut.EndConnect(three);
				TcpPegasusIn.EndConnect(four);

				connected = true;
			}
			catch (SocketException)
			{
				// Pass
			}

			if (connected == true)
			{
				// Get stream objects of the TCP connections.
				BIN = TcpBattleNetIn.GetStream();
				BOUT = TcpBattleNetOut.GetStream();
				PIN = TcpPegasusIn.GetStream();
				POUT = TcpPegasusOut.GetStream();

				HookRegistry.Get().Log("Attached to analyzer!");

				// Disable timer that checks for the analyzer.
				PollingAnalyzer.Stop();
			}
			else
			{
				HookRegistry.Get().Log("Analyzer not found!");
			}
		}

		private void ClearStreams()
		{
			try
			{
				// close off streams
				TcpBattleNetIn.Close();
				TcpBattleNetOut.Close();
				TcpPegasusIn.Close();
				TcpPegasusOut.Close();
			}
			catch (Exception)
			{
				// Do nothing
			}
			finally
			{
				BIN = null;
				BOUT = null;
				PIN = null;
				POUT = null;

				HookRegistry.Get().Log("Detached from analyzer!");

				// Reset timer to poll analyzer!
				PollingAnalyzer.Start();
			}
		}

		private void FinishSend(IAsyncResult a)
		{
			try
			{
				((Stream)a.AsyncState).EndWrite(a);
			}
			catch (IOException)
			{
				ClearStreams();
			}
		}

		public void WriteBattlePacket(byte[] data, bool incoming)
		{
			//HookRegistry.Get().Log("Entry write battle packet");
			try
			{
				if (incoming == true)
				{
					BIN?.BeginWrite(data, 0, data.Length, FinishSend, BIN);

				}
				else
				{
					BOUT?.BeginWrite(data, 0, data.Length, FinishSend, BOUT);
				}
			}
			catch (Exception)
			{
				ClearStreams();
			}

			//HookRegistry.Get().Log("END write battle packet");
		}

		public void WritePegasusPacket(byte[] data, bool incoming)
		{
			//HookRegistry.Get().Log("Entry write pegasus packet");
			try
			{
				if (incoming == true)
				{
					PIN?.BeginWrite(data, 0, data.Length, FinishSend, PIN);
				}
				else
				{
					POUT?.BeginWrite(data, 0, data.Length, FinishSend, POUT);
				}
			}
			catch (Exception)
			{
				ClearStreams();
			}

			//HookRegistry.Get().Log("END write pegasus packet");
		}

		private static TeeStream _thisObj;
		public static TeeStream Get()
		{
			if (_thisObj == null)
			{
				_thisObj = new TeeStream();
				// Initialise our object
				_thisObj.Initialise();
			}

			return _thisObj;
		}
	}
}

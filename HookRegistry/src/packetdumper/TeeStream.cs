using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Timers;

namespace Hooks.PacketDumper
{
    class TeeStream
    {

        public const int BNET_SEND_PORT = 30123;
        public const int PEGASUS_SEND_PORT = 30124;

        // Stream where all battle.net packets are being written to
        private TcpClient StreamBattleNet;
        // Stream where all Pegasus packets are being written to
        private TcpClient StreamPegasus;
        // Timer used to check if the tcp connection is still alive
        private Timer StreamAliveChecker;

        private TeeStream()
        {
            StreamBattleNet = new TcpClient(); // "localhost", 30123
            StreamPegasus = new TcpClient(); // "localhost", 30124
            StreamAliveChecker = new Timer();
        }

        private void Initialise()
        {
            // Wait 3 seconds max before timing out the connection attempt
            var waitSeconds = 3;

            // Bind to localhost so the packets use the loopback interface to reach the listener.
            // This prevents sending packets to other devices on the connected network.
            // Start connection task
            var bConnectResult = StreamBattleNet.BeginConnect("localhost", BNET_SEND_PORT, null, null);
            var pConnectResult = StreamPegasus.BeginConnect("localhost", PEGASUS_SEND_PORT, null, null);
            var bWaitHandle = bConnectResult.AsyncWaitHandle;
            var pWaitHandle = pConnectResult.AsyncWaitHandle;
            try
            {
                // Block thread while trying to connect
                var bnetSucces = bConnectResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(waitSeconds), false);
                var pegSucces = pConnectResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(waitSeconds), false);

                // Forces success or socket exception 
                StreamBattleNet.EndConnect(bConnectResult);
                StreamPegasus.EndConnect(pConnectResult);

                HookRegistry.Get().Log("PacketDumper connections installed!");
                // Monitor the streams
                SetConnectionCheckTimer();
            }
            catch (SocketException e)
            {
                var msg = string.Format("Couldn't open a stream to dump the network packets. Message: `{0}`", e.Message);
                HookRegistry.Get().Log(msg);
            }
            finally
            {
                bWaitHandle.Close();
                pWaitHandle.Close();
            }
        }

        // Enable a monitor for our TCP streams
        private void SetConnectionCheckTimer()
        {
            // Tick every 2 seconds
            StreamAliveChecker.Interval = 2000;
            // Repeat
            StreamAliveChecker.AutoReset = true;
            // Run alive check
            StreamAliveChecker.Elapsed += TestConnectionActivity;
            // Enable timer
            StreamAliveChecker.Enabled = true;
        }

        // Test state of our streams, shut them down if something went wrong to prevent exceptions when writing
        private void TestConnectionActivity(Object source, ElapsedEventArgs e)
        {
            if (StreamBattleNet.GetState() != TcpState.Established || StreamPegasus.GetState() != TcpState.Established)
            {
                // Disable timer
                StreamAliveChecker.Enabled = false;
                try
                {
                    HookRegistry.Get().Log("Shutting down TeeStream connections because of invalid state!");
                    // close off streams
                    StreamBattleNet.Close();
                    StreamPegasus.Close();
                    StreamBattleNet = null;
                    StreamPegasus = null;
                }
                catch (Exception)
                {
                    // Do nothing
                }
            }
        }

        public void WriteBattlePacket(byte[] data)
        {
            StreamBattleNet?.GetStream().Write(data, 0, data.Length);
        }

        public void WritePegasusPacket(byte[] data)
        {
            StreamPegasus?.GetStream().Write(data, 0, data.Length);
        }

        private static TeeStream _thisObj;
        public static TeeStream Get()
        {
            if (_thisObj == null)
            {
                _thisObj = new TeeStream();
                // Initialise our object
                _thisObj.Initialise();
                // Immediately test for failures
                _thisObj.TestConnectionActivity(null, null);
            }

            return _thisObj;
        }
    }

    public static class TeeStreamHelper
    {
        // Returns the state of a given TcpClient object. This method can be used to 
        // determine the state of the tcp connection before writing.
        // TcpState.Established is the valid state where writing/reading is allowed.
        public static TcpState GetState(this TcpClient tcpClient)
        {
            var foo = IPGlobalProperties.GetIPGlobalProperties()
              .GetActiveTcpConnections()
              .SingleOrDefault(x => x.LocalEndPoint.Equals(tcpClient.Client.LocalEndPoint));
            return foo != null ? foo.State : TcpState.Unknown;
        }
    }
}

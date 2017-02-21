using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Timers;

namespace Hooks.PacketDumper
{
    class TeeStream
    {
        object[] EMPTY_ARGS = { };

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
        private Timer StreamAliveChecker;

        private Stream BIN;
        private Stream BOUT;
        private Stream PIN;
        private Stream POUT;

        // Represents bgs.BattleNetPacket
        Type TypeBattleNetPacket;
        // Represents bnet.protocol.Header -> the header of battle.net packet
        Type TypeBattleNetHeader;

        // Represents PegasusPacket
        Type TypePegasusPacket;

        private TeeStream()
        {
            TcpBattleNetIn = new TcpClient(); // "localhost", 30123
            TcpBattleNetOut = new TcpClient();
            TcpPegasusIn = new TcpClient(); // "localhost", 30124
            TcpPegasusOut = new TcpClient();

            StreamAliveChecker = new Timer();

            BIN = null;
            BOUT = null;
            PIN = null;
            POUT = null;
        }

        private void Initialise()
        {
            // Bind to localhost so the packets use the loopback interface to reach the listener.
            // This prevents sending packets to other devices on the connected network.
            try
            {
                TcpBattleNetOut.Connect(IPAddress.Loopback, BNET_SEND_PORT);
                TcpBattleNetIn.Connect(IPAddress.Loopback, BNET_RECV_PORT);

                TcpPegasusOut.Connect(IPAddress.Loopback, PEGASUS_SEND_PORT);
                TcpPegasusIn.Connect(IPAddress.Loopback, PEGASUS_RECV_PORT);

                // Get stream objects of the TCP connections.
                BIN = TcpBattleNetIn.GetStream();
                BOUT = TcpBattleNetOut.GetStream();
                PIN = TcpPegasusIn.GetStream();
                POUT = TcpPegasusOut.GetStream();


                HookRegistry.Get().Log("PacketDumper connections installed!");
                // Monitor the streams
                SetConnectionCheckTimer();
            }
            catch (SocketException e)
            {
                var msg = string.Format("Couldn't open a stream to dump the network packets. Message: `{0}`", e.ToString());
                HookRegistry.Get().Log(msg);
            }
            finally
            {

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
            if (TcpBattleNetIn.Connected != true || TcpPegasusIn.Connected != true ||
                TcpBattleNetOut.Connected != true || TcpPegasusOut.Connected != true)
            {
                // Disable timer
                StreamAliveChecker.Enabled = false;
                try
                {
                    HookRegistry.Get().Log("Shutting down TeeStream connections because of invalid state!");
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
                }
            }
        }

        public void WriteBattlePacket(byte[] data, bool incoming)
        {
            //HookRegistry.Get().Log("Entry write battle packet");

            try
            {
                if (incoming == true)
                {
                    if (BIN?.CanWrite == true)
                    {
                        BIN?.Write(data, 0, data.Length);
                    }
                }
                else
                {
                    if (BOUT?.CanWrite == true)
                    {
                        BOUT?.Write(data, 0, data.Length);
                    }
                }
            }
            catch (SocketException)
            {

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
                    if (PIN?.CanWrite == true)
                    {
                        PIN?.Write(data, 0, data.Length);
                    }
                }
                else
                {
                    if (POUT?.CanWrite == true)
                    {
                        POUT?.Write(data, 0, data.Length);
                    }
                }
            }
            catch (SocketException)
            {

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

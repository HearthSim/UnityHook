using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace Hooks.PacketDumper
{
    class TeeStream
    {

        public const int BNET_LISTEN_PORT = 30123;
        public const int PEGASUS_LISTEN_PORT = 30124;

        // Stream where all battle.net packets are being written to
        private TcpClient StreamBattleNet;
        // Stream where all Pegasus packets are being written to
        private TcpClient StreamPegasus;

        private TeeStream()
        { }

        private void Initialise()
        {
            try
            {
                // Bind to localhost so the packets use the loopback interface to reach the listener.
                // This prevents sending packets to other devices on the connected network.
                StreamBattleNet = new TcpClient(); // "localhost", 30123
                StreamPegasus = new TcpClient(); // "localhost", 30124
                // Start connection task
                var bnetTask = StreamBattleNet.BeginConnect("localhost", BNET_LISTEN_PORT, null, null);
                var pegTask = StreamPegasus.BeginConnect("localhost", PEGASUS_LISTEN_PORT, null, null);
                // Wait 3 seconds max before timing out the connection attempt
                var waitSeconds = 3;
                var bnetSucces = bnetTask.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(waitSeconds));
                var pegSucces = pegTask.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(waitSeconds));

                if (!bnetSucces || !pegSucces)
                {
                    HookRegistry.Get().Log("Creating connection to packetlistener failed!");
                    StreamBattleNet.Close();
                    StreamPegasus.Close();
                    StreamBattleNet = null;
                    StreamPegasus = null;
                }

            }
            catch (SocketException e)
            {
                var msg = string.Format("Couldn't open a stream to dump the network packets. Message: `{0}`", e.Message);
                HookRegistry.Get().Log(msg);
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
                _thisObj.Initialise();
            }

            return _thisObj;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LENet;
using LeaguePackets;
using Newtonsoft.Json;

namespace Engine
{
    public class LeagueDisconnectedEventArgs
    {
        public int ClientID { get; private set; }
        public string EventName => "disconnected";
        public LeagueDisconnectedEventArgs(int clientID)
        {
            ClientID = clientID;
        }
    }

    public class LeagueConnectedEventArgs
    {
        public int ClientID { get; private set; }
        public string EventName => "connected";
        public LeagueConnectedEventArgs(int clientID)
        {
            ClientID = clientID;
        }
    }

    public class LeaguePacketEventArgs : EventArgs
    {
        public string EventName => "packet";
        public int ClientID { get; set; }
        public ChannelID ChannelID { get; private set; }
        public BasePacket Packet { get; private set; }
        public LeaguePacketEventArgs(int clientID, ChannelID channel, BasePacket packet)
        {
            ClientID = clientID;
            ChannelID = channel;
            Packet = packet;
        }
    }

    public class LeagueBadPacketEventArgs : EventArgs
    {
        public string EventName => "badpacket";
        public int ClientID { get; set; }
        public ChannelID ChannelID { get; private set; }
        public byte[] RawData { get; private set; }
        public Exception Exception { get; private set; }
        public LeagueBadPacketEventArgs(int clientID, ChannelID channel, byte[] rawData, Exception exception)
        {
            ClientID = clientID;
            ChannelID = channel;
            RawData = rawData;
            Exception = exception;
        }
    }


    public static class ByteExtension 
    {
        public static void PrintHex(this byte[] data, int perline = 8)
        {
            for (int i = 0; i < data.Length; i += perline)
            {
                for (int c = i; c < (i + perline) && c < data.Length; c++)
                {
                    Console.Write("{0:X2} ", (uint)data[c]);
                }
                Console.Write("\r\n");
            }
        }
    }

    public static class PeerExtension
    {
        public static bool Send(this Peer peer, ChannelID channel, byte[] data,
                                bool reliable = true, bool unsequenced = false)
        {
            var flags = PacketFlags.NONE;
            if(reliable)
            {
                flags |= PacketFlags.RELIABLE;
            }
            if(unsequenced)
            {
                flags |= PacketFlags.UNSEQUENCED;
            }
            var packet = new Packet(data, flags);
            return peer.Send((byte)channel, packet) == 0;
        }
    }


    public class LeagueServer
    {
        private Host _host;
        private BlowFish _blowfish;
        private Dictionary<int, Peer?> _peers = new();
        public event EventHandler<LeagueDisconnectedEventArgs> OnDisconnected;
        public event EventHandler<LeagueConnectedEventArgs> OnConnected;
        public event EventHandler<LeaguePacketEventArgs> OnPacket;
        public event EventHandler<LeagueBadPacketEventArgs> OnBadPacket;

        public LeagueServer(Address address, byte[] key, int maxClientID)
        {
            _host = new Host(LENet.Version.Patch420, address, 32, 8, 0, 0);
            _blowfish = new BlowFish(key);
            for(int cid = 0; cid < maxClientID; cid++)
            {
                _peers[cid] = null;
            }
        }

        private bool SendEncrypted(Peer peer, ChannelID channel, BasePacket packet,
                                bool reliable = true, bool unsequenced = false)
        {
            var data = packet.GetBytes();
            data = _blowfish.Encrypt(data);
            return peer.Send(channel, data, reliable, unsequenced);
        }

        static JsonSerializerSettings jSettings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
        };
        public bool SendEncrypted(int client, ChannelID channel, BasePacket packet,
                                bool reliable = true, bool unsequenced = false)
        {
            if(_peers.TryGetValue(client, out var peer) && peer != null)
            {
                Console.WriteLine(JsonConvert.SerializeObject(packet, jSettings));
                return SendEncrypted(peer, channel, packet, reliable, unsequenced);
            }
            //TODO: throw here?
            return false;
        }

        public void RunOnce(uint timeout = 8)
        {
            Event eevent = new();
            while (_host.HostService(eevent, timeout) != 0)
            {
                switch (eevent.Type)
                {
                    case EventType.NONE:
                        break;
                    case EventType.CONNECT:
                        eevent.Peer.MTU = 996;
                        break;
                    case EventType.DISCONNECT:
                        if(eevent.Peer.UserData != null)
                        {
                            var cid = (int)eevent.Peer.UserData;
                            _peers[cid] = null;
                            OnDisconnected(this, new LeagueDisconnectedEventArgs(cid));
                        }
                        break;
                    case EventType.RECEIVE:
                        if(eevent.Peer.UserData == null)
                        {
                            if(eevent.ChannelID != (byte)ChannelID.Default)
                            {
                                eevent.Peer.Disconnect(0);
                            }
                            else
                            {
                                HandleAuth(eevent.Peer, eevent.Packet);
                            }
                        }
                        else
                        {
                            HandlePacketParse((ChannelID)eevent.ChannelID, eevent.Peer, eevent.Packet);
                        }
                        break;
                }
                eevent.Reset();
            }
        }

        private void HandlePacketParse(ChannelID channel, Peer peer, Packet rawPacket)
        {
            var cid = (int)peer.UserData;
            var rawData = rawPacket.Data;
            rawData = _blowfish.Decrypt(rawData);
            try
            {
                var packet = BasePacket.Create(rawData, channel);
                OnPacket(this, new LeaguePacketEventArgs(cid, channel, packet));
            }
            catch (NotImplementedException exception)
            {
                OnBadPacket(this, new LeagueBadPacketEventArgs(cid, channel, rawData, exception));
            }
            catch (IOException exception)
            {
                OnBadPacket(this, new LeagueBadPacketEventArgs(cid, channel, rawData, exception));
            }
        }


        private void HandleAuth(Peer peer, Packet rawPacket)
        {
            var rawData = rawPacket.Data;
            try
            {
                KeyCheckPacket clientAuthPacket = KeyCheckPacket.Create(rawData);
                if(_blowfish.Encrypt((ulong)clientAuthPacket.PlayerID) != clientAuthPacket.CheckSum)
                {
                    Console.WriteLine($"Got bad checksum({clientAuthPacket.CheckSum} for {clientAuthPacket.PlayerID})");
                    peer.Disconnect(0);
                    return;
                }
                //TODO: fix
                var cid = (int)clientAuthPacket.PlayerID - 1;
                if(!_peers.ContainsKey(cid))
                {
                    Console.WriteLine($"Client id: {cid} not in allowed cid list!");
                    peer.Disconnect(0);
                    return;
                }
                if(_peers[cid] != null)
                {
                    Console.WriteLine($"Client already connected!");
                    peer.Disconnect(0);
                    return;
                }
                peer.UserData = cid;
                _peers[cid] = peer;

                KeyCheckPacket serverAuthPacket = new KeyCheckPacket();;
                serverAuthPacket.ClientID = cid;
                serverAuthPacket.PlayerID = clientAuthPacket.PlayerID;
                serverAuthPacket.VersionNumber = clientAuthPacket.VersionNumber;
                serverAuthPacket.CheckSum = clientAuthPacket.CheckSum;
                SendEncrypted(peer, ChannelID.Default, serverAuthPacket);
                OnConnected(this, new LeagueConnectedEventArgs(cid));
            }
            catch(IOException)
            {
                Console.WriteLine("Failed to read/write KeyCheck packet!");
                rawData.PrintHex();
                peer.Disconnect(0);
                return;
            }
        }
    }
}

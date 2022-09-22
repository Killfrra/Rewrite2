using System;
using System.IO;
using System.Collections.Generic;
using LENet;
using LeaguePackets;
using LeaguePackets.Game;
using LeaguePackets.Game.Common;
using LeaguePackets.LoadScreen;
using Newtonsoft.Json;
using System.Numerics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;

namespace Engine
{
    public enum Team: uint {
        None,
        Blue = 100,
        Purple = 200,
        Neutral = 300,
    }
    public class Avatar {
        public ushort Level;
        public Team Team;
        public int Icon;
        public uint[] Runes = new uint[2];
        public string Name;
        public string Champion;
        public string Rank;
        public int Skin = 0;
        public bool IsBot = false;
        public byte Bitfield; // ?
    }
    public class Game
    {
        public static float Time;
        public static Avatar[] Avatars = new Avatar[]
        {
            new Avatar()
            {
                Level = 30,
                Team = Team.Blue,
                Rank = "BRONZE",
                Icon = 666,
                Bitfield = 108,
                Runes = new uint[2]
                {
                    105565333,
                    104222500
                },
                Skin = 0,
                Champion = "Nautilus",
                Name = "Test",
            }
        };
        static byte[] key = Convert.FromBase64String("17BLOhi6KZsTtldTsizvHg==");
        static Address address = new(Address.Any, 5119);
        static LeagueServer server = new(address, key, Avatars.Length);
        static int MapNum = 1;
        static string MapMode = "CLASSIC";
        static ulong GameFeatures = (1 << 0x1) | (1 << 0x4) | (1 << 0x6) | (1 << 0x7) | (1 << 0x8);
        
        static JsonSerializerSettings jSettings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
        };
        static List<KeyValuePair<Regex, MethodInfo>> commandsList = new();
        public static void Main(string[] args)
        {
            for(int cid = 0; cid < Avatars.Length; cid++)
            {
                var avatar = Avatars[cid];
                var champ = new Champion();
                champ.Add();
            }

            foreach(var method in typeof(Commands).GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                var regex = new Regex(@"\." + method.Name + @"((?:\s(?:.*)|))?", RegexOptions.IgnoreCase);
                commandsList.Add(new KeyValuePair<Regex, MethodInfo>(regex, method));
            }

            server.OnPacket += OnPacket;
            server.OnBadPacket += (s, e) =>
            {
                Console.WriteLine(JsonConvert.SerializeObject(e, jSettings));
            };
            server.OnConnected += (s, e) =>
            {
                Console.WriteLine(JsonConvert.SerializeObject(e, jSettings));
            };
            server.OnDisconnected += (s, e) =>
            {
                Console.WriteLine(JsonConvert.SerializeObject(e, jSettings));
            };

            while(true)
            {
                server.RunOnce();
            }
        }

        static void OnPacket(object? sender, LeaguePacketEventArgs e){
            var packet = e.Packet;
            var cid = e.ClientID;
            var pid = cid + 1;
            var channel = e.ChannelID;
            Console.WriteLine($"Recieving {packet.GetType().Name} on {channel.ToString()} from {(uint)cid}");
            if(packet is IUnusedPacket)
            {
                
            }
            else if (packet is C2S_QueryStatusReq statusReq)
            {
                var answer = new S2C_QueryStatusAns()
                {
                    Response = true
                };
                server.SendEncrypted(cid, ChannelID.Broadcast, answer);
            }
            else if(packet is RequestJoinTeam reqJoinTeam)
            {
                var answer1 = new TeamRosterUpdate()
                {
                    TeamSizeOrder = 1,
                    TeamSizeOrderCurrent = 1,
                };
                answer1.OrderMembers[0] = pid;

                server.SendEncrypted(cid, ChannelID.LoadingScreen, answer1);

                var avatar = Avatars[0];
                var answer2 = new RequestReskin()
                {
                    PlayerID = pid,
                    SkinID = avatar.Skin,
                    SkinName = avatar.Champion,
                };
                server.SendEncrypted(cid, ChannelID.LoadingScreen, answer2);

                var answer3 = new RequestRename()
                {
                    PlayerID = pid,
                    SkinID = 0,
                    PlayerName = avatar.Name,
                };
                server.SendEncrypted(cid, ChannelID.LoadingScreen, answer3);
            }
            else if(packet is C2S_Ping_Load_Info reqPingLoadInfo)
            {
                var answer = new S2C_Ping_Load_Info();
                answer.ConnectionInfo = reqPingLoadInfo.ConnectionInfo;
                answer.ConnectionInfo.ClientID = cid;
                answer.ConnectionInfo.PlayerID = pid;
                server.SendEncrypted(cid, ChannelID.BroadcastUnreliable, answer);
            }
            else if (packet is SynchVersionC2S syncReq)
            {
                var answer = new SynchVersionS2C()
                {
                    VersionMatches = true,
                    VersionString = syncReq.Version,

                    GameFeatures = GameFeatures,
                    MapToLoad = MapNum,
                    MapMode = MapMode,
                
                    PlatformID = "EUW",
                };

                var avatar = Avatars[0];
                answer.PlayerInfo[0] = new()
                {
                    PlayerID = pid,
                    SummonorLevel = avatar.Level,
                    TeamId = (uint)avatar.Team,
                    EloRanking = avatar.Rank,
                    ProfileIconId = avatar.Icon,
                    Bitfield = avatar.Bitfield,
                    SummonorSpell1 = avatar.Runes[0],
                    SummonorSpell2 = avatar.Runes[1],
                };
                
                server.SendEncrypted(cid, ChannelID.Broadcast, answer);
            }
            else if(packet is C2S_CharSelected reqSelected)
            {
                var startSpawn = new S2C_StartSpawn();
                server.SendEncrypted(cid, ChannelID.Broadcast, startSpawn);

                var avatar = Avatars[0];

                var spawnHero = new S2C_CreateHero();
                spawnHero.Name = avatar.Name;
                spawnHero.Skin = avatar.Champion;
                spawnHero.SkinID = avatar.Skin;
                spawnHero.NetNodeID = 0x40;
                spawnHero.NetID = 0x40000001;
                spawnHero.TeamIsOrder = avatar.Team == Team.Blue;
                spawnHero.CreateHeroDeath = CreateHeroDeath.Alive;
                spawnHero.ClientID = cid;
                spawnHero.SpawnPositionIndex = 2;
                server.SendEncrypted(cid, ChannelID.Broadcast, spawnHero);

                var avatarInfo = new AvatarInfo_Server();
                avatarInfo.SenderNetID = 0x40000001;
                avatarInfo.SummonerIDs[0] = avatarInfo.SummonerIDs2[0] = avatar.Runes[0];
                avatarInfo.SummonerIDs[1] = avatarInfo.SummonerIDs2[1] = avatar.Runes[0];
                server.SendEncrypted(cid, ChannelID.Broadcast, avatarInfo);

                var endSpawn = new S2C_EndSpawn();
                server.SendEncrypted(cid, ChannelID.Broadcast, endSpawn);
            }
            else if(packet is C2S_ClientReady reqReady)
            {
                var startGame = new S2C_StartGame();
                startGame.EnablePause = true;
                server.SendEncrypted(cid, ChannelID.Broadcast, startGame);

                var answer = new TeamRosterUpdate();
                answer.TeamSizeOrder = 1;
                answer.OrderMembers[0] = pid;
                answer.TeamSizeOrderCurrent = 1;
                server.SendEncrypted(cid, ChannelID.LoadingScreen, answer);
            }
            else if(packet is World_SendCamera_Server reqCamerPosition)
            {
                //Console.WriteLine($"{reqCamerPosition.CameraPosition} {reqCamerPosition.CameraDirection} {reqCamerPosition.SyncID}");
                var answer = new World_SendCamera_Server_Acknologment
                {
                    SyncID = reqCamerPosition.SyncID,
                };
                server.SendEncrypted(cid, ChannelID.ClientToServer, answer);
            }
            else if(packet is World_LockCamera_Server reqLockCameraServer)
            {
                
            }
            else if(packet is Chat reqChat)
            {
                foreach(var kvp in commandsList)
                {
                    var match = kvp.Key.Match(reqChat.Message);
                    if(match.Groups.Count == 2)
                    {
                        object value = match.Groups[1].Value;
                        object result = kvp.Value.Invoke(null, new object?[] {
                            server, e.ClientID, value 
                        });
                        if(result != null && result is string strResult)
                        {

                            var response = new Chat();
                            response.Localized = false;
                            response.Message = strResult;
                            response.ChatType = 1;
                            response.ClientID = e.ClientID;
                            //response.Params = "Command";
                            server.SendEncrypted(e.ClientID, ChannelID.Chat, response);
                        }
                        break;
                    }
                }
            }
            else if(packet is NPC_IssueOrderReq movReq && movReq.OrderType == 2)
            {
                var resWaypoints = new WaypointGroup();
                resWaypoints.SenderNetID = 0x40000001;
                resWaypoints.SyncID = (int)Environment.TickCount;
                resWaypoints.Movements.Add(movReq.MovementData);
                server.SendEncrypted(cid, ChannelID.Broadcast, resWaypoints);
            }
            else if(packet is C2S_Exit)
            {
                
            }
            else
            {
                Console.WriteLine(JsonConvert.SerializeObject(e, jSettings));   
            }
        }
    }
}

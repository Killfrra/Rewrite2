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
    public class Summoner {
        public ushort Level;
        public int Icon;
        public string Name;
        public Champion Champion;
        public string Rank;
        public bool IsBot = false;
        public byte Bitfield; // ?
        public bool Connected = false;
        public bool Ready = false;
    }
    public class Game
    {
        public static float Time;
        public static Summoner[] Summoners = new Summoner[]
        {
            new Summoner()
            {
                Name = "Test",
                Level = 30,
                Rank = "BRONZE",
                Icon = 666,
                Bitfield = 108,
                Champion = new Champion()
                {
                    Team = Team.Blue,
                    Name = "Heimerdinger",
                    Skin = 0,
                },
            }
        };
        static byte[] key = Convert.FromBase64String("17BLOhi6KZsTtldTsizvHg==");
        static Address address = new(Address.Any, 5119);
        public static LeagueServer Server = new(address, key, Summoners.Length);
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
            for(int cid = 0; cid < Summoners.Length; cid++)
            {
                var summoner = Summoners[cid];
                var champ = summoner.Champion;
                champ.Summoner = summoner;
                champ.Slots.SetSummonerSpell(0, new Flash());
                champ.Slots.SetSummonerSpell(1, new Heal());
                champ.Add();
            }

            foreach(var method in typeof(Commands).GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                var regex = new Regex(@"\." + method.Name + @"((?:\s(?:.*)|))?", RegexOptions.IgnoreCase);
                commandsList.Add(new KeyValuePair<Regex, MethodInfo>(regex, method));
            }

            Server.OnPacket += OnPacket;
            Server.OnBadPacket += (s, e) =>
            {
                Console.WriteLine(JsonConvert.SerializeObject(e, jSettings));
            };
            Server.OnConnected += (s, e) =>
            {
                Console.WriteLine(JsonConvert.SerializeObject(e, jSettings));
            };
            Server.OnDisconnected += (s, e) =>
            {
                Console.WriteLine(JsonConvert.SerializeObject(e, jSettings));
            };

            while(true)
            {
                uint d = 1000 / 60;
                //GameObject.Loop(d);
                Server.RunOnce(d);
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
                Server.SendEncrypted(cid, ChannelID.Broadcast, answer);
            }
            else if(packet is RequestJoinTeam reqJoinTeam)
            {
                var answer1 = new TeamRosterUpdate()
                {
                    TeamSizeOrder = 1,
                    TeamSizeOrderCurrent = 1,
                };
                answer1.OrderMembers[0] = pid;

                Server.SendEncrypted(cid, ChannelID.LoadingScreen, answer1);

                var summoner = Summoners[0];
                var answer2 = new RequestReskin()
                {
                    PlayerID = pid,
                    SkinID = summoner.Champion.Skin,
                    SkinName = summoner.Champion.Name,
                };
                Server.SendEncrypted(cid, ChannelID.LoadingScreen, answer2);

                var answer3 = new RequestRename()
                {
                    PlayerID = pid,
                    SkinID = 0,
                    PlayerName = summoner.Name,
                };
                Server.SendEncrypted(cid, ChannelID.LoadingScreen, answer3);
            }
            else if(packet is C2S_Ping_Load_Info reqPingLoadInfo)
            {
                var answer = new S2C_Ping_Load_Info();
                answer.ConnectionInfo = reqPingLoadInfo.ConnectionInfo;
                answer.ConnectionInfo.ClientID = cid;
                answer.ConnectionInfo.PlayerID = pid;
                Server.SendEncrypted(cid, ChannelID.BroadcastUnreliable, answer);
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

                var summoner = Summoners[0];
                answer.PlayerInfo[0] = new()
                {
                    PlayerID = pid,
                    SummonorLevel = summoner.Level,
                    TeamId = (uint)summoner.Champion.Team,
                    EloRanking = summoner.Rank,
                    ProfileIconId = summoner.Icon,
                    Bitfield = summoner.Bitfield,
                    SummonorSpell1 = summoner.Champion.Slots.GetSummonerSpell(0)?.GetHash() ?? 0,
                    SummonorSpell2 = summoner.Champion.Slots.GetSummonerSpell(1)?.GetHash() ?? 0,
                };
                
                Server.SendEncrypted(cid, ChannelID.Broadcast, answer);
            }
            else if(packet is C2S_CharSelected reqSelected)
            {
                var startSpawn = new S2C_StartSpawn();
                Server.SendEncrypted(cid, ChannelID.Broadcast, startSpawn);

                GameObject.ReSync(cid);

                var endSpawn = new S2C_EndSpawn();
                Server.SendEncrypted(cid, ChannelID.Broadcast, endSpawn);
            }
            else if(packet is C2S_ClientReady reqReady)
            {
                var startGame = new S2C_StartGame();
                startGame.EnablePause = true;
                Server.SendEncrypted(cid, ChannelID.Broadcast, startGame);

                /*
                var answer = new TeamRosterUpdate();
                answer.TeamSizeOrder = 1;
                answer.OrderMembers[0] = pid;
                answer.TeamSizeOrderCurrent = 1;
                Server.SendEncrypted(cid, ChannelID.LoadingScreen, answer);
                */

                var lockRequest = new S2C_LockCamera()
                {
                    Lock = true
                };
                Server.SendEncrypted(cid, ChannelID.Broadcast, lockRequest);
            }
            else if(packet is World_SendCamera_Server reqCamerPosition)
            {
                //Console.WriteLine($"{reqCamerPosition.CameraPosition} {reqCamerPosition.CameraDirection} {reqCamerPosition.SyncID}");
                var answer = new World_SendCamera_Server_Acknologment
                {
                    SyncID = reqCamerPosition.SyncID,
                };
                Server.SendEncrypted(cid, ChannelID.ClientToServer, answer);
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
                            Server, cid, value 
                        });
                        if(result != null && result is string strResult)
                        {

                            var response = new Chat();
                            response.Localized = false;
                            response.Message = strResult;
                            response.ChatType = 1;
                            response.ClientID = cid;
                            //response.Params = "Command";
                            Server.SendEncrypted(cid, ChannelID.Chat, response);
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
                Server.SendEncrypted(cid, ChannelID.Broadcast, resWaypoints);
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

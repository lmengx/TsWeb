using TShockAPI;
using Terraria;
using Terraria.ID;
using Rests;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace TShockData
{
    public static class BossProgress
    {
        private static readonly Dictionary<int, string> BossNames = new Dictionary<int, string>
        {
            { NPCID.KingSlime, "史莱姆王" },
            { NPCID.EyeofCthulhu, "克苏鲁之眼" },
            { NPCID.EaterofWorldsHead, "世界吞噬者" },
            { NPCID.BrainofCthulhu, "克苏鲁之脑" },
            { NPCID.QueenBee, "蜂后" },
            { NPCID.Deerclops, "巨鹿" },
            { NPCID.SkeletronHead, "骷髅王" },
            { NPCID.WallofFlesh, "血肉墙" },
            { NPCID.QueenSlimeBoss, "史莱姆皇后" },
            { NPCID.TheDestroyer, "毁灭者" },
            { NPCID.SkeletronPrime, "机械骷髅王" },
            { NPCID.Retinazer, "双子魔眼" },
            { NPCID.Plantera, "世纪之花" },
            { NPCID.Golem, "石巨人" },
            { NPCID.DukeFishron, "猪龙鱼公爵" },
            { NPCID.HallowBoss, "光之女皇" },
            { NPCID.CultistBoss, "拜月教教徒" },
            { NPCID.MoonLordCore, "月亮领主" }
        };

        private static readonly Dictionary<int, string> EventNames = new Dictionary<int, string>
        {
            { 0, "哥布林入侵" },
            { 2, "海盗入侵" },
            { 4, "日食" },
            { 3, "火星人入侵" },
            { 8, "冰雪女王" },
            { 9, "南瓜王" }
        };

        public static void GetBossInfo(CommandArgs args)
        {
            var player = args.Player;
            var message = "[i:3868] Boss击杀进度: ";
            int killedCount = 0;
            int totalCount = BossNames.Count;
            int lineCount = 0;

            foreach (var boss in BossNames)
            {
                int killCount = GetKillCount(boss.Key);
                bool isKilled = killCount > 0;

                if (isKilled)
                {
                    killedCount++;
                    message += $"[c/00ff00:✓{boss.Value}] ";
                }
                else
                {
                    message += $"[c/ff0000:✗{boss.Value}] ";
                }

                lineCount++;
                if (lineCount % 5 == 0 && lineCount < totalCount)
                {
                    message += "\n";
                }
            }

            message += $"\n[c/00a8ff:Boss总进度: {killedCount}/{totalCount} ({(killedCount * 100 / totalCount)}%)]";

            message += "\n[i:3458] 事件进度: ";
            int eventCount = 0;
            int totalEventCount = EventNames.Count;
            lineCount = 0;

            foreach (var evt in EventNames)
            {
                bool isCompleted = IsEventCompleted(evt.Key);

                if (isCompleted)
                {
                    eventCount++;
                    message += $"[c/00ff00:✓{evt.Value}] ";
                }
                else
                {
                    message += $"[c/ff0000:✗{evt.Value}] ";
                }

                lineCount++;
                if (lineCount % 5 == 0 && lineCount < totalEventCount)
                {
                    message += "\n";
                }
            }

            message += $"\n[c/00a8ff:事件总进度: {eventCount}/{totalEventCount} ({(eventCount * 100 / totalEventCount)}%)]";
            player.SendMessage(message, Microsoft.Xna.Framework.Color.White);
        }

        public static object GetBossInfoJson(RestRequestArgs args)
        {
            var bossList = new List<object>();
            int killedCount = 0;
            int totalBossCount = BossNames.Count;

            foreach (var boss in BossNames)
            {
                int killCount = GetKillCount(boss.Key);
                bool isKilled = killCount > 0;
                
                if (isKilled)
                {
                    killedCount++;
                }

                bossList.Add(new
                {
                    Name = boss.Value,
                    NPCID = boss.Key,
                    KillCount = killCount,
                    IsKilled = isKilled
                });
            }

            var eventList = new List<object>();
            int completedEventCount = 0;
            int totalEventCount = EventNames.Count;

            foreach (var evt in EventNames)
            {
                bool isCompleted = IsEventCompleted(evt.Key);
                
                if (isCompleted)
                {
                    completedEventCount++;
                }

                eventList.Add(new
                {
                    Name = evt.Value,
                    EventID = evt.Key,
                    IsCompleted = isCompleted
                });
            }

            return new
            {
                TotalBossCount = totalBossCount,
                KilledCount = killedCount,
                BossProgressPercent = (int)(killedCount * 100.0 / totalBossCount),
                Bosses = bossList,
                TotalEventCount = totalEventCount,
                CompletedEventCount = completedEventCount,
                EventProgressPercent = (int)(completedEventCount * 100.0 / totalEventCount),
                Events = eventList
            };
        }

        public static bool GetWorldStatus(string name)
        {
            if (name == "始终生效")
                return true;

            foreach (var boss in BossNames)
            {
                if (boss.Value == name)
                {
                    return GetKillCount(boss.Key) == 0;
                }
            }

            foreach (var evt in EventNames)
            {
                if (evt.Value == name)
                {
                    return !IsEventCompleted(evt.Key);
                }
            }

            return false;
        }

        internal static int GetKillCount(int npcNetId)
        {
            try
            {
                return Main.BestiaryTracker.Kills.GetKillCount(ContentSamples.NpcBestiaryCreditIdsByNpcNetIds[npcNetId]);
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsEventCompleted(int eventId)
        {
            try
            {
                return eventId switch
                {
                    0 => NPC.downedGoblins,
                    2 => NPC.downedPirates,
                    3 => NPC.downedMartians,
                    4 => Main.BestiaryTracker.Kills.GetKillCount(ContentSamples.NpcBestiaryCreditIdsByNpcNetIds[NPCID.Mothron]) > 0,
                    8 => NPC.downedChristmasIceQueen,
                    9 => NPC.downedHalloweenKing,
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }
    }
}
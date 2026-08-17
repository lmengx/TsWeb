using TShockAPI;
using Terraria;
using Terraria.ID;
using Rests;
using Newtonsoft.Json;
using System;
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

        /// <summary>Boss 进度顺序表（显式数组保证确定性，Dictionary 枚举序不可依赖）</summary>
        private static readonly int[] BossOrder =
        {
            NPCID.KingSlime,
            NPCID.EyeofCthulhu,
            NPCID.EaterofWorldsHead,
            NPCID.BrainofCthulhu,
            NPCID.QueenBee,
            NPCID.Deerclops,
            NPCID.SkeletronHead,
            NPCID.WallofFlesh,
            NPCID.QueenSlimeBoss,
            NPCID.TheDestroyer,
            NPCID.SkeletronPrime,
            NPCID.Retinazer,
            NPCID.Plantera,
            NPCID.Golem,
            NPCID.DukeFishron,
            NPCID.HallowBoss,
            NPCID.CultistBoss,
            NPCID.MoonLordCore
        };

        /// <summary>
        /// Boss → 召唤物物品 ID（状态面板 [i:id] 图标提示用）。
        /// 15 个为真实召唤物（已实证）；3 个无召唤物物品的 boss 用最接近的代表物：
        ///   骷髅王=服装商巫毒娃娃(1307，老人/服装商关联)、世纪之花=世纪之花幼苗(4806)、拜月教教徒=拜月教邪教徒圣物(4937)
        /// </summary>
        private static readonly Dictionary<int, int> BossSpawnItemIds = new Dictionary<int, int>
        {
            { NPCID.KingSlime, 560 },
            { NPCID.EyeofCthulhu, 43 },
            { NPCID.EaterofWorldsHead, 70 },
            { NPCID.BrainofCthulhu, 1331 },
            { NPCID.QueenBee, 1133 },
            { NPCID.Deerclops, 5120 },
            { NPCID.SkeletronHead, 1307 },
            { NPCID.WallofFlesh, 267 },
            { NPCID.QueenSlimeBoss, 4988 },
            { NPCID.TheDestroyer, 556 },
            { NPCID.SkeletronPrime, 557 },
            { NPCID.Retinazer, 544 },
            { NPCID.Plantera, 4806 },
            { NPCID.Golem, 1293 },
            { NPCID.DukeFishron, 2673 },
            { NPCID.HallowBoss, 4961 },
            { NPCID.CultistBoss, 4937 },
            { NPCID.MoonLordCore, 3601 }
        };

        /// <summary>
        /// Boss NPCID → downed 击败标记读取（世界进度页悬浮窗修改用，字段键与 /data/worldmodify 一致）。
        /// 血肉墙对应 hardMode（困难模式开关）。
        /// </summary>
        private static readonly Dictionary<int, Func<bool>> BossDownedGetters = new Dictionary<int, Func<bool>>
        {
            { NPCID.KingSlime, () => NPC.downedSlimeKing },
            { NPCID.EyeofCthulhu, () => NPC.downedBoss1 },
            { NPCID.EaterofWorldsHead, () => NPC.downedBoss2 },
            { NPCID.BrainofCthulhu, () => NPC.downedBoss2 },
            { NPCID.QueenBee, () => NPC.downedQueenBee },
            { NPCID.Deerclops, () => NPC.downedDeerclops },
            { NPCID.SkeletronHead, () => NPC.downedBoss3 },
            { NPCID.WallofFlesh, () => Main.hardMode },
            { NPCID.QueenSlimeBoss, () => NPC.downedQueenSlime },
            { NPCID.TheDestroyer, () => NPC.downedMechBoss1 },
            { NPCID.SkeletronPrime, () => NPC.downedMechBoss3 },
            { NPCID.Retinazer, () => NPC.downedMechBoss2 },
            { NPCID.Plantera, () => NPC.downedPlantBoss },
            { NPCID.Golem, () => NPC.downedGolemBoss },
            { NPCID.DukeFishron, () => NPC.downedFishron },
            { NPCID.HallowBoss, () => NPC.downedEmpressOfLight },
            { NPCID.CultistBoss, () => NPC.downedAncientCultist },
            { NPCID.MoonLordCore, () => NPC.downedMoonlord }
        };

        /// <summary>
        /// 事件 EventID → downed 击败标记读取。日食(4)无独立 downed 字段，以蛾怪击杀记录判定（只读，不可修改）。
        /// </summary>
        private static readonly Dictionary<int, Func<bool>> EventDownedGetters = new Dictionary<int, Func<bool>>
        {
            { 0, () => NPC.downedGoblins },
            { 2, () => NPC.downedPirates },
            { 3, () => NPC.downedMartians },
            { 4, () => IsEventCompleted(4) },
            { 8, () => NPC.downedChristmasIceQueen },
            { 9, () => NPC.downedHalloweenKing }
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
            int downedCount = 0;
            int totalBossCount = BossNames.Count;

            foreach (var boss in BossNames)
            {
                int killCount = GetKillCount(boss.Key);
                bool isKilled = killCount > 0;
                bool downed = BossDownedGetters.TryGetValue(boss.Key, out var dg) && dg();
                
                if (isKilled)
                {
                    killedCount++;
                }
                if (downed)
                {
                    downedCount++;
                }

                bossList.Add(new
                {
                    Name = boss.Value,
                    NPCID = boss.Key,
                    KillCount = killCount,
                    IsKilled = isKilled,
                    Downed = downed
                });
            }

            var eventList = new List<object>();
            int completedEventCount = 0;
            int downedEventCount = 0;
            int totalEventCount = EventNames.Count;

            foreach (var evt in EventNames)
            {
                bool isCompleted = IsEventCompleted(evt.Key);
                bool downed = EventDownedGetters.TryGetValue(evt.Key, out var eg) && eg();
                
                if (isCompleted)
                {
                    completedEventCount++;
                }
                if (downed)
                {
                    downedEventCount++;
                }

                eventList.Add(new
                {
                    Name = evt.Value,
                    EventID = evt.Key,
                    IsCompleted = isCompleted,
                    Downed = downed
                });
            }

            return new
            {
                TotalBossCount = totalBossCount,
                KilledCount = killedCount,
                DownedBossCount = downedCount,
                BossProgressPercent = (int)(killedCount * 100.0 / totalBossCount),
                DownedBossPercent = totalBossCount == 0 ? 0 : (int)(downedCount * 100.0 / totalBossCount),
                Bosses = bossList,
                TotalEventCount = totalEventCount,
                CompletedEventCount = completedEventCount,
                DownedEventCount = downedEventCount,
                EventProgressPercent = (int)(completedEventCount * 100.0 / totalEventCount),
                DownedEventPercent = totalEventCount == 0 ? 0 : (int)(downedEventCount * 100.0 / totalEventCount),
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

        /// <summary>按进度顺序返回第一个未击杀的 Boss NPCID；全部击杀返回 -1</summary>
        internal static int GetCurrentProgressBossNpcId()
        {
            foreach (var npcId in BossOrder)
            {
                if (GetKillCount(npcId) == 0)
                    return npcId;
            }
            return -1;
        }

        /// <summary>当前开荒进度 Boss 名（第一个未击杀）；全部击杀返回「全部完成」</summary>
        internal static string GetCurrentProgressBossName()
        {
            var npcId = GetCurrentProgressBossNpcId();
            return npcId < 0 ? "全部完成" : BossNames[npcId];
        }

        /// <summary>当前开荒进度 Boss 的召唤物物品 ID；无召唤物用代表物；全部击杀返回 0</summary>
        internal static int GetCurrentProgressBossSpawnItemId()
        {
            var npcId = GetCurrentProgressBossNpcId();
            if (npcId < 0)
                return 0;
            return BossSpawnItemIds.TryGetValue(npcId, out var itemId) ? itemId : 0;
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
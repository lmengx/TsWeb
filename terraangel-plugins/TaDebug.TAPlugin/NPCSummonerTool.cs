using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using TerraAngel.Input;
using TerraAngel.Tools;
using TerraAngel.Utility;

namespace TaDebug.TAPlugin;

/// <summary>
/// NPC Summoner — 生物选择器召唤面板（ImGui）。
/// 扩展 TerraAngel 的 NPCBrowserTool：它只能召 Boss（SpawnBossUseLicenseStartEvent 只吃 MPAllowedEnemies），
/// 本工具支持召唤任意生物：
///   - 单机 (netMode==0)：直接 NPC.NewNPC
///   - 多人 (netMode==1)：发 FishOutNPC(130) 包 → 服务端 case 130 NPC.NewNPC(任意坐标, 任意type)，无白名单/范围检查，TShock 不拦截
/// </summary>
public class NPCSummonerTool : Tool
{
    public override string Name => "NPC Summoner";
    public override ToolTabs Tab => ToolTabs.NewTab;

    private string _search = "";
    private int _categoryIndex = 0;
    private int _count = 1;
    private bool _spawnAtMouse = false;

    private static readonly string[] CategoryNames = { "全部", "Boss/事件", "城镇NPC", "宠物/坐骑", "小动物", "其他" };

    public override void DrawUI(ImGuiIOPtr io)
    {
        ImGui.TextUnformatted("搜索:");
        ImGui.SameLine();
        ImGui.InputText("##npcsummon_search", ref _search, 64);

        ImGui.Combo("分类", ref _categoryIndex, CategoryNames, CategoryNames.Length);
        ImGui.InputInt("数量", ref _count);
        if (_count < 1) _count = 1;
        ImGui.Checkbox("在鼠标位置召唤", ref _spawnAtMouse);

        ImGui.TextUnformatted("点击列表项即召唤（多人走 FishOutNPC 包）");

        if (ImGui.BeginChild("##npcsummon_list"))
        {
            for (int i = NPCID.NegativeIDCount + 1; i < NPCID.Count; i++)
            {
                if (i == NPCID.None)
                    continue;

                if (!MatchSearch(i) || !MatchCategory(i))
                    continue;

                if (ImGui.Selectable($"{Lang.GetNPCName(i).Value} ({i})"))
                {
                    SpawnNPC(i, _count, _spawnAtMouse);
                }
            }
            ImGui.EndChild();
        }
    }

    private bool MatchSearch(int npcId)
    {
        if (string.IsNullOrEmpty(_search))
            return true;
        string s = _search.ToLowerInvariant();
        return Lang.GetNPCName(npcId).Value.ToLowerInvariant().Contains(s) ||
               npcId.ToString().StartsWith(s);
    }

    private bool MatchCategory(int npcId)
    {
        switch (_categoryIndex)
        {
            case 0: return true;
            case 1: return NPCID.Sets.MPAllowedEnemies[npcId];
            case 2: return NPCID.Sets.TownNPCBestiaryPriority.Contains(npcId) || NPCID.Sets.IsTownSlime[npcId];
            case 3: return NPCID.Sets.ProjectileNPC[npcId];
            case 4: return NPCID.Sets.TownCritter[npcId];
            case 5:
                return !NPCID.Sets.MPAllowedEnemies[npcId] &&
                       !NPCID.Sets.TownNPCBestiaryPriority.Contains(npcId) && !NPCID.Sets.IsTownSlime[npcId] &&
                       !NPCID.Sets.ProjectileNPC[npcId] && !NPCID.Sets.TownCritter[npcId];
            default: return true;
        }
    }

    /// <summary>召唤 NPC 到服务器（或单机本地）。</summary>
    public static void SpawnNPC(int npcType, int count, bool atMouse)
    {
        Vector2 basePos = atMouse
            ? Util.ScreenToWorldWorld(InputSystem.MousePosition)
            : Main.LocalPlayer.Center;

        // 多人模式不支持负 NPC type（变体），服务端 SetDefaults(负值) 行为不可控
        if (Main.netMode != 0 && npcType < 0)
            return;

        if (Main.netMode == 0)
        {
            for (int k = 0; k < count; k++)
            {
                var pos = basePos + new Vector2(Main.rand.Next(-48, 49), Main.rand.Next(-24, 25));
                NPC.NewNPC(new EntitySource_SpawnNPC(), (int)pos.X, (int)pos.Y, npcType);
            }
            return;
        }

        // 多人：FishOutNPC(130) —— 服务端 case 130：NPC.NewNPC(EntitySource_FishedOut, tileX*16, tileY*16, npcType)
        // 无 MPAllowedEnemies 白名单、无范围检查；TShock HandleFishOutNPC 仅事件无校验 → 放行
        for (int k = 0; k < count; k++)
        {
            var pos = basePos + new Vector2(Main.rand.Next(-48, 49), Main.rand.Next(-24, 25));
            int tileX = Math.Max(0, (int)(pos.X / 16f));
            int tileY = Math.Max(0, (int)(pos.Y / 16f));
            PacketBuilder.FastSendPacket(MessageID.FishOutNPC, b =>
            {
                b.Write((ushort)tileX);
                b.Write((ushort)tileY);
                b.Write((short)npcType);
            });
        }
    }
}

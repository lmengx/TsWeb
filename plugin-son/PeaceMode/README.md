# PeaceMode 和平模式

- 作者: lmx12330
- 说明: 开启后禁止世界中全部 NPC 与事件生成

## 功能

| 类别 | 覆盖范围 |
|------|---------|
| NPC 生成 | 全部敌对 NPC（含 BOSS、事件怪）生成即拦截；变形同样拦截。城镇 NPC（商人/护士等友方）默认保留，可配置一并禁止 |
| 事件 | 入侵、血月、日食、南瓜月、霜月、史莱姆雨、旧日军团、沙尘暴 —— 每秒强制清除，防止再次触发 |
| 天气 | 下雨（可配置） |
| 世界 | 陨石坠落（可配置） |

## 指令

| 语法 | 权限 | 说明 |
|------|------|------|
| `/peace` | peacemode.admin | 查看当前状态与用法 |
| `/peace on` | peacemode.admin | 开启和平模式（立即清除场上敌对 NPC + 当前事件） |
| `/peace off` | peacemode.admin | 关闭和平模式 |
| `/peace status` | peacemode.admin | 查看当前状态与配置 |
| `/peace reload` | peacemode.admin | 重新加载配置文件 |

别名: `/和平`

## 配置

配置文件位置: `tshock/PeaceMode.json`

```json5
{
  "Enabled": false,                  // 插件加载时是否自动开启和平模式
  "ClearExistingNpcsOnEnable": true, // 开启时是否立即清空场上所有敌对 NPC
  "IncludeTownNpcs": false,          // 是否连城镇 NPC 一起禁止生成（默认保留城镇 NPC）
  "BanEvents": true,                 // 是否禁止全部事件（入侵/血月/日食/南瓜月/霜月/史莱姆雨/旧日军团/沙尘暴）
  "BanRain": true,                   // 是否禁止下雨
  "BanMeteor": true                  // 是否禁止陨石坠落
}
```

## 实现说明

- NPC 拦截使用 `ServerApi.Hooks.NpcSpawn` / `NpcTransform`，对所有 NPC 生成来源（自然刷怪、玩家召唤、事件怪）统一生效；
- 事件与天气状态在 `GameUpdate` 中每秒轮询一次强制清除，并广播 `WorldInfo` 同步客户端，防止事件再次触发；
- 开启瞬间可选清空场上已有敌对 NPC；关闭后世界恢复正常。

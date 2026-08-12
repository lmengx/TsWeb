using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria.GameContent.NetModules;
using Terraria.Localization;
using Terraria.Net;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace TShockData
{
    /// <summary>
    /// 跨服聊天 + 本地聊天取消名字转义（MonoMod 实现）：
    ///
    /// 1) 本地聊天「取消对玩家名字的转义」（MonoMod RuntimeDetour）：
    ///    默认头顶模式（EnableChatAboveHeads=true）下，TShock 以 authorId=玩家 广播聊天
    ///    （NetTextModule.SerializeServerMessage(text, color, who)）。客户端 DisplayMessage
    ///    对 author<255 的消息执行 NameTagHandler.GenerateTag(名字) → 名字里的 [i:xxx]
    ///    被转义成字面文本（显示为 <名字[i:2711]>）。
    ///    本模块 detour SerializeServerMessage：author<255（真实玩家消息）时改写为
    ///    ChatFormat 完整文本 + author=255 → 客户端不再转义名字，[i:xxx] 由渲染层
    ///    ParseMessage 解析为物品图标。TShock 正常广播流程（日志/控制台/发送者回显/
    ///    未登录可见）全部保留。
    ///
    /// 2) 跨服聊天（每服配置，SSE 握手由后端下发）：
    ///    - crossChat：本服是否加入跨服聊天（发 + 收）
    ///    - crossChatPrefix：已渲染前缀文本（可含 [c/HEX:...] 转义），如 "[c/#4DABF7:主服]"
    ///    - crossChatColor：跨服消息最外层颜色（默认白 #FFFFFF）
    ///    本地聊天经 SSE 事件 "cross-chat" 上报 → 后端转发给其他启用服 →
    ///    目标服 POST /tsweb/crosschat（HMAC 验签）→ 广播显示。玩家名字不转义，物品标签生效。
    /// </summary>
    public static class CrossChat
    {
        // ════════════════════════════════════════════
        //  配置（SSE 握手时由后端下发）
        // ════════════════════════════════════════════
        private static bool _enabled;                 // 本服是否加入跨服聊天（发 + 收）
        private static string _prefix = "";           // 已渲染前缀文本（可含 [c/HEX:...]）
        private static string _serverId = "";         // 本服 serverId
        private static string _serverName = "";       // 本服服务器名
        private static byte _colorR = 255, _colorG = 255, _colorB = 255; // crossChatColor（默认白）

        private static bool _initialized;

        // 消息长度上限（与跨服转发校验一致）
        private const int MaxChatLength = 300;

        // [c/#RRGGBB:...] → [c/RRGGBB:...]：客户端 ColorTagHandler 用 NumberStyles.AllowHexSpecifier
        // 解析（不接受 # 前缀），配置里手写带 # 的颜色码运行时统一清洗
        private static readonly Regex ColorHashTagRegex = new(@"\[c/#([0-9a-fA-F]{3,8})", RegexOptions.Compiled);

        // ════════════════════════════════════════════
        //  MonoMod detour：本地聊天取消名字转义
        // ════════════════════════════════════════════
        private static Hook? _hook;

        /// <summary>NetTextModule.SerializeServerMessage(NetworkText, Color, byte) 原始委托（public static，无 this）</summary>
        private delegate NetPacket OrigSerializeServerMessage(NetworkText text, Color color, byte authorId);

        // ════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;

            // 1) MonoMod detour：改写聊天发送的 authorId（取消名字转义）
            try
            {
                var method = typeof(NetTextModule).GetMethod("SerializeServerMessage",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(NetworkText), typeof(Color), typeof(byte) }, null);
                if (method == null)
                {
                    TShock.Log.ConsoleError("[CrossChat] 未找到 NetTextModule.SerializeServerMessage，本地聊天取消转义未启用");
                }
                else
                {
                    _hook = new Hook(method, OnSerializeServerMessage);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[CrossChat] detour 注册失败: {ex.Message}");
            }

            // 2) 玩家聊天事件：跨服上报（不 Handled，TShock 照常广播，文本由 detour 改写）
            PlayerHooks.PlayerChat += OnPlayerChat;

            TShock.Log.ConsoleInfo($"[CrossChat] 跨服聊天已初始化 (enabled={_enabled})");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            PlayerHooks.PlayerChat -= OnPlayerChat;
            try { _hook?.Dispose(); } catch { }
            _hook = null;
        }

        /// <summary>SSE 握手时由后端下发配置（WebRestServer.HandleSseAsync 调用）</summary>
        public static void SetConfig(bool enabled, string prefix, string colorHex, string serverId, string serverName)
        {
            _enabled = enabled;
            _prefix = SanitizePrefix(prefix ?? "");
            _serverId = serverId ?? "";
            _serverName = serverName ?? "";
            ParseColor(colorHex, out _colorR, out _colorG, out _colorB);
        }

        public static bool IsEnabled => _enabled;

        // ════════════════════════════════════════════
        //  MonoMod detour：author<255（真实玩家消息）→
        //  改写为 ChatFormat 完整文本 + author=255（客户端不再转义名字）
        // ════════════════════════════════════════════

        private static NetPacket OnSerializeServerMessage(OrigSerializeServerMessage orig,
            NetworkText text, Color color, byte authorId)
        {
            if (authorId < byte.MaxValue)
            {
                try
                {
                    var player = authorId < TShock.Players.Length ? TShock.Players[authorId] : null;
                    if (player != null && player.Active)
                    {
                        var message = text.ToString();
                        var group = player.Group;
                        var chatFormat = TShock.Config.Settings.ChatFormat;
                        if (string.IsNullOrEmpty(chatFormat)) chatFormat = "{1}{2}{3}: {4}";
                        var full = string.Format(chatFormat,
                            group?.Name ?? "", group?.Prefix ?? "", player.Name, group?.Suffix ?? "", message);
                        return orig(NetworkText.FromLiteral(full), color, byte.MaxValue);
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[CrossChat] 聊天改写异常: {ex.Message}");
                }
            }
            return orig(text, color, authorId);
        }

        // ════════════════════════════════════════════
        //  跨服上报（PlayerChat 事件，不 Handled）
        // ════════════════════════════════════════════

        private static void OnPlayerChat(PlayerChatEventArgs e)
        {
            if (e.Handled) return;
            var p = e.Player;
            if (p == null || string.IsNullOrEmpty(e.RawText)) return;
            var text = e.RawText.Trim();
            if (text.Length == 0 || text.Length > MaxChatLength) return;

            // 跨服上报（仅本服启用跨服聊天）
            if (!_enabled) return;
            try
            {
                var group = p.Group;
                var payload = new JObject
                {
                    ["serverId"] = _serverId,
                    ["serverName"] = _serverName,
                    ["prefix"] = _prefix,                 // 已渲染前缀（含 [c/...]），接收端直接显示
                    ["player"] = p.Name,                  // 名字原样（含 [i:] 标签，不转义）
                    ["groupPrefix"] = group?.Prefix ?? "",
                    ["groupSuffix"] = group?.Suffix ?? "",
                    ["text"] = text,
                    ["r"] = _colorR, ["g"] = _colorG, ["b"] = _colorB  // crossChatColor（消息最外层颜色）
                };
                WebRestServer.Broadcast("cross-chat", payload.ToString(Formatting.None));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[CrossChat] 跨服上报失败: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════
        //  /tsweb/crosschat 接收（WebRestServer 调用）
        // ════════════════════════════════════════════

        /// <summary>
        /// 处理后端转发的跨服聊天推送，返回 JSON 响应体。
        /// 只有本服启用了跨服聊天才显示（未启用时后端本就不会推送到本服，此处为防御）。
        /// </summary>
        public static string HandlePush(string body, Dictionary<string, string> headers)
        {
            if (!WebhookAuth.VerifySignature(headers, body))
                return "{\"status\":\"401\",\"error\":\"Invalid signature\"}";

            if (!_enabled)
                return "{\"status\":\"200\",\"ok\":true}";

            try
            {
                var payload = JsonConvert.DeserializeObject<JObject>(body);
                if (payload == null)
                    return "{\"status\":\"400\",\"error\":\"Bad payload\"}";

                var prefix = SanitizePrefix(payload["prefix"]?.ToString() ?? "");
                var player = payload["player"]?.ToString() ?? "";
                var groupPrefix = payload["groupPrefix"]?.ToString() ?? "";
                var groupSuffix = payload["groupSuffix"]?.ToString() ?? "";
                var text = payload["text"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(text)) return "{\"status\":\"200\",\"ok\":true}";

                var r = ParseByte(payload["r"], _colorR);
                var g = ParseByte(payload["g"], _colorG);
                var b = ParseByte(payload["b"], _colorB);

                // 组装显示文本：前缀(带色) + 组前缀 + 玩家名(不转义，[i:] 生效) + 组后缀 + 消息
                var msg = prefix.Length > 0
                    ? $"{prefix} {groupPrefix}{player}{groupSuffix}: {text}"
                    : $"{groupPrefix}{player}{groupSuffix}: {text}";

                // author=255 → 客户端不转义名字；前缀 [c/HEX:...] 与名字 [i:] 均被 ParseMessage 解析
                TShock.Utils.Broadcast(msg, r, g, b);
                return "{\"status\":\"200\",\"ok\":true}";
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[CrossChat] 跨服消息处理失败: {ex}");
                return "{\"status\":\"500\",\"error\":\"Internal error\"}";
            }
        }

        // ════════════════════════════════════════════
        //  工具
        // ════════════════════════════════════════════

        /// <summary>解析 #RRGGBB → r/g/b，非法回退默认白</summary>
        private static void ParseColor(string hex, out byte r, out byte g, out byte b)
        {
            r = 255; g = 255; b = 255;
            if (string.IsNullOrEmpty(hex)) return;
            var h = hex.TrimStart('#');
            if (h.Length != 6) return;
            if (byte.TryParse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rr) &&
                byte.TryParse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var gg) &&
                byte.TryParse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bb))
            {
                r = rr; g = gg; b = bb;
            }
        }

        /// <summary>把 [c/#RRGGBB:...] 清洗为 [c/RRGGBB:...]（客户端不接受 # 前缀）</summary>
        private static string SanitizePrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return prefix;
            return ColorHashTagRegex.Replace(prefix, "[c/$1");
        }

        private static byte ParseByte(JToken? token, byte fallback)
        {
            if (token == null) return fallback;
            var v = token.ToObject<int?>();
            return v.HasValue && v.Value >= 0 && v.Value <= 255 ? (byte)v.Value : fallback;
        }
    }
}

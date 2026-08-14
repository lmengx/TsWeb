using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TShockData
{
    /// <summary>
    /// 后端 → 插件 推送的 HMAC-SHA256 签名校验（与 /hook 协议一致）。
    ///
    /// 协议（与 qqAccountService.js / hookAuth.js 对齐）：
    ///   headers: X-Server-Id / X-Timestamp / X-Nonce / X-Signature
    ///   signature = HMAC(pushSecret, `${ts}.${nonce}.${sha256(rawBody)}`)
    ///   校验：时间窗 ±300s + nonce 去重 + constant-time 比对。
    /// 由 AccountSync（/tsweb/qqsync）与 CrossChat（/tsweb/crosschat）共用。
    /// </summary>
    public static class WebhookAuth
    {
        // nonce key → 首次收到时间戳（毫秒）。按时间窗惰性淘汰，避免超量后整体清空放大重放窗口
        private static readonly Dictionary<string, long> _nonceCache = new();
        private static readonly object _nonceLock = new();

        /// <summary>校验 HMAC 签名。通过返回 true，并消耗 nonce（防重放）。</summary>
        public static bool VerifySignature(Dictionary<string, string> headers, string body)
        {
            var secret = SSELogger.GetWebhookSecret();
            if (string.IsNullOrEmpty(secret)) return false;

            if (!headers.TryGetValue("X-Server-Id", out var sid) ||
                !headers.TryGetValue("X-Timestamp", out var tsRaw) ||
                !headers.TryGetValue("X-Nonce", out var nonce) ||
                !headers.TryGetValue("X-Signature", out var sig))
                return false;

            if (!long.TryParse(tsRaw, out var ts)) return false;
            if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts) > 300_000) return false;

            var key = $"{sid}:{nonce}";
            lock (_nonceLock)
            {
                // 惰性淘汰时间窗（±300s）外的旧 nonce
                if (_nonceCache.Count > 5000)
                {
                    var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 300_000;
                    foreach (var kv in _nonceCache.Where(kv => kv.Value < cutoff).ToList())
                        _nonceCache.Remove(kv.Key);
                }
                if (_nonceCache.ContainsKey(key)) return false;
                _nonceCache[key] = ts;
            }

            var bodyHash = Sha256Hex(body);
            var expected = HmacSha256Hex(secret, $"{tsRaw}.{nonce}.{bodyHash}");
            return string.Equals(sig, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>清空 nonce 去重缓存（插件释放时调用）</summary>
        public static void ClearNonceCache()
        {
            lock (_nonceLock) _nonceCache.Clear();
        }

        public static string Sha256Hex(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(bytes);
        }

        public static string HmacSha256Hex(string key, string input)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(bytes);
        }
    }
}

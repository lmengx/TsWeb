using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TShockAPI;

namespace TShockData
{
    public class ProjRestrictionConfig
    {
        [JsonProperty("启用")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("伤害上限")]
        public int DamageLimit { get; set; } = 20000;

        [JsonProperty("限制列表")]
        public List<ProjRestriction> Restrictions { get; set; } = new List<ProjRestriction>();
    }

    public class ProjRestriction
    {
        [JsonProperty("进度")]
        public string Progress { get; set; } = "始终生效";

        [JsonProperty("限制弹幕")]
        public List<RestrictedProj> Projectiles { get; set; } = new List<RestrictedProj>();
    }

    public class RestrictedProj
    {
        [JsonProperty("id")]
        public int ID { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; } = "log";
    }

    public static class ProjDetection
    {
        private static List<RestrictedProj> _currentRestrictedProjectiles = new List<RestrictedProj>();

        public static void Initialize()
        {
            RefreshRestrictedProjectiles();
            TShock.Log.ConsoleInfo($"[ProjDetection] 弹幕检测已初始化，违禁弹幕数: {_currentRestrictedProjectiles.Count}");
        }

        public static void ShowRestrictedList(CommandArgs args)
        {
            args.Player.SendInfoMessage("=== 当前生效的违禁弹幕列表 ===");
            args.Player.SendInfoMessage($"总数: {_currentRestrictedProjectiles.Count}");

            foreach (var proj in _currentRestrictedProjectiles)
            {
                string methodDesc = proj.Method switch
                {
                    "ban" => "封禁",
                    "kick" => "踢出",
                    "log" => "记录",
                    _ => $"命令: {proj.Method}"
                };

                args.Player.SendInfoMessage($"弹幕ID: {proj.ID}, 处理方式: {methodDesc}");
            }

            if (_currentRestrictedProjectiles.Count == 0)
            {
                args.Player.SendInfoMessage("当前没有生效的违禁弹幕");
            }
        }

        public static void RefreshRestrictedProjectiles()
        {
            _currentRestrictedProjectiles.Clear();

            var config = AntiCheat.GetProjConfig();
            if (config?.Restrictions == null)
                return;

            foreach (var restriction in config.Restrictions)
            {
                bool progressMet = BossProgress.GetWorldStatus(restriction.Progress);

                if (progressMet)
                {
                    foreach (var proj in restriction.Projectiles)
                    {
                        proj.Method = proj.Method?.ToLower() ?? "log";
                        _currentRestrictedProjectiles.Add(proj);
                    }
                }
            }
        }

        public static bool CheckProjectile(TSPlayer player, short type, short damage)
        {
            if (player == null || !player.Active || !player.IsLoggedIn)
                return false;

            if (player.HasPermission("tshock.admin.projectileban"))
                return false;

            var config = AntiCheat.GetProjConfig();
            if (config == null || !config.Enabled)
                return false;

            if (damage > config.DamageLimit || damage < -10)
            {
                TShock.Log.ConsoleError($"[ProjDetection] 检测到异常伤害弹幕! 玩家: {player.Name}, 弹幕ID: {type}, 伤害: {damage}");

                if (player.HasPermission("tshock.projectiles.usebanned"))
                    return true;

                ViolationExecutor.ExecuteViolation(player, "ban");
                return true;
            }

            var matchedProjs = _currentRestrictedProjectiles.Where(p => p.ID == type).ToList();
            if (matchedProjs.Count > 0)
            {
                RefreshRestrictedProjectiles();
                var confirmedProjs = _currentRestrictedProjectiles.Where(p => p.ID == type).ToList();
                if (confirmedProjs.Count > 0)
                {
                    if (player.HasPermission("tshock.projectiles.usebanned"))
                        return true;

                    foreach (var confirmedProj in confirmedProjs)
                    {
                        TShock.Log.ConsoleError($"[ProjDetection] 检测到违禁弹幕! 玩家: {player.Name}, 弹幕ID: {type}, 伤害: {damage}, 处理方式: {confirmedProj.Method}");
                        ViolationExecutor.ExecuteViolation(player, confirmedProj.Method, projId: type);
                    }
                    return true;
                }
            }
            return false;
        }
    }
}

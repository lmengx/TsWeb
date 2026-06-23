﻿﻿﻿﻿﻿﻿﻿using System;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace TShockData
{
    public class RuntimeHooks
    {
        public static void Initialize()
        {
            GetDataHandlers.NewProjectile.Register(OnNewProjectile);
            TShock.Log.ConsoleInfo("[RuntimeHooks] 已注册 NewProjectile 事件处理器");
        }

        public static void Dispose()
        {
            GetDataHandlers.NewProjectile.UnRegister(OnNewProjectile);
        }

        private static void OnNewProjectile(object sender, GetDataHandlers.NewProjectileEventArgs args)
        {
            try
            {
                short type = args.Type;
                short damage = args.Damage;
                byte owner = args.Owner;
                short identity = args.Identity;

                //TShock.Log.ConsoleInfo($"[RuntimeHooks] 检测弹幕创建 - Type:{type}, Damage:{damage}, Owner:{owner}");

                bool shouldBlock = false;

                if (owner >= 0 && owner < Main.maxPlayers)
                {
                    var player = TShock.Players[owner];
                    if (player != null && player.Active)
                    {
                        shouldBlock = ProjDetection.CheckProjectile(player, type, damage);
                    }
                    else if (owner >= 0)
                    {
                        shouldBlock = CheckProjectileByOwner(owner, type, damage);
                    }
                }

                if (shouldBlock)
                {
                    string playerName = owner >= 0 && owner < Main.maxPlayers && TShock.Players[owner] != null 
                        ? TShock.Players[owner].Name 
                        : $"Unknown({owner})";
                    
                    TShock.Log.ConsoleInfo($"[RuntimeHooks] 拦截违规弹幕 - 玩家:{playerName}, Type:{type}, Damage:{damage}");
                    
                    args.Handled = true;
                    
                    if (owner >= 0 && owner < Main.maxPlayers && TShock.Players[owner] != null)
                    {
                        TShock.Players[owner].RemoveProjectile(identity, owner);
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[RuntimeHooks] 事件处理异常: {ex.Message}");
            }
        }

        private static bool CheckProjectileByOwner(int owner, short type, short damage)
        {
            var config = AntiCheat.GetProjConfig();
            if (config == null || !config.Enabled)
                return false;

            if (damage > config.DamageLimit || damage < 0)
            {
                TShock.Log.ConsoleError($"[ProjDetection] 检测到异常伤害弹幕! 所有者:{owner}, 弹幕ID:{type}, 伤害:{damage}");
                return true;
            }

            return false;
        }
    }
}

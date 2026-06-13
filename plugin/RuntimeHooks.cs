﻿using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.RuntimeDetour;
using TShockAPI;
using Terraria;

namespace TShockData
{
    public class RuntimeHooks
    {
        private static Hook _newProjectileHook;

        public static void Initialize()
        {
            HookNewProjectile();
        }

        public static void Dispose()
        {
            _newProjectileHook?.Dispose();
        }

        private static void HookNewProjectile()
        {
            var getDataHandlersType = typeof(GetDataHandlers);
            
            var onNewProjectileMethod = getDataHandlersType.GetMethod(
                "OnNewProjectile",
                BindingFlags.Static | BindingFlags.NonPublic
            );

            if (onNewProjectileMethod == null)
            {
                TShock.Log.ConsoleError("[RuntimeHooks] 未找到 OnNewProjectile 方法");
                return;
            }

            _newProjectileHook = new Hook(onNewProjectileMethod, OnNewProjectileHook);

            TShock.Log.ConsoleInfo("[RuntimeHooks] 弹幕 Hook 已初始化");
        }

        private delegate bool OrigOnNewProjectile(
            System.IO.MemoryStream data,
            short ident,
            Microsoft.Xna.Framework.Vector2 pos,
            Microsoft.Xna.Framework.Vector2 vel,
            float knockback,
            short dmg,
            byte owner,
            short type,
            int index,
            TSPlayer player,
            float[] ai
        );

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool OnNewProjectileHook(
            OrigOnNewProjectile orig,
            System.IO.MemoryStream data,
            short ident,
            Microsoft.Xna.Framework.Vector2 pos,
            Microsoft.Xna.Framework.Vector2 vel,
            float knockback,
            short dmg,
            byte owner,
            short type,
            int index,
            TSPlayer player,
            float[] ai
        )
        {
            TShock.Log.ConsoleInfo($"[弹幕Hook] 玩家: {player.Name}, 弹幕ID: {type}, 伤害: {dmg}");

            return orig(data, ident, pos, vel, knockback, dmg, owner, type, index, player, ai);
        }
    }
}
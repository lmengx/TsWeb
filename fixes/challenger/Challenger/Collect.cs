using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Challenger;

internal static class Collect
{
    public static CProjectile[] cprojs = new CProjectile[1000];

    public static CNPC[] cnpcs = new CNPC[200];

    public static CPlayer[] cplayers = new CPlayer[255];

    public static int worldevent = 0;

    public static HashSet<int> noneedlifeNPC = new HashSet<int> { 115, 116, 488 };

    public static int MyNewProjectile(IEntitySource? spawnSource, float X, float Y, float SpeedX, float SpeedY, int Type, int Damage, float KnockBack, int Owner = 255, float ai0 = 0f, float ai1 = 0f, float ai2 = 0f)
    {
        if (Owner == -1)
        {
            Owner = Main.myPlayer;
        }
        if (Main.netMode != 0 && Owner != Main.myPlayer)
        {
            // 1.4.5.8 服务器：原版 Projectile.NewProjectile 只许 owner==myPlayer(255)
            // （Invariant.Assert + return 1000），玩家弹幕一律由客户端发起。
            // 挑战者的套装被动/血包需要在服务器代造玩家归属弹幕，必须手动走
            // NewProjectileSetup 分配 ProjectileKey(owner, index, gen)。
            return ServerSpawnProjectileForPlayer(spawnSource, X, Y, SpeedX, SpeedY, Type, Damage, KnockBack, Owner, ai0, ai1, ai2);
        }
        // 服务器(owner=255)/单机：原版静态方法内部处理槽位分配、generation、入水、
        // aiStyle 限速、StardustDragon 链接与 27 号包广播等全部细节。
        return Projectile.NewProjectile(spawnSource, X, Y, SpeedX, SpeedY, Type, Damage, KnockBack, Owner, ai0, ai1, ai2);
    }

    // 对齐原版 mfwh_NewProjectile（OTAPI 反编译）除 owner==myPlayer 专属块外的完整初始化，
    // 并让 ProjectileKey 以 Owner 为 Spawner 注册（keyToIndex/slotGenerations 均为 public）。
    private static int ServerSpawnProjectileForPlayer(IEntitySource? spawnSource, float X, float Y, float SpeedX, float SpeedY, int Type, int Damage, float KnockBack, int Owner, float ai0, float ai1, float ai2)
    {
        int num = 1000;
        for (var i = 0; i < 1000; i++)
        {
            if (!Main.projectile[i].active)
            {
                num = i;
                break;
            }
        }
        if (num == 1000)
        {
            num = Projectile.FindOldestProjectile();
        }
        var projectile = Projectile.NewProjectileSetup(new ProjectileKey(Owner, num, ++Projectile.slotGenerations[num]));
        var whoAmI = projectile.whoAmI;
        projectile.SetDefaults(Type);
        projectile.position.X = X - (projectile.width * 0.5f);
        projectile.position.Y = Y - (projectile.height * 0.5f);
        projectile.owner = Owner;
        projectile.velocity.X = SpeedX;
        projectile.velocity.Y = SpeedY;
        projectile.damage = Damage;
        projectile.knockBack = KnockBack;
        projectile.gfxOffY = 0f;
        projectile.stepSpeed = 1f;
        projectile.wet = Collision.WetCollision(projectile.position, projectile.width, projectile.height);
        if (projectile.ignoreWater)
        {
            projectile.wet = false;
        }
        projectile.honeyWet = Collision.honey;
        projectile.shimmerWet = Collision.shimmer;
        projectile.ApplyStatsFromSource(spawnSource);
        projectile.FindBannerToAssociateTo(spawnSource);
        projectile.TrackMinionSpawnSource(spawnSource);
        if (projectile.aiStyle == 1)
        {
            while (projectile.velocity.X >= 16f || projectile.velocity.X <= -16f || projectile.velocity.Y >= 16f || projectile.velocity.Y < -16f)
            {
                projectile.velocity.X *= 0.97f;
                projectile.velocity.Y *= 0.97f;
            }
        }
        projectile.ai[0] = ai0;
        projectile.ai[1] = ai1;
        projectile.ai[2] = ai2;
        if (Type == 434)
        {
            projectile.ai[0] = projectile.position.X;
            projectile.ai[1] = projectile.position.Y;
        }
        if (Type == 249)
        {
            projectile.frame = Main.rand.Next(5);
        }
        projectile.FinalizeProjectile();
        return whoAmI;
    }

    public static int MyNewProjectile(IEntitySource? spawnSource, Vector2 postion, Vector2 velocity, int Type, int Damage, float KnockBack, int Owner = -1, float ai0 = 0f, float ai1 = 0f, float ai2 = 0f)
    {
        return MyNewProjectile(spawnSource, postion.X, postion.Y, velocity.X, velocity.Y, Type, Damage, KnockBack, Owner, ai0, ai1, ai2);
    }
}
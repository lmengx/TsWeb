import { Context } from 'koishi'
import type { Config } from '../utils/config'
import { safeHttpGet, safeHttpPost } from '../utils/config'

export const name = 'tshock-nickname'

/**
 * QQ 昵称刷新（后端 → 本插件轮询领取任务 → 取昵称 → 回报）
 *
 * 为什么是轮询而不是后端直接调机器人：
 *   后端与机器人之间只有「机器人 → 后端」这一个方向（机器人持 token 调后端），
 *   后端**没有任何到机器人的出站通道**，也没有 QQ 框架接口配置。
 *   所以管理页点「获取昵称」只能在后端登记一个任务，由本插件定时领取。
 *
 * 为什么不需要机器人收到消息才能取：
 *   bot.getUser(qq) 是平台主动查询接口（OneBot 的 get_stranger_info），
 *   对**任意** QQ 都可用，不要求对方在群里发言或加好友。
 *
 * 日志纪律（本项目硬要求）：**只在状态跃迁时打日志**——
 * 每 15 秒打一条"轮询中"会把日志刷爆（RecipeBrowser 单会话 9MB 的教训）。
 * 因此：无任务不打、连续失败只在第一次打、恢复时打一条。
 */
export function apply(ctx: Context, config: Config) {
  const seconds = Number(config.昵称刷新轮询秒) || 0
  if (seconds <= 0) {
    ctx.logger.info('[昵称刷新] 未启用（昵称刷新轮询秒 = 0）')
    return
  }
  if (!config.后端地址 || !config.机器人密钥) {
    ctx.logger.warn('[昵称刷新] 后端地址或机器人密钥未配置，昵称刷新不会启动')
    return
  }

  const base = `http://${config.后端地址}`
  /** 上一次轮询是否失败（用于"只报一次"与"恢复时报一次"） */
  let lastFailed = false
  /** 上一轮是否仍在进行（慢查询时避免叠加） */
  let busy = false

  /** 取一个可用机器人实例（昵称查询必须经平台适配器） */
  function pickBot() {
    const bots = (ctx as any).bots || []
    return bots.find((b: any) => b && b.status === 'online') || bots[0] || null
  }

  async function pollOnce() {
    if (busy) return
    busy = true
    try {
      const res = await safeHttpGet(ctx, `${base}/api/bot/nickname-task`, {
        token: config.机器人密钥
      })
      if (!res.ok) {
        if (!lastFailed) {
          lastFailed = true
          ctx.logger.warn('[昵称刷新] 领取任务失败（后续失败不再重复记录）:', res.msg)
        }
        return
      }
      if (lastFailed) {
        lastFailed = false
        ctx.logger.info('[昵称刷新] 后端已恢复')
      }

      const task = (res.data && res.data.task) || null
      if (!task || !Array.isArray(task.qqs) || task.qqs.length === 0) return

      const bot = pickBot()
      if (!bot) {
        ctx.logger.warn('[昵称刷新] 当前没有可用的机器人实例，无法获取昵称')
        // 仍然回报一次：否则任务会一直挂在 claimed，管理页只能等到超时才看到失败
        await safeHttpPost(ctx, `${base}/api/bot/qq-nickname`, { token: config.机器人密钥 }, {
          taskId: task.id,
          entries: task.qqs.map((qq: string) => ({ qq, error: '机器人没有可用实例' }))
        })
        return
      }

      ctx.logger.info(`[昵称刷新] 领取任务 ${task.id}，共 ${task.qqs.length} 个 QQ`)

      // 适配器可能没有实现主动查询（getUser）：显式回报失败，让管理页看到原因，
      // 而不是整批静默失败或抛异常把任务挂死
      if (typeof bot.getUser !== 'function') {
        ctx.logger.warn('[昵称刷新] 当前平台适配器不支持 getUser，无法获取昵称')
        await safeHttpPost(ctx, `${base}/api/bot/qq-nickname`, { token: config.机器人密钥 }, {
          taskId: task.id,
          entries: task.qqs.map((qq: string) => ({ qq, error: '当前平台适配器不支持主动查询昵称' }))
        })
        return
      }

      const entries: Array<{ qq: string; nickname?: string; error?: string }> = []
      for (const qq of task.qqs) {
        try {
          const user = await bot.getUser(String(qq))
          // 取全局 QQ 昵称（name）；部分平台只给 nick，兜底用它
          const nickname = String((user && (user.name || user.nick)) || '').trim()
          if (nickname) entries.push({ qq: String(qq), nickname })
          else entries.push({ qq: String(qq), error: '平台未返回昵称' })
        } catch (err: any) {
          // 单个 QQ 失败不影响其余：逐条记录原因，让管理页能看到"哪些没取到"
          entries.push({ qq: String(qq), error: err && err.message ? err.message : '查询失败' })
        }
      }

      const report = await safeHttpPost(ctx, `${base}/api/bot/qq-nickname`, {
        token: config.机器人密钥
      }, { taskId: task.id, entries })

      if (!report.ok) {
        ctx.logger.warn('[昵称刷新] 回报失败:', report.msg)
        return
      }
      const okCount = entries.filter(e => e.nickname).length
      ctx.logger.info(`[昵称刷新] 任务 ${task.id} 完成：成功 ${okCount} / 共 ${entries.length}`)
    } catch (err: any) {
      if (!lastFailed) {
        lastFailed = true
        ctx.logger.warn('[昵称刷新] 轮询异常（后续异常不再重复记录）:', err && err.message)
      }
    } finally {
      busy = false
    }
  }

  // 用原生 setInterval 并在 dispose 时清理：本项目对"定时器泄漏"有明确教训，
  // 显式清理比依赖框架隐式回收更可靠（插件热重载时尤其重要）
  const timer = setInterval(() => { pollOnce().catch(() => {}) }, seconds * 1000)
  ctx.on('dispose', () => clearInterval(timer))

  ctx.logger.info(`[昵称刷新] 已启用，每 ${seconds} 秒向后端领取一次昵称刷新任务`)
}

import { Context, Session } from 'koishi'

export const name = 'temp-msg-debug'

export function apply(ctx: Context) {
  ctx.logger.info("[tempMsg] 调试插件已加载")

  ctx.on('message', async (session: Session) => {
    // 不做任何过滤，每条消息都打印完整信息
    console.log("========== 消息调试 ==========")
    console.log("content:", session.content)
    console.log("用户ID(session.userId):", session.userId)
    console.log("群ID(session.guildId):", session.guildId)
    console.log("频道ID(session.channelId):", session.channelId)
    console.log("消息ID(session.messageId):", session.messageId)
    console.log("平台(session.platform):", session.platform)

    // 遍历 session 所有自有属性（不含原型链）
    console.log("--- session 所有属性 ---")
    for (const key of Object.keys(session)) {
      try {
        const val = (session as any)[key]
        if (typeof val !== 'function' && typeof val !== 'object') {
          console.log(`  ${key}:`, val)
        } else if (typeof val === 'object' && val !== null) {
          console.log(`  ${key}:`, JSON.stringify(val).slice(0, 200))
        }
      } catch {}
    }

    // 检查 event 原始事件
    console.log("--- session.event ---")
    try {
      const ev = (session as any).event
      console.log("  event:", JSON.stringify(ev, null, 2).slice(0, 500))
    } catch (e: any) {
      console.log("  event 获取失败:", e.message)
    }

    console.log("--- session.rawEvent ---")
    try {
      const raw = (session as any).rawEvent
      console.log("  rawEvent:", JSON.stringify(raw, null, 2).slice(0, 500))
    } catch (e: any) {
      console.log("  rawEvent 获取失败:", e.message)
    }

    // 尝试 getUser 看能不能拿到信息
    try {
      const user = await session.bot.getUser(session.userId)
      console.log("getUser:", JSON.stringify(user, null, 2))
    } catch (e: any) {
      console.log("getUser 失败:", e.message)
    }

    // 如果有 guildId，尝试获取群成员信息
    if (session.guildId) {
      try {
        const member = await session.bot.getGuildMember(session.guildId, session.userId)
        console.log("getGuildMember:", JSON.stringify(member, null, 2))
      } catch (e: any) {
        console.log("getGuildMember 失败:", e.message)
      }
    }

    // 尝试回复
    try {
      await session.send("1")
      console.log("send(1) 成功")
    } catch (e: any) {
      console.log("send(1) 失败:", e.message)
      // 尝试备用方式
      try {
        const bot = session.bot as any
        if (session.guildId) {
          await bot.sendGroupMsg(session.guildId, `[CQ:at,qq=${session.userId}] 1`)
          console.log("sendGroupMsg 成功")
        } else {
          await bot.sendPrivateMsg(session.userId, "1")
          console.log("sendPrivateMsg 成功")
        }
      } catch (e2: any) {
        console.log("备用发送也失败:", e2.message)
      }
    }

    console.log("==================================")
  })
}

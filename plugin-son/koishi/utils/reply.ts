import { Context, Session } from 'koishi'

/** 判断是否为临时会话（有 guildId 但 subtype 为 private） */
export function isTempSession(session: Session): boolean {
  return !!(session.guildId && (session as any).subsubtype === 'private')
}

/**
 * 回复临时消息
 * Koishi 适配层对临时会话的 send 有问题（user_id 转内部ID、缺 group_id）
 * 这里直接用 bot.internal.sendPrivateMsg + group_id 绕过
 */
export async function replyTemp(ctx: Context, session: Session, text: string) {
  // 方式1: 原生 API sendPrivateMsg + group_id（OneBot 标准）
  try {
    const bot = session.bot as any
    if (bot.internal?.sendPrivateMsg) {
      await bot.internal.sendPrivateMsg(session.userId, text, {
        group_id: Number(session.guildId)
      })
      return
    }
  } catch {}

  // 方式2: HTTP API 直调 NapCat
  try {
    const bot = session.bot as any
    await ctx.http.post(`http://127.0.0.1:${bot.port || 3001}/send_private_msg`, {
      user_id: Number(session.userId),
      group_id: Number(session.guildId),
      message: text
    }, {
      headers: { Authorization: `Bearer ${bot.token || ''}` }
    })
    return
  } catch {}

  console.log('[replyTemp] 无法回复临时会话, userId:', session.userId)
}

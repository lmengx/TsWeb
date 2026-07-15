import { Context, Session, h } from 'koishi'
import type { Config } from '../utils/config'
import { safeHttpGet } from '../utils/config'

export const name = 'tshock-private'

export function apply(ctx: Context, config: Config) {
  ctx.logger.info('[tshock-private] 私聊/临时会话处理器已加载')

  ctx.on('message', async (session: Session) => {
    if (session.guildId) return

    const content = session.content.trim()
    if (!content.startsWith('改密码 ')) return

    const newPassword = content.replace('改密码 ', '').trim()
    if (!newPassword) {
      await tryReply(ctx, session, config, '密码不能为空')
      return
    }

    const res = await safeHttpGet(ctx, `http://${config.服务器地址}/data/qq/reset-password`, {
      token: config.接口密钥, qq: session.userId, password: newPassword
    })

    if (res.ok) {
      ctx.logger.info('[改密码]QQ', session.userId, '密码已修改')
      await tryReply(ctx, session, config, '✅密码修改成功✅\n请使用新密码登录游戏')
    } else {
      ctx.logger.error('[改密码]QQ', session.userId, '失败：', res.msg)
      await tryReply(ctx, session, config, res.msg)
    }
  })
}

async function tryReply(ctx: Context, session: Session, config: Config, text: string) {
  // 直接用内部 API 发私聊，方便检测是否成功
  try {
    const bot = session.bot as any
    if (bot.internal?.sendPrivateMsg) {
      await bot.internal.sendPrivateMsg(session.userId, text)
    } else {
      await session.send(text)
    }
    return
  } catch {}

  // 在生效群列表中逐个查找该用户
  ctx.logger.info('[私聊] 尝试在群中查找用户:', session.userId)

  for (const groupId of config.生效群列表) {
    try {
      const member = await session.bot.getGuildMember(String(groupId), session.userId)
      ctx.logger.info('[私聊] 在群', groupId, '找到用户:', JSON.stringify(member).slice(0, 100))
      await session.bot.sendMessage(
        String(groupId),
        h('at', { id: session.userId }) + ' ' + text
      )
      ctx.logger.info('[私聊] 已在群', groupId, '通知用户:', session.userId)
      return
    } catch (e: any) {
      ctx.logger.info('[私聊] 群', groupId, '查找失败:', e.message)
    }
  }

  ctx.logger.error('[私聊] 所有生效群中均未找到用户:', session.userId)
}

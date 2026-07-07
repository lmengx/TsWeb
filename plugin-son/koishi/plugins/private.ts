import { Context, Session } from 'koishi'
import { getConfig } from '../utils/config'

export const name = 'tshock-private'

export function apply(ctx: Context) {
  const config = getConfig()
  ctx.logger.info('[tshock-private] 私聊处理器已加载')

  ctx.on('message', async (session: Session) => {
    // 只处理好友私聊（无 guildId 且不是临时会话）
    if (session.guildId) return

    const content = session.content.trim()
    if (!content.startsWith('改密码 ')) return

    const newPassword = content.replace('改密码 ', '').trim()
    if (!newPassword) {
      await session.send('🔴密码不能为空')
      return
    }

    try {
      await ctx.http.get(`http://${config.服务器地址}/data/qq/reset-password`, {
        params: { token: config.接口密钥, qq: session.userId, password: newPassword }
      })
      await session.send('✅密码修改成功✅\n请使用新密码登录游戏')
      ctx.logger.info('[改密码]QQ', session.userId, '密码已修改（私聊）')
    } catch (err: any) {
      const reason = err.response?.data?.error ?? err.message
      await session.send(`🔴修改失败🔴\n${reason}`)
      ctx.logger.error('[改密码]QQ', session.userId, '失败：', reason)
    }
  })
}

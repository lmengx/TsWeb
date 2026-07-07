import { Context, Session } from 'koishi'
import { getConfig } from '../utils/config'
import { isTempSession, replyTemp } from '../utils/reply'

export const name = 'tshock-temp'

export function apply(ctx: Context) {
  const config = getConfig()
  ctx.logger.info('[tshock-temp] 临时会话处理器已加载')

  ctx.on('message', async (session: Session) => {
    if (!isTempSession(session)) return
    if (Number(session.guildId) !== config.目标群号) return

    const content = session.content.trim()

    if (!content.startsWith('改密码 ')) return

    const newPassword = content.replace('改密码 ', '').trim()
    if (!newPassword) {
      await replyTemp(ctx, session, '密码不能为空')
      return
    }

    try {
      await ctx.http.get(`http://${config.服务器地址}/data/qq/reset-password`, {
        params: { token: config.接口密钥, qq: session.userId, password: newPassword }
      })
      await replyTemp(ctx, session, '密码修改成功，请使用新密码登录游戏')
      ctx.logger.info('[改密码]QQ', session.userId, '密码已修改（临时会话）')
    } catch (err: any) {
      const reason = err.response?.data?.error ?? err.message
      await replyTemp(ctx, session, `修改失败：${reason}`)
      ctx.logger.error('[改密码]QQ', session.userId, '失败：', reason)
    }
  })
}

import { Context, Session } from 'koishi'
import { getConfig } from '../utils/config'
import { isTempSession } from '../utils/reply'

export const name = 'tshock-group'

export function apply(ctx: Context) {
  const config = getConfig()
  ctx.logger.info('[tshock-group] 群聊处理器已加载')

  ctx.on('message', async (session: Session) => {
    // 只处理群聊（排除临时会话）
    if (!session.guildId) return
    if (isTempSession(session)) return
    if (Number(session.guildId) !== config.目标群号) return

    const content = session.content.trim()
    const senderQQ = session.userId

    // — 绑定 —
    if (content.startsWith('绑定 ')) {
      const playerName = content.replace('绑定 ', '').trim()
      try {
        await ctx.http.get(`http://${config.服务器地址}/data/qq/bind`, {
          params: { token: config.接口密钥, player: playerName, qq: senderQQ }
        })
        await session.send('✅绑定成功✅\n服务器地址：tlry.me，端口37777\n请愉快地游玩吧 ')
      } catch (err: any) {
        const reason = err.response?.data?.error ?? err.message
        await session.send(`🔴绑定失败🔴\n${reason}`)
        ctx.logger.error('[绑定]QQ', senderQQ, '失败：', reason)
      }
      return
    }

    // — 注册 —
    if (content.startsWith('注册 ')) {
      const playerName = content.replace('注册 ', '').trim()
      try {
        await ctx.http.get(`http://${config.服务器地址}/data/qq/register`, {
          params: { token: config.接口密钥, player: playerName, qq: senderQQ }
        })
        await session.send(`✅注册成功✅\n角色名：${playerName}\n请私聊我：改密码 密码 来设置您的登录密码`)
        ctx.logger.info('[注册]QQ', senderQQ, '注册角色', playerName, '成功')
      } catch (err: any) {
        const reason = err.response?.data?.error ?? err.message
        await session.send(`🔴注册失败🔴\n${reason}`)
        ctx.logger.error('[注册]QQ', senderQQ, '失败：', reason)
      }
      return
    }

    // — 误发改密码，撤回 —
    if (content.startsWith('改密码')) {
      try { await session.bot.deleteMessage(session.channelId, session.messageId) } catch {}
      await session.send(`${senderQQ} 改密码请私聊我发送，已撤回`)
      return
    }
  })
}

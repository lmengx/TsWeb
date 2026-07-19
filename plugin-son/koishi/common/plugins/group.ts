import { Context, Session, h } from 'koishi'
import type { Config } from '../utils/config'
import { safeHttpGet } from '../utils/config'
import { renderHtml, playerInfoCard } from '../utils/render'

export const name = 'tshock-group'

export function apply(ctx: Context, config: Config) {
  const groupSet = new Set(config.生效群列表)
  ctx.logger.info('[tshock-group] 群聊处理器已加载，生效群数:', groupSet.size)

  ctx.on('message', async (session: Session) => {
    if (!groupSet.has(Number(session.guildId))) return

    const content = session.content.trim()
    const senderQQ = session.userId

    // — 绑定 —
    if (content.startsWith('绑定 ')) {
      const playerName = content.replace('绑定 ', '').trim()
      const res = await safeHttpGet(ctx, `http://${config.服务器地址}/data/qq/bind`, {
        token: config.接口密钥, player: playerName, qq: senderQQ
      })
      if (res.ok) {
        await session.send('✅绑定成功✅\n请愉快地游玩吧 ')
      } else {
        await session.send(h('at', { id: senderQQ }) + res.msg)
      }
      return
    }

    // — 注册 —
    if (content.startsWith('注册 ')) {
      const playerName = content.replace('注册 ', '').trim()
      const res = await safeHttpGet(ctx, `http://${config.服务器地址}/data/qq/register`, {
        token: config.接口密钥, player: playerName, qq: senderQQ
      })
      if (res.ok) {
        await session.send(`✅注册成功✅\n角色名：${playerName}\n发送「改密码 密码」设密码`)
        ctx.logger.info('[注册]QQ', senderQQ, '注册角色', playerName, '成功')
      } else {
        await session.send(h('at', { id: senderQQ }) + res.msg)
      }
      return
    }

    // — 改密码（群聊不允许，撤回 + 提示走私聊） —
    if (content.startsWith('改密码')) {
      const rest = content.slice(3).trim()
      if (rest) {
        try { await session.bot.deleteMessage(session.channelId, session.messageId) } catch {}
      }
      await session.send(h('at', { id: senderQQ }) + ' 改密码请私聊我发送')
      return
    }

    // — 我的信息（图片渲染） —
    if (content === '我的信息') {
      ctx.logger.info('[我的信息] QQ:', senderQQ)

      const res = await safeHttpGet(ctx, `http://${config.服务器地址}/data/qq/query-player`, {
        token: config.接口密钥,
        qq: senderQQ
      })

      if (!res.ok) {
        await session.send(h('at', { id: senderQQ }) + ' ' + res.msg)
        return
      }

      try {
        const html = playerInfoCard(res.data)
        const buf = await renderHtml(html, 2, '.card')
        await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
      } catch (err: any) {
        ctx.logger.error('[我的信息] 截图失败:', err.message)
        const d = res.data
        const hours = Math.floor(d.online_minutes / 60)
        const mins = d.online_minutes % 60
        await session.send(
          `━━━ 玩家信息 ━━━\n` +
          `🎮 角色名：${d.player}\n` +
          `👥 用户组：${d.group}\n` +
          `⏱ 在线时长：${hours}小时${mins}分钟\n` +
          `💀 死亡次数：${d.deaths}\n` +
          `🎣 钓鱼任务：${d.fishing_quests}\n` +
          `📅 注册时间：${d.registered}\n` +
          `━━━━━━━━━━━`
        )
      }
      return
    }
  })
}

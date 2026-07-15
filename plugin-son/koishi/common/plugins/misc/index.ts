import { Context, Session, h } from 'koishi'
import type { Config } from '../../utils/config'
import { safeHttpGet } from '../../utils/config'
import { renderHtml, playerInfoCard } from '../../utils/render'

export const name = 'tshock-misc'

export function apply(ctx: Context, config: Config) {
  const groupSet = new Set(config.生效群列表)
  ctx.logger.info('[tshock-misc] 杂项处理器已加载')

  // — 自动同意好友请求 —
  ctx.on('friend-request', async (session: Session) => {
    ctx.logger.info('[好友请求] 来自:', session.userId)
    try {
      await session.approve()
      ctx.logger.info('[好友请求] 已同意:', session.userId)
    } catch (e: any) {
      ctx.logger.error('[好友请求] 同意失败:', e.message)
    }
  })

  // — 我的信息（群聊查询，图片渲染） —
  ctx.on('message', async (session: Session) => {
    if (!session.guildId) return
    if (!groupSet.has(Number(session.guildId))) return

    const content = session.content.trim()
    if (content !== '我的信息') return

    const senderQQ = session.userId
    ctx.logger.info('[我的信息] QQ:', senderQQ)

    const res = await safeHttpGet(ctx, `http://${config.服务器地址}/data/qq/query-player`, {
      token: config.接口密钥,
      qq: senderQQ
    })

    if (!res.ok) {
      await session.send(h('at', { id: senderQQ }) + ' ' + res.msg)
      return
    }

    // 渲染图片
    try {
      const html = playerInfoCard(res.data)
      const buf = await renderHtml(html, 2, '.card')
      await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
    } catch (err: any) {
      ctx.logger.error('[我的信息] 截图失败:', err.message)
      // 降级为文本
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
  })
}

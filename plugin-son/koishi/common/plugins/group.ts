import { Context, Session, h } from 'koishi'
import type { Config } from '../utils/config'
import { safeHttpGet, safeHttpPost } from '../utils/config'
import { renderHtml, playerInfoCard, bossProgressCard, onlineListCard } from '../utils/render'

export const name = 'tshock-group'

export function apply(ctx: Context, config: Config) {
  const groupSet = new Set(config.生效群列表)
  ctx.logger.info('[tshock-group] 群聊处理器已加载，生效群数:', groupSet.size)

  // 后端未配置时的提示（绑定/注册/服务器列表依赖后端）
  const backendReady = () => !!(config.后端地址 && config.机器人密钥)

  ctx.on('message', async (session: Session) => {
    if (!groupSet.has(Number(session.guildId))) return

    const content = session.content.trim()
    const senderQQ = session.userId

    // — 服务器列表（后端配置的服务器） —
    if (content === '服务器列表') {
      if (!backendReady()) {
        await session.send('机器人后端地址未配置，请联系管理员')
        return
      }
      const res = await safeHttpGet(ctx, `http://${config.后端地址}/api/bot/servers`, {
        token: config.机器人密钥
      })
      if (!res.ok) {
        await session.send(h('at', { id: senderQQ }) + ' ' + res.msg)
        return
      }
      const list = (res.data.servers || []).map((s: any) => `· ${s.name}${s.enabled ? '' : '（停用）'}${s.note ? ' — ' + s.note : ''}`)
      await session.send(`━━━ 服务器列表 ━━━\n${list.join('\n') || '（暂无服务器）'}`)
      return
    }

    // — 绑定（走后端，支持「绑定 服名 角色名」） —
    if (content.startsWith('绑定 ')) {
      if (!backendReady()) {
        await session.send('机器人后端地址未配置，请联系管理员')
        return
      }
      const rest = content.replace('绑定 ', '').trim()
      const parts = rest.split(/\s+/)
      let playerName = rest
      let serverName = ''
      if (parts.length >= 2) {
        serverName = parts[0]
        playerName = parts.slice(1).join(' ')
      }

      let serverId = ''
      if (serverName) {
        const listRes = await safeHttpGet(ctx, `http://${config.后端地址}/api/bot/servers`, {
          token: config.机器人密钥
        })
        const hit = listRes.ok
          ? (listRes.data.servers || []).find((s: any) =>
              s.name.includes(serverName) || serverName.includes(s.name))
          : null
        if (!hit) {
          await session.send(h('at', { id: senderQQ }) + ` 未找到服务器「${serverName}」，发送「服务器列表」查看`)
          return
        }
        serverId = hit.id
      }

      const res = await safeHttpPost(ctx, `http://${config.后端地址}/api/bot/bind`, {
        token: config.机器人密钥
      }, {
        qq: senderQQ, player: playerName, serverId
      })
      if (res.ok) {
        await session.send(`✅绑定成功✅\n角色名：${res.data.player || playerName}\n可在所有服务器使用该角色登录`)
      } else {
        await session.send(h('at', { id: senderQQ }) + res.msg)
      }
      return
    }

    // — 注册（走后端，随机密码 + 提示改密） —
    if (content.startsWith('注册 ')) {
      if (!backendReady()) {
        await session.send('机器人后端地址未配置，请联系管理员')
        return
      }
      const playerName = content.replace('注册 ', '').trim()
      const res = await safeHttpPost(ctx, `http://${config.后端地址}/api/bot/register`, {
        token: config.机器人密钥
      }, {
        qq: senderQQ, player: playerName
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

    // — 进度（Boss击杀图片渲染） —
    if (content === '进度') {
      ctx.logger.info('[进度] QQ:', senderQQ)

      const res = await safeHttpGet(ctx, `http://${config.服务器地址}/data/boss/progress`, {
        token: config.接口密钥
      })

      if (!res.ok) {
        await session.send(h('at', { id: senderQQ }) + ' ' + res.msg)
        return
      }

      try {
        const html = bossProgressCard(res.data)
        const buf = await renderHtml(html, 2, '.wrap')
        await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
      } catch (err: any) {
        ctx.logger.error('[进度] 截图失败:', err.message)
        const d = res.data
        await session.send(
          `━━━ Boss进度 ━━━\n` +
          `击杀: ${d.KilledCount}/${d.TotalBossCount} (${d.BossProgressPercent}%)\n` +
          `事件: ${d.CompletedEventCount}/${d.TotalEventCount} (${d.EventProgressPercent}%)`
        )
      }
      return
    }

    // — 在线（在线列表图片渲染） —
    if (content === '在线') {
      ctx.logger.info('[在线] QQ:', senderQQ)

      const res = await safeHttpGet(ctx, `http://${config.服务器地址}/v2/server/status`, {
        token: config.接口密钥,
        players: true
      })

      if (!res.ok) {
        await session.send(h('at', { id: senderQQ }) + ' ' + res.msg)
        return
      }

      try {
        const html = onlineListCard(res.data)
        const buf = await renderHtml(html, 2, '.wrap')
        await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
      } catch (err: any) {
        ctx.logger.error('[在线] 截图失败:', err.message)
        const d = res.data
        const names = (d.players || []).filter((p: any) => p && p.nickname).map((p: any) => p.nickname)
        await session.send(
          `━━━ 在线列表 ━━━\n` +
          `在线: ${d.playercount} / ${d.maxplayers}\n` +
          (names.length ? names.map((n: string) => `· ${n}`).join('\n') : '当前无人在线')
        )
      }
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

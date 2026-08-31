import { Context, Session, h } from 'koishi'
import type { Config } from '../utils/config'
import { safeHttpGet, safeHttpPost } from '../utils/config'
import { renderHtml, playerInfoCard, bossProgressCard, onlineListCard, multiOnlineCard, voteListCard, voteDetailCard } from '../utils/render'

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
        await session.send(`✅注册成功✅\n角色名：${playerName}\n私聊发送「改密码 密码」设密码`)
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

    // — 进度（Boss击杀图片渲染，走后端；默认主服，「进度 服名」查指定服） —
    if (content === '进度' || content.startsWith('进度 ')) {
      ctx.logger.info('[进度] QQ:', senderQQ)
      if (!backendReady()) {
        await session.send('机器人后端地址未配置，请联系管理员')
        return
      }
      const rest = content.replace('进度', '').trim()
      const params: any = { token: config.机器人密钥 }
      if (rest) params.server = rest

      const res = await safeHttpGet(ctx, `http://${config.后端地址}/api/bot/boss-progress`, params)

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
        const tag = d.server ? `服务器：${d.server.name}\n` : ''
        await session.send(
          `━━━ Boss进度 ━━━\n` +
          tag +
          `击杀: ${d.KilledCount}/${d.TotalBossCount} (${d.BossProgressPercent}%)\n` +
          `事件: ${d.CompletedEventCount}/${d.TotalEventCount} (${d.EventProgressPercent}%)`
        )
      }
      return
    }

    // — 在线（走后端；「在线」按配置模式，「在线 服名」查指定服详情） —
    if (content === '在线' || content.startsWith('在线 ')) {
      ctx.logger.info('[在线] QQ:', senderQQ)
      if (!backendReady()) {
        await session.send('机器人后端地址未配置，请联系管理员')
        return
      }
      const rest = content.replace('在线', '').trim()
      const params: any = { token: config.机器人密钥 }
      if (rest) params.server = rest

      const res = await safeHttpGet(ctx, `http://${config.后端地址}/api/bot/online`, params)

      if (!res.ok) {
        await session.send(h('at', { id: senderQQ }) + ' ' + res.msg)
        return
      }

      try {
        const html = res.data.mode === 'single'
          ? onlineListCard(res.data.data)
          : multiOnlineCard(res.data)
        const buf = await renderHtml(html, 2, '.wrap')
        await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
      } catch (err: any) {
        ctx.logger.error('[在线] 截图失败:', err.message)
        const servers = (res.data.servers || []).map((s: any) =>
          s.players && s.players.length
            ? `· ${s.name}: ${s.online}/${s.max} (${s.players.join('、')})`
            : `· ${s.name}: ${s.online}/${s.max}`
        )
        await session.send(`━━━ 在线列表 ━━━\n${servers.join('\n') || '当前无人在线'}`)
      }
      return
    }

    // — 投票（走后端；「投票」查全部/单轮，「投票 名称」指定轮次） —
    if (content === '投票' || content.startsWith('投票 ')) {
      ctx.logger.info('[投票] QQ:', senderQQ)
      if (!backendReady()) {
        await session.send('机器人后端地址未配置，请联系管理员')
        return
      }
      const rest = content.replace('投票', '').trim()
      const params: any = { token: config.机器人密钥 }
      if (rest) params.name = rest

      const res = await safeHttpGet(ctx, `http://${config.后端地址}/api/bot/votes`, params)

      if (!res.ok) {
        await session.send(h('at', { id: senderQQ }) + ' ' + res.msg)
        return
      }

      const round: any = res.data.round || null
      const rounds: any[] = res.data.rounds || []

      try {
        let html: string
        if (round) {
          html = voteDetailCard(round)
        } else if (rounds.length === 1) {
          html = voteDetailCard(rounds[0])
        } else if (rounds.length > 1) {
          html = voteListCard(rounds)
        } else {
          await session.send(h('at', { id: senderQQ }) + ' 当前没有进行中的投票')
          return
        }
        const buf = await renderHtml(html, 2, '.wrap')
        await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
      } catch (err: any) {
        ctx.logger.error('[投票] 截图失败:', err.message)
        // 文本兜底
        if (round) {
          const statusText = round.status === 'open' ? '进行中' : '已结束'
          const lines = (round.options || []).map((o: any) => `· ${o.text} — ${o.score} 分 (${o.votes} 票)`)
          await session.send(
            `━━━ 投票：${round.title}（${statusText}）━━━\n` +
            (round.description ? round.description + '\n' : '') +
            lines.join('\n')
          )
        } else if (rounds.length) {
          const list = rounds.map((r: any) => `· ${r.title}（${r.status === 'open' ? '进行中' : '已结束'}）`).join('\n')
          await session.send(`━━━ 投票列表 ━━━\n${list}\n发送「投票 名称」查看指定投票详情`)
        }
      }
      return
    }

    // — 我的信息（图片渲染，走后端：多服时长 + 主服游戏数据） —
    if (content === '我的信息') {
      ctx.logger.info('[我的信息] QQ:', senderQQ)
      if (!backendReady()) {
        await session.send('机器人后端地址未配置，请联系管理员')
        return
      }

      const res = await safeHttpGet(ctx, `http://${config.后端地址}/api/bot/player-info`, {
        token: config.机器人密钥,
        qq: senderQQ
      })

      if (!res.ok) {
        await session.send(h('at', { id: senderQQ }) + ' ' + res.msg)
        return
      }

      try {
        const d = res.data
        const html = playerInfoCard({
          player: d.username,
          qq: d.qq,
          group: d.game?.group || '未知',
          registered: d.game?.registered || '',
          online_minutes: d.playtime?.total || 0,
          deaths: d.game?.deaths ?? 0,
          fishing_quests: d.game?.fishing_quests ?? 0
        })
        const buf = await renderHtml(html, 2, '.card')
        await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
      } catch (err: any) {
        ctx.logger.error('[我的信息] 截图失败:', err.message)
        const d = res.data
        const total = d.playtime?.total || 0
        const hours = Math.floor(total / 60)
        const mins = total % 60
        await session.send(
          `━━━ 玩家信息 ━━━\n` +
          `🎮 角色名：${d.username}\n` +
          `👥 用户组：${d.game?.group || '未知'}\n` +
          `⏱ 多服在线时长：${hours}小时${mins}分钟\n` +
          `💀 死亡次数：${d.game?.deaths ?? 0}\n` +
          `🎣 钓鱼任务：${d.game?.fishing_quests ?? 0}\n` +
          `📅 注册时间：${d.game?.registered || ''}\n` +
          `━━━━━━━━━━━`
        )
      }
      return
    }
  })
}

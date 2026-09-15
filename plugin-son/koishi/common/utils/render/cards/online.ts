import { escapeHtml, frame, toNum } from '../frame'

export interface OnlinePlayer {
  nickname: string
  username?: string
  group?: string
  active?: boolean
}

export interface OnlineStatusData {
  name?: string
  world?: string
  playercount: number
  maxplayers: number
  uptime?: string
  players?: OnlinePlayer[]
}

export interface MultiOnlineServer {
  id: string
  name: string
  online: number | null
  max: number | null
  players: string[] | null
}

export interface MultiOnlineData {
  mode: string
  mainServer?: { id: string; name: string } | null
  servers?: MultiOnlineServer[]
}

/** 占用率对应的语义色（走主题令牌，不写死颜色） */
function loadColorVar(online: number, max: number): string {
  const pct = max > 0 ? Math.min(100, Math.round((online / max) * 100)) : 0
  if (pct >= 80) return 'var(--tw-danger)'
  if (pct >= 50) return 'var(--tw-warning)'
  return 'var(--tw-success)'
}

/** 单服在线列表卡片（调用方截图选择器：.wrap） */
export function onlineListCard(data: OnlineStatusData): string {
  const online = toNum(data.playercount)
  const max = toNum(data.maxplayers)
  const players = (data.players || []).filter(p => p && p.nickname)
  const pct = max > 0 ? Math.min(100, Math.round((online / max) * 100)) : 0
  const color = loadColorVar(online, max)
  const serverName = data.name || 'Terraria 服务器'
  const worldName = data.world || ''

  const rows = players.length
    ? players.map(p => `<span class="chip">${escapeHtml(p.nickname)}</span>`).join('')
    : '<div class="empty">当前无人在线</div>'

  return frame(`
<div class="card">
  <div class="head">
    <div>
      <div class="head-title">${escapeHtml(serverName)}</div>
      <div class="head-sub">${worldName ? escapeHtml(worldName) : 'Terraria World'}</div>
    </div>
    <div class="online-pill">
      <span class="online-dot" style="background:${color};box-shadow:0 0 8px ${color}"></span>
      <span class="online-num">${online}</span>
      <span class="online-total">/ ${max}</span>
    </div>
  </div>
  <div class="occ-bar"><div class="occ-inner" style="width:${pct}%;background:${color}"></div></div>
  <div class="list">${rows}</div>
  <div class="foot">
    <span class="foot-name">在线玩家列表</span>
    <span class="foot-tag">LIVE</span>
  </div>
</div>`)
}

/** 多服在线卡片：每服一块，人数不可见时只显示大数字（调用方截图选择器：.wrap） */
export function multiOnlineCard(data: MultiOnlineData): string {
  const servers = data.servers || []
  const blocks = servers.map(s => {
    const online = s.online == null ? '?' : toNum(s.online)
    const max = s.max == null ? '?' : toNum(s.max)
    const mainTag = data.mainServer?.id === s.id ? ' · 主服' : ''
    const names = s.players || []
    let rows: string
    if (names.length) {
      rows = names.map(n => `<span class="chip">${escapeHtml(n)}</span>`).join('')
    } else if (s.players === null) {
      // 仅显示人数：大数字徽章（不重复展示玩家名）
      rows = `<div class="only-badge"><span class="ob-num">${escapeHtml(online)}</span><span class="ob-txt">人在线</span></div>`
    } else {
      rows = '<div class="row muted">当前无人在线</div>'
    }
    return `<div class="sv">
      <div class="sv-head">
        <div class="sv-name">${escapeHtml(s.name)}${escapeHtml(mainTag)}</div>
        <div class="sv-count">${escapeHtml(online)} / ${escapeHtml(max)}</div>
      </div>
      <div class="sv-list">${rows}</div>
    </div>`
  }).join('\n')

  return frame(blocks, { wrapClass: 'col' })
}

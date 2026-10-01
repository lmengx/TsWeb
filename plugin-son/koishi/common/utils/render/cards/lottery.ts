import { escapeHtml, footerBadgeText, frame, toNum } from '../frame'

export interface LotteryPlayer {
  username: string
  nickname?: string
  group?: string
  serverId?: string
  serverName?: string
}

export interface LotteryServerInfo {
  id?: string
  name?: string
  count?: number
  fetchedAt?: string
  error?: string
}

export interface LotteryRecordData {
  id?: string
  at?: string
  pool?: string[]
  poolHash?: string
  seed?: string
  index?: number
  count?: number
  algo?: string
  winner?: LotteryPlayer
  players?: LotteryPlayer[]
  servers?: LotteryServerInfo[]
  prizeText?: string
}

function fmtTime(t?: string): string {
  if (!t) return '未知时间'
  const d = new Date(t)
  // 非法日期字符串会解析出 Invalid Date，直接取字段会渲染成 NaN月NaN日
  if (Number.isNaN(d.getTime())) return '未知时间'
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getMonth() + 1}月${d.getDate()}日 ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** 只取前 n 位：群里当场核对够用，完整值在台账里 */
function shortOf(s: unknown, n = 16): string {
  const v = String(s ?? '')
  return v ? v.slice(0, n) : '-'
}

/**
 * 抽奖结果卡（调用方截图选择器：.card）
 *
 * 设计要点：奖池名单整列展示并高亮中奖者 —— 只报一个名字的抽奖无法自证公平，
 * 列出全部候选才能让群成员当场核对「我确实在名单里、中奖者确实来自名单」。
 */
export function lotteryResultCard(data: LotteryRecordData): string {
  const w = data.winner || ({} as LotteryPlayer)
  const players = Array.isArray(data.players) && data.players.length
    ? data.players
    : (data.pool || []).map(u => ({ username: u } as LotteryPlayer))
  const count = toNum(data.count, players.length)
  const winnerName = String(w.username || '')
  const servers = data.servers || []
  const okCount = servers.filter(s => s && !s.error).length
  const failed = servers.filter(s => s && s.error)

  const chips = players.map(p => {
    const isWin = String(p.username || '') === winnerName
    return `<span class="chip${isWin ? ' win' : ''}">${escapeHtml(p.nickname || p.username)}</span>`
  }).join('')

  const tags = [
    w.username ? `<span class="lt-tag">角色 ${escapeHtml(w.username)}</span>` : '',
    w.group ? `<span class="lt-tag">用户组 ${escapeHtml(w.group)}</span>` : '',
    w.serverName ? `<span class="lt-tag">${escapeHtml(w.serverName)}</span>` : '',
  ].filter(Boolean).join('')

  const failLine = failed.length
    ? `<div class="lt-warn">${escapeHtml(failed.map(s => s.name || s.id || '未知服').join('、'))} 查询失败，该服玩家未进入奖池</div>`
    : ''

  const prizeLine = data.prizeText
    ? `<div class="lt-prize">奖品：${escapeHtml(data.prizeText)}</div>`
    : ''

  const singleNote = count <= 1 ? '（仅 1 人参与）' : ''

  return frame(`
<div class="card glow">
  <div class="head">
    <div>
      <div class="head-title">抽奖结果</div>
      <div class="head-sub">LOTTERY DRAW</div>
    </div>
    <span class="foot-tag">${escapeHtml(data.id || '')}</span>
  </div>
  <div class="lt-win">
    <div class="lt-crown">中奖</div>
    <div class="lt-wname">${escapeHtml(w.nickname || w.username || '未知')}</div>
    <div class="lt-wtags">${tags}</div>
  </div>
  ${prizeLine}
  <div class="lt-stats">
    <div class="lt-stat"><div class="lt-num">${count}</div><div class="lt-label">奖池人数</div></div>
    <div class="lt-stat"><div class="lt-num">${okCount}</div><div class="lt-label">参与服务器</div></div>
    <div class="lt-stat"><div class="lt-num">${escapeHtml(fmtTime(data.at))}</div><div class="lt-label">开奖时间</div></div>
  </div>
  ${failLine}
  <div class="lt-sec">
    <div class="lt-sec-title">奖池名单${escapeHtml(singleNote)}</div>
    <div class="list">${chips || '<div class="empty">当前无人在线</div>'}</div>
  </div>
  <div class="lt-proof">
    <div class="lt-prow"><span class="lt-plabel">奖池指纹</span><span class="lt-pval">${escapeHtml(shortOf(data.poolHash))}</span></div>
    <div class="lt-prow"><span class="lt-plabel">随机种子</span><span class="lt-pval">${escapeHtml(shortOf(data.seed))}</span></div>
    <div class="lt-prow"><span class="lt-plabel">抽取算法</span><span class="lt-pval">${escapeHtml(data.algo || 'seedmod1')} · 第 ${toNum(data.index) + 1} 位</span></div>
  </div>
  <div class="foot">
    <span class="foot-name">按奖池指纹与种子可复算本次结果</span>
    <span class="foot-tag">${escapeHtml(footerBadgeText('DRAW'))}</span>
  </div>
</div>`)
}

/** 抽奖记录卡（调用方截图选择器：.wrap） */
export function lotteryHistoryCard(records: LotteryRecordData[]): string {
  const rows = (records || []).map((r, i) => {
    const w = r?.winner || ({} as LotteryPlayer)
    return `<div class="lh">
      <div class="lh-num">${i + 1}</div>
      <div class="lh-main">
        <div class="lh-title">${escapeHtml(w.nickname || w.username || '未知')}<span class="lh-user">${escapeHtml(w.username || '')}</span></div>
        <div class="lh-meta">${toNum(r?.count)} 人参与 · ${escapeHtml(fmtTime(r?.at))} · 指纹 ${escapeHtml(shortOf(r?.poolHash, 12))}</div>
      </div>
    </div>`
  }).join('\n')

  return frame(`
  <div class="head"><div><div class="head-title">抽奖记录</div><div class="head-sub">LOTTERY HISTORY</div></div></div>
  ${rows || '<div class="empty">暂无开奖记录</div>'}
  <div class="tip">发送「抽奖」从当前在线玩家中抽一名</div>`, { wrapClass: 'col' })
}

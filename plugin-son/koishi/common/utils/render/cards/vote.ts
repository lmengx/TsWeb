import { escapeHtml, frame } from '../frame'

export interface VoteOptionData {
  id: string
  text: string
  type?: string
  proposer?: string
  anonymous?: boolean
  votes?: number
  score?: number
}

export interface WeightRule {
  field?: string
  op?: string
  threshold?: number
  weight?: number
}

export interface VoteRoundData {
  id: string
  title: string
  description?: string
  status?: string
  createdAt?: string
  endAt?: string | null
  closedAt?: string | null
  maxVotesPerUser?: number
  baseWeight?: number
  weightRules?: WeightRule[]
  options?: VoteOptionData[]
}

export interface VoteStateData {
  mode: 'voted' | 'proposed'
  qq: string
  username: string
  unbound?: boolean
  weight?: number
  existing?: boolean
  option?: { text: string }
  round: VoteRoundData & {
    my?: {
      votedOptions?: string[]
      votesLeft?: number
      myProposals?: number
      proposalsLeft?: number
      weight?: number
      baseWeight?: number
      weightRules?: WeightRule[]
    }
  }
}

function fmtDateTime(t?: string | null): string {
  if (!t) return '长期有效'
  const d = new Date(t)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getMonth() + 1}月${d.getDate()}日 ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function ruleLineOf(round: VoteRoundData): string {
  const desc = (round.weightRules || [])
    .filter(r => r && r.field)
    .map(r => `游玩时长 ${escapeHtml(r.op)} ${escapeHtml(r.threshold)}h 加 ${escapeHtml(r.weight)} 分`)
    .join('，')
  const base = `基础 ${Number(round.baseWeight ?? 1)} 分`
  return desc ? `${base} · ${desc}` : base
}

function optionTag(o: VoteOptionData): string {
  if (o.type !== 'custom') return ''
  return o.anonymous
    ? '<span class="vo-tag anon">匿名提案</span>'
    : `<span class="vo-tag">${escapeHtml(o.proposer || '')} 提案</span>`
}

/** 投票列表卡片：多个轮次（调用方截图选择器：.wrap） */
export function voteListCard(rounds: VoteRoundData[]): string {
  const rows = (rounds || []).map((r, i) => {
    const opts = r.options || []
    const totalVotes = opts.reduce((s, o) => s + (Number(o.votes) || 0), 0)
    const open = r.status === 'open'
    const badge = `<span class="rv-badge ${open ? 'open' : 'closed'}">${open ? '进行中' : '已结束'}</span>`
    return `<div class="rv">
      <div class="rv-num">${i + 1}</div>
      <div class="rv-main">
        <div class="rv-title">${escapeHtml(r.title)} ${badge}</div>
        <div class="rv-meta">${opts.length} 个选项 · ${totalVotes} 票 · 截止 ${escapeHtml(fmtDateTime(r.endAt))}</div>
      </div>
    </div>`
  }).join('\n')

  return frame(`
  <div class="head"><div><div class="head-title">投票列表</div><div class="head-sub">VOTE LIST</div></div></div>
  ${rows}
  <div class="tip">发送「投票 名称」查看指定投票详情</div>`, { wrapClass: 'col' })
}

/** 投票详情卡片：单轮次计票（调用方截图选择器：.wrap） */
export function voteDetailCard(round: VoteRoundData): string {
  const open = round.status === 'open'
  const opts = round.options || []
  const totalScore = opts.reduce((s, o) => s + (Number(o.score) || 0), 0)
  const optionRows = opts.map(o => {
    const pct = totalScore > 0 ? Math.round((Number(o.score || 0) / totalScore) * 1000) / 10 : 0
    return `<div class="vo">
      <div class="vo-head">
        <span class="vo-text">${escapeHtml(o.text)}</span>
        ${optionTag(o)}
        <span class="vo-score">${Number(o.score || 0)} 分 · ${Number(o.votes || 0)} 票</span>
      </div>
      <div class="vo-bar"><div class="vo-fill" style="width:${pct}%"></div></div>
      <div class="vo-pct">${pct}%</div>
    </div>`
  }).join('\n')

  const badge = `<span class="vd-badge ${open ? 'open' : 'closed'}">${open ? '进行中' : '已结束'}</span>`
  const timeLine = open
    ? `<div class="vd-meta"><b>截止</b> ${escapeHtml(fmtDateTime(round.endAt))}</div>`
    : round.closedAt
      ? `<div class="vd-meta"><b>已结束于</b> ${escapeHtml(fmtDateTime(round.closedAt))}</div>`
      : '<div class="vd-meta"><b>已结束</b></div>'

  return frame(`
<div class="card">
  <div class="vd-top">${badge}<span class="foot-name">${escapeHtml(fmtDateTime(round.createdAt))} 发起</span></div>
  <div class="vd-title">${escapeHtml(round.title)}</div>
  ${round.description ? `<div class="vd-desc">${escapeHtml(round.description)}</div>` : ''}
  ${timeLine}
  <div class="vd-rules">每用户可投 ${Number(round.maxVotesPerUser ?? 1)} 票 · ${ruleLineOf(round)}</div>
  <div class="vd-options">${optionRows}</div>
</div>`)
}

/** 投票/提案后的个人状态卡片（调用方截图选择器：.wrap） */
export function voteStateCard(data: VoteStateData): string {
  const r = data.round || ({} as VoteStateData['round'])
  const my = r.my || {}
  const opts = r.options || []
  const totalScore = opts.reduce((s, o) => s + (Number(o.score) || 0), 0)
  const votedIds: string[] = my.votedOptions || []

  const identity = data.unbound
    ? `<span class="vs-ident">未绑定 QQ ${escapeHtml(data.qq)}</span><span class="vs-ident-tag unbound">未绑定 · 基础权重</span>`
    : `<span class="vs-ident">${escapeHtml(data.username)}</span><span class="vs-ident-tag">已绑定</span>`

  const banner = data.mode === 'proposed'
    ? `<div class="vs-banner">${data.existing ? '提案已存在，已为你定位' : '提案已提交'}：${escapeHtml(data.option?.text || '')}</div>`
    : `<div class="vs-banner">投票成功 · 本次权重 ${Number(data.weight ?? 0)} 分</div>`

  const optionRows = opts.map(o => {
    const voted = votedIds.includes(o.id)
    const pct = totalScore > 0 ? Math.round((Number(o.score || 0) / totalScore) * 1000) / 10 : 0
    return `<div class="vo ${voted ? 'voted' : ''}">
      <div class="vo-head">
        ${voted ? '<span class="vo-check">✓</span>' : ''}
        <span class="vo-text">${escapeHtml(o.text)}</span>
        ${optionTag(o)}
        <span class="vo-score">${Number(o.score || 0)} 分 · ${Number(o.votes || 0)} 票</span>
      </div>
      <div class="vo-bar"><div class="vo-fill" style="width:${pct}%"></div></div>
    </div>`
  }).join('\n')

  return frame(`
<div class="card">
  <div class="vs-top">
    <div class="vs-title">${escapeHtml(r.title || '')}</div>
    <div class="vs-ident-row">${identity}</div>
  </div>
  ${banner}
  <div class="vs-stats">
    <div class="vs-stat"><div class="vs-num">${Number(my.votesLeft ?? 0)}</div><div class="vs-label">还可投</div></div>
    <div class="vs-stat"><div class="vs-num">${(my.votedOptions || []).length}</div><div class="vs-label">已投</div></div>
    <div class="vs-stat"><div class="vs-num">${Number(my.weight ?? 0)}</div><div class="vs-label">权重 分/票</div></div>
    <div class="vs-stat"><div class="vs-num">${Number(my.proposalsLeft ?? 0)}</div><div class="vs-label">可提案</div></div>
  </div>
  <div class="vd-options">${optionRows}</div>
  <div class="vs-rules"><b>每用户可投 ${Number(r.maxVotesPerUser ?? 1)} 票</b> · ${ruleLineOf(r)}</div>
</div>`)
}

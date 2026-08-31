import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'
import { getPlaytimeRecords } from './qqPlaytimeService.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const VOTES_PATH = path.join(__dirname, '..', 'data', 'votes.json')

// ═══════════════════════════════════════════════════════════
// 投票服务（后端本地权威）
//   data/votes.json: { rounds: [...], votes: [...] }
//
// round: {
//   id, title, description(说明,玩家页标题下展示,可空), createdAt(开始时间), endAt(截止,可空),
//   closedAt(手动结束时间,可空), endedBy,
//   archived(是否归档:手动归档后不再出现在玩家页,可取消归档,默认false), archivedBy, archivedAt,
//   maxVotesPerUser(每用户可投选项数,默认1),
//   baseWeight(初始权重/每次投票分值基数,默认1,允许小数),
//   allowProposals(是否允许自主提案), maxProposalsPerUser(每用户最多提案数,默认1),
//   weightRules: [ { field:'playtime_hours', op:'>'|'>=', threshold: 50, weight: 0.5 } ] 多条可叠加,
//   options: [ { id, text, type:'preset'|'custom', proposer?, anonymous? } ]
// }
//
// 玩家页可见范围（活跃中）= 未归档轮次 = 进行中 + 已结束未归档；归档 = 手动操作，可逆。
// vote:   { roundId, username, qq, optionId, weight(投票时刻实际加分快照), at }
//
// 约束：
//   - (roundId, username, optionId) 唯一 → 同一选项不能重复投
//   - 已投不同选项数 ≤ maxVotesPerUser（投后锁定，不可改/撤）
//   - 提案数 ≤ maxProposalsPerUser；同轮同文本去重（CI）
//   - 权重 = baseWeight + Σ(满足的 weightRules.weight)（实时计算，投票时落快照）
//   - 状态推断：closedAt 非空 或 now > endAt → closed；否则 open（惰性判断，无需定时器）
//
// ⚠️ 实时读文件（不缓存）：与 qq_accounts.json 同策略，外部写入立即可见。
// ═══════════════════════════════════════════════════════════

async function load() {
  try {
    const content = await fs.readFile(VOTES_PATH, 'utf8')
    const data = JSON.parse(content)
    if (!data || typeof data !== 'object') return { rounds: [], votes: [] }
    if (!Array.isArray(data.rounds)) data.rounds = []
    if (!Array.isArray(data.votes)) data.votes = []
    // 旧数据兼容：无 archived 字段的轮次视为未归档；无 description 视为空说明
    for (const r of data.rounds) {
      if (r && r.archived === undefined) r.archived = false
      if (r && r.description === undefined) r.description = ''
    }
    return data
  } catch {
    return { rounds: [], votes: [] }
  }
}

async function persist(data) {
  // 防御：始终以 { rounds, votes } 外壳写盘
  const rounds = Array.isArray(data?.rounds) ? data.rounds : []
  const votes = Array.isArray(data?.votes) ? data.votes : []
  try {
    await fs.mkdir(path.dirname(VOTES_PATH), { recursive: true })
    await fs.writeFile(VOTES_PATH, JSON.stringify({ rounds, votes }, null, 2), 'utf8')
  } catch (err) {
    console.error('[投票] 保存失败:', err.message)
  }
}

function genId(prefix) {
  return prefix + '-' + crypto.randomBytes(4).toString('hex')
}

/** 轮次状态（惰性推断）：closedAt 非空或已过截止 → closed；否则 open */
export function roundStatus(round, now = Date.now()) {
  if (round.closedAt) return 'closed'
  if (round.endAt && now > new Date(round.endAt).getTime()) return 'closed'
  return 'open'
}

/** 玩家累计游玩小时数（大小写不敏感） */
async function playtimeHoursOf(username) {
  const records = await getPlaytimeRecords()
  for (const [name, rec] of Object.entries(records)) {
    if (String(name).toLowerCase() === String(username).toLowerCase()) {
      return Number(rec?.total || 0) / 60
    }
  }
  return 0
}

/**
 * 计算玩家在该轮的实际投票权重（服务端权威）
 * weight = baseWeight + Σ(满足的规则.weight)，当前条件字段仅 playtime_hours
 */
export async function calcUserWeight(round, username) {
  const baseWeight = Number(round.baseWeight ?? 1)
  if (!Array.isArray(round.weightRules) || round.weightRules.length === 0) return baseWeight
  const hours = await playtimeHoursOf(username)
  let weight = baseWeight
  for (const rule of round.weightRules) {
    if (!rule || rule.field !== 'playtime_hours') continue
    const threshold = Number(rule.threshold)
    const op = rule.op || '>'
    const matched = op === '>=' ? hours >= threshold : hours > threshold
    if (matched) weight += Number(rule.weight || 0)
  }
  return weight
}

/** 轮次计票：每个选项的票数（投票次数）与加权分（score） */
export function tally(round, votes) {
  const roundVotes = votes.filter(v => v.roundId === round.id)
  return round.options.map(o => {
    const vs = roundVotes.filter(v => v.optionId === o.id)
    const score = vs.reduce((s, v) => s + (Number(v.weight) || 0), 0)
    return {
      ...o,
      votes: vs.length,
      score: Math.round(score * 100) / 100
    }
  })
}

/** 玩家在轮次内的投票状态（已投选项、已投提案数、剩余可投） */
function myState(round, votes, username) {
  const mine = votes.filter(v => v.roundId === round.id && String(v.username).toLowerCase() === String(username).toLowerCase())
  const myProposals = round.options.filter(o => o.type === 'custom' && String(o.proposer || '').toLowerCase() === String(username).toLowerCase())
  return {
    votedOptions: mine.map(v => v.optionId),
    votesLeft: Math.max(0, (round.maxVotesPerUser ?? 1) - mine.length),
    myProposals: myProposals.length,
    proposalsLeft: Math.max(0, (round.maxProposalsPerUser ?? 1) - myProposals.length)
  }
}

// ═══════════════════════════════════════════════════════════
// 管理端
// ═══════════════════════════════════════════════════════════

/**
 * 创建轮次
 * @param {object} p { title, options: string[], maxVotesPerUser, baseWeight, allowProposals, maxProposalsPerUser, weightRules, endAt }
 */
export async function createRound(p) {
  const data = await load()
  const title = String(p.title || '').trim()
  if (!title) throw new Error('标题不能为空')

  const options = (Array.isArray(p.options) ? p.options : [])
    .map(t => String(t || '').trim())
    .filter(Boolean)
  if (options.length === 0) throw new Error('至少需要一个初始选项')

  const round = {
    id: genId('r'),
    title,
    description: String(p.description || '').trim(),
    createdAt: new Date().toISOString(),
    endAt: p.endAt ? new Date(p.endAt).toISOString() : null,
    closedAt: null,
    endedBy: null,
    archived: false,
    archivedBy: null,
    archivedAt: null,
    maxVotesPerUser: Math.max(1, parseInt(p.maxVotesPerUser) || 1),
    baseWeight: Number(p.baseWeight) || 1,
    allowProposals: !!p.allowProposals,
    maxProposalsPerUser: Math.max(1, parseInt(p.maxProposalsPerUser) || 1),
    weightRules: Array.isArray(p.weightRules) ? p.weightRules : [],
    options: options.map(text => ({ id: genId('o'), text, type: 'preset' }))
  }

  data.rounds.push(round)
  await persist(data)
  return round
}

/**
 * 编辑轮次基本信息（标题 / 说明 / 截止时间），未传字段保持不变
 * @param {object} patch { title?, description?, endAt? }
 */
export async function updateRound(id, patch = {}) {
  const data = await load()
  const round = data.rounds.find(r => r.id === id)
  if (!round) throw new Error('轮次不存在')
  if (patch.title !== undefined) {
    const t = String(patch.title).trim()
    if (!t) throw new Error('标题不能为空')
    round.title = t
  }
  if (patch.description !== undefined) {
    round.description = String(patch.description ?? '').trim()
  }
  if (patch.endAt !== undefined) {
    round.endAt = patch.endAt ? new Date(patch.endAt).toISOString() : null
  }
  await persist(data)
  return round
}

/** 手动结束轮次 */
export async function closeRound(id, endedBy = '') {
  const data = await load()
  const round = data.rounds.find(r => r.id === id)
  if (!round) throw new Error('轮次不存在')
  if (roundStatus(round)) { /* 已结束也允许重复设置，幂等 */ }
  round.closedAt = new Date().toISOString()
  round.endedBy = endedBy || round.endedBy || ''
  await persist(data)
  return round
}

/** 删除轮次（连带其投票记录） */
export async function deleteRound(id) {
  const data = await load()
  const idx = data.rounds.findIndex(r => r.id === id)
  if (idx === -1) return false
  data.rounds.splice(idx, 1)
  data.votes = data.votes.filter(v => v.roundId !== id)
  await persist(data)
  return true
}

/** 手动归档：从玩家页隐藏（可取消） */
export async function archiveRound(id, by = '') {
  const data = await load()
  const round = data.rounds.find(r => r.id === id)
  if (!round) throw new Error('轮次不存在')
  round.archived = true
  round.archivedBy = by || round.archivedBy || ''
  round.archivedAt = new Date().toISOString()
  await persist(data)
  return round
}

/** 取消归档：重新对玩家页可见 */
export async function unarchiveRound(id) {
  const data = await load()
  const round = data.rounds.find(r => r.id === id)
  if (!round) throw new Error('轮次不存在')
  round.archived = false
  round.archivedBy = null
  round.archivedAt = null
  await persist(data)
  return round
}

/**
 * 全部轮次（含计票与状态），可附带某玩家的 myState 与该轮权重
 * @param {object} o { includeClosed, excludeArchived(玩家页过滤归档), username }
 * 排序：进行中在前（endAt 升序，无截止最后），已结束在后（endAt 升序），同分按创建时间
 */
export async function listRounds({ includeClosed = true, excludeArchived = false, username = null } = {}) {
  const data = await load()
  const now = Date.now()
  const result = []
  for (const round of data.rounds) {
    const status = roundStatus(round, now)
    if (!includeClosed && status === 'closed') continue
    if (excludeArchived && round.archived) continue
    const item = {
      ...round,
      status,
      options: tally(round, data.votes)
    }
    if (username) {
      item.my = myState(round, data.votes, username)
      try {
        item.my.weight = await calcUserWeight(round, username)
      } catch {
        item.my.weight = Number(round.baseWeight ?? 1)
      }
      item.my.baseWeight = Number(round.baseWeight ?? 1)
      item.my.weightRules = Array.isArray(round.weightRules) ? round.weightRules : []
    }
    result.push(item)
  }
  return result.sort((a, b) => {
    if (a.status !== b.status) return a.status === 'open' ? -1 : 1
    const ea = a.endAt ? new Date(a.endAt).getTime() : Infinity
    const eb = b.endAt ? new Date(b.endAt).getTime() : Infinity
    if (ea !== eb) return ea - eb
    return (a.createdAt || '').localeCompare(b.createdAt || '')
  })
}

export async function getRound(id) {
  const data = await load()
  return data.rounds.find(r => r.id === id) || null
}

/**
 * 轮次管理端明细：每个选项附带投票人列表（管理员视角，匿名对管理员无效，全部显示）
 * 返回 { ...round, options: [ { ...o, votes, score, voters: [{ username, qq, weight, at }] } ] }
 *  - 投票明细 = 全部选项的 voters
 *  - 提案明细 = options 中 type==='custom' 的项（proposer 可见）
 */
export async function getRoundDetail(id) {
  const data = await load()
  const round = data.rounds.find(r => r.id === id)
  if (!round) return null
  const roundVotes = data.votes.filter(v => v.roundId === id)
  const options = round.options.map(o => {
    const vs = roundVotes.filter(v => v.optionId === o.id)
    return {
      ...o,
      votes: vs.length,
      score: Math.round(vs.reduce((s, v) => s + (Number(v.weight) || 0), 0) * 100) / 100,
      voters: vs
        .map(v => ({ username: v.username, qq: v.qq || '', weight: Number(v.weight) || 0, at: v.at }))
        .sort((a, b) => String(a.at || '').localeCompare(String(b.at || '')))
    }
  })
  return { ...round, options }
}

// ═══════════════════════════════════════════════════════════
// 玩家端
// ═══════════════════════════════════════════════════════════

/** 投一票（目标选项 + 玩家权重），返回 { vote, state, option } */
export async function castVote(round, username, qq, optionId) {
  if (roundStatus(round) !== 'open') throw new Error('该轮投票已结束')
  const option = round.options.find(o => o.id === optionId)
  if (!option) throw new Error('投票选项不存在')

  const data = await load()
  const votes = data.votes.filter(v => v.roundId === round.id)

  // 同一选项不能重复投
  const dup = votes.find(v => v.optionId === optionId && String(v.username).toLowerCase() === String(username).toLowerCase())
  if (dup) throw new Error('不能重复投同一个选项')

  // 已投不同选项数 ≤ maxVotesPerUser
  const votedDistinct = new Set(votes.filter(v => String(v.username).toLowerCase() === String(username).toLowerCase()).map(v => v.optionId))
  if (votedDistinct.size >= (round.maxVotesPerUser ?? 1)) {
    throw new Error(`每用户最多投 ${round.maxVotesPerUser} 个不同选项，已达上限`)
  }

  const weight = await calcUserWeight(round, username)
  const vote = {
    roundId: round.id,
    username,
    qq: String(qq || ''),
    optionId,
    weight,
    at: new Date().toISOString()
  }
  data.votes.push(vote)
  await persist(data)
  return { vote, state: myState(round, data.votes, username), option }
}

/**
 * 提交自定义提案（立即上架）
 * @returns { option } 新提案选项（若同轮同文本已存在则复用返回 existing: true）
 */
export async function propose(round, username, text, anonymous = false) {
  if (roundStatus(round) !== 'open') throw new Error('该轮投票已结束')
  if (!round.allowProposals) throw new Error('本轮不允许自主提案')

  const clean = String(text || '').trim()
  if (!clean) throw new Error('提案内容不能为空')
  if (clean.length > 50) throw new Error('提案内容过长（最多 50 字）')

  const data = await load()
  // ⚠️ 关键：操作 data.rounds 里的最新轮次对象，而不是传入的 round 快照。
  // controller 传入的是 getRound(id) 读出的独立对象；persist(data) 写的是 data 里的对象，
  // 直接改 round.options 会导致新提案写不进文件（历史教训：已复现并修复）。
  const target = data.rounds.find(r => r.id === round.id)
  if (!target) throw new Error('轮次不存在')

  // 同轮同文本去重（CI）：复用已有选项，不重复上架
  const lower = clean.toLowerCase()
  const existing = target.options.find(o => o.text.toLowerCase() === lower)
  if (existing) return { option: existing, existing: true }

  // 每用户提案数 ≤ maxProposalsPerUser
  const mineCount = target.options.filter(o => o.type === 'custom' && String(o.proposer || '').toLowerCase() === String(username).toLowerCase()).length
  if (mineCount >= (target.maxProposalsPerUser ?? 1)) {
    throw new Error(`每用户最多提案 ${target.maxProposalsPerUser} 个`)
  }

  // 选项总数上限（防刷）
  if (target.options.length >= 100) throw new Error('本轮选项已达上限（100）')

  const option = {
    id: genId('o'),
    text: clean,
    type: 'custom',
    proposer: username,
    anonymous: !!anonymous
  }
  target.options.push(option)
  await persist(data)
  return { option, existing: false }
}

export default { roundStatus, calcUserWeight, tally, createRound, updateRound, closeRound, deleteRound, archiveRound, unarchiveRound, listRounds, getRound, getRoundDetail, castVote, propose }

import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'
import { getPlaytimeRecords } from './qqPlaytimeService.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
// 可用环境变量覆盖数据路径（测试/迁移场景隔离，默认 backend/data/votes.json）
const VOTES_PATH = process.env.TSWeb_VOTES_PATH || path.join(__dirname, '..', 'data', 'votes.json')

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
      if (r && r.allowUnbound === undefined) r.allowUnbound = true   // 旧数据默认允许未绑定参与
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
    allowUnbound: p.allowUnbound !== false,   // 是否允许未绑定玩家（QQ机器人渠道）参与，默认允许
    options: options.map(text => ({ id: genId('o'), text, type: 'preset' }))
  }

  data.rounds.push(round)
  await persist(data)
  return round
}

/**
 * 可编辑的“投票规则”字段（仅进行中轮次允许改动；title/description/endAt 任何状态可改）
 */
const RULE_FIELDS = ['maxVotesPerUser', 'baseWeight', 'weightRules', 'allowProposals', 'maxProposalsPerUser', 'allowUnbound']

/** 加权规则入参校验（返回清洗后的规则数组） */
function sanitizeWeightRules(rules) {
  if (!Array.isArray(rules)) throw new Error('加权规则格式不正确')
  return rules.map(r => {
    if (!r || r.field !== 'playtime_hours') throw new Error('加权规则字段不支持')
    if (r.op !== '>' && r.op !== '>=') throw new Error('加权规则比较符仅支持 > 或 >=')
    const threshold = Number(r.threshold)
    const weight = Number(r.weight)
    if (!Number.isFinite(threshold) || !Number.isFinite(weight)) throw new Error('加权规则的阈值/权重必须是数字')
    return { field: r.field, op: r.op, threshold, weight }
  })
}

/**
 * 编辑轮次：发布后修改发起参数，未传字段保持不变
 *  - title / description / endAt：任意状态可改（纠错/改期）
 *  - 规则类字段（RULE_FIELDS）：仅“进行中”轮次可改；对已投出的票不追溯，只影响后续投票
 * @param {object} patch { title?, description?, endAt?, maxVotesPerUser?, baseWeight?, weightRules?, allowProposals?, maxProposalsPerUser?, allowUnbound? }
 */
export async function updateRound(id, patch = {}) {
  const data = await load()
  const round = data.rounds.find(r => r.id === id)
  if (!round) throw new Error('轮次不存在')

  // ── 基本信息：任何状态可改 ──
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

  // ── 规则类字段：仅进行中轮次可改（已结束 = 结果锁定，改规则无意义且令人困惑） ──
  const wantsRules = RULE_FIELDS.some(k => patch[k] !== undefined)
  if (wantsRules && roundStatus(round) !== 'open') {
    throw new Error('轮次已结束，投票参数已锁定，仅可修改标题/说明/截止时间')
  }

  if (patch.maxVotesPerUser !== undefined) {
    const n = parseInt(patch.maxVotesPerUser, 10)
    if (!Number.isFinite(n) || n < 1) throw new Error('每用户可投选项数必须 ≥ 1')
    // 一致性：不能低于“该轮任意身份已投的不同选项数”，否则产生“已投数 > 上限”的矛盾状态
    const byIdentity = new Map()
    for (const v of data.votes) {
      if (v.roundId !== round.id) continue
      const ident = String(v.qq || '').toLowerCase() || String(v.username || '').toLowerCase()
      if (!byIdentity.has(ident)) byIdentity.set(ident, new Set())
      byIdentity.get(ident).add(v.optionId)
    }
    const maxDistinct = [...byIdentity.values()].reduce((m, s) => Math.max(m, s.size), 0)
    if (n < maxDistinct) {
      throw new Error(`不能降到低于玩家已投数量：本轮有人已投 ${maxDistinct} 个不同选项（当前上限 ${round.maxVotesPerUser}）`)
    }
    round.maxVotesPerUser = n
  }
  if (patch.baseWeight !== undefined) {
    const b = Number(patch.baseWeight)
    if (!Number.isFinite(b) || b <= 0) throw new Error('初始权重必须大于 0')
    round.baseWeight = b   // 历史票已落快照，不追溯
  }
  if (patch.weightRules !== undefined) {
    round.weightRules = sanitizeWeightRules(patch.weightRules)  // 空数组 = 清除全部加权规则
  }
  if (patch.allowProposals !== undefined) {
    round.allowProposals = !!patch.allowProposals
  }
  if (patch.maxProposalsPerUser !== undefined) {
    const n = parseInt(patch.maxProposalsPerUser, 10)
    if (!Number.isFinite(n) || n < 1) throw new Error('每用户最多提案数必须 ≥ 1')
    round.maxProposalsPerUser = n   // 仅约束未来提案，已有提案不追溯
  }
  if (patch.allowUnbound !== undefined) {
    round.allowUnbound = !!patch.allowUnbound
  }

  await persist(data)
  return round
}

/**
 * 管理员添加选项（仅进行中轮次；与玩家提案同防刷上限，同文本去重拒绝）
 * @returns { option } 新选项（type='preset'，addedBy 记录操作人）
 */
export async function addOption(id, text, by = '') {
  const data = await load()
  const round = data.rounds.find(r => r.id === id)
  if (!round) throw new Error('轮次不存在')
  if (roundStatus(round) !== 'open') throw new Error('轮次已结束，选项已锁定，无法添加')

  const clean = String(text || '').trim()
  if (!clean) throw new Error('选项内容不能为空')
  if (clean.length > 50) throw new Error('选项内容过长（最多 50 字）')
  if (round.options.length >= 100) throw new Error('本轮选项已达上限（100）')

  const lower = clean.toLowerCase()
  const existing = round.options.find(o => o.text.toLowerCase() === lower)
  if (existing) throw new Error('该选项已存在，无需重复添加')

  const option = { id: genId('o'), text: clean, type: 'preset' }
  if (by) option.addedBy = by
  round.options.push(option)
  await persist(data)
  return option
}

/**
 * 管理员删除选项（仅进行中轮次；预设与玩家提案均可删）
 * 连带真实删除指向该选项的全部选票——不留悬空引用、不残留脏数据，计票/配额即时一致。
 * @returns { option, removedVotes } 被删选项 + 连带删除的票数
 */
export async function removeOption(id, optionId, by = '') {
  const data = await load()
  const round = data.rounds.find(r => r.id === id)
  if (!round) throw new Error('轮次不存在')
  if (roundStatus(round) !== 'open') throw new Error('轮次已结束，选项已锁定，无法删除')
  if (round.options.length <= 1) throw new Error('轮次至少需保留一个选项')

  const idx = round.options.findIndex(o => o.id === optionId)
  if (idx === -1) throw new Error('选项不存在')

  const [option] = round.options.splice(idx, 1)
  const before = data.votes.length
  data.votes = data.votes.filter(v => !(v.roundId === round.id && v.optionId === optionId))
  const removedVotes = before - data.votes.length
  await persist(data)
  return { option, removedVotes, removedBy: by || undefined }
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

// ═══════════════════════════════════════════════════════════
// QQ 机器人渠道（QQ 维度身份与防重）
//  绑定玩家：username=台账角色名；未绑定玩家：username=`qq:{qq}`
//  ⚠️ 配额与去重一律按 qq 维度（绑定前后共享），防止“未绑定投一次+绑定后再投一次”双投
// ═══════════════════════════════════════════════════════════

/** 某轮内该 QQ 的全部投票记录（含未绑定 qq:xxx 与绑定角色名两种身份） */
function votesByQq(votes, roundId, qq) {
  return votes.filter(v => v.roundId === roundId && String(v.qq) === String(qq))
}

/** QQ 维度个人状态（已投选项按 qq 汇总；提案归属 = username 或历史 qq:xxx 身份） */
export async function qqState(round, votes, qq, username) {
  const mine = votesByQq(votes, round.id, qq)
  const myProposals = round.options.filter(o => o.type === 'custom' && (
    String(o.proposer || '').toLowerCase() === String(username).toLowerCase() ||
    String(o.proposer || '') === 'qq:' + qq
  ))
  const weight = await calcUserWeight(round, username)
  return {
    votedOptions: mine.map(v => v.optionId),
    votesLeft: Math.max(0, (round.maxVotesPerUser ?? 1) - mine.length),
    myProposals: myProposals.length,
    proposalsLeft: Math.max(0, (round.maxProposalsPerUser ?? 1) - myProposals.length),
    weight,
    baseWeight: Number(round.baseWeight ?? 1),
    weightRules: Array.isArray(round.weightRules) ? round.weightRules : []
  }
}

/**
 * 机器人渠道投票：QQ 维度防重 + 防超限，按当前身份落库
 * @returns { vote, option }
 */
export async function castVoteForQq(round, qq, username, optionId) {
  if (roundStatus(round) !== 'open') throw new Error('该轮投票已结束')
  const option = round.options.find(o => o.id === optionId)
  if (!option) throw new Error('投票选项不存在')

  const data = await load()
  const mine = votesByQq(data.votes, round.id, qq)
  if (mine.some(v => v.optionId === optionId)) throw new Error('不能重复投同一个选项')
  if (mine.length >= (round.maxVotesPerUser ?? 1)) {
    throw new Error(`每用户最多投 ${round.maxVotesPerUser} 个不同选项，已达上限`)
  }

  const weight = await calcUserWeight(round, username)
  const vote = {
    roundId: round.id,
    username,
    qq: String(qq),
    optionId,
    weight,
    at: new Date().toISOString()
  }
  data.votes.push(vote)
  await persist(data)
  return { vote, option }
}

/** 轮次 + QQ 维度个人状态（供机器人渲染状态结果图） */
export async function roundWithQqState(roundId, qq, username) {
  const data = await load()
  const round = data.rounds.find(r => r.id === roundId)
  if (!round) return null
  const item = { ...round, status: roundStatus(round), options: tally(round, data.votes) }
  item.my = await qqState(round, data.votes, qq, username)
  return item
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

export default { roundStatus, calcUserWeight, tally, createRound, updateRound, addOption, removeOption, closeRound, deleteRound, archiveRound, unarchiveRound, listRounds, getRound, getRoundDetail, castVote, castVoteForQq, qqState, roundWithQqState, propose }

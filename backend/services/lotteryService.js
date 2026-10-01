import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
// 可用环境变量覆盖数据路径（测试/迁移场景隔离，默认 backend/data/lottery.json）
const LOTTERY_PATH = process.env.TSWeb_LOTTERY_PATH || path.join(__dirname, '..', 'data', 'lottery.json')

// ═══════════════════════════════════════════════════════════
// 抽奖服务（后端本地权威）
//   data/lottery.json: { config: {...}, records: [...] }
//
// record: {
//   id, at, guildId, operatorQq, scope{ all, serverId, serverName },
//   servers: [ { id, name, count, fetchedAt } | { id, name, count:0, error } ],
//   pool: [ 角色名, 升序 ], poolHash(sha256 of pool.join('\n')), count,
//   seed(64 位 hex), index,
//   winner: { username, nickname, group, serverId, serverName },
//   players: [ 同上结构, 升序，供渲染奖池名单 ], algo
// }
//
// 公平性（三条可独立复算）：
//   1. sha256(pool.join('\n')) === poolHash       奖池未被事后篡改
//   2. int(seed[0:16] 大端) mod count === index   索引确实由该种子算出
//   3. pool[index] === winner.username            中奖者确实是该位置上的人
//   seed 为 crypto.randomBytes(32)，不用 Math.random（后者可预测、可反推）
//
// 约束：
//   - 奖池由调用方现场查询游戏服得到（/v2/server/status?players=true），不是定时快照
//   - 同一角色名在多服同时在线只算一份（按角色名大小写不敏感去重）
//   - 排序固定为 UTF-16 码元序（不用 localeCompare：后者依赖 ICU 区域设置，跨环境不可复算）
//   - records 只追加、不修改；本版无作废/重抽功能
//   - 冷却按 guildId 派生自最近一条同群记录，不额外存状态
//
// 注意：实时读文件（不缓存）：与 votes.json / qq_accounts.json 同策略，外部写入立即可见。
// ═══════════════════════════════════════════════════════════

/** 默认配置（data/lottery.json 的 config 字段可覆盖） */
const DEFAULT_CONFIG = {
  cooldownSec: 30,     // 同群两次抽奖的最小间隔（秒）
  minPlayers: 1,       // 开奖所需最少在线人数
  historyLimit: 10,    // 「抽奖 记录」默认返回条数
  prizeText: ''        // 奖品说明（可选，仅用于卡片展示）
}

async function load() {
  try {
    const content = await fs.readFile(LOTTERY_PATH, 'utf8')
    const data = JSON.parse(content)
    if (!data || typeof data !== 'object') return { config: {}, records: [] }
    if (!Array.isArray(data.records)) data.records = []
    if (!data.config || typeof data.config !== 'object') data.config = {}
    return data
  } catch {
    return { config: {}, records: [] }
  }
}

async function persist(data) {
  // 防御：始终以 { config, records } 外壳写盘
  const records = Array.isArray(data?.records) ? data.records : []
  const config = (data?.config && typeof data.config === 'object') ? data.config : {}
  try {
    await fs.mkdir(path.dirname(LOTTERY_PATH), { recursive: true })
    await fs.writeFile(LOTTERY_PATH, JSON.stringify({ config, records }, null, 2), 'utf8')
  } catch (err) {
    console.error('[抽奖] 保存失败:', err.message)
    throw err   // 与 votes 不同：抽奖台账写盘失败必须让整次开奖失败，不能产生“有图无账”
  }
}

function genId(prefix) {
  return prefix + '-' + crypto.randomBytes(4).toString('hex')
}

/** 合并默认配置与文件配置 */
export async function getConfig() {
  const data = await load()
  return { ...DEFAULT_CONFIG, ...(data.config || {}) }
}

/** 奖池指纹：对升序角色名按换行拼接后取 sha256 */
function poolHashOf(pool) {
  return crypto.createHash('sha256').update(pool.join('\n'), 'utf8').digest('hex')
}

/**
 * 同群冷却剩余秒数（0 = 可开奖）。
 * 直接派生自最近一条同群记录，不额外维护状态，重启后依然有效。
 */
export async function cooldownRemaining(guildId) {
  const data = await load()
  const cfg = { ...DEFAULT_CONFIG, ...(data.config || {}) }
  const key = String(guildId || '')
  let last = null
  for (let i = data.records.length - 1; i >= 0; i--) {
    if (String(data.records[i]?.guildId || '') === key) { last = data.records[i]; break }
  }
  if (!last || !last.at) return 0
  const elapsed = (Date.now() - new Date(last.at).getTime()) / 1000
  if (!Number.isFinite(elapsed)) return 0
  return Math.max(0, Math.ceil(Number(cfg.cooldownSec) - elapsed))
}

/**
 * 落一次开奖。
 * @param {object} p
 *   players   [{ username, nickname, group, serverId, serverName }] 现场查到的在线玩家（未排序、未去重）
 *   servers   [{ id, name, count, fetchedAt } | { id, name, count:0, error }] 参与查询的服务器及结果
 *   scope     { all, serverId, serverName }
 *   guildId   群号（冷却分桶键，可为空）
 *   operatorQq 开奖人 QQ（仅记录）
 * @returns { record, config }
 */
export async function saveDraw({ players = [], servers = [], scope = {}, guildId = '', operatorQq = '' } = {}) {
  const data = await load()
  const config = { ...DEFAULT_CONFIG, ...(data.config || {}) }

  // 去重：同一角色名（大小写不敏感）在多服同时在线只算一份
  const seen = new Set()
  const unique = []
  for (const p of players) {
    const name = String(p?.username || '').trim()
    if (!name) continue
    const key = name.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)
    unique.push({ ...p, username: name })
  }

  if (unique.length === 0) {
    throw Object.assign(new Error('当前没有玩家在线，无法抽取'), { status: 400 })
  }
  const minPlayers = Math.max(1, Number(config.minPlayers) || 1)
  if (unique.length < minPlayers) {
    throw Object.assign(new Error(`在线玩家不足 ${minPlayers} 人（当前 ${unique.length} 人），无法抽取`), { status: 400 })
  }

  // 固定排序（UTF-16 码元序）：poolHash 依赖顺序，排序规则一旦变更必须递增 algo
  const sorted = [...unique].sort((a, b) => (a.username < b.username ? -1 : a.username > b.username ? 1 : 0))
  const pool = sorted.map(p => p.username)

  const seed = crypto.randomBytes(32).toString('hex')
  const n = pool.length
  const index = Number(BigInt('0x' + seed.slice(0, 16)) % BigInt(n))
  const winner = sorted[index]

  const record = {
    id: genId('l'),
    at: new Date().toISOString(),
    guildId: String(guildId || ''),
    operatorQq: String(operatorQq || ''),
    scope: {
      all: scope.all !== false,
      serverId: scope.serverId || null,
      serverName: scope.serverName || null
    },
    servers,
    pool,
    poolHash: poolHashOf(pool),
    count: n,
    seed,
    index,
    winner,
    players: sorted,
    algo: 'seedmod1'
  }

  data.records.push(record)
  await persist(data)
  return { record, config }
}

/** 最近 N 条开奖记录（新的在前） */
export async function listRecords(limit) {
  const data = await load()
  const cfg = { ...DEFAULT_CONFIG, ...(data.config || {}) }
  const n = Math.max(1, Math.min(100, parseInt(limit, 10) || Number(cfg.historyLimit) || 10))
  return data.records.slice(-n).reverse()
}

/** 最近一次开奖记录 */
export async function latestRecord() {
  const data = await load()
  return data.records.length ? data.records[data.records.length - 1] : null
}

export default {
  getConfig,
  cooldownRemaining,
  saveDraw,
  listRecords,
  latestRecord
}

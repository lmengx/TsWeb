import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
// 可用环境变量覆盖数据路径（测试/迁移场景隔离，默认 backend/data/lottery.json）
const LOTTERY_PATH = process.env.TSWeb_LOTTERY_PATH || path.join(__dirname, '..', 'data', 'lottery.json')

// ═══════════════════════════════════════════════════════════
// 抽奖服务（后端本地权威）
//
// 职责只有两件：从给定奖池里抽一个人、把这次开奖追加进台账。
// 奖池怎么来由调用方决定（见 botController.lotteryDraw，逐服现场查询）。
//
// 本模块刻意不提供任何查询/列表接口：抽奖是管理员的一次性动作，
// 「在线抽奖」只回一张结果图，没有历史命令。台账仅作事后追查用，
// 直接读 data/lottery.json 即可，不出现在机器人侧。
//
// record: {
//   id, at, guildId, operatorQq,
//   servers: [ { id, name, count, fetchedAt } | { id, name, count:0, error } ],
//   pool: [角色名, 升序], poolHash(sha256 of pool.join('\n')), count,
//   seed(64 位 hex), index, winner: { username, nickname, group, serverId, serverName }
// }
//
// 公平性（三条可独立复算，台账里字段齐全，出图不展示）：
//   1. sha256(pool.join('\n')) === poolHash       奖池未被事后篡改
//   2. int(seed[0:16] 大端) mod count === index   索引确实由该种子算出
//   3. pool[index] === winner.username            中奖者确实是该位置上的人
//   seed 为 crypto.randomBytes(32)，不用 Math.random（后者可预测、可反推）
//
// 约束：
//   - 同一角色名在多服同时在线只算一份（按角色名大小写不敏感去重）
//   - 排序固定为 UTF-16 码元序（不用 localeCompare：后者依赖 ICU 区域设置，跨环境不可复算）
//   - 台账只追加、不修改
//
// 注意：实时读文件（不缓存），与 votes.json / qq_accounts.json 同策略。
// ═══════════════════════════════════════════════════════════

async function load() {
  try {
    const content = await fs.readFile(LOTTERY_PATH, 'utf8')
    const data = JSON.parse(content)
    if (!data || typeof data !== 'object') return { records: [] }
    if (!Array.isArray(data.records)) data.records = []
    return data
  } catch {
    return { records: [] }
  }
}

async function persist(records) {
  try {
    await fs.mkdir(path.dirname(LOTTERY_PATH), { recursive: true })
    await fs.writeFile(LOTTERY_PATH, JSON.stringify({ records }, null, 2), 'utf8')
  } catch (err) {
    console.error('[抽奖] 保存台账失败:', err.message)
    // 与 votes 不同：台账写盘失败必须让整次开奖失败，
    // 否则会出现「群里出了中奖图，台账里查不到」——事后无法追查
    throw err
  }
}

function genId() {
  return 'l-' + crypto.randomBytes(4).toString('hex')
}

/** 奖池指纹：对升序角色名按换行拼接后取 sha256 */
function poolHashOf(pool) {
  return crypto.createHash('sha256').update(pool.join('\n'), 'utf8').digest('hex')
}

/**
 * 落一次开奖。
 * @param {object} p
 *   players    [{ username, nickname, group, serverId, serverName }] 现场查到的在线玩家（未排序、未去重）
 *   servers    [{ id, name, count, fetchedAt } | { id, name, count:0, error }] 参与查询的服务器及结果
 *   guildId    群号（仅记入台账）
 *   operatorQq 开奖人 QQ（仅记入台账）
 * @returns { record }
 */
export async function saveDraw({ players = [], servers = [], guildId = '', operatorQq = '' } = {}) {
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

  // 固定排序（UTF-16 码元序）：poolHash 依赖顺序，排序规则一旦变更必须递增 algo
  const sorted = [...unique].sort((a, b) => (a.username < b.username ? -1 : a.username > b.username ? 1 : 0))
  const pool = sorted.map(p => p.username)

  const seed = crypto.randomBytes(32).toString('hex')
  const index = Number(BigInt('0x' + seed.slice(0, 16)) % BigInt(pool.length))

  const record = {
    id: genId(),
    at: new Date().toISOString(),
    guildId: String(guildId || ''),
    operatorQq: String(operatorQq || ''),
    servers,
    pool,
    poolHash: poolHashOf(pool),
    count: pool.length,
    seed,
    index,
    winner: sorted[index]
  }

  const data = await load()
  data.records.push(record)
  await persist(data.records)
  return record
}

export default { saveDraw }

import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

/**
 * QQ 昵称缓存存储路径。
 * 允许用环境变量覆盖（自测用临时文件，避免污染真实 data/）。
 */
function storePath() {
  return process.env.TSWEB_QQ_NICKNAMES_PATH
    || path.join(__dirname, '..', 'data', 'qq_nicknames.json')
}

// ═══════════════════════════════════════════════════════════
// QQ 昵称缓存（后端）
//   records: { QQ号: { nickname, updatedAt } }
//
// 为什么不写进 qq_accounts.json 台账：
//   1. upsertAccount / renameAccount 都会**重建整条记录**（只搬 qq/passwordHash/updatedAt），
//      塞进去的字段下一次注册/改密/改名就被抹掉，且症状是"昵称莫名其妙没了"。
//   2. 台账会经 buildFullPayload 原样推给各游戏服；昵称是 QQ 侧的展示信息，
//      没有理由混进账号同步负载（徒增一份要跟着改的 C# 端解析）。
//   3. 昵称的归属是 QQ 号（QQ:账号 = 1:1），按 QQ 建键天然对得上改绑/改名，
//      不会出现"改绑后昵称串到别人身上"。
//
// 定位：**缓存**，非权威数据。玩家人数、QQ 号、绑定关系一律以 qq_accounts.json 为准。
// 因此读盘失败/文件损坏时降级为空缓存并告警，绝不因此让 QQ 页面整页加载失败。
//
// 实时读文件（不缓存）：与 qq_accounts.json 同策略，外部写入立即可见，文件规模极小。
// ═══════════════════════════════════════════════════════════

/** 昵称最大长度（QQ 昵称可能很长，截断避免撑爆界面与文件） */
const NICKNAME_MAX = 64

/** QQ 号格式（与 qqAccountService/botController 的校验保持一致） */
const QQ_PATTERN = /^\d{5,15}$/

/**
 * 清理昵称：去首尾空白、剔除控制字符与换行（防止日志/表格被撑坏）。
 * 注意不能用多元素字符数组字面量以外的花活，保持直白。
 */
function cleanNickname(input) {
  let s = String(input == null ? '' : input)
  s = s.replace(/[\u0000-\u001f\u007f]/g, ' ')
  s = s.trim()
  if (s.length > NICKNAME_MAX) s = s.slice(0, NICKNAME_MAX)
  return s
}

function isValidQq(qq) {
  return QQ_PATTERN.test(String(qq == null ? '' : qq).trim())
}

async function load() {
  try {
    const content = await fs.readFile(storePath(), 'utf8')
    const data = JSON.parse(content)
    if (!data || typeof data !== 'object') return { schema: 1, records: {} }
    if (!data.records || typeof data.records !== 'object') data.records = {}
    return data
  } catch (err) {
    if (err && err.code === 'ENOENT') return { schema: 1, records: {} }
    // 缓存损坏只降级、不抛出：昵称为附加展示信息，不该拖垮 QQ 页面
    console.warn(`[QQ昵称] 缓存读取失败（已按空缓存处理）: ${err.message}`)
    return { schema: 1, records: {} }
  }
}

/** 原子落盘（.tmp + rename），避免写到一半留下坏文件 */
async function persist(data) {
  const file = storePath()
  const tmp = `${file}.tmp`
  data.schema = 1
  data.updatedAt = new Date().toISOString()
  try {
    await fs.mkdir(path.dirname(file), { recursive: true })
    await fs.writeFile(tmp, JSON.stringify(data, null, 2), 'utf8')
    await fs.rename(tmp, file)
  } catch (err) {
    console.error('[QQ昵称] 保存失败:', err.message)
    try { await fs.unlink(tmp) } catch { /* 清理失败无所谓 */ }
  }
}

/** 全部昵称映射 { QQ号: 昵称 } */
export async function getNicknameMap() {
  const data = await load()
  const map = {}
  for (const [qq, rec] of Object.entries(data.records)) {
    const nick = cleanNickname(rec && rec.nickname)
    if (nick) map[String(qq)] = nick
  }
  return map
}

/** 单个 QQ 的昵称（无则空串） */
export async function getNickname(qq) {
  if (!isValidQq(qq)) return ''
  const data = await load()
  const rec = data.records[String(qq).trim()]
  return cleanNickname(rec && rec.nickname)
}

/** QQ → 昵称更新时间（无则空串） */
export async function getNicknameUpdatedAt(qq) {
  if (!isValidQq(qq)) return ''
  const data = await load()
  const rec = data.records[String(qq).trim()]
  return (rec && rec.updatedAt) ? String(rec.updatedAt) : ''
}

/**
 * 批量写入昵称（机器人上报入口）。
 *
 * @param {Array<{qq:string, nickname?:string, error?:string}>} entries
 * @param {{source?:string}} [opts]
 * @returns {Promise<{stored:number, skipped:number, failed:Array<{qq:string,error:string}>}>}
 *   stored  实际写入条数
 *   skipped 机器人明确报错或昵称为空的条数（不写入，避免用空值覆盖已有昵称）
 *   failed  逐条失败原因（回给页面，让"哪些没取到"可见）
 */
export async function saveNicknames(entries, opts = {}) {
  const list = Array.isArray(entries) ? entries : []
  const failed = []
  const toWrite = []

  for (const item of list) {
    const qq = String((item && item.qq) == null ? '' : item.qq).trim()
    if (!isValidQq(qq)) {
      failed.push({ qq, error: 'QQ 号格式不正确' })
      continue
    }
    if (item && item.error) {
      failed.push({ qq, error: String(item.error).slice(0, 200) })
      continue
    }
    const nick = cleanNickname(item && item.nickname)
    if (!nick) {
      failed.push({ qq, error: '未取到昵称' })
      continue
    }
    toWrite.push({ qq, nickname: nick })
  }

  if (toWrite.length === 0) {
    return { stored: 0, skipped: failed.length, failed }
  }

  const data = await load()
  const now = new Date().toISOString()
  for (const it of toWrite) {
    data.records[it.qq] = { nickname: it.nickname, updatedAt: now, source: String(opts.source || 'bot') }
  }
  await persist(data)
  return { stored: toWrite.length, skipped: failed.length, failed }
}

/** 移除某 QQ 的昵称缓存（解绑/改绑时清，避免残留旧昵称） */
export async function removeNickname(qq) {
  if (!isValidQq(qq)) return false
  const data = await load()
  const key = String(qq).trim()
  if (!data.records[key]) return false
  delete data.records[key]
  await persist(data)
  return true
}

// ═══════════════════════════════════════════════════════════
// 昵称刷新任务（页面「获取昵称」→ 机器人轮询领取 → 回报）
//
// 为什么需要任务态：后端**没有任何到机器人的出站通道**（机器人只以 bot token
// 调后端，反过来不可达），所以"页面点一下主动拉取"只能做成
// 「后端登记任务 → 机器人定时轮询领取 → 拉完回报」。
//
// 内存态、无定时器：进程重启任务即丢（页面再点一次即可），
// 过期一律**惰性判定**（每次读取时按时间戳判断）——避免为一次性任务常驻 timer，
// 契合本项目"定时器/监听器泄漏"的历史教训。
// ═══════════════════════════════════════════════════════════

/** 任务登记后，机器人多久没来领取就算超时（毫秒） */
const PENDING_TIMEOUT_MS = 120 * 1000
/** 机器人领取后，多久没回报结果就算超时（毫秒） */
const RUNNING_TIMEOUT_MS = 300 * 1000

/** 当前任务（同一时刻只允许一个，避免连点造成多份重复拉取） */
let task = null

/** 惰性过期：按时间戳把 pending/claimed 推进到 timeout */
function touch() {
  if (!task) return null
  const now = Date.now()
  if (task.state === 'pending' && now - task.requestedAt > PENDING_TIMEOUT_MS) {
    task.state = 'timeout'
    task.completedAt = now
    task.error = '机器人未在 120 秒内响应：请确认机器人已启用「昵称刷新」并正在运行'
  } else if (task.state === 'claimed' && now - task.claimedAt > RUNNING_TIMEOUT_MS) {
    task.state = 'timeout'
    task.completedAt = now
    task.error = '机器人已开始获取昵称，但 300 秒内未回报结果'
  }
  return task
}

/** 任务对外视图（不含 qqs 明细，那是给机器人取的） */
function taskView(t) {
  if (!t) return null
  return {
    id: t.id,
    state: t.state,
    requestedAt: t.requestedAt,
    requestedBy: t.requestedBy || '',
    claimedAt: t.claimedAt || null,
    completedAt: t.completedAt || null,
    total: t.total,
    stored: t.stored,
    failedCount: Array.isArray(t.failed) ? t.failed.length : 0,
    failed: Array.isArray(t.failed) ? t.failed.slice(0, 50) : [],
    error: t.error || ''
  }
}

/**
 * 登记一次刷新（页面按钮）。
 * 已有进行中的任务则原样返回，不重复登记（连点等价于同一件事）。
 * @param {{actor?:string, qqs:string[]}} p
 */
export function requestNicknameTask({ actor = '', qqs = [] } = {}) {
  const current = touch()
  if (current && (current.state === 'pending' || current.state === 'claimed')) {
    return taskView(current)
  }
  const unique = []
  const seen = new Set()
  for (const raw of Array.isArray(qqs) ? qqs : []) {
    const qq = String(raw == null ? '' : raw).trim()
    if (!isValidQq(qq) || seen.has(qq)) continue
    seen.add(qq)
    unique.push(qq)
  }
  task = {
    id: crypto.randomUUID(),
    state: 'pending',
    requestedAt: Date.now(),
    requestedBy: String(actor || ''),
    claimedAt: null,
    completedAt: null,
    total: unique.length,
    stored: 0,
    failed: [],
    error: '',
    qqs: unique
  }
  return taskView(task)
}

/**
 * 机器人领取任务。
 * @returns {{id:string, qqs:string[], total:number}|null} 无待领取任务时返回 null
 */
export function claimNicknameTask() {
  const t = touch()
  if (!t || t.state !== 'pending') return null
  if (t.qqs.length === 0) {
    // 没有任何已绑定 QQ：直接判定完成，别让机器人做空转，也别让页面一直转圈
    t.state = 'done'
    t.completedAt = Date.now()
    t.stored = 0
    t.error = '当前没有任何已绑定的 QQ 号，无需获取'
    return { id: t.id, qqs: [], total: 0 }
  }
  t.state = 'claimed'
  t.claimedAt = Date.now()
  return { id: t.id, qqs: t.qqs.slice(), total: t.qqs.length }
}

/** 机器人回报结果 */
export function finishNicknameTask(id, { stored = 0, failed = [] } = {}) {
  const t = touch()
  if (!t || t.id !== String(id)) return null
  t.state = 'done'
  t.completedAt = Date.now()
  t.stored = Number(stored) || 0
  t.failed = Array.isArray(failed) ? failed.slice(0, 200) : []
  return taskView(t)
}

/** 当前任务状态（管理页轮询用） */
export function getNicknameTask() {
  return taskView(touch())
}

/** 仅供自测：清空任务态 */
export function _resetNicknameTaskForTest() {
  task = null
}

export default {
  getNicknameMap,
  getNickname,
  getNicknameUpdatedAt,
  saveNicknames,
  removeNickname,
  requestNicknameTask,
  claimNicknameTask,
  finishNicknameTask,
  getNicknameTask
}

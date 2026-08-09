import fs from 'fs'
import fsp from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'
import { getEventMeta, SENSITIVE_KEYS } from './auditEvents.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const LOG_DIR = path.join(__dirname, '..', 'data', 'logs')

// 单文件超过该大小后滚动（audit-YYYY-MM-DD.1.jsonl）
const MAX_FILE_SIZE = 50 * 1024 * 1024

// ═══════════════════════════════════════════════════════════
// 内部状态
// ═══════════════════════════════════════════════════════════
let writeQueue = []          // 内存写入队列
let flushTimer = null
let currentFile = null       // 当前文件名
let currentSize = 0
let rolloverIndex = 0
let initialized = false

// ═══════════════════════════════════════════════════════════
// 初始化 / 文件管理
// ═══════════════════════════════════════════════════════════

function getToday() {
  const d = new Date()
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${mm}-${dd}`
}

function resolveCurrentFile() {
  const today = getToday()
  // 日期变化 → 重建文件名
  if (!currentFile || !currentFile.startsWith(`audit-${today}`)) {
    currentFile = `audit-${today}.jsonl`
    rolloverIndex = 0
    currentSize = 0
  }
  return path.join(LOG_DIR, currentFile)
}

async function ensureReady() {
  if (initialized) return
  initialized = true
  await fsp.mkdir(LOG_DIR, { recursive: true })
  // 探测今日已有文件大小（进程重启后继续追加）
  try {
    const file = path.join(LOG_DIR, `audit-${getToday()}.jsonl`)
    const stat = await fsp.stat(file)
    currentFile = `audit-${getToday()}.jsonl`
    currentSize = stat.size
    // 若已超大小，从 .1 开始找可用序号
    if (currentSize >= MAX_FILE_SIZE) {
      let i = 1
      while (true) {
        const f = path.join(LOG_DIR, `audit-${getToday()}.${i}.jsonl`)
        try {
          const s = await fsp.stat(f)
          if (s.size < MAX_FILE_SIZE) {
            currentFile = `audit-${getToday()}.${i}.jsonl`
            currentSize = s.size
            rolloverIndex = i
            break
          }
          i++
        } catch {
          currentFile = `audit-${getToday()}.${i}.jsonl`
          currentSize = 0
          rolloverIndex = i
          break
        }
      }
    }
  } catch {
    // 今日文件不存在 → 默认新文件
  }
}

// ═══════════════════════════════════════════════════════════
// 脱敏
// ═══════════════════════════════════════════════════════════

function maskSensitive(obj) {
  const out = {}
  for (const [k, v] of Object.entries(obj || {})) {
    const lower = k.toLowerCase()
    if (SENSITIVE_KEYS.some(sk => lower.includes(sk))) {
      out[k] = '***'
    } else if (v && typeof v === 'object') {
      out[k] = maskSensitive(v)
    } else {
      out[k] = v
    }
  }
  return out
}

// ═══════════════════════════════════════════════════════════
// 核心：record
// ═══════════════════════════════════════════════════════════

/**
 * 记录一条审计事件
 * @param {string} event 注册表中的事件名
 * @param {object} ctx  上下文（含白名单字段 + actor + serverId + ip + detail 等）
 * 未在注册表中的 event 会抛错，防止手滑/遗漏定义
 */
export function record(event, ctx = {}) {
  const meta = getEventMeta(event)
  if (!meta) {
    throw new Error(`未注册的审计事件: ${event}（请在 auditEvents.js 中定义）`)
  }

  const entry = {
    id: crypto.randomUUID(),
    ts: new Date().toISOString(),
    level: meta.level,
    event,
    category: meta.category,
    title: meta.title,
    actor: ctx.actor || 'system',
    ok: ctx.ok !== false
  }

  if (ctx.serverId) entry.serverId = ctx.serverId
  if (ctx.target) entry.target = ctx.target
  if (meta.ip && ctx.ip) entry.ip = ctx.ip

  // 按白名单提取字段 → detail
  const detail = {}
  for (const f of meta.fields) {
    if (ctx[f] !== undefined && ctx[f] !== null) {
      detail[f] = ctx[f]
    }
  }
  // 额外结构化细节（已脱敏）
  if (ctx.detail && typeof ctx.detail === 'object') {
    Object.assign(detail, maskSensitive(ctx.detail))
  }
  if (Object.keys(detail).length > 0) entry.detail = maskSensitive(detail)

  enqueue(entry)
  return entry
}

// 便捷方法
export const info = (event, ctx) => record(event, { ...ctx, __forceLevel: 'info' })
export const warn = (event, ctx) => record(event, { ...ctx, __forceLevel: 'warn' })
export const error = (event, ctx) => record(event, { ...ctx, __forceLevel: 'error' })

// ═══════════════════════════════════════════════════════════
// 写入队列（节流合并，避免高频操作卡 IO）
// ═══════════════════════════════════════════════════════════

function enqueue(entry) {
  writeQueue.push(JSON.stringify(entry))
  if (!flushTimer) {
    // 每 500ms 批量落盘一次；队列超 200 条立即冲刷
    flushTimer = setTimeout(() => { flush().catch(() => {}) }, 500)
  }
  if (writeQueue.length >= 200) {
    flush().catch(() => {})
  }
}

export async function flush() {
  if (flushTimer) {
    clearTimeout(flushTimer)
    flushTimer = null
  }
  if (writeQueue.length === 0) return
  const batch = writeQueue
  writeQueue = []

  try {
    await ensureReady()
    const file = resolveCurrentFile()
    const fullPath = path.join(LOG_DIR, file)

    // 超大小 → 滚动
    if (currentSize + batch.join('\n').length + batch.length >= MAX_FILE_SIZE) {
      rolloverIndex += 1
      currentFile = `audit-${getToday()}.${rolloverIndex}.jsonl`
      currentSize = 0
    }

    const data = batch.join('\n') + '\n'
    await fsp.appendFile(path.join(LOG_DIR, currentFile), data, 'utf8')
    currentSize += Buffer.byteLength(data)
  } catch (err) {
    // 审计日志写入失败不吞掉，回退到 console（保证至少可见）
    console.error('[Audit] 审计日志写入失败:', err.message)
    writeQueue = [...batch, ...writeQueue]
  }
}

// ═══════════════════════════════════════════════════════════
// 进程退出冲刷（审计日志不允许丢失）
// ═══════════════════════════════════════════════════════════

function shutdownFlush() {
  // 同步冲刷：直接追加写，确保退出前落盘
  try {
    const dirExists = fs.existsSync(LOG_DIR)
    if (!dirExists) fs.mkdirSync(LOG_DIR, { recursive: true })
    if (writeQueue.length === 0) return
    const batch = writeQueue
    writeQueue = []
    const file = resolveCurrentFile()
    fs.appendFileSync(path.join(LOG_DIR, file), batch.join('\n') + '\n', 'utf8')
  } catch (err) {
    console.error('[Audit] 退出冲刷失败:', err.message)
  }
}

export function registerShutdownHook() {
  process.on('exit', shutdownFlush)
  process.on('SIGINT', () => { shutdownFlush(); process.exit(0) })
  process.on('SIGTERM', () => { shutdownFlush(); process.exit(0) })
}

export default { record, info, warn, error, flush, registerShutdownHook }

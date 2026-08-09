import fs from 'fs/promises'
import path from 'path'
import { fileURLToPath } from 'url'
import { listEvents } from '../services/auditEvents.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const LOG_DIR = path.join(__dirname, '..', 'data', 'logs')

/**
 * 读取全部审计日志文件（按日期 + 滚动序号排序）
 * 审计日志只读、永久保留，不提供任何删除接口（物理删除需直接操作服务器文件）
 */
async function readAllLines() {
  let files
  try {
    files = await fs.readdir(LOG_DIR)
  } catch {
    return []
  }
  const jsonlFiles = files.filter(f => /^audit-\d{4}-\d{2}-\d{2}(\.\d+)?\.jsonl$/.test(f))
  // 排序：日期升序，滚动序号升序
  jsonlFiles.sort((a, b) => {
    const [dateA, idxA = '0'] = a.replace(/^audit-|\.jsonl$/g, '').split('.')
    const [dateB, idxB = '0'] = b.replace(/^audit-|\.jsonl$/g, '').split('.')
    if (dateA !== dateB) return dateA.localeCompare(dateB)
    return parseInt(idxA) - parseInt(idxB)
  })

  const entries = []
  for (const file of jsonlFiles) {
    try {
      const content = await fs.readFile(path.join(LOG_DIR, file), 'utf8')
      for (const line of content.split('\n')) {
        if (!line.trim()) continue
        try {
          entries.push(JSON.parse(line))
        } catch { /* 单行损坏跳过 */ }
      }
    } catch { /* 文件读取失败跳过 */ }
  }
  return entries
}

/** 过滤 + 分页查询 */
export const getLogs = async (req, res) => {
  try {
    const {
      level, event, category, actor, serverId, target,
      timeFrom, timeTo, q,
      page = '1', pageSize = '50'
    } = req.query

    const levels = level ? String(level).split(',').map(s => s.trim()).filter(Boolean) : []
    const events = event ? String(event).split(',').map(s => s.trim()).filter(Boolean) : []
    const categories = category ? String(category).split(',').map(s => s.trim()).filter(Boolean) : []

    const tFrom = timeFrom ? new Date(timeFrom).getTime() : null
    const tTo = timeTo ? new Date(timeTo).getTime() : null
    const pageNum = Math.max(1, parseInt(page) || 1)
    const size = Math.min(500, Math.max(1, parseInt(pageSize) || 50))

    const all = await readAllLines()
    const filtered = all.filter(e => {
      if (levels.length && !levels.includes(e.level)) return false
      if (events.length && !events.includes(e.event)) return false
      if (categories.length && !categories.includes(e.category)) return false
      if (actor && e.actor !== actor) return false
      if (serverId && e.serverId !== serverId) return false
      if (target && e.target !== target) return false
      if (tFrom && new Date(e.ts).getTime() < tFrom) return false
      if (tTo && new Date(e.ts).getTime() > tTo) return false
      if (q) {
        const haystack = JSON.stringify(e).toLowerCase()
        if (!haystack.includes(String(q).toLowerCase())) return false
      }
      return true
    })

    // 时间倒序
    filtered.sort((a, b) => b.ts.localeCompare(a.ts))

    const total = filtered.length
    const start = (pageNum - 1) * size
    const rows = filtered.slice(start, start + size)

    res.json({
      total,
      page: pageNum,
      pageSize: size,
      rows
    })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/** 聚合统计：各 level / 各 category / 近24h warn+error */
export const getStats = async (req, res) => {
  try {
    const all = await readAllLines()
    const now = Date.now()
    const dayAgo = now - 24 * 3600 * 1000

    const byLevel = { info: 0, warn: 0, error: 0 }
    const byCategory = {}
    const byEvent = {}
    let todayTotal = 0
    let recentAlerts = 0

    for (const e of all) {
      if (byLevel[e.level] !== undefined) byLevel[e.level]++
      byCategory[e.category] = (byCategory[e.category] || 0) + 1
      byEvent[e.event] = (byEvent[e.event] || 0) + 1
      const ts = new Date(e.ts).getTime()
      if (ts >= dayAgo) {
        todayTotal++
        if (e.level === 'warn' || e.level === 'error') recentAlerts++
      }
    }

    res.json({ byLevel, byCategory, byEvent, todayTotal, recentAlerts })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/** 事件字典（前端筛选下拉动态渲染） */
export const getEvents = (req, res) => {
  res.json({ events: listEvents() })
}

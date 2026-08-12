import fs from 'fs/promises'
import path from 'path'
import { fileURLToPath } from 'url'
import { getServers, getConfig } from '../config.js'
import { getAccounts } from './qqAccountService.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const PLAYTIME_PATH = path.join(__dirname, '..', 'data', 'qq_playtime.json')

// ═══════════════════════════════════════════════════════════
// 多服游玩时长聚合（后端本地权威）
//   records: { 用户名: { qq, servers: { 服id: 累计分钟 }, total, updatedAt } }
//   后端定时向各启用服拉取 player_daily_stat 全量累计（历史累计，非逐时快照），
//   合并落盘本文件；qq 字段随 QQ 台账（qq_accounts.json）同步刷新。
//   用途：前端 QQ 绑定列表、机器人「我的信息」的多服游玩时长。
// ═══════════════════════════════════════════════════════════

let _loaded = false
let _data = null // { records: {...} }

async function load() {
  if (_loaded) return _data
  try {
    const content = await fs.readFile(PLAYTIME_PATH, 'utf8')
    _data = JSON.parse(content)
  } catch {
    _data = { records: {} }
  }
  if (!_data || typeof _data !== 'object') _data = { records: {} }
  if (!_data.records || typeof _data.records !== 'object') _data.records = {}
  _loaded = true
  return _data
}

async function persist() {
  try {
    await fs.mkdir(path.dirname(PLAYTIME_PATH), { recursive: true })
    await fs.writeFile(PLAYTIME_PATH, JSON.stringify(_data, null, 2), 'utf8')
  } catch (err) {
    console.error('[QQ时长] 保存失败:', err.message)
  }
}

function buildBaseUrl(server) {
  const host = server?.host || 'localhost'
  const h = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
  return `${h}:${server?.port || 7878}`
}

/** 向单台服务器拉取全量累计时长 { stats: { 用户名: 分钟 } }，失败返回 null */
async function fetchServerStats(server) {
  const url = `${buildBaseUrl(server)}/data/online/all-stat?token=${encodeURIComponent(server.apiKey || '')}`
  try {
    const res = await fetch(url, { method: 'GET', signal: AbortSignal.timeout(10000) })
    if (!res.ok) return null
    const json = await res.json()
    if (!json || typeof json.stats !== 'object') return null
    const stats = {}
    for (const [name, minutes] of Object.entries(json.stats)) {
      const n = String(name || '').trim()
      if (!n) continue
      stats[n] = Math.max(0, parseInt(minutes) || 0)
    }
    return stats
  } catch (e) {
    return null
  }
}

/** 全量时长记录（{ username: record }） */
export async function getPlaytimeRecords() {
  return (await load()).records
}

/** 单个玩家时长记录（无则返回 null） */
export async function getPlaytime(username) {
  const records = await getPlaytimeRecords()
  return records[username] || null
}

/**
 * 执行一轮聚合：并行拉取所有启用服全量累计 → 合并落盘
 * @returns {{ ok: number, total: number }}
 */
export async function aggregateAll() {
  const servers = (await getServers()).filter(s => s.enabled !== false && s.host && s.port && s.apiKey)
  if (servers.length === 0) return { ok: 0, total: 0 }

  const results = await Promise.allSettled(servers.map(async s => ({
    server: s,
    stats: await fetchServerStats(s)
  })))

  const records = await getPlaytimeRecords()
  let ok = 0

  for (const r of results) {
    if (r.status !== 'fulfilled' || !r.value.stats) continue
    const { server, stats } = r.value
    for (const [username, minutes] of Object.entries(stats)) {
      const rec = records[username] || (records[username] = { qq: '', servers: {}, total: 0, updatedAt: '' })
      rec.servers[server.id] = minutes
    }
    ok++
  }

  // 同步 qq 字段（跟随台账：新增补上，解绑清空；时长记录本身保留）
  const accounts = await getAccounts()
  for (const username of Object.keys(records)) {
    const acc = accounts[username]
    records[username].qq = acc ? String(acc.qq || '') : ''
  }

  // 重算 total + 刷新时间戳
  const now = new Date().toISOString()
  for (const rec of Object.values(records)) {
    rec.total = Object.values(rec.servers || {}).reduce((sum, m) => sum + (m || 0), 0)
    rec.updatedAt = now
  }

  await persist()
  if (ok !== servers.length) {
    console.warn(`[QQ时长] 聚合完成: ${ok}/${servers.length}`)
  } else {
    console.log(`[QQ时长] 聚合完成: ${ok}/${servers.length}`)
  }
  return { ok, total: servers.length }
}

// ═══════════════════════════════════════════════════════════
// 定时器
// ═══════════════════════════════════════════════════════════

let _timer = null

/** 启动聚合定时器（幂等）：立即执行一轮，再按配置间隔轮询 */
export async function startAggregation() {
  if (_timer) return
  const cfg = await getConfig()
  const minutes = Math.max(1, cfg?.bot?.pollIntervalMinutes || 10)

  aggregateAll().then(r => {
    console.log(`[QQ时长] 首轮聚合完成: ${r.ok}/${r.total} (间隔 ${minutes} 分钟)`)
  }).catch(e => console.error('[QQ时长] 首轮聚合失败:', e.message))

  _timer = setInterval(() => {
    aggregateAll().then(r => {
      if (r.ok !== r.total) console.warn(`[QQ时长] 部分失败: ${r.ok}/${r.total}`)
    }).catch(e => console.error('[QQ时长] 聚合失败:', e.message))
  }, minutes * 60 * 1000)
  // 定时器不阻止进程退出
  if (_timer.unref) _timer.unref()
}

/** 停止聚合定时器（测试/重载用） */
export function stopAggregation() {
  if (_timer) {
    clearInterval(_timer)
    _timer = null
  }
}

export default { getPlaytimeRecords, getPlaytime, aggregateAll, startAggregation, stopAggregation }

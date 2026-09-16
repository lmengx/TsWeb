import { getServers } from '../config.js'
import { getAccounts } from './qqAccountService.js'

// ═══════════════════════════════════════════════════════════
// 账号属性多服聚合（每服独立计算 + 后端合并）
//   各 TShock 服调用插件 REST /data/account/attributes 获取本服账号属性明细，
//   后端并行拉取全部启用服 → 标注来源服 → 合并 → 概览统计 + 筛选分页。
//   账号体系在服内独立，聚合以「(服, 账号)」为粒度，语义清晰可重复。
// ═══════════════════════════════════════════════════════════

const PAGE_SIZE = 1000
const REQUEST_TIMEOUT = 20000

function buildBaseUrl(server) {
  const host = server?.host || 'localhost'
  const h = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
  return `${h}:${server?.port || 7878}`
}

/** 分页拉取单服全部账号属性，返回 { accounts, total } 或 { error } */
async function fetchServerAttributes(server) {
  const all = []
  let page = 1
  try {
    for (;;) {
      const url = `${buildBaseUrl(server)}/data/account/attributes?page=${page}&pageSize=${PAGE_SIZE}&token=${encodeURIComponent(server.apiKey || '')}`
      const res = await fetch(url, { method: 'GET', signal: AbortSignal.timeout(REQUEST_TIMEOUT) })
      if (!res.ok) return { error: `HTTP ${res.status}` }
      const json = await res.json()
      if (json.error) return { error: json.error }
      const list = Array.isArray(json.accounts) ? json.accounts : []
      all.push(...list)
      if (all.length >= (json.total || 0)) break
      if (list.length === 0) break // 防御：无数据且 total 异常时避免死循环
      page++
    }
    return { accounts: all, total: all.length }
  } catch (e) {
    return { error: e.message }
  }
}

/** 判定规则元信息（取第一台成功服的返回；所有服插件同版本，规则一致） */
export async function getRuleMeta() {
  const servers = enabledServers()
  for (const s of servers) {
    try {
      const url = `${buildBaseUrl(s)}/data/account/attributes?page=1&pageSize=1&token=${encodeURIComponent(s.apiKey || '')}`
      const res = await fetch(url, { method: 'GET', signal: AbortSignal.timeout(10000) })
      if (!res.ok) continue
      const json = await res.json()
      if (json.meta) return json.meta
    } catch { /* 该服失败，试下一台 */ }
  }
  return null
}

/**
 * 聚合全部启用服账号属性。
 * @param {object} filters { attr, keyword, minMinutes, maxMinutes, sortBy, sortDir, page, pageSize, serverId }
 * @returns 聚合结果：{ generatedAt, servers, summary, total, page, pageSize, accounts }
 */
export async function aggregateAttributes(filters = {}) {
  const servers = (await getServers()).filter(s => s.enabled !== false && s.host && s.port)
  const { serverId } = filters

  const targets = serverId ? servers.filter(s => String(s.id) === String(serverId)) : servers
  const results = await Promise.allSettled(targets.map(async s => ({
    server: s,
    data: await fetchServerAttributes(s)
  })))

  const serverInfo = []
  const accounts = []
  for (const r of results) {
    const s = r.value.server
    const data = r.status === 'fulfilled' ? r.value.data : { error: r.status }
    serverInfo.push({
      id: s.id,
      name: s.name,
      status: data.error ? 'error' : 'ok',
      error: data.error || '',
      total: data.error ? 0 : (data.total || 0)
    })
    if (!data.error) {
      for (const acc of data.accounts || []) {
        accounts.push({
          ...acc,
          serverId: s.id,
          serverName: s.name
        })
      }
    }
  }

  // 合并 QQ 绑定（后端台账，原始大小写 key）
  const qqMap = new Map()
  try {
    const records = await getAccounts()
    for (const [username, rec] of Object.entries(records || {})) {
      if (rec?.qq) qqMap.set(username, String(rec.qq))
    }
  } catch { /* 台账读取失败不阻断 */ }

  for (const acc of accounts) {
    acc.qq = qqMap.get(acc.username) || ''
  }

  // ── 概览统计（基于全量聚合，与筛选无关；语义固定）──
  const summary = buildSummary(accounts)

  // ── 筛选 ──
  const { attr, keyword, minMinutes, maxMinutes, activeDays14Min, sortBy = 'totalMinutes', sortDir = 'desc' } = filters
  let filtered = accounts
  if (attr) {
    const attrSet = new Set(String(attr).split(',').map(a => a.trim().toLowerCase()).filter(Boolean))
    if (attrSet.has('__none__')) {
      // 前端"全不选"：不显示任何账号
      filtered = []
    } else {
      const showNormal = attrSet.has('normal')
      filtered = filtered.filter(a => {
        const attrs = a.attributes || []
        if (attrs.length === 0) return showNormal            // 无属性标签账号 = 普通账号
        return attrs.some(x => attrSet.has(x))
      })
    }
  }
  if (keyword) {
    const kw = String(keyword).toLowerCase()
    filtered = filtered.filter(a => String(a.username || '').toLowerCase().includes(kw))
  }
  if (minMinutes != null && !Number.isNaN(Number(minMinutes))) {
    const m = Number(minMinutes)
    filtered = filtered.filter(a => (a.totalMinutes || 0) >= m)
  }
  if (maxMinutes != null && !Number.isNaN(Number(maxMinutes))) {
    const m = Number(maxMinutes)
    filtered = filtered.filter(a => (a.totalMinutes || 0) <= m)
  }
  if (activeDays14Min != null && !Number.isNaN(Number(activeDays14Min))) {
    const m = Number(activeDays14Min)
    filtered = filtered.filter(a => (a.activeDays14 || 0) >= m)
  }

  // 筛选后分布（饼图/条形图数据：当前选中项目内各属性占比）
  //   byPrimary   主属性分布（互斥分区，饼图用）
  //   byAttribute 属性标签分布（可重叠，一个账号可命中多个属性，条形图用）
  const filteredSummary = {
    total: filtered.length,
    byPrimary: {},
    byAttribute: {}
  }
  for (const a of filtered) {
    const primary = a.primaryAttribute || 'normal'
    filteredSummary.byPrimary[primary] = (filteredSummary.byPrimary[primary] || 0) + 1
    for (const attr of a.attributes || []) {
      filteredSummary.byAttribute[attr] = (filteredSummary.byAttribute[attr] || 0) + 1
    }
  }

  const getSortVal = (a) => {
    switch (sortBy) {
      case 'lastAccess': return a.lastAccess || ''
      case 'registered': return a.registered || ''
      case 'activeDays14': return a.activeDays14 || 0
      case 'relGroupSize': return a.relGroupSize || 1
      default: return a.totalMinutes || 0
    }
  }
  filtered = [...filtered].sort((a, b) => {
    const va = getSortVal(a)
    const vb = getSortVal(b)
    let cmp = 0
    if (typeof va === 'string' && typeof vb === 'string') cmp = va.localeCompare(vb)
    else cmp = (va > vb ? 1 : va < vb ? -1 : 0)
    return sortDir === 'asc' ? cmp : -cmp
  })

  const total = filtered.length
  const page = Math.max(1, parseInt(filters.page) || 1)
  const pageSize = Math.min(1000, Math.max(1, parseInt(filters.pageSize) || 100))
  const pageAccounts = filtered.slice((page - 1) * pageSize, page * pageSize)

  return {
    generatedAt: new Date().toISOString(),
    servers: serverInfo,
    summary,
    filteredSummary,
    total,
    page,
    pageSize,
    accounts: pageAccounts
  }
}

/** 全量筛选结果（CSV 导出用，不受分页限制） */
export async function getAllFiltered(filters = {}) {
  const agg = await aggregateAttributes({ ...filters, page: 1, pageSize: 1000000 })
  return { ...agg, page: 1, pageSize: agg.total, accounts: agg.accounts }
}

// ── 概览统计 ──

const ATTR_LABELS = {
  alt: '小号',
  guest: '游客账号',
  churn: '流失玩家',
  new_active: '近期新增活跃',
  returning: '回流玩家',
  sustained: '持续活跃',
  dormant: '长期沉睡',
  high_risk_group: '高风险关联组',
  normal: '普通账号'
}

const ATTR_ORDER = ['alt', 'guest', 'churn', 'dormant', 'returning', 'new_active', 'sustained', 'high_risk_group']

export function attrLabel(key) {
  return ATTR_LABELS[key] || key
}

export function attrList() {
  return ATTR_ORDER.map(k => ({ key: k, label: ATTR_LABELS[k] }))
}

function buildSummary(accounts) {
  const byPrimary = {}
  const byAttribute = {}
  let total = accounts.length
  let altGroupCount = 0      // 关联组总数（组大小 > 1）
  let highRiskGroupCount = 0 // 高风险组数（>= 3 账号）
  let qqBound = 0

  const relGroups = new Map() // serverId:groupIndex -> size

  for (const a of accounts) {
    const primary = a.primaryAttribute || 'normal'
    byPrimary[primary] = (byPrimary[primary] || 0) + 1

    for (const attr of a.attributes || []) {
      byAttribute[attr] = (byAttribute[attr] || 0) + 1
    }

    if (a.qq) qqBound++

    if ((a.relGroupSize || 1) > 1) {
      const key = `${a.serverId}:${a.relGroupIndex}`
      relGroups.set(key, a.relGroupSize || 0)
    }
  }
  for (const size of relGroups.values()) {
    altGroupCount++
    if (size >= 3) highRiskGroupCount++
  }

  // 实际玩家数：每个关联账号组合并为一个玩家（组内 size 个账号只算 1 人），无关联账号各算 1 人
  let actualPlayers = total
  for (const size of relGroups.values()) {
    actualPlayers -= (size - 1)
  }

  return {
    total,
    actualPlayers,
    qqBound,
    altGroupCount,
    highRiskGroupCount,
    byPrimary,
    byAttribute
  }
}

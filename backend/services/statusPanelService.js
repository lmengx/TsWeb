/**
 * 状态面板全服在线服务
 *
 * 数据流：
 *  ① 后端启动 / 服务器配置变更 → refreshStatusPanelPush()：并行拉取所有启用服
 *     /v2/server/status?players=true → 填 onlineCounts 缓存 → 下发所有插件
 *  ② 各服插件玩家上下线 → SSE 事件 "online"（{ online: N }）上报本服在线数
 *     → onOnlineReport() 更新缓存 → 300ms debounce 合并高频变化 → 下发所有插件
 *
 * 下发：POST /tsweb/statuspanel（HMAC-SHA256 签名，与 /tsweb/qqsync 协议一致）。
 * 拉取失败的服务器不计入 total（仅统计已知值）。
 */
import crypto from 'crypto'
import { getServers } from '../config.js'

// serverId -> 本服在线数（仅统计已知值；undefined = 未知，不计入 total）
const onlineCounts = new Map()

// 高频上下线合并：300ms debounce 后再统一下发
let debounceTimer = null

function buildBaseUrl(server) {
  const host = server?.host || 'localhost'
  const h = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
  return `${h}:${server?.port || 7878}`
}

/** 启用且可调用的服务器列表 */
async function enabledServers() {
  return (await getServers()).filter(s => s.enabled !== false && s.host && s.port && s.apiKey)
}

function signPayload(secret, body) {
  const ts = Date.now().toString()
  const nonce = crypto.randomBytes(16).toString('hex')
  const bodyHash = crypto.createHash('sha256').update(body).digest('hex')
  const signature = crypto
    .createHmac('sha256', secret)
    .update(`${ts}.${nonce}.${bodyHash}`)
    .digest('hex')
  return { ts, nonce, signature }
}

/** 向单台服务器 POST /tsweb/statuspanel（全服在线推送） */
async function postToServer(server, payloadObj) {
  if (!server?.pushSecret) return { ok: false, error: 'no pushSecret' }
  const body = JSON.stringify(payloadObj)
  const { ts, nonce, signature } = signPayload(server.pushSecret, body)
  const url = `${buildBaseUrl(server)}/tsweb/statuspanel`
  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Server-Id': String(server.id),
        'X-Timestamp': ts,
        'X-Nonce': nonce,
        'X-Signature': signature
      },
      body,
      signal: AbortSignal.timeout(10000)
    })
    if (!res.ok) {
      const text = await res.text()
      console.warn(`[状态面板] 全服在线推送失败 ${server.name}: HTTP ${res.status} ${text.slice(0, 200)}`)
      return { ok: false, status: res.status, error: text }
    }
    return { ok: true, status: res.status }
  } catch (e) {
    console.warn(`[状态面板] 全服在线推送失败 ${server.name}: ${e.message}`)
    return { ok: false, error: e.message }
  }
}

/** 按当前缓存聚合 + 下发所有启用服（单台失败不影响其他，仅 warn） */
async function pushAll() {
  const servers = await enabledServers()
  if (servers.length === 0) return { ok: 0, total: 0 }

  // total = Σ 已知在线数（未知服不计入）
  let total = 0
  const detail = []
  for (const s of servers) {
    const online = onlineCounts.get(s.id)
    if (typeof online === 'number' && online >= 0) total += online
    detail.push({ id: s.id, name: s.name, online: typeof online === 'number' ? online : null })
  }

  const payload = { type: 'online', total, servers: detail }
  const results = await Promise.allSettled(servers.map(s => postToServer(s, payload)))
  const okCount = results.filter(r => r.status === 'fulfilled' && r.value.ok).length
  if (okCount !== servers.length) {
    console.warn(`[状态面板] 全服在线下发异常: ${okCount}/${servers.length}`)
  }
  return { ok: okCount, total: servers.length }
}

/**
 * 全量刷新：拉取所有启用服在线数 → 填缓存 → 下发。
 * 调用时机：后端启动、服务器配置增删改（serverController create/update/remove 后）。
 */
export async function refreshStatusPanelPush() {
  const servers = await enabledServers()
  if (servers.length === 0) return { ok: 0, total: 0 }

  const results = await Promise.allSettled(servers.map(async s => {
    const url = `${buildBaseUrl(s)}/v2/server/status?players=true&token=${encodeURIComponent(s.apiKey || '')}`
    try {
      const res = await fetch(url, { method: 'GET', signal: AbortSignal.timeout(8000) })
      if (!res.ok) return { server: s, online: null }
      const d = await res.json()
      const n = parseInt(d?.playercount)
      return { server: s, online: Number.isFinite(n) ? n : null }
    } catch {
      return { server: s, online: null } // 拉取失败：不计入（仅统计已知值）
    }
  }))

  for (const r of results) {
    if (typeof r.value?.online === 'number' && r.value.online >= 0) {
      onlineCounts.set(r.value.server.id, r.value.online)
    } else {
      onlineCounts.delete(r.value.server.id)
    }
  }

  return pushAll()
}

/**
 * 收到插件 SSE 上报（玩家上下线）→ 更新缓存 → 300ms debounce 合并 → 下发。
 * 来源 serverId 以 SSE 连接为准（sseConnection 传入），不信任插件自报。
 */
export function onOnlineReport(serverId, online) {
  const n = parseInt(online)
  if (!Number.isFinite(n) || n < 0) return
  onlineCounts.set(String(serverId), n)

  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    debounceTimer = null
    pushAll().catch(() => {})
  }, 300)
}

/** 当前缓存的全服在线总数（调试/测试用） */
export function getOnlineCounts() {
  return { counts: Object.fromEntries(onlineCounts), total: [...onlineCounts.values()].reduce((a, b) => a + b, 0) }
}

export default { refreshStatusPanelPush, onOnlineReport, getOnlineCounts }

/**
 * 日志广播共享模块：按服务器分组的内存队列 + SSE 客户端广播
 * 供 sseConnection（插件 SSE 常连的 log 事件）与前端日志流共用。
 *
 * 多服架构：日志必须按 serverId 隔离 ——
 *  - 内存队列：serverId -> 数组（上限 MaxQueueLines）
 *  - SSE 客户端：serverId -> Set（只向订阅同一台服务器的前端广播）
 *  - 去重哈希：serverId -> 最近 N 条（避免跨服务器相同文本日志被误去重）
 * 未携带 serverId 的旧调用方归入 DefaultServerId 兜底组。
 */

// ═══ 内存日志队列 + SSE 客户端管理（按服务器分组） ═══
const _logQueues = new Map()            // serverId -> string[]
const _sseClientGroups = new Map()      // serverId -> Set<client>
const _recentHashesByServer = new Map() // serverId -> string[]
const MaxQueueLines = 2000
const MaxRecentHashes = 4

// 未携带 serverId 时的兜底组（兼容旧调用方；正常多服链路均带真实 serverId）
const DefaultServerId = 'default'

/** 统一服务器分组 key：防御 id 类型差异（数字/字符串），空值归入兜底组 */
function normalizeKey(serverId) {
  return serverId === undefined || serverId === null || serverId === ''
    ? DefaultServerId
    : String(serverId)
}

export function pushWebhookLog(line, serverId) {
  const key = normalizeKey(serverId)

  // SSE 常连主通道与 webhook 附加通道可能推送同一行，
  // 按 服务器+内容 对最近几条去重，避免跨服务器误去重
  let hashes = _recentHashesByServer.get(key)
  if (!hashes) { hashes = []; _recentHashesByServer.set(key, hashes) }
  const hash = String(line)
  if (hashes.includes(hash)) return
  hashes.push(hash)
  if (hashes.length > MaxRecentHashes) hashes.shift()

  // 存入该服务器的内存队列
  let queue = _logQueues.get(key)
  if (!queue) { queue = []; _logQueues.set(key, queue) }
  queue.push(line)
  if (queue.length > MaxQueueLines) {
    queue.splice(0, queue.length - MaxQueueLines)
  }

  // 只广播给订阅同一服务器的 SSE 客户端（跳过僵尸连接）
  const clients = _sseClientGroups.get(key)
  if (!clients || clients.size === 0) return
  const data = JSON.stringify([line])
  for (const client of clients) {
    try {
      if (client.socket?.destroyed || !client.writable) {
        clients.delete(client)
        continue
      }
      client.write(`data: ${data}\n\n`)
    } catch {
      clients.delete(client)
    }
  }
}

/** 获取某服务器的 SSE 客户端集合（缺省返回兜底组） */
export function getSseClients(serverId) {
  return _sseClientGroups.get(normalizeKey(serverId)) || new Set()
}

/** 注册一个 SSE 客户端到指定服务器分组 */
export function addSseClient(serverId, client) {
  const key = normalizeKey(serverId)
  let clients = _sseClientGroups.get(key)
  if (!clients) { clients = new Set(); _sseClientGroups.set(key, clients) }
  clients.add(client)
}

/** 从指定服务器分组移除 SSE 客户端 */
export function removeSseClient(serverId, client) {
  const key = normalizeKey(serverId)
  const clients = _sseClientGroups.get(key)
  if (clients) {
    clients.delete(client)
    if (clients.size === 0) _sseClientGroups.delete(key)
  }
}

/** SSE 客户端数量：传 serverId 返回该服务器数量；不传返回全部服务器总和 */
export function sseClientCount(serverId) {
  if (serverId) return _sseClientGroups.get(normalizeKey(serverId))?.size || 0
  let total = 0
  for (const set of _sseClientGroups.values()) total += set.size
  return total
}

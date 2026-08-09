/**
 * 日志广播共享模块：内存队列 + SSE 客户端广播
 * 被 onlineController（/api/online/log-webhook，兼容旧端点）与
 * hookController（/hook/log，新签名端点）共用
 */

// ═══ 内存日志队列 + SSE 客户端管理 ═══
const _logQueue = []
const _sseClients = new Set()
const MaxQueueLines = 2000

// SSE 常连主通道与 webhook 附加通道可能推送同一行，按内容对最近几条去重，避免前端重复
const _recentHashes = []
const MaxRecentHashes = 4

export function pushWebhookLog(line) {
  const hash = String(line)
  if (_recentHashes.includes(hash)) return
  _recentHashes.push(hash)
  if (_recentHashes.length > MaxRecentHashes) _recentHashes.shift()

  // 存入内存队列
  _logQueue.push(line)
  if (_logQueue.length > MaxQueueLines) {
    _logQueue.splice(0, _logQueue.length - MaxQueueLines)
  }

  // 广播给所有 SSE 客户端（跳过僵尸连接）
  const data = JSON.stringify([line])
  for (const client of _sseClients) {
    try {
      if (client.socket?.destroyed || !client.writable) {
        _sseClients.delete(client)
        continue
      }
      client.write(`data: ${data}\n\n`)
    } catch {
      _sseClients.delete(client)
    }
  }
}

export function getLogQueue() {
  return _logQueue
}

export function getSseClients() {
  return _sseClients
}

export function addSseClient(client) {
  _sseClients.add(client)
}

export function removeSseClient(client) {
  _sseClients.delete(client)
}

export function sseClientCount() {
  return _sseClients.size
}

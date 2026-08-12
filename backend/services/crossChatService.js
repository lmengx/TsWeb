/**
 * 跨服聊天转发服务
 *
 * 数据流：插件 A 本地聊天 → SSE 事件 "cross-chat" 上报 → 本服务转发到
 * 所有启用跨服聊天（crossChat=true）且非来源的服务器 → 各目标插件
 * POST /tsweb/crosschat（HMAC-SHA256 签名，与 /tsweb/qqsync 协议一致）。
 *
 * 来源可信：serverId 取自 SSE 连接（conn.server.id），不信任插件 body 自报。
 * 转发内容原样保留（玩家名字/前缀/消息里的 [i:] [c/] 标签均不转义，
 * 由接收端客户端渲染层解析为物品图标/颜色）。
 */
import crypto from 'crypto'
import { getServers } from '../config.js'

function buildBaseUrl(server) {
  const host = server?.host || 'localhost'
  const h = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
  return `${h}:${server?.port || 7878}`
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

/** 向单台服务器 POST /tsweb/crosschat */
async function postToServer(server, payloadObj) {
  if (!server?.pushSecret) return { ok: false, error: 'no pushSecret' }
  const body = JSON.stringify(payloadObj)
  const { ts, nonce, signature } = signPayload(server.pushSecret, body)
  const url = `${buildBaseUrl(server)}/tsweb/crosschat`
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
      signal: AbortSignal.timeout(15000)
    })
    if (!res.ok) {
      const text = await res.text()
      console.warn(`[跨服聊天] 推送失败 ${server.name}: HTTP ${res.status} ${text.slice(0, 200)}`)
      return { ok: false, status: res.status, error: text }
    }
    return { ok: true, status: res.status }
  } catch (e) {
    console.warn(`[跨服聊天] 推送失败 ${server.name}: ${e.message}`)
    return { ok: false, error: e.message }
  }
}

/**
 * 跨服聊天广播：转发给所有启用 crossChat 且 id ≠ fromServerId 的服务器。
 * 单台目标失败不影响其他目标（仅 warn）。
 */
export async function broadcastCrossChat(fromServerId, payload) {
  const servers = await getServers()
  const targets = servers.filter(s =>
    s.enabled !== false && s.crossChat === true && s.id !== fromServerId)
  if (targets.length === 0) return { ok: 0, total: 0 }

  const results = await Promise.allSettled(targets.map(s => postToServer(s, payload)))
  const okCount = results.filter(r => r.status === 'fulfilled' && r.value.ok).length
  if (okCount !== targets.length) {
    console.warn(`[跨服聊天] 转发异常: ${okCount}/${targets.length}`)
  }
  return { ok: okCount, total: targets.length }
}

export default { broadcastCrossChat }

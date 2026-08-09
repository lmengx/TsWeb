import crypto from 'crypto'
import { getServerById } from '../config.js'

/**
 * Webhook 回传签名验证中间件（插件 → 后端 /hook/* 端点）
 *
 * 协议（HMAC-SHA256）：
 *   Headers:
 *     X-Server-Id  : 目标服务器 id
 *     X-Timestamp  : Unix 毫秒
 *     X-Nonce      : 32位随机 hex（防重放）
 *     X-Signature  : hex HMAC
 *   signature = HMAC-SHA256(pushSecret, `${timestamp}.${nonce}.${sha256Hex(rawBody)}`)
 *
 * 校验顺序：查密钥 → 时间窗(±300s) → nonce 去重 → 签名比对
 */

// 时间窗（毫秒）
const TIME_WINDOW = 300 * 1000
// nonce 去重缓存（LRU 简化：Map + 容量限制 + 过期清理）
const _nonceCache = new Map()
const NONCE_CACHE_MAX = 10000

function sha256Hex(data) {
  return crypto.createHash('sha256').update(data).digest('hex')
}

function safeEqual(a, b) {
  const bufA = Buffer.from(String(a), 'utf8')
  const bufB = Buffer.from(String(b), 'utf8')
  if (bufA.length !== bufB.length) return false
  return crypto.timingSafeEqual(bufA, bufB)
}

function checkNonce(serverId, nonce, timestamp) {
  if (!nonce || nonce.length < 16) return false
  const key = `${serverId}:${nonce}`
  if (_nonceCache.has(key)) return false
  _nonceCache.set(key, timestamp)
  // 简单容量清理
  if (_nonceCache.size > NONCE_CACHE_MAX) {
    const cutoff = Date.now() - TIME_WINDOW
    for (const [k, t] of _nonceCache) {
      if (t < cutoff) _nonceCache.delete(k)
    }
    // 若仍超限，删最旧
    while (_nonceCache.size > NONCE_CACHE_MAX) {
      _nonceCache.delete(_nonceCache.keys().next().value)
    }
  }
  return true
}

/**
 * 校验 webhook 请求签名。失败返回 { ok:false, status, error }；成功返回 { ok:true, server }
 */
export async function verifyWebhookSignature(req, rawBody) {
  const serverId = req.headers['x-server-id']
  const timestamp = req.headers['x-timestamp']
  const nonce = req.headers['x-nonce']
  const signature = req.headers['x-signature']

  if (!serverId) return { ok: false, status: 401, error: 'Missing X-Server-Id' }
  if (!timestamp || !nonce || !signature) {
    return { ok: false, status: 401, error: 'Missing signature headers' }
  }

  const server = await getServerById(serverId)
  if (!server || !server.pushSecret) {
    return { ok: false, status: 401, error: 'Unknown server or no pushSecret' }
  }

  // 时间窗
  const ts = parseInt(timestamp)
  if (!ts || isNaN(ts) || Math.abs(Date.now() - ts) > TIME_WINDOW) {
    return { ok: false, status: 401, error: 'Timestamp out of window' }
  }

  // nonce 去重
  if (!checkNonce(serverId, nonce, ts)) {
    return { ok: false, status: 401, error: 'Replay detected' }
  }

  // 签名比对（对 body 的 sha256 签名，避免大文件重复读取）
  const bodyHash = sha256Hex(rawBody || '')
  const expected = crypto
    .createHmac('sha256', server.pushSecret)
    .update(`${ts}.${nonce}.${bodyHash}`)
    .digest('hex')

  if (!safeEqual(signature, expected)) {
    return { ok: false, status: 401, error: 'Invalid signature' }
  }

  return { ok: true, server }
}

export default { verifyWebhookSignature }

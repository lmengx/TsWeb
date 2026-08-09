import audit from '../services/auditLogger.js'
import { verifyWebhookSignature } from '../services/hookAuth.js'
import { pushWebhookLog } from '../services/logBroadcast.js'

/**
 * Webhook 日志接收端点：POST /hook/log
 * 插件端通过 HMAC 签名推送日志行（X-Server-Id / X-Timestamp / X-Nonce / X-Signature）
 * Body: { lines: ["[{\"t\":\"text\",\"c\":\"Red\"}]"] }
 * 先验签，再入队广播
 */
export const logWebhookReceiver = async (req, res) => {
  try {
    const rawBody = req.rawBody || JSON.stringify(req.body || {})
    const auth = await verifyWebhookSignature(req, rawBody)
    if (!auth.ok) {
      audit.record('auth.token_invalid', {
        actor: req.headers['x-server-id'] || 'unknown',
        reason: `webhook 签名校验失败: ${auth.error}`,
        ip: req.ip
      })
      return res.status(auth.status).json({ error: auth.error })
    }

    const { lines } = req.body || {}
    if (!Array.isArray(lines) || lines.length === 0) {
      return res.status(400).json({ error: 'Missing or invalid lines array' })
    }

    for (const line of lines) {
      pushWebhookLog(line)
    }
    res.json({ status: 'ok', received: lines.length })
  } catch (err) {
    console.error('[Hook] 日志接收失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

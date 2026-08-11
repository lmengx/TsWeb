import path from 'path'
import { fileURLToPath } from 'url'
import audit from '../services/auditLogger.js'
import { verifyWebhookSignature } from '../services/hookAuth.js'
import { pushWebhookLog } from '../services/logBroadcast.js'
import { saveFileToBackend } from '../services/sseConnection.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

/**
 * 解析 /hook 请求体：/hook 首层中间件收集 rawBody 时已消费请求流，
 * 后续 express.json() 拿不到数据（req.body 为空对象），必须从 req.rawBody 解析。
 */
function parseJsonBody(req) {
  const raw = req.rawBody || ''
  if (raw.trim()) {
    try { return JSON.parse(raw) } catch { /* 落到 req.body 兜底 */ }
  }
  return req.body || {}
}

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

    const { lines } = parseJsonBody(req)
    if (!Array.isArray(lines) || lines.length === 0) {
      return res.status(400).json({ error: 'Missing or invalid lines array' })
    }

    // 多服：日志按来源服务器分组入队，避免跨服务器混流
    const serverId = auth.server ? auth.server.id : ''
    for (const line of lines) {
      pushWebhookLog(line, serverId)
    }
    res.json({ status: 'ok', received: lines.length })
  } catch (err) {
    console.error('[Hook] 日志接收失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

/**
 * 备份接收端点：POST /hook/backup
 * 插件端自动备份完成后，HMAC 签名通知（Body: { path: 'TSWeb/Backup/xxx.zip' }，相对 TShock.SavePath），
 * 后端验签后经 SSE 定向拉取（root=tshock）到 data/backup/{serverId}/ 专门目录。
 * 失败仅记录，插件本地 zip 保留（插件不重试）。
 */
export const backupReceiver = async (req, res) => {
  try {
    const rawBody = req.rawBody || JSON.stringify(req.body || {})
    const auth = await verifyWebhookSignature(req, rawBody)
    if (!auth.ok) {
      audit.record('auth.token_invalid', {
        actor: req.headers['x-server-id'] || 'unknown',
        reason: `backup webhook 签名校验失败: ${auth.error}`,
        ip: req.ip
      })
      return res.status(auth.status).json({ error: auth.error })
    }

    const { path: relPath } = parseJsonBody(req)
    if (!relPath) {
      return res.status(400).json({ error: 'Missing path' })
    }

    const server = auth.server
    const destDir = path.join(__dirname, '..', 'data', 'backup', String(server.id))
    const result = await saveFileToBackend(server.id, String(relPath), {
      root: 'tshock',
      destDir
    })
    if (!result.success) {
      audit.record('backup.failed', {
        serverId: server.id,
        error: result.error || result.message || 'SSE 拉取失败'
      })
      return res.status(502).json({ error: result.error || result.message || '推送失败' })
    }

    audit.record('backup.received', {
      serverId: server.id,
      name: result.name,
      size: result.size
    })
    console.log(`[Hook] 备份已接收: ${server.name}/${result.name} (${result.size} bytes)`)
    res.json({ status: 'ok', name: result.name, size: result.size })
  } catch (err) {
    console.error('[Hook] 备份接收失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

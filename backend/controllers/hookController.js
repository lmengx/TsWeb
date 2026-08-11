import path from 'path'
import { fileURLToPath } from 'url'
import audit from '../services/auditLogger.js'
import { verifyWebhookSignature } from '../services/hookAuth.js'
import { saveFileToBackend } from '../services/sseConnection.js'
import { upsertAccount, addUuid, broadcastFullAll, broadcastUuid, getAccountByUsername } from '../services/qqAccountService.js'

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
 * 备份接收端点：POST /hook/backup
 * 插件端自动备份完成后，HMAC 签名通知（Body: { path: 'TSWeb/Backup/xxx.zip' }，相对 TShock.SavePath），
 * 后端验签后经 SSE 定向拉取（root=tshock）到 data/backup/{serverId}/ 专门目录。
 * 失败仅记录，插件本地 zip 保留（插件不重试）。
 *
 * 注：日志回传 webhook（/hook/log）已废弃移除，当前仅保留此备份端点。
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

/**
 * 绑定上报端点：POST /hook/identity
 * 插件侧玩家绑定时上报 { username, qq, passwordHash, uuidList }
 * （哈希与已登录设备 UUID 由账号所在服务器提供）→ 更新台账 → 广播完整台账到所有启用服。
 * 注意：绑定流程的哈希/uuidList 属于敏感数据，仅经 /hook（HMAC）内部通道传输，不落日志。
 */
export const identityReceiver = async (req, res) => {
  try {
    const rawBody = req.rawBody || JSON.stringify(req.body || {})
    const auth = await verifyWebhookSignature(req, rawBody)
    if (!auth.ok) {
      return res.status(auth.status).json({ error: auth.error })
    }

    const body = parseJsonBody(req)
    const username = String(body.username || '').trim()
    const qq = String(body.qq || '').trim()
    const passwordHash = String(body.passwordHash || '').trim()
    if (!username || !qq || !passwordHash) {
      return res.status(400).json({ error: 'Missing username/qq/passwordHash' })
    }

    const { changed } = await upsertAccount({
      username,
      qq,
      passwordHash,
      uuidList: Array.isArray(body.uuidList) ? body.uuidList : []
    })
    if (changed) {
      // 广播完整台账（绑定 = 台账变更）
      broadcastFullAll().catch(e => console.error('[QQ台账] 绑定后广播失败:', e.message))
    }
    audit.record('qq_account.bound', {
      serverId: auth.server.id,
      username,
      qq
    })
    console.log(`[QQ台账] 绑定上报: ${username} (QQ:${qq}) 来自 ${auth.server.name}`)
    res.json({ status: 'ok' })
  } catch (err) {
    console.error('[QQ台账] 绑定上报失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

/**
 * 登录新设备 UUID 上报端点：POST /hook/qq-uuid
 * 插件侧玩家登录成功且当前设备 UUID 不在本地集合时上报 { username, uuid }
 * → 台账追加 → 向所有启用 syncUUID 的服务器推单条（只推该用户）。
 */
export const qqUuidReceiver = async (req, res) => {
  try {
    const rawBody = req.rawBody || JSON.stringify(req.body || {})
    const auth = await verifyWebhookSignature(req, rawBody)
    if (!auth.ok) {
      return res.status(auth.status).json({ error: auth.error })
    }

    const body = parseJsonBody(req)
    const username = String(body.username || '').trim()
    const uuid = String(body.uuid || '').trim()
    if (!username || !uuid) {
      return res.status(400).json({ error: 'Missing username/uuid' })
    }

    const result = await addUuid(username, uuid)
    if (!result.ok) {
      // 账号不在台账：正常现象（本地独有账号登录），不广播
      return res.json({ status: 'ok', skipped: true })
    }
    if (result.added) {
      broadcastUuid(username, uuid).catch(e => console.error('[QQ台账] UUID 广播失败:', e.message))
      console.log(`[QQ台账] 新设备: ${username} +${uuid} (来自 ${auth.server.name})`)
    }
    res.json({ status: 'ok', added: result.added })
  } catch (err) {
    console.error('[QQ台账] UUID 上报失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

/**
 * UUID 免密判定查询：POST /hook/uuid-check  { username, uuid }
 * 插件连接期 ClientUUID 拦截后，本地内存缓存 miss 时调此接口确认该设备是否已授权
 * → { inList: true/false, uuidList?: [...] }（inList=true 时免密登录）
 */
export const uuidCheckReceiver = async (req, res) => {
  try {
    const rawBody = req.rawBody || JSON.stringify(req.body || {})
    const auth = await verifyWebhookSignature(req, rawBody)
    if (!auth.ok) {
      return res.status(auth.status).json({ error: auth.error })
    }

    const body = parseJsonBody(req)
    const username = String(body.username || '').trim()
    const uuid = String(body.uuid || '').trim()
    if (!username || !uuid) {
      return res.status(400).json({ error: 'Missing username/uuid' })
    }

    const account = await getAccountByUsername(username)
    if (!account) {
      return res.json({ inList: false, inLedger: false })
    }
    const uuidList = Array.isArray(account.uuidList) ? account.uuidList : []
    res.json({ inList: uuidList.includes(uuid), inLedger: true, uuidList })
  } catch (err) {
    console.error('[QQ台账] UUID 查询失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

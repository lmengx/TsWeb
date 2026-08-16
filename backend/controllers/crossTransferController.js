/**
 * 跨服传送配置管理（/api/crosstransfer/*，仅 admin，x-server-id 请求级上下文）
 * - GET  /config  读取后端配置（脱敏）+ 插件端现状（自动获取）
 * - POST /config  仅保存草稿到后端（不下发）
 * - POST /reveal  返回本服密钥明文（前端"显示"用，审计）
 * - POST /probe   让插件端实际探测目标服可达性（专线优先）
 * - POST /apply   构建插件格式配置（自动配对密钥）→ 下发插件端 → 写回后端
 */
import { getServerById, setServerCrossTransfer } from '../config.js'
import { getCurrentServer, getCurrentServerId } from '../services/tshockService.js'
import crossTransferService from '../services/crossTransferService.js'
import audit from '../services/auditLogger.js'

/** 脱敏：selfSecret / targets[].secret 只返回是否已设置，明文仅经 /reveal 返回 */
function sanitize(ct) {
  if (!ct) return null
  return {
    enabled: !!ct.enabled,
    selfServerId: ct.selfServerId || '',
    hasSelfSecret: !!(ct.selfSecret && ct.selfSecret.length > 0),
    targets: (ct.targets || []).map(t => ({
      serverId: t.serverId || '',
      name: t.name || '',
      enabled: t.enabled !== false,
      host: t.host || '',
      port: t.port || 7777,
      dedicatedHost: t.dedicatedHost || '',
      dedicatedPort: t.dedicatedPort || 0,
      version: t.version || 319,
      hasSecret: !!(t.secret && t.secret.length > 0),
      password: t.password || ''
    }))
  }
}

export const getConfig = async (req, res) => {
  try {
    const server = getCurrentServer()
    const serverId = getCurrentServerId()
    const serverCfg = serverId ? await getServerById(serverId) : null
    const configured = sanitize(serverCfg?.crossTransfer || null)
    // 插件端现状（自动获取导入源）：离线时 null
    const pluginCurrent = server ? await crossTransferService.readPluginConfig(server) : null
    res.json({ configured, pluginCurrent })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const saveConfig = async (req, res) => {
  try {
    const serverId = getCurrentServerId()
    if (!serverId) return res.status(409).json({ error: '缺少 x-server-id' })
    const body = req.body || {}
    if (!body.selfServerId || !String(body.selfServerId).trim()) {
      return res.status(400).json({ error: '本服ID不能为空' })
    }
    const saved = await setServerCrossTransfer(serverId, body)
    if (!saved) return res.status(404).json({ error: '服务器不存在' })
    audit.record('crossTransfer.save', {
      serverId,
      enabled: !!body.enabled,
      targets: (body.targets || []).length,
      actor: req.user?.username
    })
    res.json({ success: true })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const reveal = async (req, res) => {
  try {
    const serverId = getCurrentServerId()
    const serverCfg = serverId ? await getServerById(serverId) : null
    if (!serverCfg) return res.status(404).json({ error: '服务器不存在' })
    audit.record('crossTransfer.reveal', {
      serverId,
      actor: req.user?.username
    })
    res.json({ selfSecret: serverCfg.crossTransfer?.selfSecret || '' })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const probe = async (req, res) => {
  try {
    const server = getCurrentServer()
    if (!server || !server.id) return res.status(409).json({ error: '当前服务器未连接或未配置（缺少 x-server-id）' })
    const targets = Array.isArray(req.body?.targets) ? req.body.targets : []
    const result = await crossTransferService.probeFromPlugin(server, targets)
    if (result.error) return res.status(502).json({ error: result.error })
    audit.record('crossTransfer.probe', {
      serverId: server.id,
      count: targets.length,
      actor: req.user?.username
    })
    res.json({ success: true, results: result.results })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const apply = async (req, res) => {
  try {
    const server = getCurrentServer()
    if (!server || !server.id) return res.status(409).json({ error: '当前服务器未连接或未配置（缺少 x-server-id）' })
    const serverId = server.id
    const body = req.body || {}
    if (!body.selfServerId || !String(body.selfServerId).trim()) {
      return res.status(400).json({ error: '本服ID不能为空' })
    }

    const serverCfg = await getServerById(serverId)
    if (!serverCfg) return res.status(404).json({ error: '服务器不存在' })

    // 构建插件格式（自动配对：目标 name/secret 从对端服务器带出）→ 下发
    const pluginConfig = await crossTransferService.buildPluginConfig(serverCfg, body)
    const result = await crossTransferService.applyToPlugin(server, pluginConfig)
    if (!result.ok) return res.status(502).json({ error: result.error })

    // 下发成功 → 写回后端（配置权威源）
    await setServerCrossTransfer(serverId, body)
    audit.record('crossTransfer.apply', {
      serverId,
      enabled: !!body.enabled,
      targets: (body.targets || []).length,
      actor: req.user?.username
    })
    res.json({ success: true, pluginConfig })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

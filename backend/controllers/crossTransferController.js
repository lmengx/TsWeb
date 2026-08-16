/**
 * 跨服传送配置管理（/api/crosstransfer/*，仅 admin，x-server-id 请求级上下文）
 * 单服直连模式：不存后端全局配置，直接读写当前服务器插件端的 CrossTransfer.json
 * - GET  /config  读取插件端现有配置（含明文密钥，供表单回显）
 * - POST /config  前端表单 → 转插件格式 → 写入插件端（热应用）
 * - POST /probe   让插件端实际 TCP 探测目标服可达性
 */
import { getCurrentServer } from '../services/tshockService.js'
import crossTransferService from '../services/crossTransferService.js'
import audit from '../services/auditLogger.js'

export const getConfig = async (req, res) => {
  try {
    const server = getCurrentServer()
    if (!server || !server.id) return res.status(409).json({ error: '当前服务器未连接或未配置（缺少 x-server-id）' })
    const config = await crossTransferService.readPluginConfig(server)
    if (config === null) return res.status(502).json({ error: '无法读取插件端跨服配置（插件端离线或未响应）' })
    res.json({ config })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const saveConfig = async (req, res) => {
  try {
    const server = getCurrentServer()
    if (!server || !server.id) return res.status(409).json({ error: '当前服务器未连接或未配置（缺少 x-server-id）' })
    const body = req.body || {}
    if (!body.selfServerId || !String(body.selfServerId).trim()) {
      return res.status(400).json({ error: '本服ID不能为空' })
    }

    const pluginConfig = crossTransferService.buildPluginConfig(body)
    const result = await crossTransferService.applyToPlugin(server, pluginConfig)
    if (!result.ok) return res.status(502).json({ error: result.error })

    audit.record('crossTransfer.save', {
      serverId: server.id,
      enabled: !!body.enabled,
      targets: (body.targets || []).length,
      actor: req.user?.username
    })
    res.json({ success: true, pluginConfig })
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

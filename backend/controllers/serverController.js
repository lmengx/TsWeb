import audit from '../services/auditLogger.js'
import {
  getServers, getServerById, addServer, updateServer, deleteServer
} from '../config.js'
import { getServerInstance, testConnectionWith } from '../services/tshockService.js'
import { activateServer, deactivateServer } from '../services/serverActivation.js'

/** 脱敏：apiKey / pushSecret 只返回是否已设置，不返回明文；同时附上实时在线状态 */
function sanitize(server) {
  if (!server) return null
  return {
    id: server.id,
    name: server.name,
    host: server.host,
    port: server.port,
    enabled: server.enabled,
    note: server.note,
    hasApiKey: !!(server.apiKey && server.apiKey.length > 0),
    hasPushSecret: !!(server.pushSecret && server.pushSecret.length > 0),
    connected: !!getServerInstance(server.id)?.isConnected,
    // QQ 台账同步开关（前端编辑弹窗回显依赖，必须随列表返回）
    syncQQAccounts: server.syncQQAccounts === true,
    syncUUID: server.syncUUID === true,
    // 跨服聊天（前端编辑弹窗回显）
    crossChat: server.crossChat === true,
    crossChatPrefix: server.crossChatPrefix || '[c/#4DABF7:{serverName}]',
    crossChatColor: server.crossChatColor || '#FFFFFF'
  }
}

export const list = async (req, res) => {
  try {
    const servers = await getServers()
    res.json({ servers: servers.map(sanitize) })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const getOne = async (req, res) => {
  try {
    const server = await getServerById(req.params.id)
    if (!server) return res.status(404).json({ error: '服务器不存在' })
    res.json({ server: sanitize(server) })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const create = async (req, res) => {
  try {
    const { name, host, port, apiKey, note } = req.body
    if (!host || !port || !apiKey) {
      return res.status(400).json({ error: 'host、port、apiKey 均为必填' })
    }
    const server = await addServer({ name, host, port, apiKey, note })
    // 激活：注册 REST 实例 + 建立 SSE 常连（日志/文件推送主通道）+ 注册 webhook 推流
    activateServer(server)
    audit.record('server.add', {
      name: server.name,
      host: server.host,
      port: server.port,
      actor: req.user?.username
    })
    res.json({ success: true, server: sanitize(server) })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const update = async (req, res) => {
  try {
    const id = req.params.id
    const before = await getServerById(id)
    if (!before) return res.status(404).json({ error: '服务器不存在' })

    const changedKeys = Object.keys(req.body || {}).filter(k => req.body[k] !== undefined)
    const server = await updateServer(id, req.body)
    if (!server) return res.status(404).json({ error: '服务器不存在' })

    // 激活：同步 REST 实例 + 重建 SSE 常连（host/port/apiKey/enabled 可能变化）+ 重新注册 webhook
    activateServer(server)
    audit.record('server.update', {
      id: server.id,
      name: server.name,
      host: server.host,
      port: server.port,
      changedKeys,
      actor: req.user?.username
    })
    res.json({ success: true, server: sanitize(server) })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const remove = async (req, res) => {
  try {
    const id = req.params.id
    const before = await getServerById(id)
    if (!before) return res.status(404).json({ error: '服务器不存在' })
    await deleteServer(id)
    // 停用：注销 REST 实例 + 释放 SSE 常连 + 注销 webhook
    deactivateServer(id)
    audit.record('server.delete', {
      id: before.id,
      name: before.name,
      actor: req.user?.username
    })
    res.json({ success: true })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/** 仅测试连接（不落库、不注册实例）：添加向导中"测试连接"用 */
export const testOnly = async (req, res) => {
  try {
    const { host, port, apiKey } = req.body || {}
    if (!host || !port || !apiKey) {
      return res.status(400).json({ error: 'host、port、apiKey 均为必填' })
    }
    const result = await testConnectionWith(host, port, apiKey)
    res.json(result)
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/** 测试指定服务器连接（不修改配置） */
export const testConnection = async (req, res) => {
  try {
    const server = await getServerById(req.params.id)
    if (!server) return res.status(404).json({ error: '服务器不存在' })
    const result = await getServerInstance(server.id)?.testConnection()
    audit.record('server.test', {
      id: server.id,
      name: server.name,
      success: !!result?.success,
      error: result?.error,
      actor: req.user?.username
    })
    res.json(result)
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

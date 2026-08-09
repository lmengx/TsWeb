import audit from '../services/auditLogger.js'
import {
  getServers, getServerById, addServer, updateServer, deleteServer,
  rotateServerPushSecret
} from '../config.js'
import { registerServer, unregisterServer, getServerInstance, testConnectionWith } from '../services/tshockService.js'

/** 脱敏：apiKey / pushSecret 只返回是否已设置，不返回明文 */
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
    hasPushSecret: !!(server.pushSecret && server.pushSecret.length > 0)
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
    // 注册到 tshockService 实例注册表
    registerServer(server)
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

    // 同步更新实例
    registerServer(server)
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
    unregisterServer(id)
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

/** 重新生成 webhook 推送密钥（轮换） */
export const rotateSecret = async (req, res) => {
  try {
    const server = await rotateServerPushSecret(req.params.id)
    if (!server) return res.status(404).json({ error: '服务器不存在' })
    audit.record('server.update', {
      id: server.id,
      name: server.name,
      changedKeys: ['pushSecret'],
      actor: req.user?.username
    })
    res.json({ success: true, server: sanitize(server) })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

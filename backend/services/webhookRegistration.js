import { getConfig, getServers, getServerById } from '../config.js'
import { getServerInstance } from './tshockService.js'

// 内存中记录已注册的服务器 webhook 状态（serverId -> 最后注册的 url）
const _registered = new Map()

/**
 * 向指定服务器的插件注册/注销 webhook 地址
 * 使用该服务器的 apiKey（TShock REST token）作为信任根；
 * 插件端将 pushSecret 用于后续推送的 HMAC 签名（由注册响应返回/或配置下发）
 * @param {string} serverId 目标服务器 id
 * @param {string|null} url 要注册的 URL（null 表示注销）
 */
export async function updatePluginWebhook(serverId, url) {
  try {
    const server = await getServerById(serverId)
    if (!server) return { success: false, message: '服务器不存在' }
    if (!server.host || !server.port || !server.apiKey) {
      return { success: false, message: '服务器未配置完整（host/port/apiKey）' }
    }

    const tshockBase = `${server.host.startsWith('http') ? server.host : `http://${server.host}`}:${server.port}`
    const apiKey = server.apiKey

    if (url) {
      // 注册：把本后端 /hook/ 端点 + pushSecret 一起下发给插件
      const regUrl = `${tshockBase}/data/config/log-webhook/register?url=${encodeURIComponent(url)}&token=${encodeURIComponent(apiKey)}&secret=${encodeURIComponent(server.pushSecret || '')}`
      const res = await fetch(regUrl, { signal: AbortSignal.timeout(5000) })
      if (res.ok) {
        _registered.set(serverId, url)
        console.log(`[Webhook] ${server.name} 已注册: ${url}`)
        return { success: true, message: 'Webhook 已注册' }
      }
      return { success: false, message: `插件返回 ${res.status}` }
    } else {
      const unregUrl = `${tshockBase}/data/config/log-webhook/unregister?token=${encodeURIComponent(apiKey)}`
      const res = await fetch(unregUrl, { signal: AbortSignal.timeout(5000) })
      if (res.ok) {
        _registered.delete(serverId)
        console.log(`[Webhook] ${server.name} 已注销`)
        return { success: true, message: 'Webhook 已注销' }
      }
      return { success: false, message: `注销失败 ${res.status}` }
    }
  } catch (e) {
    console.warn(`[Webhook] 操作失败 (${serverId}): ${e.message}`)
    return { success: false, message: e.message }
  }
}

/**
 * 向全部启用服务器注册 webhook（后端启动时调用）
 * @param {number} actualPort 实际监听端口（端口容错后）
 */
export async function registerAllWebhooks(actualPort) {
  const cfg = await getConfig()
  const whCfg = cfg.logWebhook || {}
  if (!whCfg.enabled) {
    return { success: false, message: 'Webhook 未启用', registered: [] }
  }
  const servers = await getServers()
  const enabled = servers.filter(s => s.enabled && s.host && s.port && s.apiKey)
  const results = []
  for (const s of enabled) {
    const webhookUrl = whCfg.publicUrl || `http://127.0.0.1:${actualPort || cfg.server?.port || 3000}/hook/log`
    const r = await updatePluginWebhook(s.id, webhookUrl)
    if (r.success) results.push(s.id)
  }
  return { success: true, registered: results }
}

/**
 * 重新注册某台服务器当前 webhook（SSE 客户端连接时恢复推流 / 切换服务器后调用）
 */
export async function reRegisterWebhook(serverId) {
  const url = _registered.get(serverId)
  if (url) {
    console.log(`[Webhook] 重新注册 ${serverId}: ${url}`)
    return updatePluginWebhook(serverId, url)
  }
  const cfg = await getConfig()
  const whCfg = cfg.logWebhook || {}
  if (whCfg.enabled) {
    const server = await getServerById(serverId)
    if (server) {
      const url2 = whCfg.publicUrl || `http://127.0.0.1:${cfg.server?.port || 3000}/hook/log`
      console.log(`[Webhook] 首次注册 ${serverId}: ${url2}`)
      return updatePluginWebhook(serverId, url2)
    }
  }
  return { success: false, message: '无可用 webhook URL' }
}

/**
 * 获取当前已注册的 webhook URL（内存中）
 */
export function getCurrentWebhookUrl(serverId) {
  return _registered.get(serverId) || null
}

// 兼容旧调用（单服务器场景）：不指定 serverId 时返回当前实例的对应状态
export async function legacyGetCurrentWebhookUrl() {
  const { getCurrentServerId } = await import('./tshockService.js')
  const id = getCurrentServerId()
  return id ? _registered.get(id) || null : null
}

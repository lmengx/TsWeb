import { getConfig } from '../config.js'

let _currentUrl = null

/**
 * 向插件端注册或注销 webhook 地址
 * @param {string|null} url 要注册的 URL，传 null 或空字符串表示注销
 * @returns {Promise<{success:boolean, message:string}>}
 */
export async function updatePluginWebhook(url) {
  try {
    const cfg = await getConfig()
    if (!cfg.tshock?.host || !cfg.tshock?.port || !cfg.tshock?.apiKey) {
      return { success: false, message: 'TShock 未配置' }
    }

    const tshockBase = `${cfg.tshock.host.startsWith('http') ? cfg.tshock.host : `http://${cfg.tshock.host}`}:${cfg.tshock.port}`
    const apiKey = cfg.tshock.apiKey

    if (url) {
      // 注册
      const regUrl = `${tshockBase}/data/config/log-webhook/register?url=${encodeURIComponent(url)}&token=${encodeURIComponent(apiKey)}`
      const res = await fetch(regUrl)
      if (res.ok) {
        _currentUrl = url
        console.log(`[Webhook] 已注册: ${url}`)
        return { success: true, message: 'Webhook 已注册' }
      }
      return { success: false, message: `插件返回 ${res.status}` }
    } else {
      // 注销
      const unregUrl = `${tshockBase}/data/config/log-webhook/unregister?token=${encodeURIComponent(apiKey)}`
      const res = await fetch(unregUrl)
      if (res.ok) {
        _currentUrl = null
        console.log('[Webhook] 已注销')
        return { success: true, message: 'Webhook 已注销' }
      }
      return { success: false, message: `注销失败 ${res.status}` }
    }
  } catch (e) {
    console.warn(`[Webhook] 操作失败: ${e.message}`)
    return { success: false, message: e.message }
  }
}

/**
 * 获取当前已注册的 webhook URL（内存中）
 */
export function getCurrentWebhookUrl() {
  return _currentUrl
}

/**
 * 重新注册当前 webhook（用于 SSE 客户端连接时恢复推流）
 */
export async function reRegisterWebhook() {
  if (_currentUrl) {
    console.log(`[Webhook] 重新注册: ${_currentUrl}`)
    return updatePluginWebhook(_currentUrl)
  }
  // 尝试从配置构建 URL
  try {
    const cfg = await getConfig()
    const whCfg = cfg.logWebhook || {}
    if (whCfg.enabled && cfg.tshock?.host) {
      const url = whCfg.publicUrl || `http://127.0.0.1:${cfg.server?.port || 3000}/api/online/log-webhook`
      console.log(`[Webhook] 首次注册: ${url}`)
      return updatePluginWebhook(url)
    }
  } catch {}
  return { success: false, message: '无可用 webhook URL' }
}

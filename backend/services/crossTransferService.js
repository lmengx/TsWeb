/**
 * 跨服传送配置服务
 *
 * 配置权威源：后端 config.json 的 servers[i].crossTransfer（前端编辑）
 * 下发通道：插件端 REST /data/crosstransfer/*（GET + query 参数 + apiKey token）
 * 自动配对：目标条目 name/secret 为空时，按关联 serverId 从对端服务器的
 *           crossTransfer.selfServerId / selfSecret 自动带出（免人工复制密钥）。
 */
import { getServers } from '../config.js'

function buildBaseUrl(server) {
  if (server?.baseUrl) return server.baseUrl
  const host = server?.host || 'localhost'
  const h = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
  return `${h}:${server?.port || 7878}`
}

/** 调插件 REST（GET + query，token 鉴权），返回解析后的 JSON 或 null */
async function pluginFetch(server, path, params = {}) {
  if (!server?.apiKey) return null
  const q = new URLSearchParams(params)
  q.set('token', server.apiKey)
  const url = `${buildBaseUrl(server)}${path}?${q.toString()}`
  try {
    const res = await fetch(url, { method: 'GET', signal: AbortSignal.timeout(8000) })
    const text = await res.text()
    try { return JSON.parse(text) } catch { return { status: String(res.status), raw: text.slice(0, 200) } }
  } catch (e) {
    return null
  }
}

/** 读取插件端现有 CrossTransfer.json 配置（自动获取填充源）；离线/失败返回 null */
export async function readPluginConfig(server) {
  const data = await pluginFetch(server, '/data/crosstransfer/config')
  if (!data || typeof data !== 'object' || Array.isArray(data)) return null
  return data
}

/**
 * 探测目标服可达性（由插件端实际 TCP 探测，专线优先）
 * 入参英文键 [{name,host,port,dedicatedHost,dedicatedPort}] → 转插件中文键 JSON
 * 返回 { results: [{name,host,port,dedicatedHost,dedicatedPort,primaryOk,dedicatedOk}] } 或 { error }
 */
export async function probeFromPlugin(server, targets = []) {
  const payload = targets.map(t => ({
    '名称': t.name || '',
    '地址': t.host || '',
    '端口': parseInt(t.port) || 7777,
    '专线地址': t.dedicatedHost || '',
    '专线端口': parseInt(t.dedicatedPort) || 0
  }))
  const data = await pluginFetch(server, '/data/crosstransfer/probe', {
    targets: JSON.stringify(payload)
  })
  if (!data || !Array.isArray(data.results)) {
    return { error: data?.error || '探测失败（插件端不可用或未响应）' }
  }
  const results = data.results.map(r => ({
    name: r['名称'] || '',
    host: r['地址'] || '',
    port: r['端口'] || 0,
    dedicatedHost: r['专线地址'] || '',
    dedicatedPort: r['专线端口'] || 0,
    primaryOk: !!r['公网可达'],
    dedicatedOk: !!r['专线可达']
  }))
  return { results }
}

/**
 * 把后端 crossTransfer 转成插件 CrossTransfer.json 格式：
 * { 启用, 本服ID, 本服密钥, 目标服务器列表: [{名称,启用,地址,端口,专线地址,专线端口,协议版本,共享密钥,进服密码}] }
 * 自动配对规则：
 *  - 目标条目 name 为空且 serverId 关联到对端 → 用对端 crossTransfer.selfServerId
 *  - 目标条目 secret 为空且 serverId 关联到对端且对端已配置本服密钥 → 自动填入对端 selfSecret
 */
export async function buildPluginConfig(server, ct) {
  const allServers = await getServers()
  const targets = (ct.targets || []).map(t => {
    let name = t.name || ''
    let secret = t.secret || ''
    if (t.serverId) {
      const peer = allServers.find(s => s.id === t.serverId)
      if (peer?.crossTransfer) {
        if (!name) name = peer.crossTransfer.selfServerId || peer.name || ''
        if (!secret) secret = peer.crossTransfer.selfSecret || ''
      }
    }
    return {
      '名称': name,
      '启用': t.enabled !== false,
      '地址': t.host || '',
      '端口': parseInt(t.port) || 7777,
      '专线地址': t.dedicatedHost || null,
      '专线端口': parseInt(t.dedicatedPort) || 0,
      '协议版本': parseInt(t.version) || 319,
      '共享密钥': secret,
      '进服密码(可选)': t.password || null
    }
  })
  return {
    '启用': ct.enabled !== false,
    '本服ID': (ct.selfServerId || '').trim() || server?.name || '',
    '本服密钥': ct.selfSecret || '',
    '目标服务器列表': targets
  }
}

/** 下发插件端（全量覆盖 CrossTransfer.json 并热应用） */
export async function applyToPlugin(server, pluginConfig) {
  const data = await pluginFetch(server, '/data/crosstransfer/config/set', {
    config: JSON.stringify(pluginConfig)
  })
  if (!data) return { ok: false, error: '下发失败（插件端无响应）' }
  if (data.error) return { ok: false, error: data.error }
  return { ok: true, data }
}

export default { readPluginConfig, probeFromPlugin, buildPluginConfig, applyToPlugin }

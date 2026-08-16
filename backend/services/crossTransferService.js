/**
 * 跨服传送配置服务（单服直连模式）
 *
 * 不做后端全局存储：直接读写"当前服务器"（x-server-id）插件端的 CrossTransfer.json。
 * - 获取：GET 插件端 /data/crosstransfer/config
 * - 保存：前端表单 → 转插件格式（中文键）→ GET /data/crosstransfer/config/set 写插件端（热应用）
 * - 探测：GET /data/crosstransfer/probe 让插件端实际 TCP 探测目标服可达性
 */
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

/** 读取当前服务器插件端的跨服传送配置（中文键原始对象）；离线/失败返回 null */
export async function readPluginConfig(server) {
  const data = await pluginFetch(server, '/data/crosstransfer/config')
  if (!data || typeof data !== 'object' || Array.isArray(data)) return null
  return data
}

/**
 * 探测目标服可达性（插件端实际 TCP 探测）
 * 入参英文键 [{name,host,port}] → 转插件中文键 JSON
 * 返回 { results: [{name,host,port,ok}] } 或 { error }
 */
export async function probeFromPlugin(server, targets = []) {
  const payload = targets.map(t => ({
    '名称': t.name || '',
    '地址': t.host || '',
    '端口': parseInt(t.port) || 7777
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
    ok: !!r['可达']
  }))
  return { results }
}

/**
 * 把前端表单（英文键）转成插件 CrossTransfer.json 格式：
 * { 启用, 本服ID, 本服密钥, 目标服务器列表: [{名称,启用,地址,端口,协议版本,共享密钥,进服密码(可选)}] }
 */
export function buildPluginConfig(form) {
  const targets = (form.targets || []).map(t => ({
    '名称': t.name || '',
    '启用': t.enabled !== false,
    '地址': t.host || '',
    '端口': parseInt(t.port) || 7777,
    '协议版本': parseInt(t.version) || 319,
    '共享密钥': t.secret || '',
    '进服密码(可选)': t.password || null
  }))
  return {
    '启用': form.enabled !== false,
    '本服ID': (form.selfServerId || '').trim(),
    '本服密钥': form.selfSecret || '',
    '目标服务器列表': targets
  }
}

/** 写入插件端（全量覆盖 CrossTransfer.json 并热应用） */
export async function applyToPlugin(server, pluginConfig) {
  const data = await pluginFetch(server, '/data/crosstransfer/config/set', {
    config: JSON.stringify(pluginConfig)
  })
  if (!data) return { ok: false, error: '保存失败（插件端无响应）' }
  if (data.error) return { ok: false, error: data.error }
  return { ok: true, data }
}

export default { readPluginConfig, probeFromPlugin, buildPluginConfig, applyToPlugin }

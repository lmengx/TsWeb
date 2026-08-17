/**
 * 简易世界修改器服务（单服直连模式）
 *
 * 后端不存配置：直接读写"当前服务器"（x-server-id）插件端的 /data/worldmodify/* REST。
 * - 查看：GET  /data/worldmodify/status  （权限 worldinfo）
 * - 修改：GET  /data/worldmodify/apply?fields={json}  （权限 worldmodify，广播 WorldInfo 即时生效）
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

/** 读取当前服务器全部世界字段当前值 + 字段元数据；离线/失败返回 null */
export async function readStatus(server) {
  const data = await pluginFetch(server, '/data/worldmodify/status')
  if (!data || typeof data !== 'object' || Array.isArray(data)) return null
  if (data.error) return { error: data.error }
  return data
}

/** 应用字段修改（fields 为 { 字段名: 值 }），返回插件端结果或 { error } */
export async function applyFields(server, fields) {
  const data = await pluginFetch(server, '/data/worldmodify/apply', {
    fields: JSON.stringify(fields)
  })
  if (!data) return { error: '应用失败（插件端无响应）' }
  if (data.error) return { error: data.error }
  return data
}

export default { readStatus, applyFields }

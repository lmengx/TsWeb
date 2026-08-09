// 当前服务器状态管理（前端全局切换）
// 后端不维护 currentServerId，前端通过 x-server-id header 在每次请求时指定目标服务器
const STORAGE_KEY = 'tsweb.currentServerId'

let servers = []          // 缓存服务器列表
let currentServerId = localStorage.getItem(STORAGE_KEY) || null

export function getCurrentServerId() {
  return currentServerId
}

export function getServers() {
  return servers
}

export function getCurrentServer() {
  return servers.find(s => s.id === currentServerId) || null
}

export function selectServer(id) {
  currentServerId = id
  if (id) {
    localStorage.setItem(STORAGE_KEY, id)
  } else {
    localStorage.removeItem(STORAGE_KEY)
  }
}

export function isCurrentServerConnected() {
  const s = getCurrentServer()
  return !!s && !!s.connected
}

/** 拉取服务器列表（/api/status 无认证要求，供守卫/顶栏使用） */
export async function fetchServers() {
  try {
    const res = await fetch('/api/status')
    const data = await res.json()
    servers = data.servers || []
    // 当前服务器不存在时自动选中第一个
    if (!servers.find(s => s.id === currentServerId) && servers.length > 0) {
      selectServer(servers[0].id)
    } else if (servers.length === 0) {
      selectServer(null)
    }
    return servers
  } catch (e) {
    servers = []
    return []
  }
}

/** 初始化：登录后调用，确保有默认服务器 */
export async function initServerStore() {
  await fetchServers()
  return currentServerId
}

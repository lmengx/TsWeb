import { getCurrentServer } from './tshockService.js'

// 从请求级上下文获取当前目标服务器（由 x-server-id header 决定）
const getEndpoint = () => {
  const server = getCurrentServer()
  return server ? { baseUrl: server.baseUrl, apiKey: server.apiKey } : { baseUrl: null, apiKey: '' }
}

const tshockFetch = async (path, method = 'GET') => {
  const { baseUrl, apiKey } = getEndpoint()
  if (!baseUrl) return { error: '当前服务器未配置或未选择' }
  const url = `${baseUrl}${path}${path.includes('?') ? '&' : '?'}token=${encodeURIComponent(apiKey)}`
  console.log(`[OUTGOING] ${method} ${url}`)
  try {
    const res = await fetch(url, { method, headers: { 'Accept': 'application/json' } })
    const text = await res.text()
    try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', raw: text } }
  } catch (error) {
    return { error: error.message }
  }
}

class OnlineService {
  async getHourlyOnline(date) {
    return tshockFetch(`/data/online/hourly?date=${encodeURIComponent(date)}`)
  }

  async getRanking(mode = 'today') {
    return tshockFetch(`/data/online/ranking?mode=${encodeURIComponent(mode)}`)
  }

  async getPlayerCalendar(name, year) {
    return tshockFetch(`/data/online/player?name=${encodeURIComponent(name)}&year=${year}`)
  }

  async getRankingStats(type, page = 1, pageSize = 10) {
    return tshockFetch(`/data/online/ranking/stats?type=${encodeURIComponent(type)}&page=${encodeURIComponent(page)}&pageSize=${encodeURIComponent(pageSize)}`)
  }

  async execCommand(cmd, executor = 'SSE-Console') {
    return tshockFetch(`/data/online/log/command?cmd=${encodeURIComponent(cmd)}&executor=${encodeURIComponent(executor)}`)
  }

  /**
   * 获取 SSE 流的 URL（前端直接连接用）
   */
  getSSEUrl() {
    // 前端通过后端代理 SSE
    return `/api/online/log/stream`
  }
}

export default new OnlineService()

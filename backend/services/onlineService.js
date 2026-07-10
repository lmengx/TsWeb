import { getConfig, onConfigUpdate } from '../config.js'

let baseUrl = 'http://localhost:7878'
let apiKey = ''

class OnlineService {
  constructor() {
    onConfigUpdate((config) => {
      const host = config.tshock?.host || 'localhost'
      const h = host.startsWith('http') ? host : `http://${host}`
      baseUrl = `${h}:${config.tshock?.port || 7878}`
      apiKey = config.tshock?.apiKey || ''
    })
  }

  async init() {
    const config = await getConfig()
    const host = config.tshock?.host || 'localhost'
    const h = host.startsWith('http') ? host : `http://${host}`
    baseUrl = `${h}:${config.tshock?.port || 7878}`
    apiKey = config.tshock?.apiKey || ''
  }

  async getHourlyOnline(date) {
    await this.init()
    const url = `${baseUrl}/data/online/hourly?date=${encodeURIComponent(date)}&token=${encodeURIComponent(apiKey)}`
    console.log(`[OUTGOING] GET ${url}`)
    try {
      const res = await fetch(url, { headers: { 'Accept': 'application/json' } })
      const text = await res.text()
      console.log(`[RESPONSE] Status: ${res.status}`)
      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', raw: text }
      }
    } catch (error) {
      return { error: error.message }
    }
  }

  async getRanking(mode = 'today') {
    await this.init()
    const url = `${baseUrl}/data/online/ranking?mode=${encodeURIComponent(mode)}&token=${encodeURIComponent(apiKey)}`
    console.log(`[OUTGOING] GET ${url}`)
    try {
      const res = await fetch(url, { headers: { 'Accept': 'application/json' } })
      const text = await res.text()
      console.log(`[RESPONSE] Status: ${res.status}`)
      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', raw: text }
      }
    } catch (error) {
      return { error: error.message }
    }
  }

  async getPlayerCalendar(name, year) {
    await this.init()
    const url = `${baseUrl}/data/online/player?name=${encodeURIComponent(name)}&year=${year}&token=${encodeURIComponent(apiKey)}`
    console.log(`[OUTGOING] GET ${url}`)
    try {
      const res = await fetch(url, { headers: { 'Accept': 'application/json' } })
      const text = await res.text()
      console.log(`[RESPONSE] Status: ${res.status}`)
      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', raw: text }
      }
    } catch (error) {
      return { error: error.message }
    }
  }

  async getRankingStats(type, page = 1, pageSize = 10) {
    await this.init()
    const url = `${baseUrl}/data/online/ranking/stats?type=${encodeURIComponent(type)}&page=${encodeURIComponent(page)}&pageSize=${encodeURIComponent(pageSize)}&token=${encodeURIComponent(apiKey)}`
    console.log(`[OUTGOING] GET ${url}`)
    try {
      const res = await fetch(url, { headers: { 'Accept': 'application/json' } })
      const text = await res.text()
      console.log(`[RESPONSE] Status: ${res.status}`)
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', raw: text } }
    } catch (error) {
      return { error: error.message }
    }
  }

  async execCommand(cmd) {
    await this.init()
    const url = `${baseUrl}/data/online/log/command?cmd=${encodeURIComponent(cmd)}&token=${encodeURIComponent(apiKey)}`
    try {
      const res = await fetch(url, { headers: { 'Accept': 'application/json' } })
      const text = await res.text()
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', raw: text } }
    } catch (error) {
      return { error: error.message }
    }
  }

  /**
   * 获取 SSE 流的 URL（前端直接连接用）
   */
  getSSEUrl() {
    // 前端通过后端代理 SSE，或者直接连接 TShock 端口
    return `/api/online/log/stream`
  }
}

export default new OnlineService()

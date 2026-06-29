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
}

export default new OnlineService()

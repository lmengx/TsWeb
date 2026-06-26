import { getConfig, onConfigUpdate } from '../config.js'

let TSHOCK_HOST = 'http://localhost'
let TSHOCK_PORT = 7878
let TSHOCK_API_KEY = ''

export class TShockService {
  constructor() {
    this.baseUrl = null
    this.apiKey = ''
    this.isConnected = false

    onConfigUpdate((config) => {
      this.reloadConfig(config)
    })
  }

  async init() {
    const config = await getConfig()
    this.reloadConfig(config)
  }

  reloadConfig(config) {
    const host = config.tshock?.host || 'localhost'
    TSHOCK_HOST = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
    TSHOCK_PORT = config.tshock?.port || 7878
    TSHOCK_API_KEY = config.tshock?.apiKey || ''
    this.baseUrl = `${TSHOCK_HOST}:${TSHOCK_PORT}`
    this.apiKey = TSHOCK_API_KEY
    this.isConnected = false
    console.log(`[Config] TShock config updated: ${this.baseUrl}`)
  }

  async testConnection() {
    if (!this.baseUrl) {
      await this.init()
    }

    const url = `${this.baseUrl}/tokentest?token=${encodeURIComponent(this.apiKey)}`

    console.log(`[OUTGOING] Testing TShock connection: GET ${url}`)

    try {
      const controller = new AbortController()
      const timeoutId = setTimeout(() => controller.abort(), 3000)

      const response = await fetch(url, {
        method: 'GET',
        headers: { 'Accept': 'application/json' },
        signal: controller.signal
      })

      clearTimeout(timeoutId)
      console.log(`[RESPONSE] Status: ${response.status}`)

      if (response.status === 200) {
        this.isConnected = true
        return { success: true, message: 'Connected to TShock server' }
      } else {
        this.isConnected = false
        return { success: false, message: `Connection failed: ${response.status}` }
      }
    } catch (error) {
      this.isConnected = false
      return { success: false, message: `Connection error: ${error.message}` }
    }
  }

  getConnectionStatus() {
    return this.isConnected
  }

  async executeCommand(command) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/v3/server/rawcmd?cmd=${encodeURIComponent(command)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getUsers() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/v2/users/list`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getActiveUsers() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/v2/users/activelist`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getInventory(username) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/invsee?player=${encodeURIComponent(username)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        const data = JSON.parse(text)
        return data
      } catch {
        return { status: response.status, raw: text }
      }
    } catch (error) {
      this.isConnected = false
      return { status: 'error', error: error.message }
    }
  }

  async getUserList(username = null) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/query_detail`
    if (username) {
      url += `?username=${encodeURIComponent(username)}`
    }
    if (this.apiKey) {
      url += (username ? '&' : '?') + `token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async checkDuplicateIPs(username) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/duplicateips?username=${encodeURIComponent(username)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getAllDuplicateIPs() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/allduplicateips`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      return text
    } catch (error) {
      this.isConnected = false
      return `Error: ${error.message}`
    }
  }

  async editInventory(player, slotIndex, netID, stack, prefix) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/editinv?player=${encodeURIComponent(player)}&index=${slotIndex}&netID=${netID}&stack=${stack}&prefix=${prefix}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getGroups() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/groups/list`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getGroup(groupName) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/groups/get?groupName=${encodeURIComponent(groupName)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async createGroup(groupName, parent, commands, chatColor, prefix, suffix) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/groups/create?groupName=${encodeURIComponent(groupName)}`
    if (parent) url += `&parent=${encodeURIComponent(parent)}`
    if (commands) url += `&commands=${encodeURIComponent(commands)}`
    if (chatColor) url += `&chatColor=${encodeURIComponent(chatColor)}`
    if (prefix) url += `&prefix=${encodeURIComponent(prefix)}`
    if (suffix) url += `&suffix=${encodeURIComponent(suffix)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async updateGroup(groupName, parent, chatColor, prefix, suffix) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/groups/update?groupName=${encodeURIComponent(groupName)}`
    if (parent !== undefined) url += `&parent=${encodeURIComponent(parent)}`
    if (chatColor !== undefined) url += `&chatColor=${encodeURIComponent(chatColor)}`
    if (prefix !== undefined) url += `&prefix=${encodeURIComponent(prefix)}`
    if (suffix !== undefined) url += `&suffix=${encodeURIComponent(suffix)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async deleteGroup(groupName) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/groups/delete?groupName=${encodeURIComponent(groupName)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async addGroupPermission(groupName, permission) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/groups/permission/add?groupName=${encodeURIComponent(groupName)}&permission=${encodeURIComponent(permission)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async removeGroupPermission(groupName, permission) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/groups/permission/remove?groupName=${encodeURIComponent(groupName)}&permission=${encodeURIComponent(permission)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async banPlayer(name, reason) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/ban?name=${encodeURIComponent(name)}`
    if (reason) url += `&reason=${encodeURIComponent(reason)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getUserPassword(username) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/getpassword?username=${encodeURIComponent(username)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        const data = JSON.parse(text)
        if (response.status !== 200 || data.error) {
          return { error: data.error || `HTTP ${response.status}` }
        }
        return data
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }



  async getBossProgress() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/boss/progress`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getBanList() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/v2/bans/list`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getProjConfig() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/anticheat/proj-config/getprojconfig`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async saveProjConfig(config) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/anticheat/proj-config/saveprojconfig?config=${encodeURIComponent(JSON.stringify(config))}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] POST ${url}`)

    try {
      const response = await fetch(url, {
        method: 'POST',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getItemConfig() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/anticheat/item-config/getitemconfig`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async saveItemConfig(config) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/anticheat/item-config/saveitemconfig?config=${encodeURIComponent(JSON.stringify(config))}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] POST ${url}`)

    try {
      const response = await fetch(url, {
        method: 'POST',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async checkAnomalyItem(id, stack) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/anticheat/check-anomaly?id=${id}&stack=${stack}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getAntiCheatConfig() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/anticheat/config`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async scanItems() {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/anticheat/item-config/scanall`
    if (this.apiKey) {
      url += `?token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getPlayerStats(player) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/stats?player=${encodeURIComponent(player)}`
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url}`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async setPlayerStats(player, stats) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/stats/set?player=${encodeURIComponent(player)}`
    
    for (const [key, value] of Object.entries(stats)) {
      url += `&${key}=${encodeURIComponent(value)}`
    }
    
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] POST ${url}`)

    try {
      const response = await fetch(url, {
        method: 'POST',
        headers
      })

      console.log(`[RESPONSE] Status: ${response.status}`)
      const text = await response.text()
      console.log(`[RESPONSE] Body: ${text}`)

      try {
        return JSON.parse(text)
      } catch {
        return { error: 'Invalid JSON', rawResponse: text }
      }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async getTsWebConfig() {
    if (!this.baseUrl) await this.init()
    const url = `${this.baseUrl}/data/config/tsweb${this.apiKey ? `?token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] GET ${url}`)
    try {
      const response = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } })
      return await response.json()
    } catch (error) {
      return { status: '500', error: error.message }
    }
  }

  async setTsWebConfig(params) {
    if (!this.baseUrl) await this.init()
    const query = Object.entries(params).map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&')
    const url = `${this.baseUrl}/data/config/tsweb/set?${query}${this.apiKey ? `&token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] POST ${url}`)
    try {
      const response = await fetch(url, { method: 'POST', headers: { 'Accept': 'application/json' } })
      return await response.json()
    } catch (error) {
      return { status: '500', error: error.message }
    }
  }
}

export default new TShockService()
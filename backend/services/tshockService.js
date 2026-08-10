import { AsyncLocalStorage } from 'async_hooks'

function buildBaseUrl(server) {
  const host = server?.host || 'localhost'
  const h = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
  return `${h}:${server?.port || 7878}`
}

export class TShockService {
  constructor(server = null) {
    this.id = server?.id || null
    this.name = server?.name || ''
    this.baseUrl = server ? buildBaseUrl(server) : null
    this.apiKey = server?.apiKey || ''
    this.isConnected = false
    this.retryTimer = null
  }

  startAutoRetry() {
    this.stopAutoRetry()
    this.testConnection()
    // 无论在线/离线都定期探活：在线时保活（掉线及时转灰），离线时自动重连（恢复及时转绿）
    this.retryTimer = setInterval(() => {
      this.testConnection().catch(() => {})
    }, 15000)
  }

  stopAutoRetry() {
    if (this.retryTimer) {
      clearInterval(this.retryTimer)
      this.retryTimer = null
    }
  }

  async init() {
    // 实例已自带连接配置（构造/注册时传入），无需全局初始化
    return this
  }

  /** 更新实例连接配置（服务器编辑后调用） */
  reloadConfig(server) {
    this.id = server.id || this.id
    this.name = server.name || this.name
    this.baseUrl = buildBaseUrl(server)
    this.apiKey = server.apiKey || ''
    this.isConnected = false
    console.log(`[Config] TShock 实例已更新: ${this.baseUrl} (${this.id})`)
    this.stopAutoRetry()
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

  async testConnectionWith(host, port, apiKey) {
    // 委托给模块级独立测试函数（不依赖实例，供添加向导"仅测试"复用）
    return testConnectionWith(host, port, apiKey)
  }

  getConnectionStatus() {
    return this.isConnected
  }

  async clearCharacter(account) {
    if (!this.baseUrl) await this.init()

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/clearcharacter?account=${encodeURIComponent(account)}`
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

  async clearAllCharacter(username, password) {
    if (!this.baseUrl) await this.init()

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/clearallcharacter?username=${encodeURIComponent(username)}&password=${encodeURIComponent(password)}`
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

  async getUserData(username = null) {
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

  async batchEdit(player, data, clearUnspecified = false) {
    if (!this.baseUrl) {
      await this.init()
    }

    // 只传非空物品的 slot/netId/stack/prefix，减少 URL 长度
    const invCompact = (data.inventory || []).map(i => `${i.slot},${i.netId},${i.stack},${i.prefix}`).join('|')
    const statsStr = JSON.stringify(data.stats || {})

    let url = `${this.baseUrl}/data/users/batch-edit?player=${encodeURIComponent(player)}&stats=${encodeURIComponent(statsStr)}&inv=${encodeURIComponent(invCompact)}`
    if (clearUnspecified) {
      url += `&clear=1`
    }
    if (this.apiKey) {
      url += `&token=${encodeURIComponent(this.apiKey)}`
    }

    console.log(`[OUTGOING] GET ${url.substring(0, 500)}...`)

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers: { 'Accept': 'application/json' }
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

  async banPlayer(name, reason, character = null) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/ban?name=${encodeURIComponent(name)}`
    if (reason) url += `&reason=${encodeURIComponent(reason)}`
    if (character) url += `&character=${encodeURIComponent(character)}`
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

  async unbanPlayer(ticket, fullDelete = true) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/users/unban?ticket=${encodeURIComponent(ticket)}&fullDelete=${fullDelete}`
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

  async createUser(username, password, group) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/v2/users/create?user=${encodeURIComponent(username)}&password=${encodeURIComponent(password)}`
    if (group) url += `&group=${encodeURIComponent(group)}`
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

  async scanItemById(itemId) {
    if (!this.baseUrl) {
      await this.init()
    }

    const headers = {
      'Accept': 'application/json'
    }

    let url = `${this.baseUrl}/data/anticheat/item-config/scan-by-id?itemId=${encodeURIComponent(itemId)}`
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

  async getBossLimitStatus() {
    if (!this.baseUrl) await this.init()
    const url = `${this.baseUrl}/data/bosslimit/status${this.apiKey ? `?token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] GET ${url}`)
    try {
      const response = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } })
      return await response.json()
    } catch (error) {
      return { status: '500', error: error.message }
    }
  }

  async getBossConfig() {
    if (!this.baseUrl) await this.init()
    const url = `${this.baseUrl}/data/config/boss${this.apiKey ? `?token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] GET ${url}`)
    try {
      const response = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } })
      return await response.json()
    } catch (error) {
      return { status: '500', error: error.message }
    }
  }

  async setBossConfig(params) {
    if (!this.baseUrl) await this.init()
    const query = Object.entries(params).map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&')
    const url = `${this.baseUrl}/data/config/boss/set?${query}${this.apiKey ? `&token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] POST ${url}`)
    try {
      const response = await fetch(url, { method: 'POST', headers: { 'Accept': 'application/json' } })
      return await response.json()
    } catch (error) {
      return { status: '500', error: error.message }
    }
  }

  // ===== 文件管理 =====

  async fileRead(relativePath) {
    if (!this.baseUrl) await this.init()
    const url = `${this.baseUrl}/data/files/read?path=${encodeURIComponent(relativePath)}${this.apiKey ? `&token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] POST ${url}`)
    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Accept': 'application/json' }
      })
      const text = await response.text()
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', rawResponse: text } }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async fileWrite(relativePath, content) {
    if (!this.baseUrl) await this.init()
    const url = `${this.baseUrl}/data/files/write?path=${encodeURIComponent(relativePath)}&content=${encodeURIComponent(content)}${this.apiKey ? `&token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] POST ${url}`)
    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Accept': 'application/json' }
      })
      const text = await response.text()
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', rawResponse: text } }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async fileList(relativePath) {
    if (!this.baseUrl) await this.init()
    const url = `${this.baseUrl}/data/files/list?path=${encodeURIComponent(relativePath)}${this.apiKey ? `&token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] POST ${url}`)
    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Accept': 'application/json' }
      })
      const text = await response.text()
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', rawResponse: text } }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async fileDelete(relativePath) {
    if (!this.baseUrl) await this.init()
    const url = `${this.baseUrl}/data/files/delete?path=${encodeURIComponent(relativePath)}${this.apiKey ? `&token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] POST ${url}`)
    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Accept': 'application/json' }
      })
      const text = await response.text()
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', rawResponse: text } }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  /**
   * 分片上传：data 为 base64 片段；append=true 追加到文件末尾（非首片）
   */
  async fileUpload(relativePath, dataBase64, append = false) {
    if (!this.baseUrl) await this.init()
    const url = `${this.baseUrl}/data/files/upload${this.apiKey ? `?token=${encodeURIComponent(this.apiKey)}` : ''}`
    const body = new URLSearchParams({
      path: relativePath,
      data: dataBase64,
      append: append ? '1' : '0'
    })
    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: body.toString()
      })
      const text = await response.text()
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', rawResponse: text } }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  // ===== 权限提升配置 =====

  async getPromotionConfig() {
    if (!this.baseUrl) await this.init()
    const url = `${this.baseUrl}/data/promotion/config${this.apiKey ? `?token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] GET ${url}`)
    try {
      const response = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } })
      const text = await response.text()
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', rawResponse: text } }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  async setPromotionConfig(params) {
    if (!this.baseUrl) await this.init()
    const query = Object.entries(params).map(([k, v]) => {
      const val = typeof v === 'object' ? JSON.stringify(v) : String(v)
      return `${k}=${encodeURIComponent(val)}`
    }).join('&')
    const url = `${this.baseUrl}/data/promotion/config/set?${query}${this.apiKey ? `&token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] POST ${url.substring(0, 600)}`)
    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Accept': 'application/json' }
      })
      const text = await response.text()
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', rawResponse: text } }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }

  // ===== 通用数据代理：/data/tasks/* 等自定义端点 =====

  async proxyDataRequest(subPath, method = 'GET', params = {}) {
    if (!this.baseUrl) await this.init()

    const query = Object.entries(params).map(([k, v]) => {
      const val = typeof v === 'object' ? JSON.stringify(v) : String(v)
      return `${k}=${encodeURIComponent(val)}`
    }).join('&')

    const url = `${this.baseUrl}/data/${subPath}${query ? `?${query}` : ''}${this.apiKey ? `${query ? '&' : '?'}token=${encodeURIComponent(this.apiKey)}` : ''}`
    console.log(`[OUTGOING] ${method} ${url.substring(0, 600)}`)

    try {
      const response = await fetch(url, {
        method,
        headers: { 'Accept': 'application/json' }
      })
      const text = await response.text()
      try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', rawResponse: text } }
    } catch (error) {
      this.isConnected = false
      return { error: error.message }
    }
  }
}

/** 独立连接测试（不依赖服务器实例）：添加向导"仅测试"用 */
export async function testConnectionWith(host, port, apiKey) {
  const baseUrl = `${host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`}:${port}`
  const url = `${baseUrl}/tokentest?token=${encodeURIComponent(apiKey)}`

  console.log(`[OUTGOING] Testing TShock connection (temp): GET ${url}`)

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
      return { success: true }
    } else if (response.status === 401 || response.status === 403) {
      return { success: false, type: 'auth', status: response.status, error: `TShock REST 接口返回 ${response.status}，API 密钥无效或权限不足` }
    } else if (response.status === 404) {
      return { success: false, type: 'notfound', status: response.status, error: `TShock REST 接口返回 ${response.status}，接口路径可能不正确` }
    } else {
      return { success: false, type: 'unknown', status: response.status, error: `TShock REST 接口返回 ${response.status}` }
    }
  } catch (error) {
    console.log(`[RESPONSE] Error: ${error.message}`)
    if (error.name === 'AbortError') {
      return { success: false, type: 'timeout', status: 0, error: '连接超时（3秒），目标服务器无响应，请确认地址和端口是否正确' }
    }
    if (error.code === 'ECONNREFUSED') {
      return { success: false, type: 'refused', status: 0, error: '连接被拒绝（ECONNREFUSED），目标服务器未启动或端口错误' }
    }
    if (error.code === 'ENOTFOUND' || error.code === 'EAI_AGAIN') {
      return { success: false, type: 'dns', status: 0, error: '无法解析主机名（' + error.code + '），请检查地址是否正确' }
    }
    return { success: false, type: 'error', status: 0, error: '连接失败：请确认目标服务器已开启并且监听对应 REST 端口（' + error.message + '）' }
  }
}

// ═══════════════════════════════════════════════════════════
// 服务器实例注册表 + 请求级上下文（无全局 currentServerId）
// 当前目标服务器由每个请求的 x-server-id header 决定，
// 通过 AsyncLocalStorage 绑定到该请求的上下文，controller 零改动
// ═══════════════════════════════════════════════════════════

const serverContext = new AsyncLocalStorage()
const serverInstances = new Map()

/** 注册/更新服务器实例（config servers[] 变更时同步调用） */
export function registerServer(server) {
  let inst = serverInstances.get(server.id)
  if (inst) {
    inst.reloadConfig(server)
    // reloadConfig 内会 stopAutoRetry 并重置 isConnected，必须重新启动心跳
    inst.startAutoRetry()
    return inst
  }
  inst = new TShockService(server)
  serverInstances.set(server.id, inst)
  inst.startAutoRetry()
  return inst
}

/** 注销服务器实例 */
export function unregisterServer(id) {
  const inst = serverInstances.get(id)
  if (inst) {
    inst.stopAutoRetry()
    serverInstances.delete(id)
  }
}

/** 获取指定服务器实例（不依赖请求上下文，供注册/状态查询用） */
export function getServerInstance(id) {
  return serverInstances.get(id) || null
}

/** 全部实例状态（/api/status 用） */
export function getServicesStatus() {
  return [...serverInstances.values()].map(s => ({
    id: s.id,
    name: s.name,
    connected: s.isConnected
  }))
}

/** 在指定服务器上下文内执行（中间件用：runWithServer(serverId, () => next())） */
export function runWithServer(serverId, fn) {
  return serverContext.run(serverId, fn)
}

/** 当前请求绑定的服务器 id（无则 null） */
export function getCurrentServerId() {
  return serverContext.getStore() || null
}

/** 当前请求绑定的服务器实例（无则 null） */
export function getCurrentServer() {
  const id = serverContext.getStore()
  if (!id) return null
  return serverInstances.get(id) || null
}

// 默认导出：Proxy 转发到当前请求的服务器实例
// 无请求上下文（如后端启动阶段）→ 回退到空实例（方法返回未配置错误）
const fallback = new TShockService()
const instanceProxy = new Proxy(fallback, {
  get(target, prop, receiver) {
    const current = getCurrentServer()
    if (current && typeof current[prop] === 'function') {
      return current[prop].bind(current)
    }
    if (typeof target[prop] === 'function') {
      return target[prop].bind(target)
    }
    return current && prop in current ? current[prop] : target[prop]
  }
})

export default instanceProxy
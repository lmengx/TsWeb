import { getConfig } from '../config.js'

let TSHOCK_HOST = 'http://localhost'
let TSHOCK_PORT = 7878
let TSHOCK_API_KEY = ''

export class TShockService {
  constructor() {
    this.baseUrl = null
    this.apiKey = ''
  }

  async init() {
    const config = await getConfig()
    TSHOCK_HOST = config.tshock?.host || 'http://localhost'
    TSHOCK_PORT = config.tshock?.port || 7878
    TSHOCK_API_KEY = config.tshock?.apiKey || ''
    this.baseUrl = `${TSHOCK_HOST}:${TSHOCK_PORT}`
    this.apiKey = TSHOCK_API_KEY
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
      
      return text
    } catch (error) {
      return `Error: ${error.message}`
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
      return { error: error.message }
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
      return { error: error.message }
    }
  }
}

export default new TShockService()

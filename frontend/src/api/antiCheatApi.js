import { get, post } from '../utils/api.js'

let cachedConfig = null
let cachedProjConfig = null
let cachedItemConfig = null

export async function getAntiCheatConfig() {
  if (cachedConfig) {
    return cachedConfig
  }

  try {
    const response = await get('/api/anticheat/config')
    const data = await response.json()
    if (data.status === 200 || data.status === '200') {
      cachedConfig = data.config?.config || data.config
      return cachedConfig
    }
    return null
  } catch (error) {
    console.error('Failed to get anti-cheat config:', error)
    return null
  }
}

export async function getProjConfig() {
  if (cachedProjConfig) {
    return cachedProjConfig
  }

  try {
    const response = await get('/api/anticheat/proj-config')
    const data = await response.json()
    console.log('getProjConfig response:', data)
    if (data.status === 200 || data.status === '200') {
      cachedProjConfig = data.config?.config || data.config
      console.log('getProjConfig cachedProjConfig:', cachedProjConfig)
      return cachedProjConfig
    }
    return null
  } catch (error) {
    console.error('Failed to get projectile anti-cheat config:', error)
    return null
  }
}

export async function saveProjConfig(config) {
  try {
    const response = await post('/api/anticheat/proj-config', config)
    const data = await response.json()
    if (data.status === 200 || data.status === '200') {
      cachedProjConfig = config
      return { success: true }
    }
    return { success: false, error: data.error }
  } catch (error) {
    console.error('Failed to save projectile anti-cheat config:', error)
    return { success: false, error: error.message }
  }
}

export function clearAntiCheatCache() {
  cachedConfig = null
}

/**
 * 打开反作弊（启用检测开关 关→开 时调用）：
 * 后端在插件端无有效配置时下发默认配置（启用=true），已有配置则仅翻转开关。
 * @param {boolean} item 是否启用物品检测
 * @param {boolean} proj 是否启用弹幕检测
 */
export async function enableAntiCheat(item = false, proj = false) {
  try {
    const response = await post('/api/anticheat/enable', { item, proj })
    const data = await response.json()
    return data
  } catch (error) {
    console.error('Failed to enable anti-cheat:', error)
    return { status: 'error', error: error.message }
  }
}

export function clearProjCache() {
  cachedProjConfig = null
}

export async function checkAnomalyItem(id, stack) {
  try {
    const response = await post('/api/anticheat/check-anomaly', { id, stack })
    const data = await response.json()
    if (data.status === 200 || data.status === '200') {
      return data
    }
    return { isAnomaly: false, itemName: null }
  } catch (error) {
    console.error('Failed to check anomaly:', error)
    return { isAnomaly: false, itemName: null }
  }
}

export async function getItemConfig() {
  if (cachedItemConfig) {
    return cachedItemConfig
  }

  try {
    const response = await get('/api/anticheat/item-config')
    const data = await response.json()
    if (data.status === 200 || data.status === '200') {
      cachedItemConfig = data.config?.config || data.config
      return cachedItemConfig
    }
    return null
  } catch (error) {
    console.error('Failed to get item anti-cheat config:', error)
    return null
  }
}

export async function saveItemConfig(config) {
  try {
    const response = await post('/api/anticheat/item-config', config)
    const data = await response.json()
    if (data.status === 200 || data.status === '200') {
      cachedItemConfig = config
      return { success: true }
    }
    return { success: false, error: data.error }
  } catch (error) {
    console.error('Failed to save item anti-cheat config:', error)
    return { success: false, error: error.message }
  }
}

export function clearItemCache() {
  cachedItemConfig = null
}

export async function scanItems() {
  try {
    const response = await post('/api/tshock/itemscan')
    const data = await response.json()
    return data
  } catch (error) {
    console.error('Failed to scan items:', error)
    return { status: 'error', error: error.message }
  }
}

export async function scanItemById(itemId) {
  try {
    const response = await post('/api/tshock/itemscan-by-id', { itemId })
    const data = await response.json()
    return data
  } catch (error) {
    console.error('Failed to scan item by id:', error)
    return { status: 'error', error: error.message }
  }
}
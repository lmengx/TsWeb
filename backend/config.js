import fs from 'fs/promises'
import path from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const CONFIG_PATH = path.join(__dirname, 'config', 'config.json')

let config = null
const configUpdateListeners = new Set()

export async function loadConfig() {
  if (!config) {
    try {
      const content = await fs.readFile(CONFIG_PATH, 'utf8')
      config = JSON.parse(content)
    } catch {
      config = null
    }
  }
  return config
}

export async function getConfig() {
  return await loadConfig()
}

export function onConfigUpdate(callback) {
  configUpdateListeners.add(callback)
  return () => configUpdateListeners.delete(callback)
}

function notifyConfigUpdate() {
  configUpdateListeners.forEach(callback => callback(config))
}

export async function saveConfig(newConfig) {
  const content = await fs.readFile(CONFIG_PATH, 'utf8')
  config = { ...JSON.parse(content), ...newConfig }
  await fs.writeFile(CONFIG_PATH, JSON.stringify(config, null, 2), 'utf8')
  notifyConfigUpdate()
  return config
}

export async function saveNewConfig(setupData) {
  config = {
    server: { port: 3000 },
    tshock: {
      host: setupData.host || '',
      port: parseInt(setupData.port) || 7878,
      apiKey: setupData.apiKey || ''
    },
    security: {
      jwtSecret: generateSecret(),
      tokenExpire: '24h',
      challengeExpire: 120000
    }
  }

  try {
    await fs.mkdir(path.dirname(CONFIG_PATH), { recursive: true })
    await fs.writeFile(CONFIG_PATH, JSON.stringify(config, null, 2), 'utf8')
  } catch (err) {
    throw new Error('无法写入配置文件: ' + err.message)
  }

  notifyConfigUpdate()
  return config
}

function generateSecret() {
  const chars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()'
  let secret = ''
  for (let i = 0; i < 48; i++) {
    secret += chars.charAt(Math.floor(Math.random() * chars.length))
  }
  return secret
}

export async function getTshockConfig() {
  const cfg = await loadConfig()
  if (!cfg) return { host: '', port: 0, apiKey: '' }
  return {
    host: cfg.tshock?.host || '',
    port: cfg.tshock?.port || 0,
    apiKey: cfg.tshock?.apiKey || ''
  }
}

export async function updateTshockConfig(tshockConfig) {
  const cfg = await loadConfig()
  
  if (tshockConfig.host !== undefined || tshockConfig.port !== undefined || tshockConfig.apiKey !== undefined) {
    if (!cfg.tshock) cfg.tshock = {}
    if (tshockConfig.host !== undefined) cfg.tshock.host = tshockConfig.host
    if (tshockConfig.port !== undefined) cfg.tshock.port = tshockConfig.port
    if (tshockConfig.apiKey !== undefined) cfg.tshock.apiKey = tshockConfig.apiKey
  }
  
  config = cfg
  await fs.writeFile(CONFIG_PATH, JSON.stringify(cfg, null, 2), 'utf8')
  notifyConfigUpdate()
  return cfg
}

export async function isTshockConfigured() {
  const tcfg = await getTshockConfig()
  return !!(tcfg.host && tcfg.port && tcfg.apiKey)
}

export async function isConfigFileExists() {
  try {
    await fs.access(CONFIG_PATH)
    return true
  } catch {
    return false
  }
}

export default loadConfig

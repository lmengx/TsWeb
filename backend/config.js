import fs from 'fs/promises'

let config = null
const configUpdateListeners = new Set()

export async function loadConfig() {
  if (!config) {
    const content = await fs.readFile('./config.json', 'utf8')
    config = JSON.parse(content)
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
  const content = await fs.readFile('./config.json', 'utf8')
  config = { ...JSON.parse(content), ...newConfig }
  await fs.writeFile('./config.json', JSON.stringify(config, null, 2), 'utf8')
  notifyConfigUpdate()
  return config
}

export async function getTshockConfig() {
  const cfg = await loadConfig()
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
  await fs.writeFile('./config.json', JSON.stringify(cfg, null, 2), 'utf8')
  notifyConfigUpdate()
  return cfg
}

export async function isTshockConfigured() {
  const tcfg = await getTshockConfig()
  return !!(tcfg.host && tcfg.port && tcfg.apiKey)
}

export default loadConfig
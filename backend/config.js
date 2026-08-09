import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const CONFIG_PATH = path.join(__dirname, 'config', 'config.json')

let config = null
const configUpdateListeners = new Set()

// ═══════════════════════════════════════════════════════════
// 基础读写
// ═══════════════════════════════════════════════════════════

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

/**
 * 首次初始化配置（多服结构，无 currentServerId）
 * 注意：不迁移旧版 tshock 单服字段（已确认直接废弃旧配置）
 */
export async function saveNewConfig() {
  config = {
    server: { port: 3000, host: '0.0.0.0' },
    servers: [],
    security: {
      jwtSecret: generateSecret(),
      tokenExpire: '24h',
      challengeExpire: 120000
    },
    logWebhook: {
      enabled: false,
      publicUrl: 'http://127.0.0.1:3000/hook/log'
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
  // 使用密码学安全随机数生成器（CSPRNG）生成 JWT 密钥
  return crypto.randomBytes(48).toString('base64url')
}

export async function isConfigFileExists() {
  try {
    await fs.access(CONFIG_PATH)
    return true
  } catch {
    return false
  }
}

// ═══════════════════════════════════════════════════════════
// 服务器注册表（servers[]，无 currentServerId 全局状态）
// 当前目标服务器由请求级 x-server-id header 决定（见 server.js 中间件）
// ═══════════════════════════════════════════════════════════

function generateServerId() {
  return 'sv-' + crypto.randomBytes(6).toString('hex')
}

export async function getServers() {
  const cfg = await loadConfig()
  if (!cfg) return []
  if (!Array.isArray(cfg.servers)) {
    // 旧版配置（tshock 单服字段）→ 视为空，等待重新添加
    return []
  }
  return cfg.servers
}

export async function getServerById(id) {
  const servers = await getServers()
  return servers.find(s => s.id === id) || null
}

export async function addServer(data) {
  const cfg = await loadConfig()
  if (!cfg) throw new Error('配置未初始化')
  if (!Array.isArray(cfg.servers)) cfg.servers = []

  const server = {
    id: generateServerId(),
    name: (data.name || '').trim() || `服务器${cfg.servers.length + 1}`,
    host: (data.host || '').trim(),
    port: parseInt(data.port) || 7878,
    apiKey: (data.apiKey || '').trim(),
    // 每台服务器独立的 webhook 推送签名密钥（插件→后端 HMAC 鉴权）
    pushSecret: crypto.randomBytes(32).toString('base64url'),
    enabled: data.enabled !== false,
    note: (data.note || '').trim()
  }
  cfg.servers.push(server)
  await persistConfig(cfg)
  return server
}

export async function updateServer(id, patch) {
  const cfg = await loadConfig()
  if (!cfg || !Array.isArray(cfg.servers)) return null
  const idx = cfg.servers.findIndex(s => s.id === id)
  if (idx === -1) return null

  const s = cfg.servers[idx]
  const allowed = ['name', 'host', 'port', 'apiKey', 'enabled', 'note']
  for (const key of allowed) {
    if (patch[key] !== undefined) {
      if (key === 'port') s[key] = parseInt(patch[key]) || s[key]
      else if (key === 'enabled') s[key] = !!patch[key]
      else s[key] = String(patch[key]).trim()
    }
  }
  await persistConfig(cfg)
  return s
}

export async function deleteServer(id) {
  const cfg = await loadConfig()
  if (!cfg || !Array.isArray(cfg.servers)) return false
  const before = cfg.servers.length
  cfg.servers = cfg.servers.filter(s => s.id !== id)
  if (cfg.servers.length === before) return false
  await persistConfig(cfg)
  return true
}

/** 重新生成指定服务器的 pushSecret（webhook 密钥轮换） */
export async function rotateServerPushSecret(id) {
  const cfg = await loadConfig()
  if (!cfg || !Array.isArray(cfg.servers)) return null
  const idx = cfg.servers.findIndex(s => s.id === id)
  if (idx === -1) return null
  cfg.servers[idx].pushSecret = crypto.randomBytes(32).toString('base64url')
  await persistConfig(cfg)
  return cfg.servers[idx]
}

async function persistConfig(cfg) {
  config = cfg
  await fs.writeFile(CONFIG_PATH, JSON.stringify(cfg, null, 2), 'utf8')
  notifyConfigUpdate()
}

// ═══════════════════════════════════════════════════════════
// Webhook 回传配置（全局：插件 → 后端 /hook/ 端点）
// ═══════════════════════════════════════════════════════════

export async function getLogWebhookConfig() {
  const cfg = await loadConfig()
  if (!cfg) return { enabled: false, publicUrl: 'http://127.0.0.1:3000/hook/log' }
  return {
    enabled: cfg.logWebhook?.enabled ?? false,
    publicUrl: cfg.logWebhook?.publicUrl || `http://127.0.0.1:${cfg.server?.port || 3000}/hook/log`
  }
}

export async function saveLogWebhookConfig(data) {
  const cfg = await loadConfig()
  if (!cfg.logWebhook) cfg.logWebhook = {}
  if (data.enabled !== undefined) cfg.logWebhook.enabled = !!data.enabled
  if (data.publicUrl !== undefined && String(data.publicUrl).trim()) {
    cfg.logWebhook.publicUrl = String(data.publicUrl).trim()
  }
  config = cfg
  await fs.writeFile(CONFIG_PATH, JSON.stringify(cfg, null, 2), 'utf8')
  notifyConfigUpdate()
  return cfg.logWebhook
}

export default loadConfig

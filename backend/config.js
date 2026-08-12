import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const CONFIG_PATH = path.join(__dirname, 'data', 'config.json')

let config = null

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
  if (!config) return config
  // 迁移：旧配置缺少 bot.token（首次初始化才有）→ 自动补齐并落盘
  let migrated = false
  if (!config.bot || typeof config.bot !== 'object') { config.bot = {}; migrated = true }
  if (!config.bot.token) { config.bot.token = generateSecret(); migrated = true }
  if (migrated) {
    try { await fs.writeFile(CONFIG_PATH, JSON.stringify(config, null, 2), 'utf8') } catch { /* 忽略写失败 */ }
  }
  return config
}

export async function getConfig() {
  return await loadConfig()
}

export async function saveConfig(newConfig) {
  const content = await fs.readFile(CONFIG_PATH, 'utf8')
  config = { ...JSON.parse(content), ...newConfig }
  await fs.writeFile(CONFIG_PATH, JSON.stringify(config, null, 2), 'utf8')
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
    // QQ 账号台账同步：机器人/前端管理入口的鉴权 token
    bot: {
      token: generateSecret()
    }
  }

  try {
    await fs.mkdir(path.dirname(CONFIG_PATH), { recursive: true })
    await fs.writeFile(CONFIG_PATH, JSON.stringify(config, null, 2), 'utf8')
  } catch (err) {
    throw new Error('无法写入配置文件: ' + err.message)
  }

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
    // 每台服务器独立的推送签名密钥（插件→后端 /hook/backup 备份推送 HMAC 鉴权）
    pushSecret: crypto.randomBytes(32).toString('base64url'),
    enabled: data.enabled !== false,
    note: (data.note || '').trim(),
    // QQ 账号台账同步开关（后端→插件 /tsweb/qqsync 推送）
    syncQQAccounts: data.syncQQAccounts === true,
    syncUUID: data.syncUUID === true
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
  const allowed = ['name', 'host', 'port', 'apiKey', 'enabled', 'note', 'syncQQAccounts', 'syncUUID']
  for (const key of allowed) {
    if (patch[key] !== undefined) {
      if (key === 'port') s[key] = parseInt(patch[key]) || s[key]
      else if (key === 'enabled' || key === 'syncQQAccounts' || key === 'syncUUID') s[key] = !!patch[key]
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

async function persistConfig(cfg) {
  config = cfg
  await fs.writeFile(CONFIG_PATH, JSON.stringify(cfg, null, 2), 'utf8')
}

export default loadConfig

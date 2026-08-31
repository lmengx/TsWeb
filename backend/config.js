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
  // 机器人设置默认值（主服/在线模式/时长聚合间隔）
  if (config.bot.mainServerId === undefined) { config.bot.mainServerId = ''; migrated = true }
  if (config.bot.onlineMode === undefined) { config.bot.onlineMode = 'all'; migrated = true }
  if (config.bot.pollIntervalMinutes === undefined) { config.bot.pollIntervalMinutes = 10; migrated = true }
  // 禁止多服登录（全局）：启用后，玩家在某服登录 → 踢掉其他启用 syncUUID 的服上的同名在线角色
  if (config.singleLogin === undefined) { config.singleLogin = { enabled: false }; migrated = true }
  // 玩家（QQ 登录）JWT 有效期：独立于管理端 security.tokenExpire
  if (!config.security || typeof config.security !== 'object') { config.security = {}; migrated = true }
  if (!config.security.playerTokenExpire) { config.security.playerTokenExpire = '7d'; migrated = true }
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
      // 玩家（QQ 登录）JWT 有效期：投票跨周，独立于管理端
      playerTokenExpire: '7d',
      challengeExpire: 120000
    },
    // QQ 账号台账同步：机器人/前端管理入口的鉴权 token
    bot: {
      token: generateSecret(),
      // 主服务器 id（机器人「进度」默认服、「在线」方式2 的主服）
      mainServerId: '',
      // 在线查询方式：all = 同时显示所有服；main = 主服完整 + 其它服名指代
      onlineMode: 'all',
      // 多服游玩时长聚合间隔（分钟）
      pollIntervalMinutes: 10
    },
    // 禁止多服登录（全局）：启用后，玩家在某服登录 → 踢掉其他启用 syncUUID 的服上的同名在线角色
    singleLogin: { enabled: false }
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

/**
 * 生成随机鲜艳颜色 #RRGGBB（高饱和 + 高明度，避免暗色/灰暗的低区分度色）。
 * HSV：H 随机 0-360，S ∈ [0.75, 1]，V ∈ [0.9, 1]，转 RGB 后大写 hex。
 */
function randomVividHex() {
  const h = Math.floor(Math.random() * 360)
  const s = 0.75 + Math.random() * 0.25
  const v = 0.9 + Math.random() * 0.1
  const c = v * s
  const x = c * (1 - Math.abs((h / 60) % 2 - 1))
  const m = v - c
  let r = 0, g = 0, b = 0
  if (h < 60) { r = c; g = x }
  else if (h < 120) { r = x; g = c }
  else if (h < 180) { g = c; b = x }
  else if (h < 240) { g = x; b = c }
  else if (h < 300) { r = x; b = c }
  else { r = c; b = x }
  const toHex = n => Math.round((n + m) * 255).toString(16).padStart(2, '0').toUpperCase()
  return `#${toHex(r)}${toHex(g)}${toHex(b)}`
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
    syncUUID: data.syncUUID === true,
    // 跨服聊天：开关 + 前缀模板（占位符 {serverName}/{id}，可含 [c/...] 转义）+ 消息最外层颜色
    crossChat: data.crossChat === true,
    // 前缀初始颜色 = 随机鲜艳色（高饱和+高明度），后续可自定义
    crossChatPrefix: (data.crossChatPrefix || '').trim() || `[c/${randomVividHex()}:{serverName}]`,
    crossChatColor: (data.crossChatColor || '').trim() || '#FFFFFF'
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
  const allowed = ['name', 'host', 'port', 'apiKey', 'enabled', 'note', 'syncQQAccounts', 'syncUUID',
    'crossChat', 'crossChatPrefix', 'crossChatColor']
  for (const key of allowed) {
    if (patch[key] !== undefined) {
      if (key === 'port') s[key] = parseInt(patch[key]) || s[key]
      else if (key === 'enabled' || key === 'syncQQAccounts' || key === 'syncUUID' || key === 'crossChat') s[key] = !!patch[key]
      else if (key === 'crossChatPrefix') s[key] = String(patch[key]).trim() || '[c/4DABF7:{serverName}]'
      else if (key === 'crossChatColor') s[key] = String(patch[key]).trim() || '#FFFFFF'
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

/**
 * 更新机器人设置（bot 段）：mainServerId / onlineMode / pollIntervalMinutes
 * 仅允许这三个字段，其余忽略（token 不通过此接口修改）
 */
export async function updateBotSettings(patch) {
  const cfg = await loadConfig()
  if (!cfg) throw new Error('配置未初始化')
  if (!cfg.bot || typeof cfg.bot !== 'object') cfg.bot = {}
  const allowed = ['mainServerId', 'onlineMode', 'pollIntervalMinutes']
  for (const key of allowed) {
    if (patch[key] === undefined) continue
    if (key === 'pollIntervalMinutes') {
      cfg.bot[key] = Math.max(1, parseInt(patch[key]) || 10)
    } else {
      cfg.bot[key] = String(patch[key]).trim()
    }
  }
  await persistConfig(cfg)
  return cfg.bot
}

export default loadConfig

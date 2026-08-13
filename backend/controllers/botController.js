import crypto from 'crypto'
import bcrypt from 'bcrypt'
import { getConfig, getServers, updateBotSettings } from '../config.js'
import { upsertAccount, getAccountByQq, getAccountByUsername, removeAccount, broadcastFullAll, getAccounts } from '../services/qqAccountService.js'
import { getPlaytime, getPlaytimeRecords, startAggregation, stopAggregation } from '../services/qqPlaytimeService.js'
import audit from '../services/auditLogger.js'

// ═══════════════════════════════════════════════════════════
// QQ 机器人管理接口（/api/bot/*）
// 鉴权：config.bot.token（请求头 X-Bot-Token 或 query ?token=）
// 机器人对接后端（不再直连 TShock 插件 REST）
// ═══════════════════════════════════════════════════════════

/** 校验机器人 token */
export async function requireBotToken(req, res, next) {
  const cfg = await getConfig()
  const token = req.headers['x-bot-token'] || req.query.token
  const expected = cfg?.bot?.token
  const a = Buffer.from(String(token || ''))
  const b = Buffer.from(String(expected || ''))
  if (!expected || !token || a.length !== b.length || !crypto.timingSafeEqual(a, b)) {
    return res.status(401).json({ error: 'Invalid bot token' })
  }
  next()
}

function buildBaseUrl(server) {
  const host = server?.host || 'localhost'
  const h = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
  return `${h}:${server?.port || 7878}`
}

/** 调插件 REST（token 鉴权），返回解析后的 JSON 或 null */
async function pluginFetch(server, path, params = {}) {
  const q = new URLSearchParams(params)
  q.set('token', server.apiKey || '')
  const url = `${buildBaseUrl(server)}${path}?${q.toString()}`
  try {
    const res = await fetch(url, { method: 'GET', signal: AbortSignal.timeout(8000) })
    const text = await res.text()
    try { return JSON.parse(text) } catch { return { status: String(res.status), raw: text.slice(0, 200) } }
  } catch (e) {
    return null
  }
}

function genRandomPassword(len = 16) {
  // 字母+数字，避免歧义字符
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789'
  const bytes = crypto.randomBytes(len)
  let out = ''
  for (let i = 0; i < len; i++) out += chars[bytes[i] % chars.length]
  return out
}

/**
 * 注册：POST /api/bot/register  { qq, player }
 * 随机密码 → 台账 → 广播全量（各启用服自动创建账号），机器人提示玩家走「改密码」设自己的密码
 */
export const register = async (req, res) => {
  try {
    const qq = String(req.body?.qq || '').trim()
    const player = String(req.body?.player || '').trim()
    if (!qq || !player) return res.status(400).json({ error: '缺少参数: qq / player' })
    if (!/^\d{5,15}$/.test(qq)) return res.status(400).json({ error: 'QQ 号格式不正确' })

    if (await getAccountByUsername(player)) {
      return res.status(409).json({ error: '该角色名已被注册' })
    }
    if (await getAccountByQq(qq)) {
      return res.status(409).json({ error: '该 QQ 已绑定角色' })
    }

    const password = genRandomPassword()
    const passwordHash = await bcrypt.hash(password, 12)

    await upsertAccount({ username: player, qq, passwordHash })
    const result = await broadcastFullAll()
    audit.record('qq_account.register', { username: player, qq })
    console.log(`[QQ台账] 注册: ${player} (QQ:${qq}), 广播 ${result.ok}/${result.total}`)

    res.json({
      status: 'ok',
      player,
      message: `注册成功，请发送「改密码 新密码」设置密码`
    })
  } catch (err) {
    console.error('[QQ台账] 注册失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

/**
 * 改密：POST /api/bot/change-password  { qq, password }
 * 更新台账密码哈希 → 广播全量（各启用服覆盖本地哈希）
 */
export const changePassword = async (req, res) => {
  try {
    const qq = String(req.body?.qq || '').trim()
    const password = String(req.body?.password || '')
    if (!qq) return res.status(400).json({ error: '缺少参数: qq' })
    if (password.trim().length < 4 || password.length > 128) {
      return res.status(400).json({ error: '密码长度需在 4-128 之间' })
    }

    const account = await getAccountByQq(qq)
    if (!account) return res.status(404).json({ error: '该 QQ 未绑定任何角色' })

    const passwordHash = await bcrypt.hash(password, 12)
    await upsertAccount({
      username: account.username,
      qq,
      passwordHash
    })
    const result = await broadcastFullAll()
    audit.record('qq_account.change_password', { username: account.username, qq })
    console.log(`[QQ台账] 改密: ${account.username} (QQ:${qq}), 广播 ${result.ok}/${result.total}`)

    res.json({ status: 'ok', message: '密码修改成功，已同步到所有服务器' })
  } catch (err) {
    console.error('[QQ台账] 改密失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

/**
 * 绑定已有账号：POST /api/bot/bind  { qq, player, serverId? }
 * 指定 serverId → 只查该服；否则广播所有启用服 find-account。
 * 唯一命中 → 该服返回哈希 → 建台账 → 广播全量
 */
export const bind = async (req, res) => {
  try {
    const qq = String(req.body?.qq || '').trim()
    const player = String(req.body?.player || '').trim()
    const serverId = String(req.body?.serverId || '').trim() || null
    if (!qq || !player) return res.status(400).json({ error: '缺少参数: qq / player' })
    if (!/^\d{5,15}$/.test(qq)) return res.status(400).json({ error: 'QQ 号格式不正确' })

    if (await getAccountByQq(qq)) {
      return res.status(409).json({ error: '该 QQ 已绑定角色' })
    }

    const servers = (await getServers()).filter(s => s.enabled && s.host && s.port && s.apiKey)
    const targets = serverId ? servers.filter(s => s.id === serverId) : servers
    if (targets.length === 0) return res.status(404).json({ error: '没有可查询的服务器' })

    // 广播查询
    const results = []
    for (const s of targets) {
      const r = await pluginFetch(s, '/data/qq/find-account', { name: player })
      if (r && r.found) results.push({ server: s, data: r })
    }

    if (results.length === 0) {
      return res.status(404).json({ error: '该角色名在所有可查询的服务器中都不存在' })
    }
    if (results.length > 1) {
      return res.status(409).json({
        conflict: true,
        error: '该角色名在多个服务器存在，请指定：绑定 <服名> <角色名>',
        servers: results.map(({ server: s }) => ({ id: s.id, name: s.name }))
      })
    }

    const { server: hitServer, data } = results[0]
    if (!data.passwordHash) {
      return res.status(500).json({ error: `服务器「${hitServer.name}」未返回密码哈希` })
    }

    await upsertAccount({
      username: player,
      qq,
      passwordHash: data.passwordHash
    })
    const result = await broadcastFullAll()
    audit.record('qq_account.bind', { serverId: hitServer.id, username: player, qq })
    console.log(`[QQ台账] 绑定: ${player} (QQ:${qq}) 来自 ${hitServer.name}, 广播 ${result.ok}/${result.total}`)

    res.json({ status: 'ok', server: hitServer.name, message: '绑定成功' })
  } catch (err) {
    console.error('[QQ台账] 绑定失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

/**
 * 服务器列表：GET /api/bot/servers
 * 返回后端配置的所有服务器 id/name/在线状态（机器人「服务器列表」命令用）
 */
export const listServers = async (_req, res) => {
  try {
    const servers = (await getServers()).map(s => ({
      id: s.id,
      name: s.name,
      enabled: s.enabled !== false,
      note: s.note || ''
    }))
    res.json({ servers })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

// ═══════════════════════════════════════════════════════════
// 服务器解析辅助
// ═══════════════════════════════════════════════════════════

/** 解析服务器：精确 id → 名称包含匹配（双向）→ null */
function resolveServer(servers, keyword) {
  if (!keyword) return null
  const k = String(keyword).trim()
  if (!k) return null
  return servers.find(s => s.id === k)
    || servers.find(s => s.name && (s.name.includes(k) || k.includes(s.name)))
    || null
}

/** 启用且可调用的服务器列表 */
async function enabledServers() {
  return (await getServers()).filter(s => s.enabled !== false && s.host && s.port && s.apiKey)
}

// ═══════════════════════════════════════════════════════════
// 我的信息：GET /api/bot/player-info?qq=
// 台账(用户名) + 本地多服时长 + 主服游戏数据（用户组/注册时间/死亡/钓鱼）
// ═══════════════════════════════════════════════════════════

export const playerInfo = async (req, res) => {
  try {
    const qq = String(req.query.qq || '').trim()
    if (!qq) return res.status(400).json({ error: '缺少参数: qq' })

    const account = await getAccountByQq(qq)
    if (!account) return res.status(404).json({ error: '该 QQ 未绑定任何角色' })
    const username = account.username

    // 本地多服游玩时长
    const play = await getPlaytime(username)
    const playtime = {
      total: play?.total || 0,
      servers: play?.servers || {}
    }

    // 主服游戏数据
    const servers = await enabledServers()
    const cfg = await getConfig()
    const mainServer = servers.find(s => s.id === cfg?.bot?.mainServerId) || servers[0] || null
    let game = null
    if (mainServer) {
      const r = await pluginFetch(mainServer, '/data/qq/player-data', { name: username })
      if (r && r.found !== false) game = r
    }

    res.json({
      status: 'ok',
      username,
      qq,
      playtime,
      game,
      mainServer: mainServer ? { id: mainServer.id, name: mainServer.name } : null
    })
  } catch (err) {
    console.error('[QQ机器人] 我的信息失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

// ═══════════════════════════════════════════════════════════
// 在线：GET /api/bot/online[?server=服名|服id]
// 无参数：按配置模式（all=同时显示所有服；main=主服完整+其它服名/人数指代）
// 带参数：指定服务器完整详情（机器人「在线 服名」）
// ═══════════════════════════════════════════════════════════

export const online = async (req, res) => {
  try {
    const servers = await enabledServers()
    if (servers.length === 0) return res.json({ mode: 'none', servers: [] })

    const cfg = await getConfig()
    const keyword = String(req.query.server || '').trim()

    // 指定服 → 该服完整详情
    if (keyword) {
      const target = resolveServer(servers, keyword)
      if (!target) return res.status(404).json({ error: `未找到服务器「${keyword}」，发送「服务器列表」查看` })
      const d = await pluginFetch(target, '/v2/server/status', { players: 'true' })
      return res.json({
        mode: 'single',
        server: { id: target.id, name: target.name },
        data: d || null
      })
    }

    // 默认模式
    const mode = cfg?.bot?.onlineMode === 'main' ? 'main' : 'all'
    const mainServer = servers.find(s => s.id === cfg?.bot?.mainServerId) || servers[0]

    const list = await Promise.all(servers.map(async s => {
      const d = await pluginFetch(s, '/v2/server/status', { players: 'true' })
      return {
        id: s.id,
        name: s.name,
        online: d?.playercount ?? null,
        max: d?.maxplayers ?? null,
        players: (d?.players || []).filter(p => p && p.nickname).map(p => p.nickname)
      }
    }))

    // main 模式：主服保留玩家名，其它服折叠为服名+人数
    if (mode === 'main') {
      const mainId = mainServer?.id
      for (const s of list) {
        if (s.id !== mainId) s.players = null
      }
    }

    res.json({
      mode,
      mainServer: mainServer ? { id: mainServer.id, name: mainServer.name } : null,
      servers: list
    })
  } catch (err) {
    console.error('[QQ机器人] 在线查询失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

// ═══════════════════════════════════════════════════════════
// 进度：GET /api/bot/boss-progress[?server=服名|服id]
// 默认查主服（未设主服则查第一个启用服）
// ═══════════════════════════════════════════════════════════

export const bossProgress = async (req, res) => {
  try {
    const servers = await enabledServers()
    if (servers.length === 0) return res.status(404).json({ error: '暂无可用服务器' })

    const cfg = await getConfig()
    const keyword = String(req.query.server || '').trim()
    let target = keyword ? resolveServer(servers, keyword) : null
    if (!target) target = servers.find(s => s.id === cfg?.bot?.mainServerId) || servers[0]

    const data = await pluginFetch(target, '/data/boss/progress')
    if (!data) return res.status(502).json({ error: `服务器「${target.name}」无响应` })

    res.json({ server: { id: target.id, name: target.name }, ...data })
  } catch (err) {
    console.error('[QQ机器人] 进度查询失败:', err.message)
    res.status(500).json({ error: err.message })
  }
}

// ═══════════════════════════════════════════════════════════
// 管理接口（仅 admin，前端 QQ 配置页使用）
// ═══════════════════════════════════════════════════════════

/**
 * QQ 绑定列表：GET /api/bot/qq-list
 * 台账全量 + 多服时长聚合，按时长降序
 */
export const qqList = async (_req, res) => {
  try {
    const accounts = await getAccounts()
    const playtime = await getPlaytimeRecords()
    const list = Object.entries(accounts).map(([username, rec]) => {
      const pt = playtime[username]
      return {
        username,
        qq: rec.qq || '',
        updatedAt: rec.updatedAt || '',
        playtime: pt ? { total: pt.total || 0, servers: pt.servers || {} } : { total: 0, servers: {} }
      }
    })
    list.sort((a, b) => b.playtime.total - a.playtime.total)
    res.json({ total: list.length, list })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/**
 * 解绑：POST /api/bot/qq-unbind  { username } 或 { qq }
 * 仅删除台账绑定关系（各服本地账号保留、密码不变，下次登录不受影响）
 */
export const qqUnbind = async (req, res) => {
  try {
    const username = String(req.body?.username || '').trim()
    const qq = String(req.body?.qq || '').trim()
    let target = null
    if (username) target = { username }
    else if (qq) target = await getAccountByQq(qq)
    if (!target) return res.status(400).json({ error: '缺少参数: username 或 qq' })

    const name = target.username
    const rec = await getAccountByUsername(name)
    if (!rec) return res.status(404).json({ error: '该角色未绑定 QQ' })

    await removeAccount(name)
    // 广播全量同步解绑到各服（插件绑定快照移除该用户，解绑后登录不再触发晋升；
    // full 推送不删本地账号，符合「各服本地账号保留、密码不变」语义）
    const result = await broadcastFullAll()
    // 时长记录保留（qq 字段由下轮聚合自动清空；绑定列表 qq 以台账为准，立即失效）
    audit.record('qq_account.unbind', { username: name, qq: rec.qq || '' })
    console.log(`[QQ台账] 解绑: ${name} (QQ:${rec.qq || ''}), 广播 ${result.ok}/${result.total}`)
    res.json({ status: 'ok', message: '解绑成功' })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/**
 * 改绑 QQ：POST /api/bot/qq-rebind  { username, qq }
 * 校验新 QQ 未被其它角色绑定 → 更新台账 → 广播全量
 */
export const qqRebind = async (req, res) => {
  try {
    const username = String(req.body?.username || '').trim()
    const newQq = String(req.body?.qq || '').trim()
    if (!username || !newQq) return res.status(400).json({ error: '缺少参数: username / qq' })
    if (!/^\d{5,15}$/.test(newQq)) return res.status(400).json({ error: 'QQ 号格式不正确' })

    const rec = await getAccountByUsername(username)
    if (!rec) return res.status(404).json({ error: '该角色未绑定 QQ' })
    const oldQq = rec.qq || ''
    if (oldQq === newQq) return res.status(400).json({ error: 'QQ 号未变化' })

    const other = await getAccountByQq(newQq)
    if (other && other.username !== username) {
      return res.status(409).json({ error: `该 QQ 已绑定角色：${other.username}` })
    }

    await upsertAccount({ username, qq: newQq, passwordHash: rec.passwordHash })
    await broadcastFullAll()
    audit.record('qq_account.rebind', { username, qq: newQq, from: oldQq })
    console.log(`[QQ台账] 改绑: ${username} ${oldQq} → ${newQq}`)
    res.json({ status: 'ok', message: '改绑成功' })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/**
 * 机器人设置读取：GET /api/bot/settings
 * 返回 bot 段 + 可选服务器列表（供主服选择）
 */
export const getBotSettings = async (_req, res) => {
  try {
    const cfg = await getConfig()
    const servers = (await getServers()).map(s => ({ id: s.id, name: s.name, enabled: s.enabled !== false }))
    res.json({
      bot: {
        mainServerId: cfg?.bot?.mainServerId || '',
        onlineMode: cfg?.bot?.onlineMode === 'main' ? 'main' : 'all',
        pollIntervalMinutes: cfg?.bot?.pollIntervalMinutes || 10
      },
      servers
    })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/**
 * 机器人设置保存：POST /api/bot/settings
 * 更新 mainServerId / onlineMode / pollIntervalMinutes，聚合间隔变化时重启定时器
 */
export const setBotSettings = async (req, res) => {
  try {
    const body = req.body || {}
    const bot = await updateBotSettings(body)
    // 间隔可能变化 → 重启聚合定时器
    stopAggregation()
    await startAggregation()
    audit.record('config.bot.set', { changedKeys: Object.keys(body), actor: req.user?.username || 'admin' })
    res.json({ status: 'ok', bot })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

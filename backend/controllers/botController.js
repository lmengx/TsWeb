import crypto from 'crypto'
import bcrypt from 'bcrypt'
import { getConfig, getServers } from '../config.js'
import { upsertAccount, getAccountByQq, getAccountByUsername, broadcastFullAll } from '../services/qqAccountService.js'
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

import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'
import { getServers } from '../config.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const ACCOUNTS_PATH = path.join(__dirname, '..', 'data', 'qq_accounts.json')

// ═══════════════════════════════════════════════════════════
// QQ 账号台账（后端权威）
//   records: { 用户名: { qq, passwordHash, updatedAt } }
//   仅存 QQ号 + 密码哈希（注册/绑定/改密维护）。
//   UUID 不再存储于台账：账号登录设备由「登录上报 → 后端转发 → 各服落盘」实时同步，
//   各服数据库 Users.UUID 字段即真值，TShock 原生免密直接命中。
//
// ⚠️ 实时读文件（不缓存）：任何进程/脚本对 data/qq_accounts.json 的修改立即生效，无需重启。
//    文件规模（<1000 用户）下单次读 + JSON.parse 为毫秒级，远小于 bcrypt 校验成本（~300ms），
//    无性能顾虑；代价是磁盘 IO，换来「外部写入立即可见」的正确性。
// ═══════════════════════════════════════════════════════════

async function load() {
  try {
    const content = await fs.readFile(ACCOUNTS_PATH, 'utf8')
    const data = JSON.parse(content)
    if (!data || typeof data !== 'object') return { records: {} }
    if (!data.records || typeof data.records !== 'object') data.records = {}
    return data
  } catch {
    return { records: {} }
  }
}

async function persist(data) {
  // 防御：若误传裸 records（无 records 外壳），自动包回 { records: ... }，杜绝外壳丢失写坏文件
  const payload = (data && typeof data === 'object' && data.records)
    ? data
    : { records: (data && typeof data === 'object') ? data : {} }
  try {
    await fs.mkdir(path.dirname(ACCOUNTS_PATH), { recursive: true })
    await fs.writeFile(ACCOUNTS_PATH, JSON.stringify(payload, null, 2), 'utf8')
  } catch (err) {
    console.error('[QQ台账] 保存失败:', err.message)
  }
}

/** 全部台账记录（{username: record}） */
export async function getAccounts() {
  return (await load()).records
}

export async function getAccountByUsername(username) {
  const records = await getAccounts()
  return records[username] || null
}

/** 大小写不敏感按角色名查台账（玩家登录 / requirePlayer 中间件用），返回 { username, ...rec } */
export async function getAccountByUsernameCI(username) {
  if (!username) return null
  const target = String(username).trim().toLowerCase()
  if (!target) return null
  const records = await getAccounts()
  for (const [name, rec] of Object.entries(records)) {
    if (String(name).toLowerCase() === target) return { username: name, ...rec }
  }
  return null
}

export async function getAccountByQq(qq) {
  if (!qq) return null
  const records = await getAccounts()
  for (const [name, rec] of Object.entries(records)) {
    if (String(rec.qq) === String(qq)) return { username: name, ...rec }
  }
  return null
}

/**
 * upsert 一条台账记录（注册/绑定/改密共用）
 * @returns {{ changed: boolean, existed: boolean }}
 */
export async function upsertAccount({ username, qq = '', passwordHash = '', updatedAt }) {
  const data = await load()
  const records = data.records
  const existing = records[username]
  const existed = !!existing
  const changed = !existing
    || String(existing.qq || '') !== String(qq || '')
    || String(existing.passwordHash || '') !== String(passwordHash || '')

  records[username] = {
    qq: String(qq || ''),
    passwordHash: String(passwordHash || ''),
    updatedAt: updatedAt || new Date().toISOString()
  }
  await persist(data)
  return { changed, existed }
}

/** 移除台账记录（暂未接入删号同步，预留） */
export async function removeAccount(username) {
  const data = await load()
  const records = data.records
  if (!records[username]) return false
  delete records[username]
  await persist(data)
  return true
}

// ═══════════════════════════════════════════════════════════
// 后端 → 插件 推送（POST /tsweb/qqsync，HMAC 签名，与 /hook 协议一致）
// ═══════════════════════════════════════════════════════════

function buildBaseUrl(server) {
  const host = server?.host || 'localhost'
  const h = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
  return `${h}:${server?.port || 7878}`
}

function signPayload(secret, body) {
  const ts = Date.now().toString()
  const nonce = crypto.randomBytes(16).toString('hex')
  const bodyHash = crypto.createHash('sha256').update(body).digest('hex')
  const signature = crypto
    .createHmac('sha256', secret)
    .update(`${ts}.${nonce}.${bodyHash}`)
    .digest('hex')
  return { ts, nonce, signature }
}

/**
 * 向单台服务器 POST 同步 payload（{type:'full'|'uuid', ...}）
 */
export async function postToServer(server, payloadObj) {
  if (!server?.pushSecret) return { ok: false, error: 'no pushSecret' }
  const body = JSON.stringify(payloadObj)
  const { ts, nonce, signature } = signPayload(server.pushSecret, body)
  const url = `${buildBaseUrl(server)}/tsweb/qqsync`
  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Server-Id': String(server.id),
        'X-Timestamp': ts,
        'X-Nonce': nonce,
        'X-Signature': signature
      },
      body,
      signal: AbortSignal.timeout(15000)
    })
    const text = await res.text()
    if (!res.ok) {
      console.warn(`[QQ台账] 推送失败 ${server.name}: HTTP ${res.status} ${text.slice(0, 200)}`)
      return { ok: false, status: res.status, error: text }
    }
    return { ok: true, status: res.status }
  } catch (e) {
    console.warn(`[QQ台账] 推送失败 ${server.name}: ${e.message}`)
    return { ok: false, error: e.message }
  }
}

/** 单台服务器是否启用账号同步（接收完整台账并创建账号） */
export function shouldSyncAccounts(server) {
  return server?.enabled !== false && server?.syncQQAccounts === true
}

/** 单台服务器是否接收完整台账（账号同步或 UUID 同步任一启用都推 full） */
export function shouldReceiveFull(server) {
  return server?.enabled !== false && (server?.syncQQAccounts === true || server?.syncUUID === true)
}

/** 单台服务器是否启用 UUID 转发（登录设备落盘） */
export function shouldSyncUuid(server) {
  return server?.enabled !== false && server?.syncUUID === true
}

/** 完整台账 payload（不含 uuid，账号密码权威） */
export async function buildFullPayload() {
  const records = await getAccounts()
  return { type: 'full', records }
}

/** 向所有启用账号/UUID 同步的服务器推送完整台账 */
export async function broadcastFullAll() {
  const servers = await getServers()
  const targets = servers.filter(shouldReceiveFull)
  const payload = await buildFullPayload()
  const results = await Promise.allSettled(targets.map(s => postToServer(s, payload)))
  const okCount = results.filter(r => r.status === 'fulfilled' && r.value.ok).length
  if (okCount !== targets.length) {
    console.warn(`[QQ台账] 全量推送完成: ${okCount}/${targets.length}`)
  } else {
    console.log(`[QQ台账] 全量推送完成: ${okCount}/${targets.length}`)
  }
  return { ok: okCount, total: targets.length }
}

/**
 * 向所有启用 syncUUID 的服务器转发单条 UUID（登录设备同步，不落台账）。
 * @param {string} username
 * @param {string} uuid
 * @param {{ kick?: boolean, excludeServerId?: string|null }} [options]
 *   kick: 禁止多服登录（全局开关）——转发时带 kick 标志，目标服插件踢掉本服同名在线角色
 *   excludeServerId: 排除来源服务器（否则刚登录的玩家会被自己服踢掉）
 */
export async function broadcastUuid(username, uuid, { kick = false, excludeServerId = null } = {}) {
  const servers = await getServers()
  // 踢人只作用于启用 syncUUID 的服务器：转发目标保持不变（不开 uuid 同步的服不收不踢）
  const targets = servers.filter(s =>
    s.enabled !== false && s.id !== excludeServerId && s.syncUUID === true)
  const payload = kick
    ? { type: 'uuid', username, uuid, kick: true }
    : { type: 'uuid', username, uuid }
  const results = await Promise.allSettled(targets.map(s => postToServer(s, payload)))
  const okCount = results.filter(r => r.status === 'fulfilled' && r.value.ok).length
  // uuid 同步日志保持简洁：成功不刷屏，仅转发不全时警告（不暴露具体 uuid 值）
  if (okCount !== targets.length) {
    console.warn(`[SSE] UUID 转发异常: ${okCount}/${targets.length}`)
  }
  return { ok: okCount, total: targets.length }
}

/** SSE 连接建立后由 sseConnection 调用：向该服务器推送全量（若启用账号/UUID 同步） */
export async function pushFullIfEnabled(server) {
  if (!shouldReceiveFull(server)) return { ok: false, skipped: true }
  const payload = await buildFullPayload()
  return postToServer(server, payload)
}

export default { getAccounts, getAccountByUsername, getAccountByUsernameCI, getAccountByQq, upsertAccount, removeAccount, broadcastFullAll, broadcastUuid, pushFullIfEnabled }

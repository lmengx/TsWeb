import { getServers } from '../config.js'
import { getAccountByUsernameCI, removeAccount, renameAccount, broadcastFullAll } from './qqAccountService.js'
import { renameKey } from './qqPlaytimeService.js'
import { renameLinkedTo, deleteAccount as deleteBackendAccount } from './accountService.js'
import { TShockService } from './tshockService.js'

// ═══════════════════════════════════════════════════════════
// 玩家账号管理服务（useradmin）
// 编排多服广播（插件 /data/users/* REST）+ 后端台账/时长/管理账户联动。
// 广播模式复用 qqAccountService 的 Promise.allSettled + 逐服结果汇总。
// ═══════════════════════════════════════════════════════════

function buildBaseUrl(server) {
  const host = server?.host || 'localhost'
  const h = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
  return `${h}:${server?.port || 7878}`
}

/** 启用且可调用的服务器（enabled + host + port + apiKey） */
async function enabledServers() {
  return (await getServers()).filter(s => s.enabled !== false && s.host && s.port && s.apiKey)
}

/** 向单台服务器调插件 REST（GET，token 鉴权），返回解析后的 JSON 或 null */
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

/**
 * 账号改名（后端编排）
 * mode='qq'|'both'：改台账 key → 广播各服改名 → 改时长 key → 改后端账户 linkedTo
 * mode='server'：仅广播指定服（或全部服）改名，不碰台账/时长/后端账户
 * @returns {{ ok, total, ledger, playtime, backendAccounts, failed: [] }}
 */
export async function renameUser({ username, newName, mode = 'qq', serverId = '' }) {
  const name = String(username || '').trim()
  const target = String(newName || '').trim()
  if (!name || !target) throw new Error('缺少参数: username / newName')

  // 1) 台账联动（mode='qq'|'both'）
  let ledger = false
  let playtime = false
  let backendAccounts = 0
  if (mode === 'qq' || mode === 'both') {
    // 台账存在且改名成功 → 联动时长/后端账户；台账不存在 → 视为单服改名（仅广播）
    const ledgerRec = await getAccountByUsernameCI(name)
    if (ledgerRec) {
      ledger = await renameAccount(ledgerRec.username, target)
      if (ledger) {
        playtime = await renameKey(ledgerRec.username, target)
        backendAccounts = await renameLinkedTo(ledgerRec.username, target)
      }
    }
  }

  // 2) 广播各服改名（mode='server' 时按 serverId 过滤目标）
  const servers = await enabledServers()
  const targets = serverId ? servers.filter(s => s.id === serverId) : servers
  if (targets.length === 0) return { ok: 0, total: 0, ledger, playtime, backendAccounts, failed: [] }

  const results = await Promise.allSettled(targets.map(async s => {
    const r = await pluginFetch(s, '/data/users/rename', {
      username: name,
      newName: target,
      updateBans: 'true'
    })
    if (!r) return { server: s, error: '无响应' }
    if (r.ok !== true) return { server: s, error: r.error || '插件返回失败' }
    return { server: s, ok: true }
  }))

  let ok = 0
  const failed = []
  for (const r of results) {
    if (r.status === 'fulfilled' && r.value?.ok === true) ok++
    else if (r.status === 'fulfilled') failed.push({ server: r.value.server.name, error: r.value.error })
    else failed.push({ server: 'unknown', error: r.reason?.message || '异常' })
  }

  return { ok, total: targets.length, ledger, playtime, backendAccounts, failed }
}

/**
 * 删除账号（后端编排）
 * 广播各服删除 → 可选删台账（unbindQq）→ 可选删后端管理账户（deleteBackendAccount）
 * @returns {{ ok, total, unbindQq, backendAccountDeleted, failed: [] }}
 */
export async function deleteUser({ username, deleteCharacter = true, deleteBans = true, unbindQq = true, deleteBackendAccount = false }) {
  const name = String(username || '').trim()
  if (!name) throw new Error('缺少参数: username')

  // 1) 广播各服删除
  const servers = await enabledServers()
  const results = await Promise.allSettled(servers.map(async s => {
    const r = await pluginFetch(s, '/data/users/delete', {
      username: name,
      deleteCharacter: deleteCharacter ? 'true' : 'false',
      deleteBans: deleteBans ? 'true' : 'false'
    })
    if (!r) return { server: s, error: '无响应' }
    if (r.ok !== true) return { server: s, error: r.error || '插件返回失败' }
    return { server: s, ok: true }
  }))

  let ok = 0
  const failed = []
  for (const r of results) {
    if (r.status === 'fulfilled' && r.value?.ok === true) ok++
    else if (r.status === 'fulfilled') failed.push({ server: r.value.server.name, error: r.value.error })
    else failed.push({ server: 'unknown', error: r.reason?.message || '异常' })
  }

  // 2) 台账联动
  let unbound = false
  if (unbindQq) {
    const rec = await getAccountByUsernameCI(name)
    if (rec) {
      unbound = await removeAccount(rec.username)
      // 台账删除后广播全量（各服绑定快照移除，避免已删账号仍被识别为 QQ 绑定）
      await broadcastFullAll()
    }
  }

  // 3) 后端管理账户（铁律：禁止删除当前登录账户，由 controller 校验）
  let backendAccountDeleted = false
  if (deleteBackendAccount) {
    try {
      await deleteBackendAccount(name)
      backendAccountDeleted = true
    } catch (err) {
      // 账户不存在或删除失败 → 记入 failed 但不阻断整体
      failed.push({ server: 'backend', error: err.message })
    }
  }

  return { ok, total: servers.length, unbindQq: unbound, backendAccountDeleted, failed }
}

/**
 * UUID 清除/替换（后端编排）
 * broadcast=true → 广播到所有启用服；false → 仅当前选中服（x-server-id）
 * @returns {{ ok, total, failed: [] }}
 */
export async function setUserUuid({ username, uuid = '', serverId = '', broadcast = true }) {
  const name = String(username || '').trim()
  if (!name) throw new Error('缺少参数: username')

  const servers = await enabledServers()
  // broadcast=true：全部启用服；否则仅指定服（x-server-id 解析）
  const targets = broadcast
    ? servers
    : (serverId ? servers.filter(s => s.id === serverId) : servers)

  const results = await Promise.allSettled(targets.map(async s => {
    const r = await pluginFetch(s, '/data/users/uuid', {
      username: name,
      uuid
    })
    if (!r) return { server: s, error: '无响应' }
    if (r.ok !== true) return { server: s, error: r.error || '插件返回失败' }
    return { server: s, ok: true }
  }))

  let ok = 0
  const failed = []
  for (const r of results) {
    if (r.status === 'fulfilled' && r.value?.ok === true) ok++
    else if (r.status === 'fulfilled') failed.push({ server: r.value.server.name, error: r.value.error })
    else failed.push({ server: 'unknown', error: r.reason?.message || '异常' })
  }

  return { ok, total: targets.length, failed }
}

/**
 * 批量账号操作（改组/封禁/重置密码/解绑 QQ/踢出/群发消息）
 * 复用现有单条逻辑：TShock 原生命令经 /api/tshock/command 代理逐用户执行，
 * QQ 解绑复用 removeAccount + broadcastFullAll。
 * @returns {{ total, ok, failed: [{username, error}] }}
 */
export async function batchOperate({ action, users = [], params = {} }) {
  const list = Array.isArray(users) ? users.slice(0, 100) : []
  if (!action || list.length === 0) throw new Error('缺少参数: action / users')

  const failed = []
  let ok = 0

  for (const username of list) {
    try {
      switch (action) {
        case 'unbind': {
          const rec = await getAccountByUsernameCI(username)
          if (!rec) { failed.push({ username, error: '未绑定 QQ' }); continue }
          await removeAccount(rec.username)
          await broadcastFullAll()
          ok++
          break
        }
        case 'group': {
          const group = String(params.group || '').trim()
          if (!group) { failed.push({ username, error: '缺少 group 参数' }); continue }
          // TShock /user group <用户名> <组> 命令通道（改组权限以命令为准）
          const rs = await executeCommandAll(`user group ${username} ${group}`)
          if (rs.length === 0 || rs.every(r => r.status !== 'fulfilled')) {
            failed.push({ username, error: '服务器无响应' })
          } else {
            ok++
          }
          break
        }
        case 'password': {
          const password = String(params.password || '').trim()
          if (!password) { failed.push({ username, error: '缺少 password 参数' }); continue }
          // TShock /user password <用户名> <新密码> 命令通道
          const rs = await executeCommandAll(`user password ${username} ${password}`)
          if (rs.length === 0 || rs.every(r => r.status !== 'fulfilled')) {
            failed.push({ username, error: '服务器无响应' })
          } else {
            ok++
          }
          break
        }
        case 'ban': {
          // TShock /ban add <用户名> -a（账号封禁）命令通道
          const rs = await executeCommandAll(`ban add ${username} -a`)
          if (rs.length === 0 || rs.every(r => r.status !== 'fulfilled')) {
            failed.push({ username, error: '服务器无响应' })
          } else {
            ok++
          }
          break
        }
        case 'kick': {
          // 踢出在线玩家：经命令代理
          await executeCommandAll(`kick ${username}`)
          ok++
          break
        }
        case 'message': {
          const msg = String(params.message || '').trim()
          if (!msg) { failed.push({ username, error: '缺少 message 参数' }); continue }
          await executeCommandAll(`broadcast ${username}: ${msg}`)
          ok++
          break
        }
        default:
          failed.push({ username, error: `未知操作: ${action}` })
      }
    } catch (err) {
      failed.push({ username, error: err.message })
    }
  }

  return { total: list.length, ok, failed }
}

/** 向所有启用服执行命令（复用 /api/tshock/command 的插件命令通道） */
async function executeCommandAll(command) {
  const servers = await enabledServers()
  const results = await Promise.allSettled(servers.map(s => {
    const inst = new TShockService(s)
    return inst.executeCommand(command)
  }))
  return results
}

export default { renameUser, deleteUser, setUserUuid, batchOperate }

/**
 * 插件 SSE 常驻长连接服务
 * 后端启动即对每台启用服务器建立到插件 /tsweb/stream 的 SSE 长连接（REST token 鉴权），
 * 持续接收日志与定向文件推送；不随前端连接状态变化而断联。
 *
 * 事件协议：
 *   connected  -> { connected: true, clientId }
 *   ping       -> 心跳
 *   log        -> { id, time, level, segments:[{t,c}] }   → pushWebhookLog 入队广播给前端
 *   file.begin / file.chunk / file.end / file.error       → 定向文件组装保存
 */
import fs from 'fs'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'
import { getServers, getConfig } from '../config.js'
import { pushWebhookLog } from './logBroadcast.js'
import { pushFullIfEnabled, broadcastUuid } from './qqAccountService.js'
import { broadcastCrossChat } from './crossChatService.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

// serverId -> 连接状态
const _conns = new Map()

// tag -> 下载会话（文件管理页下载：插件 file.* 事件实时转发给前端，不落盘）
const downloadSessions = new Map()

const sleep = (ms) => new Promise(r => setTimeout(r, ms))
const delay = (retry) => Math.min(1000 * 2 ** Math.min(retry, 5), 30000) // 1s→30s 指数退避

/**
 * 启动时对全部启用服务器建立 SSE 长连接
 */
export async function connectAll() {
  const servers = await getServers()
  const enabled = servers.filter(s => s.enabled && s.host && s.port && s.apiKey)
  for (const s of enabled) {
    connect(s)
  }
  return { success: true, connected: enabled.length }
}

/**
 * 对单台服务器建立（或重建）SSE 连接
 */
export function connect(server) {
  disconnect(server.id)
  const conn = {
    server,
    clientId: null,
    connected: false,
    retry: 0,
    closed: false,
    transfers: new Map() // 文件传输 id -> 组装状态
  }
  _conns.set(server.id, conn)
  runLoop(conn)
}

export function disconnect(serverId) {
  const conn = _conns.get(serverId)
  if (!conn) return
  conn.closed = true
  _conns.delete(serverId)
}

async function runLoop(conn) {
  while (!conn.closed) {
    const base = `${conn.server.host.startsWith('http') ? conn.server.host : `http://${conn.server.host}`}:${conn.server.port}`
    // 附带 serverId/pushSecret/hookBase：插件端 SSE 握手时存储（自动备份推送 /hook/backup 链路，
    // SSE 常连是后端无条件建立的连接）
    const cfg = await getConfig().catch(() => null)
    // 后端基础地址（备份推送 /hook/backup 用）：由监听配置推导；
    // 0.0.0.0/:: 通配监听映射为 127.0.0.1（本地可达），具体网卡地址可显式配置 server.host
    const listenPort = cfg?.server?.port || 3000
    const listenHost = cfg?.server?.host || '0.0.0.0'
    const displayHost = (listenHost === '0.0.0.0' || listenHost === '::') ? '127.0.0.1' : listenHost
    const hookBase = `http://${displayHost}:${listenPort}`
    const url = `${base}/tsweb/stream?token=${encodeURIComponent(conn.server.apiKey)}` +
      `&serverId=${encodeURIComponent(conn.server.id)}` +
      `&secret=${encodeURIComponent(conn.server.pushSecret || '')}` +
      `&hookBase=${encodeURIComponent(hookBase)}` +
      `&syncQQAccounts=${conn.server.syncQQAccounts ? '1' : '0'}` +
      `&syncUUID=${conn.server.syncUUID ? '1' : '0'}` +
      `&crossChat=${conn.server.crossChat ? '1' : '0'}` +
      `&crossChatPrefix=${encodeURIComponent(renderCrossChatPrefix(conn.server))}` +
      `&crossChatColor=${encodeURIComponent(conn.server.crossChatColor || '#FFFFFF')}` +
      `&serverName=${encodeURIComponent(conn.server.name || '')}`
    // 仅连接阶段 15s 超时；连接建立后移除，避免定时器切断长连接（靠心跳/读流异常检测断线）
    const ac = new AbortController()
    const connectTimer = setTimeout(() => ac.abort(), 15000)
    let res
    try {
      res = await fetch(url, { signal: ac.signal })
    } catch (e) {
      if (!conn.closed) console.warn(`[SSE] 与插件 ${conn.server.name} 连接失败: ${e.message}`)
    } finally {
      clearTimeout(connectTimer)
    }

    if (res && res.ok && res.body) {
      conn.retry = 0
      conn.connected = true
      console.log(`[SSE] 已连接插件 ${conn.server.name} (${conn.server.host}:${conn.server.port})`)
      // SSE 常连建立 = 后端连接上服务器 → 推送完整 QQ 台账（若该服启用 syncQQAccounts）
      pushFullIfEnabled(conn.server).then(r => {
        if (r && !r.skipped && !r.ok) {
          console.warn(`[SSE] ${conn.server.name} QQ 台账全量推送失败: ${r.error || r.status}`)
        }
      })
      try {
        const reader = res.body.getReader()
        const decoder = new TextDecoder()
        let buffer = ''
        while (!conn.closed) {
          const { done, value } = await reader.read()
          if (done) break
          buffer += decoder.decode(value, { stream: true })
          let idx
          while ((idx = buffer.indexOf('\n\n')) >= 0) {
            const frame = buffer.slice(0, idx)
            buffer = buffer.slice(idx + 2)
            handleFrame(conn, frame)
          }
        }
      } catch (e) {
        if (!conn.closed) console.warn(`[SSE] 与插件 ${conn.server.name} 连接断开: ${e.message}`)
      } finally {
        conn.connected = false
        conn.clientId = null
      }
    } else if (!conn.closed) {
      conn.connected = false
      console.warn(`[SSE] 与插件 ${conn.server.name} 连接失败: HTTP ${res ? res.status : 'no response'}`)
    }

    if (conn.closed) break
    await sleep(delay(conn.retry++))
  }
}

function handleFrame(conn, frame) {
  let event = 'message'
  let data = ''
  for (const line of frame.split('\n')) {
    if (line.startsWith('event:')) event = line.slice(6).trim()
    else if (line.startsWith('data:')) data += line.slice(5).replace(/^\s+/, '') + '\n'
  }
  data = data.replace(/\n$/, '')
  if (!data) return

  let parsed
  try { parsed = JSON.parse(data) } catch { return }

  switch (event) {
    case 'connected':
      conn.clientId = parsed.clientId
      console.log(`[SSE] ${conn.server.name} clientId=${parsed.clientId}`)
      break
    case 'ping':
      break
    case 'log':
      // data 即插件端包装好的日志 JSON 字符串，按来源服务器入队广播给前端
      pushWebhookLog(data, conn.server.id)
      break
    case 'qq-uuid':
      // 插件登录设备上报（复用 SSE 通道）→ 后端不落库，仅转发到其他启用 syncUUID 的服落盘覆盖
      handleQqUuidEvent(conn, parsed)
      break
    case 'cross-chat':
      // 插件本地聊天上报 → 转发到其他启用跨服聊天的服务器
      handleCrossChatEvent(conn, parsed)
      break
    default:
      if (event.startsWith('file.')) {
        // 下载会话优先：tag 匹配时实时转发给前端（不落盘）；否则走资源拉取落盘逻辑
        if (parsed.tag && downloadSessions.has(parsed.tag)) {
          forwardToDownloadSession(parsed.tag, event, parsed)
        } else {
          handleFileEvent(conn, event, parsed)
        }
      }
  }
}

/** 插件经 SSE 上报登录设备 uuid：后端不落库，仅转发到其他启用 syncUUID 的服务器落盘覆盖 */
async function handleQqUuidEvent(conn, parsed) {
  const username = String(parsed?.username || '').trim()
  const uuid = String(parsed?.uuid || '').trim()
  if (!username || !uuid) return

  // uuid 同步与 QQ 台账无关：上报侧只显示「xxx 的 uuid 已上传」，不暴露具体 uuid 值
  console.log(`[SSE] ${username} 的 uuid 已上传`)

  // 禁止多服登录（全局开关 config.singleLogin.enabled）：
  // 转发带 kick 标志 → 其他启用 syncUUID 的服踢掉本服同名在线角色；排除来源服避免误踢自己
  let kick = false
  try {
    const cfg = await getConfig().catch(() => null)
    kick = cfg?.singleLogin?.enabled === true
  } catch {
    kick = false
  }
  broadcastUuid(username, uuid, { kick, excludeServerId: conn.server.id })
}

/** 渲染跨服聊天前缀模板：替换 {serverName}/{id} 占位符（[c/HEX:...] 转义原样保留） */
function renderCrossChatPrefix(server) {
  const raw = server.crossChatPrefix || '[c/4DABF7:{serverName}]'
  return String(raw)
    .replaceAll('{serverName}', server.name || '')
    .replaceAll('{id}', server.id || '')
}

/** 插件经 SSE 上报本地聊天 → 转发给其他启用跨服聊天的服务器（名字/内容标签不转义） */
function handleCrossChatEvent(conn, parsed) {
  const text = String(parsed?.text || '').trim()
  if (!text || text.length > 300) return
  const player = String(parsed?.player || '').slice(0, 64)
  if (!player) return

  const payload = {
    serverId: conn.server.id,              // 后端注入来源（不信任插件自报）
    serverName: conn.server.name || '',
    prefix: String(parsed?.prefix || '').slice(0, 128),   // 已渲染前缀（含 [c/...]）
    player,                                 // 玩家名字原样（含 [i:] 标签，不转义）
    groupPrefix: String(parsed?.groupPrefix || '').slice(0, 64),
    groupSuffix: String(parsed?.groupSuffix || '').slice(0, 64),
    text,
    r: clampByte(parsed?.r), g: clampByte(parsed?.g), b: clampByte(parsed?.b)
  }
  broadcastCrossChat(conn.server.id, payload)
}

function clampByte(v) {
  const n = Number(v)
  if (!Number.isFinite(n)) return 255
  return Math.max(0, Math.min(255, Math.round(n)))
}

// ═══════════════ 下载会话（实时转发，不落盘） ═══════════════

/**
 * 注册一个下载会话：插件推送的 file.* 事件（带相同 tag）将转发给 handler
 * @param {string} tag
 * @param {(eventName: string, parsed: object) => void} handler
 * @returns {() => void} 取消注册函数
 */
export function registerDownloadSession(tag, handler) {
  downloadSessions.set(tag, { handler })
  return () => downloadSessions.delete(tag)
}

function forwardToDownloadSession(tag, event, parsed) {
  const session = downloadSessions.get(tag)
  if (!session) return
  try {
    session.handler(event, parsed)
  } catch (e) {
    console.warn(`[SSE] 下载会话转发异常: ${e.message}`)
    downloadSessions.delete(tag)
  }
  // 传输结束自动清理会话
  if (event === 'file.end' || event === 'file.error') {
    downloadSessions.delete(tag)
  }
}

// ═══════════════ 文件接收组装 ═══════════════

const SaveRoot = path.join(__dirname, '..', 'data', 'resource', '导出数据', 'sse-files')
const TransferRoot = path.join(__dirname, '..', 'data', 'transfer')
const BuildingRoot = path.join(__dirname, '..', 'data', 'transfer', 'building')

// 转存 waiter：`${serverId}:${safeName}` -> { resolve, reject }
// 文件管理页「保存到后端」：finishFile 落盘完成后通知，再移动至 transfer 目录
const saveWaiters = new Map()

/** 转存根目录（文件管理页「保存到后端」的落盘目录） */
export const getTransferRoot = () => TransferRoot

/** 建筑存档目录（data/transfer/building/，平铺） */
export const getBuildingRoot = () => BuildingRoot

/** 目标目录下生成不冲突文件名：已存在则追加 _N 后缀（如 xxx_1.tsb） */
function uniqueName(dir, name) {
  const ext = path.extname(name)
  const base = path.basename(name, ext)
  let candidate = name
  let i = 1
  while (fs.existsSync(path.join(dir, candidate))) {
    candidate = `${base}_${i}${ext}`
    i++
  }
  return candidate
}

function resolveSaveWaiter(conn, safeName, err) {
  const key = `${conn.server.id}:${safeName}`
  const w = saveWaiters.get(key)
  if (!w) return
  saveWaiters.delete(key)
  if (err) w.reject(err)
  else w.resolve()
}

function handleFileEvent(conn, event, parsed) {
  const id = parsed.id
  if (event === 'file.begin') {
    conn.transfers.set(id, {
      name: parsed.name || 'file',
      size: parsed.size || 0,
      chunks: parsed.chunks || 0,
      chunkSize: parsed.chunkSize || 0,
      received: new Array(parsed.chunks || 0).fill(null),
      count: 0
    })
  } else if (event === 'file.chunk') {
    const t = conn.transfers.get(id)
    if (!t || parsed.n == null || parsed.n >= t.received.length) return
    if (t.received[parsed.n] !== null) return // 该段已收到，忽略重复
    t.received[parsed.n] = Buffer.from(parsed.d || '', 'base64')
    t.count++
    if (t.count >= t.chunks) finishFile(conn, id, t)
  } else if (event === 'file.end') {
    const t = conn.transfers.get(id)
    if (t) finishFile(conn, id, t, parsed.sha256)
  } else if (event === 'file.error') {
    console.warn(`[SSE] ${conn.server.name} 文件传输失败: ${parsed.reason || 'unknown'}`)
    conn.transfers.delete(id)
  }
}

function finishFile(conn, id, t, sha256) {
  if (t.saved) return
  const buf = Buffer.concat(t.received.map(b => b || Buffer.alloc(0)))
  const safeName = path.basename(String(t.name || 'file')).replace(/[\\/:*?"<>|]/g, '_')
  if (sha256) {
    const hash = crypto.createHash('sha256').update(buf).digest('hex')
    if (hash.toLowerCase() !== String(sha256).toLowerCase()) {
      console.warn(`[SSE] ${conn.server.name} 文件校验失败: ${t.name}`)
      resolveSaveWaiter(conn, safeName, new Error('文件校验失败 (sha256)'))
      conn.transfers.delete(id)
      return
    }
  }
  try {
    const dir = path.join(SaveRoot, String(conn.server.id))
    fs.mkdirSync(dir, { recursive: true })
    const full = path.join(dir, safeName)
    fs.writeFileSync(full, buf)
    console.log(`[SSE] ${conn.server.name} 文件已保存: ${safeName} (${buf.length} bytes)`)
  } catch (e) {
    console.error(`[SSE] 文件保存失败: ${e.message}`)
    resolveSaveWaiter(conn, safeName, e)
  }
  resolveSaveWaiter(conn, safeName)
  t.saved = true
  conn.transfers.delete(id)
}

// ═══════════════ 对外接口 ═══════════════

/**
 * 请求插件定向推送一个文件到本后端的 SSE 连接
 * @param {string} serverId
 * @param {string} filePath 相对路径
 * @param {{ root?: 'app'|'tshock', tag?: string }} [options]
 *   root: 'app'=TShock程序目录（文件管理页）/ 'tshock'=TShock.SavePath（默认，兼容资源拉取）
 *   tag:  随 file.* 事件回传的标识（用于下载会话关联）
 */
export async function requestFile(serverId, filePath, options = {}) {
  const conn = _conns.get(serverId)
  if (!conn || !conn.connected || !conn.clientId) {
    return { success: false, message: 'SSE 未连接' }
  }
  const server = conn.server
  const base = `${server.host.startsWith('http') ? server.host : `http://${server.host}`}:${server.port}`
  const q = new URLSearchParams({
    token: server.apiKey,
    clientId: conn.clientId,
    path: filePath
  })
  if (options.root) q.set('root', options.root)
  if (options.tag) q.set('tag', options.tag)
  const url = `${base}/tsweb/file?${q.toString()}`
  try {
    // 注意：插件 /tsweb/file 会同步推完全部 chunk 后才返回 HTTP 响应，
    // 响应时间 ≈ 文件推送时间。不能用短超时（大文件必被 abort），
    // 用 5 分钟兜底：SSE 断连时插件会立即 404 返回，不会真正挂死。
    const res = await fetch(url, { signal: AbortSignal.timeout(300000) })
    const json = await res.json().catch(() => ({}))
    return { success: res.ok, ...json }
  } catch (e) {
    return { success: false, message: e.message }
  }
}

/**
 * 保存文件到后端目录（默认 data/transfer/{serverId}/，可经 options.destDir 覆盖为平铺目录如 data/transfer/building）
 * 链路：插件 /tsweb/file（SSE）→ 后端落盘 sse-files → 完成后移动至目标目录
 * @param {string} serverId
 * @param {string} filePath 插件端相对路径（root='app' 时相对 TShock 程序目录）
 * @param {{ root?: 'app'|'tshock', destDir?: string }} [options]
 *   destDir: 指定目标目录（平铺，同名自动加后缀）；缺省按 serverId 分目录（同名覆盖，文件管理页语义）
 */
export async function saveFileToBackend(serverId, filePath, options = {}) {
  const conn = _conns.get(serverId)
  if (!conn || !conn.connected || !conn.clientId) {
    return { success: false, message: 'SSE 未连接' }
  }
  const safeName = path.basename(String(filePath).replace(/\\/g, '/')).replace(/[\\/:*?"<>|]/g, '_')
  const key = `${serverId}:${safeName}`
  // 目标目录：destDir（平铺 rename）或默认 transfer/{serverId}（覆盖语义）
  const destDir = options.destDir ? path.resolve(options.destDir) : path.join(TransferRoot, String(serverId))
  const destName = options.destDir ? uniqueName(destDir, safeName) : safeName
  const destFull = path.join(destDir, destName)

  // 预删残留旧文件（仅默认按服务器分目录模式；平铺模式保留历史文件）
  if (!options.destDir) {
    try { if (fs.existsSync(destFull)) fs.unlinkSync(destFull) } catch { /* ignore */ }
  }

  // 注册 waiter（必须在触发推送之前，避免 finishFile 先于等待注册）
  const waiter = new Promise((resolve, reject) => {
    saveWaiters.set(key, { resolve, reject })
  })

  const result = await requestFile(serverId, filePath, { root: options.root })
  if (!result.success) {
    saveWaiters.delete(key)
    return { success: false, message: result.message }
  }
  if (String(result.status || '200') !== '200') {
    saveWaiters.delete(key)
    return { success: false, error: result.error || `HTTP ${result.status}` }
  }

  // 等待 finishFile 落盘完成（60s 超时兜底）
  const timeout = setTimeout(() => { saveWaiters.delete(key) }, 60000)
  try {
    await waiter
    clearTimeout(timeout)
    // 从 sse-files 移至目标目录（同盘瞬时）
    const srcFull = path.join(SaveRoot, String(serverId), safeName)
    fs.mkdirSync(destDir, { recursive: true })
    if (fs.existsSync(srcFull)) fs.renameSync(srcFull, destFull)
    const size = fs.existsSync(destFull) ? fs.statSync(destFull).size : 0
    return { success: true, name: destName, size, path: destFull }
  } catch (e) {
    clearTimeout(timeout)
    return { success: false, message: e.message }
  }
}

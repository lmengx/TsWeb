import onlineService from '../services/onlineService.js'
import jwt from 'jsonwebtoken'
import path from 'path'
import fs from 'fs'
import { fileURLToPath } from 'url'
import { getConfig, getServers } from '../config.js'
import audit from '../services/auditLogger.js'
import { getCurrentServerId } from '../services/tshockService.js'
import { requestFile } from '../services/sseConnection.js'
import { pushWebhookLog, getSseClients, addSseClient, removeSseClient, sseClientCount } from '../services/logBroadcast.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const SseFilesRoot = path.join(__dirname, '..', 'data', 'resource', '导出数据', 'sse-files')

// ═══ 说明：日志主通道已改为后端→插件 SSE 常驻长连接（sseConnection.js），
// 前端日志流由后端内存队列 + 广播提供，不再需要随前端连接状态注册/注销插件 webhook ═══

export const getHourlyOnline = async (req, res) => {
  const { date } = req.query
  if (!date) {
    return res.status(400).json({ error: 'date parameter is required (yyyy-MM-dd)' })
  }
  const result = await onlineService.getHourlyOnline(date)
  res.json(result)
}

export const getRanking = async (req, res) => {
  const mode = req.query.mode || 'today'
  const result = await onlineService.getRanking(mode)
  res.json(result)
}

export const getPlayerCalendar = async (req, res) => {
  const { name, year } = req.query
  if (!name) {
    return res.status(400).json({ error: 'name parameter is required' })
  }
  const yearNum = parseInt(year) || new Date().getFullYear()
  const result = await onlineService.getPlayerCalendar(name, yearNum)
  res.json(result)
}

export const getRankingStats = async (req, res) => {
  const type = req.query.type || 'online'
  const page = parseInt(req.query.page) || 1
  const pageSize = parseInt(req.query.pageSize) || 10
  const result = await onlineService.getRankingStats(type, page, pageSize)
  res.json(result)
}

/**
 * Webhook 接收端点 — 兼容旧端点（无签名，仅供向后兼容；新端点走 /hook/log 带 HMAC 签名）
 * POST /api/online/log-webhook
 * Body: { lines: ["[{\"t\":\"text\",\"c\":\"Red\"}]"] }
 */
export const logWebhookReceiver = (req, res) => {
  const { lines } = req.body || {}
  if (!Array.isArray(lines) || lines.length === 0) {
    return res.status(400).json({ error: 'Missing or invalid lines array' })
  }

  // 多服：从 X-Server-Id 头取来源服务器，按服务器分组入队
  const serverId = req.headers['x-server-id'] || ''
  for (const line of lines) {
    pushWebhookLog(line, serverId)
  }

  res.json({ status: 'ok', received: lines.length })
}

/**
 * SSE 日志流 — 通过 Webhook 接收插件日志后转发给前端
 * GET /api/online/log/stream?token=xxx
 * EventSource 无法设置 Authorization 头，从 query 取 token
 */
export const streamLogs = async (req, res) => {
  try {
    const token = req.query.token
    if (!token) {
      return res.status(401).json({ error: 'Missing token' })
    }

    // 多服：SSE 流必须绑定目标服务器（EventSource 无法携带 header，经 query 传入）
    const serverId = req.query.serverId
    if (!serverId) {
      return res.status(400).json({ error: 'Missing serverId parameter' })
    }

    // 校验 serverId 是已配置的服务器，防止任意订阅导致跨服务器日志泄漏
    const servers = await getServers()
    if (!servers.some(s => String(s.id) === String(serverId))) {
      return res.status(400).json({ error: 'Unknown serverId' })
    }

    // 验证 JWT
    const cfg = await getConfig()
    const secret = cfg?.security?.jwtSecret
    if (!secret) {
      // 密钥未配置时拒绝服务，绝不允许回退到弱密钥
      return res.status(500).json({ error: 'JWT secret not configured' })
    }
    let decoded
    try {
      decoded = jwt.verify(token, secret)
    } catch (e) {
      console.error('[SSE] JWT 验证失败:', e.message)
      return res.status(401).json({ error: 'Invalid token' })
    }

    // 检查管理权限（admin/subadmin 均可查看日志流）
    const userGroups = (decoded.usergroup || '').split(',').map(g => g.trim().toLowerCase())
    const managerRoles = ['admin', 'subadmin']
    if (!userGroups.some(g => managerRoles.includes(g))) {
      console.error('[SSE] 用户无管理权限:', decoded.username, userGroups)
      return res.status(403).json({ error: 'Forbidden' })
    }

    // SSE 响应头
    res.writeHead(200, {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache',
      'Connection': 'keep-alive',
      'Access-Control-Allow-Origin': '*'
    })

    // 发送连接成功事件
    res.write(`data: ${JSON.stringify({ connected: true, transport: 'webhook' })}\n\n`)

    // 注册到该服务器的 SSE 客户端分组（多服：只接收该服务器的日志广播）
    addSseClient(serverId, res)

    // 定时心跳 + 僵尸检测
    const keepAlive = setInterval(() => {
      try {
        // 检测底层 socket 是否已断开
        if (res.socket?.destroyed || !res.writable) {
          throw new Error('Socket closed')
        }
        res.write(': heartbeat\n\n')
      } catch {
        clearInterval(keepAlive)
        removeSseClient(serverId, res)
      }
    }, 30000)

    // 额外监听 socket 级别断开（比 req.close 更可靠）
    res.socket?.once('close', () => {
      clearInterval(keepAlive)
      removeSseClient(serverId, res)
    })

    // 客户端断开
    req.on('close', () => {
      clearInterval(keepAlive)
      removeSseClient(serverId, res)
      console.log('[SSE] 客户端断开')
    })

  } catch (error) {
    console.error('[SSE] 启动失败:', error.message)
    if (!res.headersSent) {
      res.status(502).json({ error: error.message })
    }
  }
}

/**
 * 请求插件定向推送一个文件（TShock.SavePath 相对路径）到本后端 SSE 连接并保存
 * POST /api/online/file/pull  body: { path: "tshock.json" }
 */
export const pullFile = async (req, res) => {
  const { path: filePath } = req.body || {}
  if (!filePath) {
    return res.status(400).json({ error: 'Missing path' })
  }
  const serverId = getCurrentServerId()
  const result = await requestFile(serverId, String(filePath))
  res.json(result)
}

/**
 * 列出已通过 SSE 接收保存的文件
 * GET /api/online/file/list
 */
export const listReceivedFiles = (req, res) => {
  const serverId = getCurrentServerId()
  const dir = path.join(SseFilesRoot, String(serverId))
  let files = []
  try {
    if (fs.existsSync(dir)) {
      files = fs.readdirSync(dir)
        .filter(f => fs.statSync(path.join(dir, f)).isFile())
        .map(f => {
          const st = fs.statSync(path.join(dir, f))
          return { name: f, size: st.size, mtime: st.mtimeMs }
        })
        .sort((a, b) => b.mtime - a.mtime)
    }
  } catch (e) {
    return res.status(500).json({ error: e.message })
  }
  res.json({ files })
}

/**
 * 下载已接收的文件
 * GET /api/online/file/download?name=xxx
 */
export const downloadReceivedFile = (req, res) => {
  const serverId = getCurrentServerId()
  const name = String(req.query.name || '')
  if (!name) return res.status(400).json({ error: 'Missing name' })
  const safeName = path.basename(name).replace(/[\\/:*?"<>|]/g, '_')
  const full = path.join(SseFilesRoot, String(serverId), safeName)
  if (!fs.existsSync(full)) {
    return res.status(404).json({ error: 'File not found' })
  }
  res.download(full, safeName)
}

export const execCommand = async (req, res) => {
  const { cmd } = req.body
  const executor = req.user?.username || 'SSE-Console'
  const serverId = getCurrentServerId()
  if (!cmd) {
    return res.status(400).json({ error: 'Missing cmd parameter' })
  }
  console.log('[SSE CMD] 执行命令:', cmd, 'by', executor)
  try {
    const result = await onlineService.execCommand(cmd, executor)
    console.log('[SSE CMD] 结果:', JSON.stringify(result).substring(0, 100))
    if (result && result.error) {
      audit.record('command.execute_failed', {
        command: String(cmd).substring(0, 200),
        serverId,
        actor: executor,
        error: result.error
      })
    } else {
      audit.record('command.execute', {
        command: String(cmd).substring(0, 200),
        serverId,
        actor: executor
      })
    }
    res.json(result)
  } catch (err) {
    console.error('[SSE CMD] 错误:', err.message)
    audit.record('command.execute_failed', {
      command: String(cmd).substring(0, 200),
      serverId,
      actor: executor,
      error: err.message
    })
    res.status(500).json({ error: err.message })
  }
}

import onlineService from '../services/onlineService.js'
import jwt from 'jsonwebtoken'
import { getConfig, getServers } from '../config.js'
import audit from '../services/auditLogger.js'
import { getCurrentServerId } from '../services/tshockService.js'
import { addSseClient, removeSseClient, getRecentLines } from '../services/logBroadcast.js'

// ═══ 说明：日志主通道为后端→插件 SSE 常驻长连接（sseConnection.js），
// 前端日志流由后端内存队列 + 广播提供；历史 webhook 日志回传（/api/online/log-webhook）已废弃移除 ═══

export const getHourlyOnline = async (req, res) => {
  const { date } = req.query
  if (!date) {
    return res.status(400).json({ error: 'date parameter is required (yyyy-MM-dd)' })
  }
  const result = await onlineService.getHourlyOnline(date)
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
 * SSE 日志流 — 通过 SSE 常连接收插件日志后转发给前端
 * GET /api/online/log/stream?token=xxx&serverId=xxx
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

    // 连接即补发该服务器的最近日志（纯内存队列，按 serverId 隔离，绝不混服）
    const history = getRecentLines(serverId, 200)
    if (history.length > 0) {
      res.write(`data: ${JSON.stringify(history)}\n\n`)
    }

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

import onlineService from '../services/onlineService.js'
import jwt from 'jsonwebtoken'
import { getConfig } from '../config.js'
import { updatePluginWebhook, reRegisterWebhook } from '../services/webhookRegistration.js'
import audit from '../services/auditLogger.js'
import { getCurrentServerId } from '../services/tshockService.js'
import { pushWebhookLog, getSseClients, addSseClient, removeSseClient, sseClientCount } from '../services/logBroadcast.js'

// ═══ SSE 客户端数量监听（防抖） ═══
let _sseThrottleTimer = null

/**
 * SSE 客户端数量变化时，自动注册/注销 webhook
 */
function onSseClientCountChanged(serverId) {
  if (_sseThrottleTimer) clearTimeout(_sseThrottleTimer)
  _sseThrottleTimer = setTimeout(async () => {
    _sseThrottleTimer = null
    const count = sseClientCount()
    // 显式绑定服务器 id（避免依赖 AsyncLocalStorage 在定时器中的传播）
    if (count === 0) {
      await updatePluginWebhook(serverId, null)
    } else {
      await reRegisterWebhook(serverId)
    }
  }, 2000)
}

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

  for (const line of lines) {
    pushWebhookLog(line)
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

    // 注册到 SSE 客户端集合
    addSseClient(res)
    // 显式捕获当前请求的服务器 id（AsyncLocalStorage 在异步回调中不保证传播）
    const boundServerId = getCurrentServerId()

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
        removeSseClient(res)
        onSseClientCountChanged(boundServerId)
      }
    }, 30000)

    // 额外监听 socket 级别断开（比 req.close 更可靠）
    res.socket?.once('close', () => {
      clearInterval(keepAlive)
      removeSseClient(res)
      onSseClientCountChanged(boundServerId)
    })

    // 客户端断开
    req.on('close', () => {
      clearInterval(keepAlive)
      removeSseClient(res)
      onSseClientCountChanged(boundServerId)
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

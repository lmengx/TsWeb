import onlineService from '../services/onlineService.js'
import jwt from 'jsonwebtoken'
import { getConfig } from '../config.js'
import { updatePluginWebhook } from '../services/webhookRegistration.js'

// ═══ 内存日志队列 + SSE 客户端管理 ═══
const _logQueue = []
const _sseClients = new Set()
const _queueLock = {}
const MaxQueueLines = 2000

// ═══ SSE 客户端数量监听（防抖） ═══
let _sseThrottleTimer = null

/**
 * SSE 客户端数量变化时，自动注册/注销 webhook
 */
function onSseClientCountChanged() {
  if (_sseThrottleTimer) clearTimeout(_sseThrottleTimer)
  _sseThrottleTimer = setTimeout(async () => {
    _sseThrottleTimer = null
    const count = _sseClients.size
    if (count === 0) {
      // 没有客户端了 → 注销 webhook，让插件停止推流
      await updatePluginWebhook(null)
    }
    // 有客户端时不自动注册，由配置变化或启动时触发注册
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
 * Webhook 接收端点 — 插件端通过 HTTP POST 推送日志行到此端点
 * POST /api/online/log-webhook
 * Body: { lines: ["[{\"t\":\"text\",\"c\":\"Red\"}]"] }
 */
export const logWebhookReceiver = (req, res) => {
  const { lines } = req.body || {}
  if (!Array.isArray(lines) || lines.length === 0) {
    return res.status(400).json({ error: 'Missing or invalid lines array' })
  }

  for (const line of lines) {
    // 存入内存队列
    _logQueue.push(line)
    if (_logQueue.length > MaxQueueLines) {
      _logQueue.splice(0, _logQueue.length - MaxQueueLines)
    }

    // 广播给所有 SSE 客户端
    const data = JSON.stringify([line])
    for (const client of _sseClients) {
      try {
        client.write(`data: ${data}\n\n`)
      } catch {
        _sseClients.delete(client)
      }
    }
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
    const secret = cfg?.security?.jwtSecret || 'fallback-secret'
    let decoded
    try {
      decoded = jwt.verify(token, secret)
    } catch (e) {
      console.error('[SSE] JWT 验证失败:', e.message)
      return res.status(401).json({ error: 'Invalid token' })
    }

    // 检查 admin 权限
    const userGroups = (decoded.usergroup || '').split(',').map(g => g.trim().toLowerCase())
    const adminRoles = ['owner', 'superadmin']
    if (!userGroups.some(g => adminRoles.includes(g))) {
      console.error('[SSE] 用户无 admin 权限:', decoded.username, userGroups)
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
    _sseClients.add(res)

    // 定时心跳
    const keepAlive = setInterval(() => {
      try {
        res.write(': heartbeat\n\n')
      } catch {
        clearInterval(keepAlive)
        _sseClients.delete(res)
        onSseClientCountChanged()
      }
    }, 30000)

    // 客户端断开
    req.on('close', () => {
      clearInterval(keepAlive)
      _sseClients.delete(res)
      onSseClientCountChanged()
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
  if (!cmd) {
    return res.status(400).json({ error: 'Missing cmd parameter' })
  }
  console.log('[SSE CMD] 执行命令:', cmd, 'by', executor)
  try {
    const result = await onlineService.execCommand(cmd, executor)
    console.log('[SSE CMD] 结果:', JSON.stringify(result).substring(0, 100))
    res.json(result)
  } catch (err) {
    console.error('[SSE CMD] 错误:', err.message)
    res.status(500).json({ error: err.message })
  }
}

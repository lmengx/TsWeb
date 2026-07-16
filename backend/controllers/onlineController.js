import onlineService from '../services/onlineService.js'
import jwt from 'jsonwebtoken'
import { getConfig } from '../config.js'
import { RconClient } from '../services/rconClient.js'

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
 * SSE 日志流 — 通过 RCON 获取实时日志
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
      console.error('[SSE Proxy] JWT 验证失败:', e.message)
      return res.status(401).json({ error: 'Invalid token' })
    }

    // 检查 admin 权限
    const userGroups = (decoded.usergroup || '').split(',').map(g => g.trim().toLowerCase())
    const adminRoles = ['owner', 'superadmin']
    if (!userGroups.some(g => adminRoles.includes(g))) {
      console.error('[SSE Proxy] 用户无 admin 权限:', decoded.username, userGroups)
      return res.status(403).json({ error: 'Forbidden' })
    }

    // 获取 RCON 连接信息
    const tshockHost = cfg.tshock?.host || '127.0.0.1'
    const tshockPort = cfg.tshock?.port || 7878
    const apiKey = cfg.tshock?.apiKey || ''

    // 从插件获取 RCON 配置
    const configUrl = `${tshockHost.startsWith('http') ? tshockHost : `http://${tshockHost}`}:${tshockPort}/data/config/tsweb?token=${encodeURIComponent(apiKey)}`
    let rconPort = 7880
    let rconEnabled = false
    
    try {
      const configRes = await fetch(configUrl, { headers: { 'Accept': 'application/json' } })
      if (configRes.ok) {
        const configData = await configRes.json()
        rconEnabled = configData.rconEnabled || false
        rconPort = configData.rconPort || 7880
      }
    } catch (e) {
      console.error('[SSE Proxy] 获取插件配置失败:', e.message)
    }

    // SSE 响应头
    res.writeHead(200, {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache',
      'Connection': 'keep-alive',
      'Access-Control-Allow-Origin': '*'
    })

    if (!rconEnabled) {
      // RCON 未启用，发送一条错误事件后保持连接
      const msg = JSON.stringify({ error: 'RCON_NOT_ENABLED', message: 'RCON 未启用，无法获取实时日志。请在插件配置中启用 RCON 并重启服务器' })
      res.write(`data: ${msg}\n\n`)
      // 定时发送心跳，让前端知道连接还在
      const keepAlive = setInterval(() => {
        res.write(': heartbeat\n\n')
      }, 30000)
      req.on('close', () => {
        clearInterval(keepAlive)
        console.log('[SSE Proxy] RCON 未启用，客户端断开')
      })
      return
    }

    // 连接 RCON
    const rcon = new RconClient('127.0.0.1', rconPort, apiKey)
    
    try {
      await rcon.connect()
      console.log('[SSE Proxy] RCON 连接成功')
      res.write(`data: ${JSON.stringify({ connected: true, transport: 'rcon' })}\n\n`)
    } catch (err) {
      console.error('[SSE Proxy] RCON 连接失败:', err.message)
      res.write(`data: ${JSON.stringify({ error: 'RCON_CONNECT_FAILED', message: `RCON 连接失败: ${err.message}` })}\n\n`)
      const keepAlive = setInterval(() => { res.write(': heartbeat\n\n') }, 30000)
      req.on('close', () => { clearInterval(keepAlive) })
      return
    }

    // 日志回调 → 转发到 SSE
    // line 是 LogSegment[] JSON 字符串，如 [{"t":"text","c":"Red"}]
    // 前端 ConsoleTerminal 期望外层是字符串数组：data: ["[{\"t\":...}]"]
    rcon.onLog = (line) => {
      try {
        res.write(`data: ${JSON.stringify([line])}\n\n`)
      } catch {
        rcon.close()
      }
    }

    rcon.onError = (err) => {
      console.error('[SSE Proxy] RCON 错误:', err.message)
    }

    rcon.onEnd = () => {
      console.log('[SSE Proxy] RCON 断开')
      try { res.end() } catch {}
    }

    // 客户端断开 → 关闭 RCON
    req.on('close', () => {
      console.log('[SSE Proxy] 客户端断开')
      rcon.close()
    })

  } catch (error) {
    console.error('[SSE Proxy] 启动失败:', error.message)
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

import onlineService from '../services/onlineService.js'
import jwt from 'jsonwebtoken'
import { getConfig } from '../config.js'

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

export const streamLogs = async (req, res) => {
  try {
    // EventSource 无法设置 Authorization 头，从 query 取 token
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

    // 检查是否为 admin
    const userGroups = (decoded.usergroup || '').split(',').map(g => g.trim().toLowerCase())
    const adminRoles = ['owner', 'superadmin']
    const hasAdminAccess = userGroups.some(g => adminRoles.includes(g))
    if (!hasAdminAccess) {
      console.error('[SSE Proxy] 用户无 admin 权限:', decoded.username, userGroups)
      return res.status(403).json({ error: 'Forbidden' })
    }

    console.log('[SSE Proxy] JWT 验证通过:', decoded.username, '| 组:', decoded.usergroup)

    const host = cfg.tshock?.host || 'localhost'
    const h = host.startsWith('http') ? host : `http://${host}`
    const baseUrl = `${h}:${cfg.tshock?.port || 7878}`
    const apiKey = cfg.tshock?.apiKey || ''

    const sseUrl = `${baseUrl}/data/online/log/stream?token=${encodeURIComponent(apiKey)}`
    console.log('[SSE Proxy] 连接插件 SSE:', sseUrl)

    // 连接到插件 SSE
    let pluginRes
    try {
      pluginRes = await fetch(sseUrl)
    } catch (fetchErr) {
      console.error('[SSE Proxy] fetch 失败:', fetchErr.message)
      return res.status(502).json({ error: `无法连接到插件: ${fetchErr.message}` })
    }

    console.log('[SSE Proxy] 插件响应状态:', pluginRes.status, pluginRes.statusText)

    if (!pluginRes.ok) {
      const body = await pluginRes.text().catch(() => '')
      console.error('[SSE Proxy] 插件返回错误:', pluginRes.status, body)
      return res.status(502).json({ error: 'Failed to connect to plugin SSE', status: pluginRes.status, body })
    }

    console.log('[SSE Proxy] 连接成功，开始转发 SSE 流')

    // SSE 头
    res.writeHead(200, {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache',
      'Connection': 'keep-alive',
      'Access-Control-Allow-Origin': '*'
    })

    // 从插件 SSE 流读取并转发
    const reader = pluginRes.body.getReader()
    let bytesForwarded = 0
    let msgCount = 0
    const pump = async () => {
      try {
        while (true) {
          const { done, value } = await reader.read()
          if (done) {
            console.log('[SSE Proxy] 插件流结束')
            break
          }
          res.write(value)
          bytesForwarded += value.length
          msgCount++
          if (msgCount % 10 === 0) {
            console.log(`[SSE Proxy] 已转发 ${msgCount} 条消息 (${bytesForwarded} bytes)`)
          }
        }
      } catch (err) {
        console.error('[SSE Proxy] 读取错误:', err.message)
      } finally {
        reader.cancel()
        console.log('[SSE Proxy] 关闭 SSE 代理, 共转发:', msgCount, '条消息,', bytesForwarded, 'bytes')
        res.end()
      }
    }

    req.on('close', () => {
      reader.cancel()
    })

    pump()
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

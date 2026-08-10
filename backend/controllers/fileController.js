import crypto from 'crypto'
import { getCurrentServerId } from '../services/tshockService.js'
import tshockService from '../services/tshockService.js'
import { requestFile, registerDownloadSession } from '../services/sseConnection.js'

// 文件管理（仅 admin）：完全管理 TShock 程序目录下所有文件
// 安全边界由插件端保证（防路径穿越 / 符号链接）；后端仅做路径合法性兜底。

function normalizePath(p) {
  const s = String(p || '').replace(/\\/g, '/')
  // 拒绝绝对路径 / 穿越
  if (s.startsWith('/') || /^[A-Za-z]:/.test(s)) return null
  const parts = s.split('/').filter(x => x && x !== '.' && x !== '..')
  return parts.join('/')
}

// GET /api/files/list?path=xxx — 列出目录内容（path 为空 = 根目录）
export async function listDir(req, res) {
  try {
    const relativePath = req.query.path
    if (relativePath === undefined) {
      return res.status(400).json({ error: 'path is required' })
    }
    const normalized = normalizePath(relativePath)
    const result = await tshockService.fileList(normalized || '')
    if (result.error) return res.status(500).json({ error: result.error })
    res.json(result)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// GET /api/files/read?path=xxx — 读取文本内容（文本预览）
export async function readFile(req, res) {
  try {
    const relativePath = req.query.path
    if (!relativePath) {
      return res.status(400).json({ error: 'path is required' })
    }
    const normalized = normalizePath(relativePath)
    if (!normalized) return res.status(403).json({ error: 'invalid path' })

    const result = await tshockService.fileRead(normalized)
    if (result.error && !result.content) {
      const status = String(result.status || '500') === '404' ? 404 : 500
      return res.status(status).json({ error: result.error })
    }
    res.json(result)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/files/write { path, content }
export async function writeFile(req, res) {
  try {
    const relativePath = req.body?.path
    const content = req.body?.content
    if (!relativePath || content === undefined || content === null) {
      return res.status(400).json({ error: 'path and content are required' })
    }
    const normalized = normalizePath(relativePath)
    if (!normalized) return res.status(403).json({ error: 'invalid path' })

    const result = await tshockService.fileWrite(normalized, content)
    if (result.error) return res.status(500).json({ error: result.error })
    res.json(result)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/files/delete { path }
export async function deleteFile(req, res) {
  try {
    const relativePath = req.body?.path
    if (!relativePath) {
      return res.status(400).json({ error: 'path is required' })
    }
    const normalized = normalizePath(relativePath)
    if (!normalized) return res.status(403).json({ error: 'invalid path' })

    const result = await tshockService.fileDelete(normalized)
    if (result.error) {
      const status = String(result.status || '500') === '404' ? 404 : 500
      return res.status(status).json({ error: result.error })
    }
    res.json(result)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/files/upload { path, data(base64), append }
// 分片上传：append=false 覆盖/创建，true 追加（非首片）
export async function uploadFile(req, res) {
  try {
    const relativePath = req.body?.path
    const data = req.body?.data
    if (!relativePath || typeof data !== 'string' || !data) {
      return res.status(400).json({ error: 'path and data are required' })
    }
    const normalized = normalizePath(relativePath)
    if (!normalized) return res.status(403).json({ error: 'invalid path' })

    const result = await tshockService.fileUpload(normalized, data, !!req.body?.append)
    if (result.error) return res.status(500).json({ error: result.error })
    res.json(result)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// GET /api/files/download?path=xxx — SSE 实时下载（不落盘后端）
// 链路：插件 /tsweb/file（SSE 分块推送）→ 后端常驻 SSE 收到 file.* 事件 → 按 tag 实时转发到浏览器
// 前端 fetch 流式读取并组装文件，全程不经后端磁盘
export async function downloadFile(req, res) {
  const relativePath = req.query.path
  if (!relativePath) {
    return res.status(400).json({ error: 'path is required' })
  }
  const normalized = normalizePath(relativePath)
  if (!normalized) return res.status(403).json({ error: 'invalid path' })

  const serverId = getCurrentServerId()
  if (!serverId) {
    return res.status(400).json({ error: 'server context missing' })
  }

  // 立即建立 SSE 响应（等插件 file.begin 事件到达后开始传数据）
  res.setHeader('Content-Type', 'text/event-stream')
  res.setHeader('Cache-Control', 'no-cache')
  res.setHeader('Connection', 'keep-alive')
  res.setHeader('X-Accel-Buffering', 'no')
  res.flushHeaders()

  const tag = 'dl-' + crypto.randomUUID()
  let ended = false
  let unregister = () => {}
  let gotBegin = false

  const endStream = () => {
    if (ended) return
    ended = true
    unregister()
    clearTimeout(stallTimer)
    try { res.end() } catch { /* ignore */ }
  }

  // 防呆：若 30 秒内未收到插件 file.begin 事件，主动报错关闭，避免前端无限等待
  // （典型场景：插件 DLL 未更新，/tsweb/file 推送的事件不带 tag，后端无法关联转发）
  const stallTimer = setTimeout(() => {
    if (!gotBegin) {
      try {
        res.write(`event: file.error\ndata: ${JSON.stringify({ reason: '未收到插件文件事件（请确认插件已更新并重启 TShock）' })}\n\n`)
      } catch { /* ignore */ }
      endStream()
    }
  }, 30000)

  // 注册下载会话：插件 file.* 事件（带同一 tag）实时转发
  unregister = registerDownloadSession(tag, (event, parsed) => {
    try {
      res.write(`event: ${event}\ndata: ${JSON.stringify(parsed)}\n\n`)
      if (event === 'file.begin') gotBegin = true
    } catch {
      endStream()
      return
    }
    if (event === 'file.end' || event === 'file.error') {
      // 延迟一帧关闭，确保 end 帧已 flush
      setTimeout(endStream, 50)
    }
  })

  // 客户端断开时清理
  req.on('close', endStream)

  const result = await requestFile(serverId, normalized, { root: 'app', tag })
  if (!result.success) {
    res.write(`event: file.error\ndata: ${JSON.stringify({ reason: result.message || 'SSE 未连接' })}\n\n`)
    return endStream()
  }
  // 插件侧 HTTP 错误（如 404/413）直接以 file.error 通知前端
  const status = String(result.status || '200')
  if (status !== '200') {
    res.write(`event: file.error\ndata: ${JSON.stringify({ reason: result.error || `HTTP ${status}` })}\n\n`)
    return endStream()
  }
  // 正常路径：等待 file.begin/chunk/end 事件驱动转发与结束
}

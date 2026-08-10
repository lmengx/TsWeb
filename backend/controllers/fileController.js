import crypto from 'crypto'
import fs from 'fs'
import path from 'path'
import { getCurrentServerId } from '../services/tshockService.js'
import tshockService from '../services/tshockService.js'
import { requestFile, registerDownloadSession, saveFileToBackend, getTransferRoot } from '../services/sseConnection.js'

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

// ═══════════════ 保存到后端（data/transfer/{serverId}/） ═══════════════

// POST /api/files/save { path } — 经 SSE 拉取并保存到后端转存目录
// 链路：插件 /tsweb/file（SSE）→ 后端落盘 sse-files → 移动至 data/transfer
// 需要先建立到插件的常驻 SSE 连接（sseConnection），否则返回 SSE 未连接
export async function saveFile(req, res) {
  try {
    const relativePath = req.body?.path
    if (!relativePath) {
      return res.status(400).json({ error: 'path is required' })
    }
    const normalized = normalizePath(relativePath)
    if (!normalized) return res.status(403).json({ error: 'invalid path' })

    const serverId = getCurrentServerId()
    if (!serverId) return res.status(400).json({ error: 'server context missing' })

    const result = await saveFileToBackend(serverId, normalized, { root: 'app' })
    if (!result.success) {
      return res.status(502).json({ error: result.error || result.message || '保存失败' })
    }
    res.json({ success: true, name: result.name, size: result.size })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// GET /api/files/saved — 列出已保存到后端的文件
export async function listSavedFiles(req, res) {
  try {
    const serverId = getCurrentServerId()
    if (!serverId) return res.status(400).json({ error: 'server context missing' })
    const dir = path.join(getTransferRoot(), String(serverId))
    let files = []
    if (fs.existsSync(dir)) {
      files = fs.readdirSync(dir)
        .filter(f => fs.statSync(path.join(dir, f)).isFile())
        .map(f => {
          const st = fs.statSync(path.join(dir, f))
          return { name: f, size: st.size, mtime: st.mtimeMs }
        })
        .sort((a, b) => b.mtime - a.mtime)
    }
    res.json({ files })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// GET /api/files/saved/download?name= — 下载已保存文件
export async function downloadSavedFile(req, res) {
  try {
    const serverId = getCurrentServerId()
    if (!serverId) return res.status(400).json({ error: 'server context missing' })
    const name = String(req.query.name || '')
    if (!name) return res.status(400).json({ error: 'name is required' })
    const safeName = path.basename(name).replace(/[\\/:*?"<>|]/g, '_')
    const full = path.join(getTransferRoot(), String(serverId), safeName)
    if (!fs.existsSync(full) || !fs.statSync(full).isFile()) {
      return res.status(404).json({ error: '文件不存在' })
    }
    res.download(full, safeName)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/files/saved/delete { name } — 删除已保存文件
export async function deleteSavedFile(req, res) {
  try {
    const serverId = getCurrentServerId()
    if (!serverId) return res.status(400).json({ error: 'server context missing' })
    const name = req.body?.name
    if (!name) return res.status(400).json({ error: 'name is required' })
    const safeName = path.basename(String(name)).replace(/[\\/:*?"<>|]/g, '_')
    const full = path.join(getTransferRoot(), String(serverId), safeName)
    if (!fs.existsSync(full)) {
      return res.status(404).json({ error: '文件不存在' })
    }
    fs.unlinkSync(full)
    res.json({ success: true })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

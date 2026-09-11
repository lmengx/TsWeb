import fs from 'fs'
import path from 'path'
import { getCurrentServerId } from '../services/tshockService.js'
import tshockService from '../services/tshockService.js'
import { getTransferRoot } from '../services/sseConnection.js'

/**
 * 玩家角色（.plr）导入导出。
 *
 * 链路：
 *   导出  → 插件 REST /data/players/export（tsCharacter → .plr）
 *          → download: 返回 base64（前端可下载或存后端目录）
 *          → server:   存服务端 PlayerExports/
 *   导入  → base64（浏览器上传 / 后端目录读取）→ 插件 REST /data/players/import
 *          → 服务端文件 → 插件 REST /data/players/import-from-server
 *   后端目录 → data/transfer/{serverId}/plr/（纯后端 fs，不走插件）
 */

// 安全文件名（去路径分隔符与非法字符）
function safeName(name) {
  return path.basename(String(name || '')).replace(/[\\/:*?"<>|]/g, '_')
}

// 当前服务器 plr 后端目录
function getPlrBackendDir() {
  const serverId = getCurrentServerId()
  if (!serverId) return null
  return path.join(getTransferRoot(), String(serverId), 'plr')
}

// ═══════════════ 导出 ═══════════════

// GET /api/players/export?username=X&to=download|server
// download → { base64, filename }；server → 服务端 PlayerExports 保存结果
export async function exportPlayer(req, res) {
  const username = req.query?.username
  const to = req.query?.to || 'download'
  if (!username) {
    return res.status(400).json({ error: 'username 为必填' })
  }
  const result = await tshockService.proxyDataRequest(`players/export?username=${encodeURIComponent(username)}&to=${encodeURIComponent(to)}`, 'GET', {})
  if (result.error && !result.base64) {
    return res.status(502).json({ error: result.error })
  }
  res.json(result)
}

// GET /api/players/export-all — 批量导出到服务端 PlayerExports/
export async function exportAll(req, res) {
  const result = await tshockService.proxyDataRequest('players/export-all', 'GET', {})
  if (result.error) {
    return res.status(502).json({ error: result.error })
  }
  res.json(result)
}

// GET /api/players/export-list — 服务端 PlayerExports/<世界名>/ 下的 .plr 文件
export async function listServerExports(req, res) {
  const result = await tshockService.proxyDataRequest('players/export-list', 'GET', {})
  if (result.error) {
    return res.status(502).json({ error: result.error })
  }
  res.json(result)
}

// GET /api/players/has-character?username=X
export async function hasCharacter(req, res) {
  const username = req.query?.username
  if (!username) {
    return res.status(400).json({ error: 'username 为必填' })
  }
  const result = await tshockService.proxyDataRequest(`players/has-character?username=${encodeURIComponent(username)}`, 'GET', {})
  if (result.error) {
    return res.status(502).json({ error: result.error })
  }
  res.json(result)
}

// ═══════════════ 导入 ═══════════════

// POST /api/players/import { username, plrBase64 } — 覆盖导入（base64 走 POST form，避免超长 URL）
export async function importPlayer(req, res) {
  const { username, plrBase64 } = req.body || {}
  if (!username || !plrBase64) {
    return res.status(400).json({ error: 'username 和 plrBase64 为必填' })
  }
  if (typeof plrBase64 !== 'string') {
    return res.status(400).json({ error: 'plrBase64 必须为字符串' })
  }
  const result = await tshockService.importPlayerData(username, plrBase64)
  if (result.error) {
    return res.status(502).json({ error: result.error })
  }
  res.json(result)
}

// POST /api/players/import-from-server { username, path } — 从服务端文件导入
export async function importFromServer(req, res) {
  const { username, path: fileName } = req.body || {}
  if (!username || !fileName) {
    return res.status(400).json({ error: 'username 和 path 为必填' })
  }
  const result = await tshockService.proxyDataRequest(
    `players/import-from-server?username=${encodeURIComponent(username)}&path=${encodeURIComponent(fileName)}`,
    'GET', {})
  if (result.error) {
    return res.status(502).json({ error: result.error })
  }
  res.json(result)
}

// ═══════════════ 后端目录（data/transfer/{serverId}/plr/） ═══════════════

// POST /api/players/backend-save { filename, base64 } — 保存 plr 到后端目录
export async function saveToBackend(req, res) {
  try {
    const dir = getPlrBackendDir()
    if (!dir) return res.status(400).json({ error: 'server context missing' })

    const filename = safeName(req.body?.filename)
    const data = req.body?.base64
    if (!filename || !filename.endsWith('.plr')) {
      return res.status(400).json({ error: 'filename 必须为 .plr 文件名' })
    }
    if (typeof data !== 'string' || !data) {
      return res.status(400).json({ error: 'base64 为必填' })
    }

    let buf
    try {
      buf = Buffer.from(data, 'base64')
    } catch {
      return res.status(400).json({ error: 'base64 无效' })
    }
    if (buf.length === 0 || buf.length > 50 * 1024 * 1024) {
      return res.status(400).json({ error: '文件为空或超过 50MB' })
    }

    fs.mkdirSync(dir, { recursive: true })
    const full = path.join(dir, filename)
    fs.writeFileSync(full, buf)
    res.json({ success: true, filename, size: buf.length, path: full })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

// GET /api/players/backend-list — 后端目录 plr 文件列表
export async function listBackend(req, res) {
  try {
    const dir = getPlrBackendDir()
    if (!dir) return res.status(400).json({ error: 'server context missing' })
    if (!fs.existsSync(dir)) return res.json({ files: [] })

    const files = fs.readdirSync(dir)
      .filter(f => f.endsWith('.plr'))
      .map(f => {
        const fp = path.join(dir, f)
        const stat = fs.statSync(fp)
        return {
          filename: f,
          size: stat.size,
          lastModified: stat.mtime.toISOString()
        }
      })
      .sort((a, b) => new Date(b.lastModified) - new Date(a.lastModified))
    res.json({ files })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

// GET /api/players/backend-download?filename=X — 读取后端目录 plr（base64）
export async function downloadFromBackend(req, res) {
  try {
    const dir = getPlrBackendDir()
    if (!dir) return res.status(400).json({ error: 'server context missing' })

    const filename = safeName(req.query?.filename)
    if (!filename || !filename.endsWith('.plr')) {
      return res.status(400).json({ error: 'filename 必须为 .plr 文件名' })
    }
    const full = path.join(dir, filename)
    if (!fs.existsSync(full)) {
      return res.status(404).json({ error: '文件不存在' })
    }
    const buf = fs.readFileSync(full)
    res.json({ success: true, filename, base64: buf.toString('base64'), size: buf.length })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

// POST /api/players/backend-delete { filename } — 删除后端目录 plr
export async function deleteFromBackend(req, res) {
  try {
    const dir = getPlrBackendDir()
    if (!dir) return res.status(400).json({ error: 'server context missing' })

    const filename = safeName(req.body?.filename)
    if (!filename || !filename.endsWith('.plr')) {
      return res.status(400).json({ error: 'filename 必须为 .plr 文件名' })
    }
    const full = path.join(dir, filename)
    if (!fs.existsSync(full)) {
      return res.status(404).json({ error: '文件不存在' })
    }
    fs.unlinkSync(full)
    res.json({ success: true })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

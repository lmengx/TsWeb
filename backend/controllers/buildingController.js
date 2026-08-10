import fs from 'fs'
import path from 'path'
import { getCurrentServerId } from '../services/tshockService.js'
import tshockService from '../services/tshockService.js'
import { saveFileToBackend, getBuildingRoot } from '../services/sseConnection.js'

// 建筑存档（房屋导入导出）：后端侧 .tsb 文件库位于 data/transfer/building/（平铺）
// 插件端 .tsb 位于 {TShock.SavePath}/TSWeb/Buildings/（root=tshock 相对路径 TSWeb/Buildings）

const PLUGIN_BUILDINGS_REL = 'TSWeb/Buildings'   // 插件端相对 tshock root

function safeTsbName(name) {
  const n = path.basename(String(name || '').replace(/\\/g, '/')).replace(/[\\/:*?"<>|]/g, '_')
  return n.endsWith('.tsb') ? n : null
}

/** 解析 .tsb JSON 外壳（不读 tile 二进制），损坏文件保留基础信息 */
function parseTsbMeta(full, file) {
  let name = '', author = '', createdAt = ''
  let width = 0, height = 0, entities = 0
  try {
    const j = JSON.parse(fs.readFileSync(full, 'utf8'))
    name = j?.meta?.name || ''
    author = j?.meta?.author || ''
    createdAt = j?.meta?.createdAt || ''
    width = j?.size?.width || 0
    height = j?.size?.height || 0
    entities = Array.isArray(j?.entities) ? j.entities.length : 0
  } catch { /* 忽略解析失败 */ }
  const st = fs.statSync(full)
  return {
    file,
    name,
    author,
    createdAt,
    width,
    height,
    entities,
    sizeBytes: st.size,
    modifiedAt: st.mtimeMs
  }
}

// GET /api/buildings/list — 列出后端 data/transfer/building/ 下的 .tsb
export async function listBuildings(req, res) {
  try {
    const dir = getBuildingRoot()
    let files = []
    if (fs.existsSync(dir)) {
      files = fs.readdirSync(dir)
        .filter(f => f.endsWith('.tsb'))
        .map(f => {
          const full = path.join(dir, f)
          if (!fs.statSync(full).isFile()) return null
          return parseTsbMeta(full, f)
        })
        .filter(Boolean)
        .sort((a, b) => b.modifiedAt - a.modifiedAt)
    }
    res.json({ files })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/buildings/send { file } — 插件本地 .tsb → 后端（平铺），不保留本地副本
export async function sendToBackend(req, res) {
  try {
    const file = safeTsbName(req.body?.file)
    if (!file) return res.status(400).json({ error: 'file 必须为 .tsb 文件名' })
    const serverId = getCurrentServerId()
    if (!serverId) return res.status(400).json({ error: 'server context missing' })

    const result = await saveFileToBackend(serverId, `${PLUGIN_BUILDINGS_REL}/${file}`, {
      root: 'tshock',
      destDir: getBuildingRoot()
    })
    if (!result.success) {
      return res.status(502).json({ error: result.error || result.message || '传输失败' })
    }
    // 不保留本地副本
    const del = await tshockService.buildingsDeleteLocal(file)
    res.json({ success: true, name: result.name, size: result.size, localDeleted: !del?.error })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/buildings/export-to-backend { house } — 房屋直接导出到后端（不经本地存档）
export async function exportToBackend(req, res) {
  try {
    const house = req.body?.house
    if (!house) return res.status(400).json({ error: 'house is required' })
    const serverId = getCurrentServerId()
    if (!serverId) return res.status(400).json({ error: 'server context missing' })

    // 插件端导出到本地 TSWeb/Buildings/
    const exp = await tshockService.proxyDataRequest('buildings/export', 'POST', { house })
    if (!exp?.success) {
      return res.status(502).json({ error: exp?.error || '房屋导出失败' })
    }
    const file = safeTsbName(exp.file)
    if (!file) return res.status(500).json({ error: '导出文件异常' })

    // SSE 拉取到后端平铺目录
    const result = await saveFileToBackend(serverId, `${PLUGIN_BUILDINGS_REL}/${file}`, {
      root: 'tshock',
      destDir: getBuildingRoot()
    })
    if (!result.success) {
      // 清理插件本地临时文件，避免残留
      await tshockService.buildingsDeleteLocal(file)
      return res.status(502).json({ error: result.error || result.message || '传输失败' })
    }
    // 不保留插件本地副本
    await tshockService.buildingsDeleteLocal(file)
    res.json({
      success: true,
      name: result.name,
      size: result.size,
      width: exp.width,
      height: exp.height,
      house: exp.file
    })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/buildings/upload { file } — 后端 .tsb → 插件 TSWeb/Buildings/（分片 base64）
export async function uploadToPlugin(req, res) {
  try {
    const file = safeTsbName(req.body?.file)
    if (!file) return res.status(400).json({ error: 'file 必须为 .tsb 文件名' })

    const full = path.join(getBuildingRoot(), file)
    if (!fs.existsSync(full) || !fs.statSync(full).isFile()) {
      return res.status(404).json({ error: '后端文件不存在: ' + file })
    }

    const buf = fs.readFileSync(full)
    const CHUNK = 3 * 1024 * 1024   // 每片 3MB 二进制 → base64 ~4MB，低于 REST 10MB 上限
    const chunks = Math.max(1, Math.ceil(buf.length / CHUNK))
    for (let i = 0; i < chunks; i++) {
      const slice = buf.subarray(i * CHUNK, Math.min((i + 1) * CHUNK, buf.length))
      const result = await tshockService.buildingsUpload(file, slice.toString('base64'), i > 0)
      if (result.error) {
        return res.status(500).json({ error: `第 ${i + 1}/${chunks} 片上传失败: ${result.error}` })
      }
    }
    res.json({ success: true, name: file, size: buf.length, chunks })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/buildings/import { file, anchor, anchorPlayer/anchorHouse/coords, align } — 导入到世界（插件端执行）
export async function importToWorld(req, res) {
  try {
    const file = req.body?.file
    if (!file) return res.status(400).json({ error: 'file is required' })

    const params = { file, anchor: req.body?.anchor || 'player', align: req.body?.align || 'center' }
    if (req.body?.anchorPlayer) params.anchorPlayer = req.body.anchorPlayer
    if (req.body?.anchorHouse) params.anchorHouse = req.body.anchorHouse
    if (req.body?.coords) params.coords = req.body.coords

    const result = await tshockService.proxyDataRequest('buildings/import', 'POST', params)
    if (result.error && !result.success) {
      return res.status(400).json({ error: result.error, startX: result.startX, startY: result.startY })
    }
    res.json(result)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/buildings/delete { file } — 删除后端 .tsb
export async function deleteBuilding(req, res) {
  try {
    const file = safeTsbName(req.body?.file)
    if (!file) return res.status(400).json({ error: 'file 必须为 .tsb 文件名' })
    const full = path.join(getBuildingRoot(), file)
    if (!fs.existsSync(full)) return res.status(404).json({ error: '文件不存在' })
    fs.unlinkSync(full)
    res.json({ success: true })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// GET /api/buildings/download?file= — 下载后端 .tsb 到浏览器
export async function downloadBuilding(req, res) {
  try {
    const file = safeTsbName(req.query.file)
    if (!file) return res.status(400).json({ error: 'file 必须为 .tsb 文件名' })
    const full = path.join(getBuildingRoot(), file)
    if (!fs.existsSync(full) || !fs.statSync(full).isFile()) {
      return res.status(404).json({ error: '文件不存在' })
    }
    res.download(full, file)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

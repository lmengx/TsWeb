import { checkPermission, getAllowedTree, loadRules, filterListResult } from '../services/fileAccessService.js'
import tshockService from '../services/tshockService.js'

// GET /api/files/access — 获取可访问的基础目录结构（懒加载）
export async function getAccessRules(req, res) {
  try {
    await loadRules()
    const tree = getAllowedTree()
    res.json({ tree })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// GET /api/files/read?path=xxx
export async function readFile(req, res) {
  try {
    const relativePath = req.query.path
    if (!relativePath) {
      return res.status(400).json({ error: 'path is required' })
    }

    if (!checkPermission(relativePath, 'read')) {
      return res.status(403).json({ error: 'permission denied' })
    }

    const result = await tshockService.fileRead(relativePath)

    // 追加可写标记
    if (result.content !== undefined) {
      result.canWrite = checkPermission(relativePath, 'write')
    }

    res.json(result)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// POST /api/files/write { path, content }
export async function writeFile(req, res) {
  try {
    const { path: relativePath, content } = req.body
    if (!relativePath || content === undefined || content === null) {
      return res.status(400).json({ error: 'path and content are required' })
    }

    if (!checkPermission(relativePath, 'write')) {
      return res.status(403).json({ error: 'permission denied' })
    }

    const result = await tshockService.fileWrite(relativePath, content)
    res.json(result)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

// GET /api/files/list?path=xxx
// 列出目录内容，并根据 read 白名单过滤结果
export async function listDir(req, res) {
  try {
    const relativePath = req.query.path
    if (!relativePath) {
      return res.status(400).json({ error: 'path is required' })
    }

    if (!checkPermission(relativePath, 'list')) {
      return res.status(403).json({ error: 'permission denied' })
    }

    // 确保以 / 结尾
    const dirPath = relativePath.endsWith('/') ? relativePath : relativePath + '/'

    const result = await tshockService.fileList(dirPath)

    // 如果插件返回了条目，用白名单过滤
    if (result.entries && Array.isArray(result.entries)) {
      result.entries = filterListResult(result.entries, dirPath)
    }

    res.json(result)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

import { Router } from 'express'
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const presetDir = path.join(__dirname, '../res/导出数据')

// 确保目录存在
if (!fs.existsSync(presetDir)) {
  fs.mkdirSync(presetDir, { recursive: true })
}

const router = Router()

// 保存预设
router.post('/save', (req, res) => {
  try {
    const { name, data } = req.body
    if (!name || !data) {
      return res.status(400).json({ error: 'name 和 data 为必填' })
    }
    // 防止路径穿越
    const safeName = name.replace(/[^a-zA-Z0-9_\u4e00-\u9fa5\-]/g, '_')
    const filePath = path.join(presetDir, safeName + '.json')
    fs.writeFileSync(filePath, JSON.stringify(data, null, 2), 'utf8')
    res.json({ success: true, name: safeName })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

// 列出预设
router.get('/list', (req, res) => {
  try {
    if (!fs.existsSync(presetDir)) {
      return res.json({ presets: [] })
    }
    const files = fs.readdirSync(presetDir)
      .filter(f => f.endsWith('.json'))
      .map(f => {
        const fp = path.join(presetDir, f)
        const stat = fs.statSync(fp)
        return {
          name: f.replace(/\.json$/, ''),
          filename: f,
          size: stat.size,
          lastModified: stat.mtime.toISOString()
        }
      })
      .sort((a, b) => new Date(b.lastModified) - new Date(a.lastModified))
    res.json({ presets: files })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

// 读取预设内容
router.get('/read', (req, res) => {
  try {
    const { name } = req.query
    if (!name) return res.status(400).json({ error: 'name 为必填' })
    const safeName = name.replace(/[^a-zA-Z0-9_\u4e00-\u9fa5\-]/g, '_')
    const filePath = path.join(presetDir, safeName + '.json')
    if (!fs.existsSync(filePath)) {
      return res.status(404).json({ error: '预设不存在' })
    }
    const content = fs.readFileSync(filePath, 'utf8')
    res.json({ success: true, data: JSON.parse(content) })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

// 删除预设
router.delete('/delete', (req, res) => {
  try {
    const { name } = req.query
    if (!name) return res.status(400).json({ error: 'name 为必填' })
    const safeName = name.replace(/[^a-zA-Z0-9_\u4e00-\u9fa5\-]/g, '_')
    const filePath = path.join(presetDir, safeName + '.json')
    if (!fs.existsSync(filePath)) {
      return res.status(404).json({ error: '预设不存在' })
    }
    fs.unlinkSync(filePath)
    res.json({ success: true })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

export default router

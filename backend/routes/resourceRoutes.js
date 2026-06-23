import { Router } from 'express'
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const resourceDir = path.join(__dirname, '../resource')

const router = Router()

router.get('/list', (req, res) => {
  try {
    if (!fs.existsSync(resourceDir)) {
      return res.json({ status: '200', files: [] })
    }

    const files = fs.readdirSync(resourceDir).map(filename => {
      const filePath = path.join(resourceDir, filename)
      const stat = fs.statSync(filePath)
      return {
        name: filename,
        size: stat.size,
        sizeFormatted: formatFileSize(stat.size),
        lastModified: stat.mtime.toISOString(),
        type: getFileType(filename)
      }
    })

    res.json({ status: '200', files })
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
})

router.get('/download/:filename', (req, res) => {
  try {
    const { filename } = req.params
    const filePath = path.join(resourceDir, filename)

    if (!fs.existsSync(filePath)) {
      return res.status(404).json({ status: '404', error: 'File not found' })
    }

    const stat = fs.statSync(filePath)
    res.setHeader('Content-Length', stat.size)
    res.setHeader('Content-Disposition', `attachment; filename="${encodeURIComponent(filename)}"`)
    
    const fileStream = fs.createReadStream(filePath)
    fileStream.pipe(res)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
})

function formatFileSize(bytes) {
  if (bytes === 0) return '0 Bytes'
  const k = 1024
  const sizes = ['Bytes', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

function getFileType(filename) {
  const ext = path.extname(filename).toLowerCase()
  const types = {
    '.exe': '可执行文件',
    '.zip': '压缩文件',
    '.rar': '压缩文件',
    '.7z': '压缩文件',
    '.txt': '文本文件',
    '.json': 'JSON文件',
    '.xml': 'XML文件',
    '.png': '图片文件',
    '.jpg': '图片文件',
    '.jpeg': '图片文件',
    '.gif': '图片文件',
    '.svg': '图片文件',
    '.pdf': 'PDF文件',
    '.doc': '文档文件',
    '.docx': '文档文件',
    '.xls': '表格文件',
    '.xlsx': '表格文件'
  }
  return types[ext] || '其他文件'
}

export default router
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'
import tshockService from '../services/tshockService.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const configDir = path.join(__dirname, '../config/反作弊')

// 许可文件
const LICENSE_PATH = path.join(__dirname, '../../frontend/public/.coffee_license')

export const getLicenseCheck = (req, res) => {
  try {
    if (fs.existsSync(LICENSE_PATH)) {
      const content = fs.readFileSync(LICENSE_PATH, 'utf8').trim()
      if (content === 'coffeed') {
        return res.json({ hidden: true })
      }
    }
    res.json({ hidden: false })
  } catch {
    res.json({ hidden: false })
  }
}

export const postLicenseClose = (req, res) => {
  try {
    const dir = path.dirname(LICENSE_PATH)
    if (!fs.existsSync(dir)) {
      fs.mkdirSync(dir, { recursive: true })
    }
    fs.writeFileSync(LICENSE_PATH, 'coffeed', 'utf8')
    res.json({ success: true })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const getConfigFile = async (req, res) => {
  try {
    const { name } = req.query
    if (!name) {
      return res.status(400).json({ status: '400', error: 'Missing file name' })
    }

    const filePath = path.join(configDir, name)
    
    if (!fs.existsSync(filePath)) {
      return res.status(404).json({ status: '404', error: 'File not found' })
    }

    const content = fs.readFileSync(filePath, 'utf8')
    res.json({ status: '200', content })
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const saveConfigFile = async (req, res) => {
  try {
    const { name, content } = req.body
    if (!name || !content) {
      return res.status(400).json({ status: '400', error: 'Missing parameters' })
    }

    const filePath = path.join(configDir, name)
    
    fs.writeFileSync(filePath, content, 'utf8')
    res.json({ status: '200', message: 'File saved successfully' })
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const getTsWebConfig = async (req, res) => {
  try {
    const result = await tshockService.getTsWebConfig()
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const setTsWebConfig = async (req, res) => {
  try {
    const result = await tshockService.setTsWebConfig(req.body)
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const getBossLimitStatus = async (req, res) => {
  try {
    const result = await tshockService.getBossLimitStatus()
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

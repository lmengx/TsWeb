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

export const getPromotionConfig = async (req, res) => {
  try {
    const result = await tshockService.getPromotionConfig()
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const setPromotionConfig = async (req, res) => {
  try {
    const result = await tshockService.setPromotionConfig(req.body)
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

// ═══ 日志 Webhook 配置 ═══

export const getLogWebhookConfig = async (req, res) => {
  try {
    const { getLogWebhookConfig } = await import('../config.js')
    const cfg = await getLogWebhookConfig()
    res.json({ status: '200', ...cfg })
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const setLogWebhookConfig = async (req, res) => {
  try {
    const { saveLogWebhookConfig } = await import('../config.js')
    const { enabled, publicUrl } = req.body
    const result = await saveLogWebhookConfig({ enabled, publicUrl })

    // 保存后立即生效：注册或注销
    if (enabled) {
      const { updatePluginWebhook } = await import('../services/webhookRegistration.js')
      const regResult = await updatePluginWebhook(result.publicUrl)
      res.json({ status: '200', ...result, registerResult: regResult })
    } else {
      const { updatePluginWebhook } = await import('../services/webhookRegistration.js')
      const unregResult = await updatePluginWebhook(null)
      res.json({ status: '200', ...result, registerResult: unregResult })
    }
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

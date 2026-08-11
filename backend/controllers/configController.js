import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'
import tshockService from '../services/tshockService.js'
import audit from '../services/auditLogger.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const configDir = path.join(__dirname, '../data/anticheat')

// 确保反作弊配置目录存在（写入时依赖）
if (!fs.existsSync(configDir)) {
  fs.mkdirSync(configDir, { recursive: true })
}

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

// ═══ 后端监听设置（server.port / server.host） ═══

export const getListenConfig = async (req, res) => {
  try {
    const { getConfig } = await import('../config.js')
    const cfg = await getConfig()
    if (!cfg) return res.json({ status: '200', server: { port: 3000, host: '0.0.0.0' } })
    res.json({ status: '200', server: { port: cfg.server?.port || 3000, host: cfg.server?.host || '0.0.0.0' } })
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const saveListenConfig = async (req, res) => {
  try {
    const { getConfig, saveConfig } = await import('../config.js')
    const { port, host } = req.body?.server || req.body || {}
    const cfg = await getConfig()
    if (!cfg) return res.status(400).json({ status: '400', error: '配置未初始化' })
    const newPort = parseInt(port)
    if (!newPort || newPort < 1 || newPort > 65535) {
      return res.status(400).json({ status: '400', error: '端口必须是 1-65535 之间的数字' })
    }
    await saveConfig({
      server: { port: newPort, host: String(host || '0.0.0.0') }
    })
    audit.record('config.update', {
      changedKeys: ['server.port', 'server.host'],
      actor: req.user?.username
    })
    res.json({ status: '200', message: '监听配置已保存（重启后生效）' })
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

export const getBossConfig = async (req, res) => {
  try {
    const result = await tshockService.getBossConfig()
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const setBossConfig = async (req, res) => {
  try {
    const result = await tshockService.setBossConfig(req.body)
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const getBackupConfig = async (req, res) => {
  try {
    const result = await tshockService.getBackupConfig()
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const setBackupConfig = async (req, res) => {
  try {
    const result = await tshockService.setBackupConfig(req.body)
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
    const { saveLogWebhookConfig, getServers } = await import('../config.js')
    const { enabled, publicUrl } = req.body
    const result = await saveLogWebhookConfig({ enabled, publicUrl })

    // 保存后立即生效：对全部已配置服务器注册/注销
    const { registerAllWebhooks, updatePluginWebhook } = await import('../services/webhookRegistration.js')
    const servers = await getServers()
    if (enabled) {
      const results = []
      for (const s of servers.filter(x => x.enabled && x.host && x.port && x.apiKey)) {
        const url = result.publicUrl || `http://127.0.0.1:${result.port || 3000}/hook/log`
        const r = await updatePluginWebhook(s.id, url)
        results.push({ serverId: s.id, ...r })
      }
      res.json({ status: '200', ...result, registerResults: results })
    } else {
      const results = []
      for (const s of servers) {
        const r = await updatePluginWebhook(s.id, null)
        results.push({ serverId: s.id, ...r })
      }
      res.json({ status: '200', ...result, registerResults: results })
    }
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

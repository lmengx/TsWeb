import { Router } from 'express'
import { saveNewConfig } from '../config.js'
import { validateSetupToken, generateSetupToken } from '../setupToken.js'
import tshockService from '../services/tshockService.js'

const router = Router()

router.get('/check', async (req, res) => {
  const token = req.query.token
  if (!token || !validateSetupToken(token)) {
    return res.json({ configured: false, needToken: true })
  }
  const { isConfigFileExists } = await import('../config.js')
  const exists = await isConfigFileExists()
  res.json({ configured: exists, needToken: false })
})

router.post('/init', async (req, res) => {
  try {
    const token = req.body.token || req.query.token
    if (!token || !validateSetupToken(token)) {
      return res.status(403).json({ error: '无效的 Setup Token' })
    }

    const { host, port, apiKey } = req.body
    if (!host || !port || !apiKey) {
      return res.status(400).json({ error: 'host、port、apiKey 均为必填' })
    }

    // 先测试连接，成功才写入
    const connected = await tshockService.testConnectionWith(host, port, apiKey)
    if (!connected) {
      return res.json({ success: false, error: '无法连接到目标 TShock 服务器，请检查地址、端口和密钥' })
    }

    await saveNewConfig({ host, port, apiKey })
    generateSetupToken()

    // 用新配置重载服务
    const { loadConfig } = await import('../config.js')
    const cfg = await loadConfig()
    tshockService.reloadConfig(cfg)

    res.json({ success: true })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

export default router

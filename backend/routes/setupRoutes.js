import { Router } from 'express'
import { saveNewConfig } from '../config.js'
import { validateSetupToken, generateSetupToken } from '../setupToken.js'
import tshockService from '../services/tshockService.js'
import { exec } from 'child_process'
import { promisify } from 'util'
import fs from 'fs/promises'
import path from 'path'

const execAsync = promisify(exec)

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
    const result = await tshockService.testConnectionWith(host, port, apiKey)
    if (!result.success) {
      return res.json({ success: false, error: result.error })
    }
    await saveNewConfig({ host, port, apiKey })
    generateSetupToken()
    const { loadConfig } = await import('../config.js')
    const cfg = await loadConfig()
    tshockService.reloadConfig(cfg)
    res.json({ success: true })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

router.get('/probe', async (req, res) => {
  try {
    const token = req.query.token
    if (!token || !validateSetupToken(token)) {
      return res.status(403).json({ error: '无效的 Setup Token' })
    }
    const port = req.query.port || '7777'
    const { stdout: netstatOut } = await execAsync(`netstat -ano | findstr :${port} `)
    const lines = netstatOut.trim().split('\n').filter(l => l.includes('LISTENING'))
    if (lines.length === 0) {
      return res.json({ found: false, port: parseInt(port), processes: [] })
    }
    const pids = [...new Set(lines.map(l => l.trim().split(/\s+/).pop()))]
    const processes = []
    for (const pid of pids) {
      let path = '未知'
      try {
        const { stdout } = await execAsync(`wmic process where processid=${pid} get executablepath /format:value`)
        const match = stdout.match(/ExecutablePath=(.+)/)
        if (match) path = match[1].trim()
      } catch {}
      if (path === '未知') {
        try {
          const { stdout } = await execAsync(`powershell -Command "(Get-Process -Id ${pid}).Path"`)
          const p = stdout.trim().split('\r\n')[0].trim()
          if (p) path = p
        } catch {}
      }
      if (path === '未知') {
        try {
          const { stdout } = await execAsync(`tasklist /FI "PID eq ${pid}" /FO CSV /NH`)
          const parts = stdout.trim().split(',')
          if (parts[0]) path = parts[0].replace(/"/g, '')
        } catch {}
      }
      processes.push({ pid: parseInt(pid), path })
    }
    res.json({ found: true, port: parseInt(port), processes })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

router.post('/auto-read', async (req, res) => {
  try {
    const token = req.body.token || req.query.token
    if (!token || !validateSetupToken(token)) {
      return res.status(403).json({ error: '无效的 Setup Token' })
    }
    const { processPath } = req.body
    if (!processPath) {
      return res.status(400).json({ error: '缺少 processPath' })
    }
    const serverDir = path.dirname(processPath)
    const configPath = path.join(serverDir, 'tshock', 'config.json')
    let raw
    try {
      raw = await fs.readFile(configPath, 'utf8')
    } catch {
      return res.json({ success: false, error: '未找到 tshock/config.json，请确认 TShock 已正确安装' })
    }
    const config = JSON.parse(raw)
    const settings = config.Settings || config
    if (!settings.RestApiPort) {
      return res.json({ success: false, error: '配置文件中未找到 RestApiPort' })
    }
    const restPort = settings.RestApiPort
    const tokenKey = generateRandomToken(35)
    settings.RestApiEnabled = true
    if (!settings.ApplicationRestTokens) {
      settings.ApplicationRestTokens = {}
    }
    settings.ApplicationRestTokens[tokenKey] = {
      Username: 'TSWeb',
      UserGroupName: 'superadmin'
    }
    await fs.writeFile(configPath, JSON.stringify(config, null, 2), 'utf8')
    res.json({ success: true, restPort, tokenKey, configPath })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

router.post('/auto-verify', async (req, res) => {
  try {
    const token = req.body.token || req.query.token
    if (!token || !validateSetupToken(token)) {
      return res.status(403).json({ error: '无效的 Setup Token' })
    }
    const { host, port, apiKey } = req.body
    if (!host || !port || !apiKey) {
      return res.status(400).json({ error: 'host、port、apiKey 均为必填' })
    }
    const connected = await tshockService.testConnectionWith(host, port, apiKey)
    if (!connected.success) {
      return res.json({ success: false, error: connected.error })
    }
    await saveNewConfig({ host, port, apiKey })
    generateSetupToken()
    const { loadConfig } = await import('../config.js')
    const cfg = await loadConfig()
    tshockService.reloadConfig(cfg)
    res.json({ success: true })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

router.post('/auto-remote', async (req, res) => {
  try {
    const token = req.body.token || req.query.token
    if (!token || !validateSetupToken(token)) {
      return res.status(403).json({ error: '无效的 Setup Token' })
    }
    const { configRaw } = req.body
    if (!configRaw) {
      return res.status(400).json({ error: '缺少 configRaw' })
    }
    let config
    try {
      config = JSON.parse(configRaw)
    } catch {
      return res.json({ success: false, error: '无效的 JSON 格式' })
    }
    const settings = config.Settings || config
    if (!settings.RestApiPort) {
      return res.json({ success: false, error: '配置文件中未找到 RestApiPort' })
    }
    const restPort = settings.RestApiPort
    const tokenKey = generateRandomToken(35)
    settings.RestApiEnabled = true
    if (!settings.ApplicationRestTokens) {
      settings.ApplicationRestTokens = {}
    }
    settings.ApplicationRestTokens[tokenKey] = {
      Username: 'TSWeb',
      UserGroupName: 'superadmin'
    }
    const modifiedRaw = JSON.stringify(config, null, 2)
    res.json({ success: true, restPort, tokenKey, modifiedRaw })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

function generateRandomToken(length) {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
  let result = ''
  for (let i = 0; i < length; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length))
  }
  return result
}

export default router

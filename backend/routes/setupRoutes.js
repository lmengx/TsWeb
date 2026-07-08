import { Router } from 'express'
import { saveNewConfig, getConfig } from '../config.js'
import { validateSetupToken, generateSetupToken } from '../setupToken.js'
import tshockService from '../services/tshockService.js'
import { exec } from 'child_process'
import { promisify } from 'util'
import fs from 'fs/promises'
import path from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

const execAsync = promisify(exec)

// 直接向 TShock REST API 发请求
const tshockFetch = async (pathname) => {
  const config = await getConfig()
  const host = config.tshock?.host || 'localhost'
  const baseUrl = (host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`) + ':' + (config.tshock?.port || 7878)
  const apiKey = config.tshock?.apiKey || ''
  const sep = pathname.includes('?') ? '&' : '?'
  const url = `${baseUrl}${pathname}${sep}token=${encodeURIComponent(apiKey)}`
  const res = await fetch(url)
  return res.json()
}

const router = Router()

router.get('/check', async (req, res) => {
  const token = req.query.token
  if (!token || !validateSetupToken(token)) {
    return res.json({ configured: false, needToken: true })
  }
  const { isConfigFileExists, getConfig } = await import('../config.js')
  const exists = await isConfigFileExists()
  let config = { host: '', port: '', apiKey: '' }
  if (exists) {
    try {
      const cfg = await getConfig()
      config = {
        host: cfg.tshock?.host || 'localhost',
        port: cfg.tshock?.port || 7878,
        apiKey: cfg.tshock?.apiKey || ''
      }
    } catch {}
  }
  res.json({ configured: exists, needToken: false, setupToken: token, config })
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

    // 检查是否已有 TSWeb 的 superadmin token，有则复用
    let tokenKey
    let reused = false
    if (settings.ApplicationRestTokens) {
      for (const [k, v] of Object.entries(settings.ApplicationRestTokens)) {
        if (v.Username === 'TSWeb' && v.UserGroupName === 'superadmin') {
          tokenKey = k
          reused = true
          break
        }
      }
    }
    if (!tokenKey) {
      tokenKey = generateRandomToken(35)
    }

    settings.RestApiEnabled = true
    if (!settings.ApplicationRestTokens) {
      settings.ApplicationRestTokens = {}
    }
    if (!reused) {
      settings.ApplicationRestTokens[tokenKey] = {
        Username: 'TSWeb',
        UserGroupName: 'superadmin'
      }
    }
    await fs.writeFile(configPath, JSON.stringify(config, null, 2), 'utf8')

    // 自动复制插件 DLL
    const pluginDir = path.join(serverDir, 'ServerPlugins')
    const pluginDst = path.join(pluginDir, 'TsWeb.dll')
    try {
      await fs.access(pluginDst)
      // 文件已存在，跳过
    } catch {
      // 文件不存在，尝试复制
      const pluginSrc = path.join(__dirname, '../res/TsWeb.dll')
      try {
        await fs.access(pluginSrc)
        await fs.mkdir(pluginDir, { recursive: true })
        await fs.copyFile(pluginSrc, pluginDst)
        console.log(`[Setup] 已复制插件: ${pluginSrc} -> ${pluginDst}`)
      } catch (copyErr) {
        console.warn(`[Setup] 插件复制失败: ${copyErr.message}`)
      }
    }

    res.json({ success: true, restPort, tokenKey, configPath, reused })
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

    // 检查是否已有 TSWeb 的 superadmin token，有则复用
    let tokenKey
    let reused = false
    if (settings.ApplicationRestTokens) {
      for (const [k, v] of Object.entries(settings.ApplicationRestTokens)) {
        if (v.Username === 'TSWeb' && v.UserGroupName === 'superadmin') {
          tokenKey = k
          reused = true
          break
        }
      }
    }
    if (!tokenKey) {
      tokenKey = generateRandomToken(35)
    }

    settings.RestApiEnabled = true
    if (!settings.ApplicationRestTokens) {
      settings.ApplicationRestTokens = {}
    }
    if (!reused) {
      settings.ApplicationRestTokens[tokenKey] = {
        Username: 'TSWeb',
        UserGroupName: 'superadmin'
      }
    }
    const modifiedRaw = JSON.stringify(config, null, 2)
    res.json({ success: true, restPort, tokenKey, modifiedRaw, reused })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

// 插件初始化完成
router.post('/plugin-init', async (req, res) => {
  const token = req.body.token || req.query.token
  if (!token || !validateSetupToken(token)) {
    return res.status(403).json({ error: '无效的 Setup Token' })
  }
  const { mode, bossLimitMode, bossLimitMinPlayers } = req.body
  if (!mode || !['default', 'auto', 'block'].includes(mode)) {
    return res.status(400).json({ error: 'mode 必须为 default/auto/block' })
  }
  try {
    // 设置模式 + BossLimit
    let path = `/data/config/tsweb/set?mode=${encodeURIComponent(mode)}`
    if (bossLimitMode && ['disabled', 'playerlimit', 'killrequired'].includes(bossLimitMode)) {
      path += `&bossLimitMode=${encodeURIComponent(bossLimitMode)}`
    }
    if (bossLimitMinPlayers !== undefined && !isNaN(bossLimitMinPlayers)) {
      path += `&bossLimitMinPlayers=${encodeURIComponent(bossLimitMinPlayers)}`
    }
    const result = await tshockFetch(path)
    res.json(result || { status: '200', message: '配置已保存' })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

// 读取 SSC 配置
router.get('/ssc-config', async (req, res) => {
  const token = req.query.token
  if (!token || !validateSetupToken(token)) {
    return res.status(403).json({ error: '无效的 Setup Token' })
  }
  try {
    const result = await tshockService.fileRead('tshock/sscconfig.json')
    res.json(result)
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

// 保存 SSC 配置
router.post('/ssc-config', async (req, res) => {
  const token = req.body.token || req.query.token
  if (!token || !validateSetupToken(token)) {
    return res.status(403).json({ error: '无效的 Setup Token' })
  }
  const { content } = req.body
  if (!content) {
    return res.status(400).json({ error: '缺少 content' })
  }
  try {
    const result = await tshockService.fileWrite('tshock/sscconfig.json', content)
    res.json(result)
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

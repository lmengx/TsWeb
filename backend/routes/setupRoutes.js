import { Router } from 'express'
import jwt from 'jsonwebtoken'
import { getConfig, getServers, addServer } from '../config.js'
import { validateSetupToken, generateSetupToken } from '../setupToken.js'
import tshockService from '../services/tshockService.js'
import { exec } from 'child_process'
import { promisify } from 'util'
import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import { fileURLToPath } from 'url'
import audit from '../services/auditLogger.js'
import { createAccount, hasAnyAccount, ADMIN_USERNAME } from '../services/accountService.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

const execAsync = promisify(exec)

// Windows 下子进程输出编码取决于系统代码页（GBK/UTF-8 因机器而异，甚至会话间不同），
// 不能猜编码：统一强制 UTF-8 输出（cmd 工具前置 chcp 65001；powershell 前置 [Console]::OutputEncoding），
// Node 端固定按 utf8 解码，彻底避免中文路径乱码。

// 直接向 TShock REST API 发请求（用于插件初始化阶段；此时可能尚未配置服务器 → 用请求级 x-server-id）
const tshockFetch = async (pathname) => {
  const servers = await getServers()
  const current = servers.find(s => s.enabled) || servers[0]
  if (!current) return { error: '尚未配置任何服务器' }
  const host = current.host || 'localhost'
  const baseUrl = (host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`) + ':' + (current.port || 7878)
  const apiKey = current.apiKey || ''
  const sep = pathname.includes('?') ? '&' : '?'
  const url = `${baseUrl}${pathname}${sep}token=${encodeURIComponent(apiKey)}`
  const res = await fetch(url)
  return res.json()
}

const router = Router()

// ═══════════════════════════════════════════════════════════
// 兼容鉴权：setup token（首次初始化）或 admin JWT（登录后管理页）二选一
// 用于初始化/自动配置类接口，使登录后的服务器管理页也能调用
// ═══════════════════════════════════════════════════════════
async function setupOrAdmin(req, res, next) {
  const token = req.body?.token || req.query?.token
  if (token && validateSetupToken(token)) {
    return next()
  }
  // 尝试 admin JWT
  const authHeader = req.headers.authorization
  if (authHeader?.startsWith('Bearer ')) {
    try {
      const cfg = await getConfig()
      const secret = cfg?.security?.jwtSecret
      if (!secret) throw new Error('no secret')
      const decoded = jwt.verify(authHeader.slice(7), secret)
      if (decoded.usergroup === 'admin') {
        req.user = decoded
        return next()
      }
    } catch { /* 无效 token，继续走 setup token 判定 */ }
  }
  res.status(403).json({ error: '无效的 Setup Token 或权限不足' })
}

router.get('/check', async (req, res) => {
  const token = req.query.token
  if (!token || !validateSetupToken(token)) {
    return res.json({ configured: false, needToken: true })
  }
  const { isConfigFileExists } = await import('../config.js')
  const exists = await isConfigFileExists()
  const servers = await getServers()
  const hasAccounts = await hasAnyAccount()
  res.json({
    configured: exists,
    needToken: false,
    setupToken: token,
    hasAccounts,
    servers: servers.map(s => ({
      id: s.id, name: s.name, host: s.host, port: s.port,
      enabled: s.enabled, hasApiKey: !!s.apiKey
    }))
  })
})

router.post('/init', setupOrAdmin, async (req, res) => {
  try {
    const { name, host, port, apiKey, note } = req.body
    if (!host || !port || !apiKey) {
      return res.status(400).json({ error: 'host、port、apiKey 均为必填' })
    }
    const result = await tshockService.testConnectionWith(host, port, apiKey)
    if (!result.success) {
      return res.json({ success: false, error: result.error })
    }
    const server = await addServer({ name, host, port, apiKey, note })
    const { activateServer } = await import('../services/serverActivation.js')
    activateServer(server)
    audit.record('setup.init', {
      serverName: server.name,
      actor: req.user?.username || 'setup'
    })
    res.json({ success: true, server: { id: server.id, name: server.name } })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

// 首次初始化：创建全局唯一 admin 账户
router.post('/create-admin', async (req, res) => {
  try {
    const token = req.body.token || req.query.token
    if (!token || !validateSetupToken(token)) {
      return res.status(403).json({ error: '无效的 Setup Token' })
    }
    const { username, password } = req.body
    if (!username || !password) {
      return res.status(400).json({ error: 'username、password 均为必填' })
    }
    if ((await hasAnyAccount())) {
      return res.status(400).json({ error: '已存在账户，无法重复初始化' })
    }
    const account = await createAccount(username, password, 'admin')
    audit.record('setup.create_admin', {
      username: account.username,
      ip: req.ip
    })

    // 创建成功后签发 JWT，前端可直接自动登录进入后台（用户选：设置密码→引导跳服务器管理页）
    let jwtToken = null
    try {
      const { getConfig } = await import('../config.js')
      const cfg = await getConfig()
      const jwt = (await import('jsonwebtoken')).default
      const secret = cfg?.security?.jwtSecret
      const expire = cfg?.security?.tokenExpire || '24h'
      if (secret) {
        jwtToken = jwt.sign(
          { username: account.username, usergroup: 'admin' },
          secret,
          { expiresIn: expire }
        )
      }
    } catch (err) {
      console.warn('[Setup] JWT 签发失败（前端将跳登录页）:', err.message)
    }

    res.json({ success: true, username: account.username, token: jwtToken })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
})

router.get('/probe', setupOrAdmin, async (req, res) => {
  try {
    const port = req.query.port || '7777'
    // findstr 无匹配时退出码非 0，需 catch 视为无结果
    let netstatOut = ''
    try {
      const { stdout } = await execAsync(`chcp 65001>nul & netstat -ano | findstr :${port} `)
      netstatOut = stdout
    } catch {
      netstatOut = ''
    }
    const lines = netstatOut.trim().split('\n').filter(l => l.includes('LISTENING'))
    if (lines.length === 0) {
      return res.json({ found: false, port: parseInt(port), processes: [] })
    }
    const pids = [...new Set(lines.map(l => l.trim().split(/\s+/).pop()))]
    const processes = []
    for (const pid of pids) {
      let path = '未知'
      // 首选 CIM（wmic 已废弃，Win11 24H2+ 已移除）；强制 UTF-8 输出，解决中文路径乱码
      try {
        const { stdout } = await execAsync(
          `powershell -NoProfile -Command "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; (Get-CimInstance Win32_Process -Filter 'ProcessId=${pid}').ExecutablePath"`
        )
        const p = (stdout || '').trim()
        if (p) path = p
      } catch {}
      if (path === '未知') {
        try {
          const { stdout } = await execAsync(`powershell -NoProfile -Command "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; (Get-Process -Id ${pid}).Path"`)
          const p = (stdout || '').trim().split('\r\n')[0].trim()
          if (p) path = p
        } catch {}
      }
      if (path === '未知') {
        try {
          const { stdout } = await execAsync(`chcp 65001>nul & tasklist /FI "PID eq ${pid}" /FO CSV /NH`)
          const parts = (stdout || '').trim().split(',')
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

router.post('/auto-read', setupOrAdmin, async (req, res) => {
  try {
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
      const pluginSrc = path.join(__dirname, '../data/resource/TsWeb.dll')
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

router.post('/auto-verify', setupOrAdmin, async (req, res) => {
  try {
    const { host, port, apiKey } = req.body
    if (!host || !port || !apiKey) {
      return res.status(400).json({ error: 'host、port、apiKey 均为必填' })
    }
    const connected = await tshockService.testConnectionWith(host, port, apiKey)
    if (!connected.success) {
      return res.json({ success: false, error: connected.error })
    }
    const server = await addServer({ host, port, apiKey })
    const { activateServer } = await import('../services/serverActivation.js')
    activateServer(server)
    audit.record('server.add', {
      name: server.name,
      host: server.host,
      port: server.port,
      actor: req.user?.username || 'setup'
    })
    res.json({ success: true, serverId: server.id })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

router.post('/auto-remote', setupOrAdmin, async (req, res) => {
  try {
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
router.post('/plugin-init', setupOrAdmin, async (req, res) => {
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
router.get('/ssc-config', setupOrAdmin, async (req, res) => {
  try {
    const result = await tshockService.fileRead('tshock/sscconfig.json')
    res.json(result)
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

// 保存 SSC 配置
router.post('/ssc-config', setupOrAdmin, async (req, res) => {
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
  // 使用密码学安全随机数生成器（CSPRNG）生成 REST API token
  return crypto.randomBytes(length).toString('base64url').slice(0, length)
}

// 在本地浏览器打开管理页面
router.get('/open', async (req, res) => {
  try {
    const config = await getConfig()
    const port = config.server?.port || 3000
    const host = config.server?.host || '0.0.0.0'
    const token = generateSetupToken()
    const url = `http://localhost:${port}/backend?token=${token}`
    exec(`start ${url}`, (err) => {
      if (err) {
        console.log(`[Setup] 打开浏览器失败: ${err.message}`)
        console.log(`[Setup] 请手动访问: ${url}`)
      }
    })
    res.json({ success: true, url })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
})

export default router

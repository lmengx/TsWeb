import express from 'express'
import cors from 'cors'
import path from 'path'
import { fileURLToPath } from 'url'
import { loadConfig, isConfigFileExists } from './config.js'
import { generateSetupToken } from './setupToken.js'
import { exec } from 'child_process'
import authRoutes from './routes/authRoutes.js'
import tshockRoutes from './routes/tshockRoutes.js'
import configRoutes from './routes/configRoutes.js'
import setupRoutes from './routes/setupRoutes.js'
import antiCheatRoutes from './routes/antiCheatRoutes.js'
import resourceRoutes from './routes/resourceRoutes.js'
import onlineRoutes from './routes/onlineRoutes.js'
import unverifiedRoutes from './routes/unverifiedRoutes.js'
import fileRoutes from './routes/fileRoutes.js'
import presetRoutes from './routes/presetRoutes.js'
import userRoutes from './routes/userRoutes.js'
import { loadRules as loadFileAccessRules } from './services/fileAccessService.js'
import tshockService from './services/tshockService.js'
import readline from 'readline'
import iconv from 'iconv-lite'

// =====================================================
// 全局错误保护 - 防止未捕获异常/拒绝导致进程退出
// =====================================================
process.on('uncaughtException', (err) => {
  console.error('[FATAL] 未捕获异常:', err)
})
process.on('unhandledRejection', (reason) => {
  console.error('[FATAL] 未处理的Promise拒绝:', reason)
})

// 设置控制台标题
process.title = 'TSWeb--made by lmx12330'

// 实际监听端口，由 listenWithFallback 设置，供控制台命令使用
let _serverPort = null

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

const app = express()

app.use(cors())
app.use(express.json())

app.use((req, res, next) => {
  const fullUrl = `${req.protocol}://${req.get('host')}${req.originalUrl}`
  console.log(`[${new Date().toISOString()}] ${req.method} ${fullUrl}`)
  if (req.body && Object.keys(req.body).length > 0) {
    console.log('Request body:', JSON.stringify(req.body))
  }
  next()
})

const frontendDistPath = path.join(__dirname, '/dist')
app.use(express.static(frontendDistPath))

app.use('/api/auth', authRoutes)
app.use('/api/tshock', tshockRoutes)
app.use('/api/config', configRoutes)
app.use('/api/anticheat', antiCheatRoutes)
app.use('/api/resources', resourceRoutes)
app.use('/api/online', onlineRoutes)
app.use('/api/unverified', unverifiedRoutes)
app.use('/api/files', fileRoutes)
app.use('/api/presets', presetRoutes)
app.use('/api/user', userRoutes)
app.use('/api/setup', setupRoutes)

app.get('/api/health', (req, res) => {
  res.json({ status: 'ok', timestamp: Date.now() })
})

app.get('/api/status', (req, res) => {
  const isConnected = tshockService.getConnectionStatus()
  res.json({ 
    status: isConnected ? 'connected' : 'disconnected',
    connected: isConnected,
    message: isConnected ? 'TShock服务器已连接' : '服务器连接失败，请联系管理员配置后重启服务'
  })
})

// IP 地理位置查询代理（绕过前端 CORS 限制，处理 GBK 编码）
app.get('/api/ip-lookup', async (req, res) => {
  const { ip } = req.query
  if (!ip) return res.status(400).json({ error: 'Missing ip parameter' })
  try {
    const response = await fetch(`https://whois.pconline.com.cn/ipJson.jsp?ip=${encodeURIComponent(ip)}&json=true`)
    // 读取原始 buffer，pconline 返回 GBK 编码
    const buffer = Buffer.from(await response.arrayBuffer())
    const text = iconv.decode(buffer, 'gbk')
    try {
      const data = JSON.parse(text)
      res.json(data)
    } catch {
      res.json({ ip, pro: '', city: '', addr: '', err: 'parse error' })
    }
  } catch (err) {
    res.json({ ip, pro: '', city: '', addr: '', err: err.message })
  }
})

app.get(/^\/.*$/, (req, res) => {
  res.sendFile(path.join(frontendDistPath, 'index.html'))
})

// =====================================================
// 加载文件访问白名单（提前加载，避免在 listen 回调中做异步操作）
// =====================================================
async function loadFileRules() {
  try {
    await loadFileAccessRules()
    console.log('[FileAccess] 文件访问白名单已加载')
  } catch (err) {
    console.warn('[FileAccess] 白名单加载失败:', err.message)
  }
}

// =====================================================
// 带端口容错的 listen 辅助函数
// 端口被占用时输出颜色警告并要求用户确认
// =====================================================
function listenWithFallback(app, port, host, onListening) {
  let currentPort = port

  function tryListen() {
    const server = app.listen(currentPort, host)

    server.on('listening', () => {
      console.log(`  Web服务器已在端口 ${currentPort} 上运行`)
      console.log(`  可访问地址: http://${host === '0.0.0.0' ? 'localhost' : host}:${currentPort}`)
      if (currentPort !== port) {
        console.log(`  [注意] 原先端口 ${port} 被占用，已切换到端口 ${currentPort}`)
      }
      if (onListening) onListening(currentPort)
    })

    server.on('error', (err) => {
      if (err.code === 'EADDRINUSE') {
        console.log('')
        console.log(`  \x1b[33m⚠ 端口 ${currentPort} 已被占用\x1b[0m`)
        const nextPort = currentPort + 1
        const rl = readline.createInterface({
          input: process.stdin,
          output: process.stdout
        })
        rl.question(`  是否使用端口 ${nextPort} 代替？(y/N) `, (answer) => {
          rl.close()
          if (answer.trim().toLowerCase() === 'y' || answer.trim().toLowerCase() === 'yes') {
            currentPort = nextPort
            setImmediate(tryListen)
          } else {
            console.log('')
            console.log('  \x1b[31m服务器未启动，控制台仍可用\x1b[0m')
            console.log('  请关闭占用端口的程序后重启服务')
            console.log('')
          }
        })
      } else {
        console.error('  [ERROR] 服务器启动失败:', err.message)
      }
    })
  }

  tryListen()
}

// =====================================================
// 启动服务器
// =====================================================
async function startServer() {
  const token = generateSetupToken()
  const hasConfig = await isConfigFileExists()

  if (!hasConfig) {
    const port = 3000
    console.log('')
    console.log('='.repeat(58))
    console.log('  TsWeb 首次启动 - 需要初始化配置')
    console.log('='.repeat(58))
    console.log('')
    console.log('  Setup Token: ' + token)
    console.log('')

    // 首次启动不阻塞，先加载白名单再 listen
    loadFileRules()

    listenWithFallback(app, port, '0.0.0.0', (actualPort) => {
      _serverPort = actualPort
      console.log('  请访问:')
      console.log('  http://localhost:' + actualPort + '/setup/intro?token=' + token)
      console.log('')
      const url = 'http://localhost:' + actualPort + '/setup/intro?token=' + token
      exec('start ' + url, (err) => {
        if (err) {
          console.log('  请手动访问: ' + url)
        }
      })
    })
    return
  }

  const config = await loadConfig()
  const port = config.server.port || 3000
  const host = config.server.host || '0.0.0.0'

  const hasTshockConfig = config.tshock?.host && config.tshock?.port && config.tshock?.apiKey

  if (hasTshockConfig) {
    console.log('')
    console.log('='.repeat(58))
    console.log('  Setup Token: ' + token)
    console.log('='.repeat(58))
    console.log('  如需修改 TShock 连接配置，请访问:')
    console.log('  http://localhost:' + port + '/setup?token=' + token)
    console.log('')
    console.log('Testing TShock connection...')
    tshockService.testConnection().catch(() => {})
  } else {
    console.log('TShock not configured. Status set to disconnected.')
    console.log('')
    console.log('  Setup Token: ' + token)
    console.log('  请访问: http://localhost:' + port + '/setup?token=' + token)
    console.log('')
  }

  // 提前加载白名单（不阻塞 listen）
  loadFileRules()

  // 启动 HTTP 服务 — 使用带端口容错的 listen
  listenWithFallback(app, port, host, async (actualPort) => {
    const displayHost = host === '0.0.0.0' ? 'localhost' : host
    _serverPort = actualPort
    console.log(`Server running on http://${displayHost}:${actualPort}`)
    if (config.tshock?.host && config.tshock?.port) {
      console.log(`TShock API: ${config.tshock.host}:${config.tshock.port}`)
    }

    // ═══ 启动后注册 webhook 到插件（需开启且 TShock 已配置） ═══
    const whCfg = config.logWebhook || {}
    if (whCfg.enabled && config.tshock?.host && config.tshock?.port && config.tshock?.apiKey) {
      const webhookUrl = whCfg.publicUrl || `http://127.0.0.1:${actualPort}/api/online/log-webhook`
      const { updatePluginWebhook } = await import('./services/webhookRegistration.js')
      const result = await updatePluginWebhook(webhookUrl)
      if (result.success) {
        console.log(`  Webhook 已注册: ${webhookUrl}`)
      } else {
        console.warn(`  Webhook 注册失败: ${result.message}`)
      }
    } else if (!whCfg.enabled) {
      console.log('  日志 Webhook 未启用，跳过注册')
    }
  })
}

startServer().catch(err => {
  console.error('Failed to start server:', err)
})

// ── 控制台命令 ──
function startConsole() {
  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
    prompt: '> '
  })

  console.log('')
  console.log('可用命令: setup - 打开管理页面, exit - 退出')
  rl.prompt()

  rl.on('line', async (line) => {
    const cmd = line.trim().toLowerCase()
    if (cmd === 'setup' || cmd === 'open' || cmd === 's') {
      try {
        const port = _serverPort || 3000
        const token = generateSetupToken()
        const url = `http://localhost:${port}/backend?token=${token}`
        const { exec } = await import('child_process')
        exec(`start ${url}`, (err) => {
          if (err) {
            console.log('请手动访问: ' + url)
          }
        })
        console.log('后台管理页面已打开: ' + url)
      } catch (err) {
        console.log('操作失败:', err.message)
      }
    } else if (cmd === 'exit' || cmd === 'quit' || cmd === 'q') {
      console.log('正在退出...')
      process.exit(0)
    } else if (cmd) {
      console.log('未知命令: ' + cmd + '  (可用: setup, exit)')
    }
    rl.prompt()
  })

  rl.on('close', () => {
    console.log('退出控制台')
    process.exit(0)
  })
}

setTimeout(startConsole, 1000)
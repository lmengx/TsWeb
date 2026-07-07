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
import { loadRules as loadFileAccessRules } from './services/fileAccessService.js'
import tshockService from './services/tshockService.js'

// =====================================================
// 全局错误保护 - 防止未捕获异常/拒绝导致进程退出
// =====================================================
process.on('uncaughtException', (err) => {
  console.error('[FATAL] 未捕获异常:', err)
})
process.on('unhandledRejection', (reason) => {
  console.error('[FATAL] 未处理的Promise拒绝:', reason)
})

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
    console.log('  请访问:')
    console.log('  http://localhost:' + port + '/setup/intro?token=' + token)
    console.log('')

    // 首次启动不阻塞，先加载白名单再 listen
    loadFileRules()

    app.listen(port, '0.0.0.0', () => {
      console.log('  Web服务器已在端口 ' + port + ' 上运行')
      console.log('  可访问地址: http://0.0.0.0:' + port + '/setup/intro?token=' + token)
      console.log('')
      const url = 'http://localhost:' + port + '/setup/intro?token=' + token
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

  // 启动 HTTP 服务 — 使用普通同步回调，避免 async 回调的 Promise 陷阱
  app.listen(port, host, () => {
    console.log(`Server running on http://${host}:${port}`)
    if (config.tshock?.host && config.tshock?.port) {
      console.log(`TShock API: ${config.tshock.host}:${config.tshock.port}`)
    }
  })
}

startServer().catch(err => {
  console.error('Failed to start server:', err)
})

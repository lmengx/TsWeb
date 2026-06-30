import express from 'express'
import cors from 'cors'
import path from 'path'
import { fileURLToPath } from 'url'
import { loadConfig } from './config.js'
import authRoutes from './routes/authRoutes.js'
import tshockRoutes from './routes/tshockRoutes.js'
import configRoutes from './routes/configRoutes.js'
import antiCheatRoutes from './routes/antiCheatRoutes.js'
import resourceRoutes from './routes/resourceRoutes.js'
import onlineRoutes from './routes/onlineRoutes.js'
import unverifiedRoutes from './routes/unverifiedRoutes.js'
import tshockService from './services/tshockService.js'

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

async function startServer() {
  const config = await loadConfig()
  const port = config.server.port || 3000
  
  const hasTshockConfig = config.tshock?.host && config.tshock?.port && config.tshock?.apiKey
  
  if (hasTshockConfig) {
    console.log('Testing TShock connection...')
    const connectionResult = await tshockService.testConnection()
    console.log(`TShock connection test: ${connectionResult.message}`)
    
    let retryInterval = null
    
    const startRetry = () => {
      if (retryInterval) return
      retryInterval = setInterval(async () => {
        console.log('[TShock] Retrying connection...')
        const result = await tshockService.testConnection()
        console.log(`[TShock] Retry result: ${result.message}`)
        
        if (result.success) {
          clearInterval(retryInterval)
          retryInterval = null
          console.log('[TShock] Connection restored successfully!')
        }
      }, 5000)
    }
    
    const checkDisconnect = () => {
      setInterval(async () => {
        const isConnected = tshockService.getConnectionStatus()
        if (!isConnected && !retryInterval) {
          console.log('[TShock] Connection lost, starting retry...')
          startRetry()
        }
      }, 5000)
    }
    
    if (!tshockService.getConnectionStatus()) {
      startRetry()
    }
    
    checkDisconnect()
  } else {
    console.log('TShock not configured. Status set to disconnected.')
  }
  
  app.listen(port, () => {
    console.log(`Server running on http://localhost:${port}`)
    if (config.tshock?.host && config.tshock?.port) {
      console.log(`TShock API: ${config.tshock.host}:${config.tshock.port}`)
    }
  })
}

startServer().catch(err => {
  console.error('Failed to start server:', err)
})

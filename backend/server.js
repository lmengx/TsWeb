import express from 'express'
import cors from 'cors'
import path from 'path'
import { fileURLToPath } from 'url'
import { loadConfig } from './config.js'
import { initDB } from './services/databaseService.js'
import authRoutes from './routes/authRoutes.js'
import tshockRoutes from './routes/tshockRoutes.js'
import configRoutes from './routes/configRoutes.js'

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

app.get('/api/health', (req, res) => {
  res.json({ status: 'ok', timestamp: Date.now() })
})

app.get(/^\/.*$/, (req, res) => {
  res.sendFile(path.join(frontendDistPath, 'index.html'))
})

async function startServer() {
  const config = await loadConfig()
  const port = config.server.port || 3000
  
  await initDB()
  
  app.listen(port, () => {
    console.log(`Server running on http://localhost:${port}`)
    console.log(`TShock API: ${config.tshock.host}:${config.tshock.port}`)
  })
}

startServer().catch(err => {
  console.error('Failed to start server:', err)
})

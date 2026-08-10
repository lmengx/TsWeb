import express from 'express'
import cors from 'cors'
import path from 'path'
import { fileURLToPath } from 'url'
import { loadConfig, getServers, isConfigFileExists, saveNewConfig } from './config.js'
import { generateSetupToken } from './setupToken.js'
import { exec } from 'child_process'
import authRoutes from './routes/authRoutes.js'
import tshockRoutes from './routes/tshockRoutes.js'
import configRoutes from './routes/configRoutes.js'
import setupRoutes from './routes/setupRoutes.js'
import antiCheatRoutes from './routes/antiCheatRoutes.js'
import onlineRoutes from './routes/onlineRoutes.js'
import unverifiedRoutes from './routes/unverifiedRoutes.js'
import fileRoutes from './routes/fileRoutes.js'
import presetRoutes from './routes/presetRoutes.js'
import userRoutes from './routes/userRoutes.js'
import serverRoutes from './routes/serverRoutes.js'
import auditRoutes from './routes/auditRoutes.js'
import hookRoutes from './routes/hookRoutes.js'
import tshockService, { registerServer, runWithServer, getServicesStatus } from './services/tshockService.js'
import { connectAll as connectAllSse } from './services/sseConnection.js'
import audit from './services/auditLogger.js'
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

// ═══════════════════════════════════════════════════════════
// /hook/* 端点（插件 → 后端 webhook 回传）：必须在全局 express.json 之前
// 1) 捕获原始 body（HMAC 签名需要对原始字节做 sha256）
// 2) 独立 json 解析（大 body 限制 10mb）
// 3) 跳过请求体日志（webhook 高频推送，避免刷屏）
// ═══════════════════════════════════════════════════════════
app.use('/hook', (req, res, next) => {
  const chunks = []
  req.on('data', (c) => chunks.push(c))
  req.on('end', () => {
    req.rawBody = Buffer.concat(chunks).toString('utf8')
    next()
  })
  req.on('error', (e) => {
    console.error('[Hook] rawBody 读取失败:', e.message)
    next(e)
  })
})
app.use('/hook', express.json({ limit: '10mb' }), hookRoutes)

// 全局 JSON 解析（其余 /api 端点）
// 文件分片上传需要承载 ~5MB 的 base64 片段，全局上限提到 10mb（与 /hook 一致）
app.use(express.json({ limit: '10mb' }))

app.use((req, res, next) => {
  // /hook 路径已在上面独立处理且不打印 body
  if (req.path.startsWith('/hook')) return next()
  const fullUrl = `${req.protocol}://${req.get('host')}${req.originalUrl}`
  console.log(`[${new Date().toISOString()}] ${req.method} ${fullUrl}`)
  // 文件上传分片 body 含大段 base64，跳过打印避免刷屏
  if (req.body && Object.keys(req.body).length > 0 && !req.originalUrl.includes('/api/files/upload')) {
    console.log('Request body:', JSON.stringify(req.body))
  }
  next()
})

// ═══════════════════════════════════════════════════════════
// x-server-id 中间件：请求级服务器上下文（无全局 currentServerId）
// 前端每个请求带 x-server-id header → AsyncLocalStorage 绑定到该请求
// 后续 tshockService 默认导出 Proxy 自动转发到目标服务器实例
// 注意：/hook/*（webhook 回传）与 /api/auth、/api/servers、/api/audit 不依赖 x-server-id
// ═══════════════════════════════════════════════════════════
app.use('/api', (req, res, next) => {
  const serverId = req.headers['x-server-id']
  if (serverId) {
    return runWithServer(serverId, next)
  }
  next()
})

const frontendDistPath = path.join(__dirname, '/dist')
app.use(express.static(frontendDistPath))

app.use('/api/auth', authRoutes)
app.use('/api/tshock', tshockRoutes)
app.use('/api/config', configRoutes)
app.use('/api/anticheat', antiCheatRoutes)
app.use('/api/online', onlineRoutes)
app.use('/api/unverified', unverifiedRoutes)
app.use('/api/files', fileRoutes)
app.use('/api/presets', presetRoutes)
app.use('/api/user', userRoutes)
app.use('/api/servers', serverRoutes)
app.use('/api/audit', auditRoutes)
app.use('/api/setup', setupRoutes)

app.get('/api/health', (req, res) => {
  res.json({ status: 'ok', timestamp: Date.now() })
})

app.get('/api/status', async (req, res) => {
  try {
    const servers = await getServers()
    // getServicesStatus 是模块级命名导出，不能通过默认导出的 Proxy 访问（实例无此方法）
    const servicesStatus = getServicesStatus() || []
    const statusList = servers.map(s => {
      const svc = servicesStatus.find(x => x.id === s.id)
      return {
        id: s.id,
        name: s.name,
        host: s.host,
        port: s.port,
        enabled: s.enabled,
        connected: svc?.connected || false
      }
    })
    res.json({
      servers: statusList,
      hasServers: statusList.length > 0
    })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
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
  const config = hasConfig ? await loadConfig() : null

  // 注册全部已配置服务器到实例注册表
  const servers = await getServers()
  for (const s of servers) {
    registerServer(s)
  }

  if (!hasConfig) {
    // 首次启动：立即创建基础配置（含随机 jwtSecret），否则后续 create-admin 无法签发 JWT
    await saveNewConfig()
    const port = 3000
    console.log('')
    console.log('='.repeat(58))
    console.log('  TsWeb 首次启动 - 需要初始化配置')
    console.log('='.repeat(58))
    console.log('')
    console.log('  Setup Token: ' + token)
    console.log('  请打开浏览器访问下方地址，设置管理员密码：')
    console.log('')

    // 首次启动不阻塞，先 listen
    listenWithFallback(app, port, '0.0.0.0', (actualPort) => {
      _serverPort = actualPort
      console.log('  请访问:')
      console.log('  http://localhost:' + actualPort + '/backend/init?token=' + token)
      console.log('')
      const url = 'http://localhost:' + actualPort + '/backend/init?token=' + token
      exec('start ' + url, (err) => {
        if (err) {
          console.log('  请手动访问: ' + url)
        }
      })
    })
    return
  }

  const port = config.server.port || 3000
  const host = config.server.host || '0.0.0.0'

  console.log('')
  console.log('='.repeat(58))
  console.log('  Setup Token: ' + token)
  console.log('='.repeat(58))
  console.log('  如需修改服务器配置，请访问:')
  console.log('  http://localhost:' + port + '/backend?token=' + token)
  console.log('')

  // 提前 listen（不阻塞）
  listenWithFallback(app, port, host, async (actualPort) => {
    const displayHost = host === '0.0.0.0' ? 'localhost' : host
    _serverPort = actualPort
    console.log(`Server running on http://${displayHost}:${actualPort}`)

    // 审计：后端启动
    audit.record('system.start', {
      version: '1.0.0',
      nodeVersion: process.version
    })

    // ═══ 启动后建立到各服务器插件的 SSE 常驻长连接（日志/文件推送主通道） ═══
    if (servers.length > 0) {
      const result = connectAllSse()
      result.then(r => {
        console.log(`  插件 SSE 长连接已建立: ${r.connected || 0} 台服务器`)
      }).catch(e => {
        console.warn(`  插件 SSE 长连接建立失败: ${e.message}`)
      })
    } else {
      console.log('  暂无已配置服务器，跳过 SSE 长连接')
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
  console.log('可用命令: backend - 打开管理页面, token - 显示 Token, reset-admin - 重置唯一管理员密码(显示一次), exit - 退出')
  rl.prompt()

  rl.on('line', async (line) => {
    const cmd = line.trim().toLowerCase()
    if (cmd === 'backend' || cmd === 'setup' || cmd === 'open' || cmd === 's') {
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
    } else if (cmd === 'token' || cmd === 't') {
      const token = generateSetupToken()
      console.log('Token: ' + token)
    } else if (cmd === 'reset-admin' || cmd === 'reset') {
      // 重置唯一管理员密码（或指定账户）：生成随机强密码，控制台显示一次，强制改密
      try {
        const { resetPassword, hasAnyAccount } = await import('./services/accountService.js')
        if (!(await hasAnyAccount())) {
          console.log('账户库为空，无需重置')
        } else {
          const target = cmd === 'reset' ? (line.trim().split(/\s+/)[1] || 'admin') : 'admin'
          const result = await resetPassword(target)
          audit.record('account.password_reset', {
            username: result.username,
            actor: 'console',
            via: 'console'
          })
          console.log('')
          console.log('[admin] 密码已重置（仅显示一次，请立即保存）:')
          console.log('  用户名: ' + result.username)
          console.log('  新密码: ' + result.plainPassword)
          console.log('')
        }
      } catch (err) {
        console.log('重置失败: ' + err.message)
      }
    } else if (cmd === 'exit' || cmd === 'quit' || cmd === 'q') {
      audit.record('system.stop', { reason: 'console-exit' })
      console.log('正在退出...')
      process.exit(0)
    } else if (cmd) {
      console.log('未知命令: ' + cmd + '  (可用: backend, token, reset-admin, exit)')
    }
    rl.prompt()
  })

  rl.on('close', () => {
    console.log('退出控制台')
    process.exit(0)
  })
}

setTimeout(startConsole, 1000)

// 审计日志退出冲刷 + system.stop 记录
audit.registerShutdownHook()
process.on('SIGINT', () => { audit.record('system.stop', { reason: 'sigint' }) })
process.on('SIGTERM', () => { audit.record('system.stop', { reason: 'sigterm' }) })
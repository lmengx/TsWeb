import { Router } from 'express'
import { register, changePassword, bind, listServers, requireBotToken } from '../controllers/botController.js'

const router = Router()

// ═══════════════════════════════════════════════════════════
// QQ 机器人接口（机器人 → 后端，与服务器解耦，支持多服）
// 全部端点需 bot token（X-Bot-Token 或 ?token=）
// ═══════════════════════════════════════════════════════════
router.use(requireBotToken)

// 注册（随机密码 + 提示改密）：POST /api/bot/register  { qq, player }
router.post('/register', register)

// 改密（广播全服覆盖哈希）：POST /api/bot/change-password  { qq, password }
router.post('/change-password', changePassword)

// 绑定已有账号（广播 find-account + 冲突指定服）：POST /api/bot/bind  { qq, player, serverId? }
router.post('/bind', bind)

// 服务器列表（机器人「服务器列表」命令）：GET /api/bot/servers
router.get('/servers', listServers)

export default router

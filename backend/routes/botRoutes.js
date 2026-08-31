import { Router } from 'express'
import {
  register, changePassword, bind, listServers, requireBotToken,
  playerInfo, online, bossProgress, votes,
  qqList, qqUnbind, qqRebind, getBotSettings, setBotSettings, refreshPlaytime
} from '../controllers/botController.js'
import { verifyToken, requireAdmin } from '../middlewares/authMiddleware.js'

const router = Router()

// ═══════════════════════════════════════════════════════════
// QQ 机器人接口（机器人 → 后端，与服务器解耦，支持多服）
// 管理类接口（绑定列表/解绑/改绑/设置）：仅 admin（JWT）
// 机器人接口：bot token（X-Bot-Token 或 ?token=）
// ═══════════════════════════════════════════════════════════

// —— 管理接口（仅 admin，前端 QQ 配置页使用）——
// QQ 绑定列表（QQ/玩家名/多服时长）：GET /api/bot/qq-list
router.get('/qq-list', verifyToken, requireAdmin, qqList)

// 手动触发多服时长聚合（重新获取并计算）：POST /api/bot/playtime-refresh
router.post('/playtime-refresh', verifyToken, requireAdmin, refreshPlaytime)

// 解绑：POST /api/bot/qq-unbind  { username } 或 { qq }
router.post('/qq-unbind', verifyToken, requireAdmin, qqUnbind)

// 改绑 QQ：POST /api/bot/qq-rebind  { username, qq }
router.post('/qq-rebind', verifyToken, requireAdmin, qqRebind)

// 机器人设置读取：GET /api/bot/settings
router.get('/settings', verifyToken, requireAdmin, getBotSettings)

// 机器人设置保存：POST /api/bot/settings
router.post('/settings', verifyToken, requireAdmin, setBotSettings)

// —— 机器人接口（bot token）——
router.use(requireBotToken)

// 注册（随机密码 + 提示改密）：POST /api/bot/register  { qq, player }
router.post('/register', register)

// 改密（广播全服覆盖哈希）：POST /api/bot/change-password  { qq, password }
router.post('/change-password', changePassword)

// 绑定已有账号（广播 find-account + 冲突指定服）：POST /api/bot/bind  { qq, player, serverId? }
router.post('/bind', bind)

// 服务器列表（机器人「服务器列表」命令）：GET /api/bot/servers
router.get('/servers', listServers)

// 我的信息（机器人「我的信息」命令）：GET /api/bot/player-info?qq=
router.get('/player-info', playerInfo)

// 在线（机器人「在线」命令，可带 ?server=服名）：GET /api/bot/online
router.get('/online', online)

// 进度（机器人「进度」命令，可带 ?server=服名）：GET /api/bot/boss-progress
router.get('/boss-progress', bossProgress)

// 投票（机器人「投票」命令，可带 ?name=投票标题）：GET /api/bot/votes
router.get('/votes', votes)

export default router

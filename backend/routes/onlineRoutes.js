import { Router } from 'express'
import { getHourlyOnline, getRanking, getPlayerCalendar, getRankingStats, streamLogs, execCommand, logWebhookReceiver } from '../controllers/onlineController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/hourly', verifyToken, requireRole('admin'), getHourlyOnline)
router.get('/ranking', verifyToken, requireRole('admin'), getRanking)
router.get('/player', verifyToken, requireRole('admin'), getPlayerCalendar)
router.get('/ranking/stats', verifyToken, requireRole('admin'), getRankingStats)
// SSE 流端点 — EventSource 无法设置 Authorization 头，从 query 取 token
router.get('/log/stream', streamLogs)
router.post('/log/command', verifyToken, requireRole('admin'), execCommand)
// Webhook 接收端点 — 插件内部调用，不验证 JWT（由插件侧验证来源）
router.post('/log-webhook', logWebhookReceiver)

export default router

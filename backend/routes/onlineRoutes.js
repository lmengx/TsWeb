import { Router } from 'express'
import { getHourlyOnline, getPlayerCalendar, getRankingStats, streamLogs, execCommand } from '../controllers/onlineController.js'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/hourly', verifyToken, requireManager, getHourlyOnline)
router.get('/player', verifyToken, requireManager, getPlayerCalendar)
router.get('/ranking/stats', verifyToken, requireManager, getRankingStats)
// SSE 流端点 — 凭据走 Authorization 头（前端用 fetch + ReadableStream 消费，不再用 EventSource）
router.get('/log/stream', streamLogs)
router.post('/log/command', verifyToken, requireManager, execCommand)

export default router

import { Router } from 'express'
import { getHourlyOnline, getPlayerCalendar, getRankingStats, streamLogs, execCommand } from '../controllers/onlineController.js'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/hourly', verifyToken, requireManager, getHourlyOnline)
router.get('/player', verifyToken, requireManager, getPlayerCalendar)
router.get('/ranking/stats', verifyToken, requireManager, getRankingStats)
// SSE 流端点 — EventSource 无法设置 Authorization 头，从 query 取 token
router.get('/log/stream', streamLogs)
router.post('/log/command', verifyToken, requireManager, execCommand)

export default router

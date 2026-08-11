import { Router } from 'express'
import { getHourlyOnline, getRanking, getPlayerCalendar, getRankingStats, streamLogs, execCommand, pullFile, listReceivedFiles, downloadReceivedFile } from '../controllers/onlineController.js'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/hourly', verifyToken, requireManager, getHourlyOnline)
router.get('/ranking', verifyToken, requireManager, getRanking)
router.get('/player', verifyToken, requireManager, getPlayerCalendar)
router.get('/ranking/stats', verifyToken, requireManager, getRankingStats)
// SSE 流端点 — EventSource 无法设置 Authorization 头，从 query 取 token
router.get('/log/stream', streamLogs)
router.post('/log/command', verifyToken, requireManager, execCommand)
// 文件定向拉取 / 已接收文件管理
router.post('/file/pull', verifyToken, requireManager, pullFile)
router.get('/file/list', verifyToken, requireManager, listReceivedFiles)
router.get('/file/download', verifyToken, requireManager, downloadReceivedFile)

export default router

import { Router } from 'express'
import { verifyToken, requireAdmin } from '../middlewares/authMiddleware.js'
import { getLogs, getStats, getEvents } from '../controllers/auditController.js'

const router = Router()

// 审计日志仅 admin 可查看；只读检索，无任何删除/清理接口
router.use(verifyToken, requireAdmin)

router.get('/logs', getLogs)
router.get('/stats', getStats)
router.get('/events', getEvents)

export default router

import { Router } from 'express'
import { verifyToken, requireAdmin } from '../middlewares/authMiddleware.js'
import { getStatus, apply } from '../controllers/worldModifyController.js'

const router = Router()

// 世界修改属世界级危险操作，仅 admin
router.use(verifyToken, requireAdmin)

router.get('/status', getStatus)
router.post('/apply', apply)

export default router

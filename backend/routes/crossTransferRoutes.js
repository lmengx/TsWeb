import { Router } from 'express'
import { verifyToken, requireAdmin } from '../middlewares/authMiddleware.js'
import { getConfig, saveConfig, reveal, probe, apply } from '../controllers/crossTransferController.js'

const router = Router()

// 跨服传送配置管理仅 admin（涉及密钥下发）
router.use(verifyToken, requireAdmin)

router.get('/config', getConfig)
router.post('/config', saveConfig)
router.post('/reveal', reveal)
router.post('/probe', probe)
router.post('/apply', apply)

export default router

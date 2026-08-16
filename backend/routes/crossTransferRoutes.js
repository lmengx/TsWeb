import { Router } from 'express'
import { verifyToken, requireAdmin } from '../middlewares/authMiddleware.js'
import { getConfig, saveConfig, probe } from '../controllers/crossTransferController.js'

const router = Router()

// 跨服传送配置管理仅 admin（涉及密钥）
router.use(verifyToken, requireAdmin)

router.get('/config', getConfig)
router.post('/config', saveConfig)
router.post('/probe', probe)

export default router

import { Router } from 'express'
import { list, detail, register, forceLogin, kick, ban } from '../controllers/unverifiedController.js'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'

const router = Router()

// 未验证玩家管理：admin + subadmin
router.get('/list', verifyToken, requireManager, list)
router.get('/detail', verifyToken, requireManager, detail)
router.post('/register', verifyToken, requireManager, register)
router.post('/force-login', verifyToken, requireManager, forceLogin)
router.post('/kick', verifyToken, requireManager, kick)
router.post('/ban', verifyToken, requireManager, ban)

export default router

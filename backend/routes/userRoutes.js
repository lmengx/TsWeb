import { Router } from 'express'
import { getSelfInfo } from '../controllers/userController.js'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'

const router = Router()

// 当前登录用户（后端账号）基本信息：仅后端账号自身信息，独立于 TShock
// 加固：requireManager 拒绝 player token（player 查自身信息走 /api/auth/player/me）
router.get('/selfinfo', verifyToken, requireManager, getSelfInfo)

export default router

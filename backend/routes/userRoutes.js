import { Router } from 'express'
import { getSelfInfo } from '../controllers/userController.js'
import { verifyToken } from '../middlewares/authMiddleware.js'

const router = Router()

// 当前登录用户（后端账号）基本信息：仅后端账号自身信息，独立于 TShock
router.get('/selfinfo', verifyToken, getSelfInfo)

export default router

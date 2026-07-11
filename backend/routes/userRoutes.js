import { Router } from 'express'
import { getSelfInfo, getSelfInventory } from '../controllers/userController.js'
import { verifyToken } from '../middlewares/authMiddleware.js'

const router = Router()

// 当前用户个人信息（不含敏感字段）
router.get('/selfinfo', verifyToken, getSelfInfo)

// 当前用户背包（展平结构）
router.get('/selfinventory', verifyToken, getSelfInventory)

export default router

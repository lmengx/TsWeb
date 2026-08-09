import { Router } from 'express'
import { verifyToken, requireAdmin } from '../middlewares/authMiddleware.js'
import * as fileController from '../controllers/fileController.js'

const router = Router()

// 文件管理：仅 admin（用户明确 subadmin 禁用）
router.get('/access', verifyToken, requireAdmin, fileController.getAccessRules)
router.get('/read', verifyToken, requireAdmin, fileController.readFile)
router.post('/write', verifyToken, requireAdmin, fileController.writeFile)
router.get('/list', verifyToken, requireAdmin, fileController.listDir)

export default router

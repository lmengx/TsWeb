import { Router } from 'express'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'
import * as fileController from '../controllers/fileController.js'

const router = Router()

router.get('/access', verifyToken, requireRole('admin'), fileController.getAccessRules)
router.get('/read', verifyToken, requireRole('admin'), fileController.readFile)
router.post('/write', verifyToken, requireRole('admin'), fileController.writeFile)
router.get('/list', verifyToken, requireRole('admin'), fileController.listDir)

export default router

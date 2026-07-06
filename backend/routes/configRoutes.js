import { Router } from 'express'
import { getConfigFile, saveConfigFile, getTsWebConfig, setTsWebConfig } from '../controllers/configController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/file', verifyToken, requireRole('admin'), getConfigFile)
router.post('/file', verifyToken, requireRole('admin'), saveConfigFile)
router.get('/tsweb', verifyToken, requireRole('admin'), getTsWebConfig)
router.post('/tsweb', verifyToken, requireRole('admin'), setTsWebConfig)

export default router

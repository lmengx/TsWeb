import { Router } from 'express'
import { getConfigStatus, getTshockSettings, updateTshockSettings, getConfigFile, saveConfigFile } from '../controllers/configController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/status', getConfigStatus)
router.get('/tshock', verifyToken, getTshockSettings)
router.post('/tshock', verifyToken, requireRole('admin'), updateTshockSettings)
router.get('/file', verifyToken, requireRole('admin'), getConfigFile)
router.post('/file', verifyToken, requireRole('admin'), saveConfigFile)

export default router
import { Router } from 'express'
import { getConfigStatus, getTshockSettings, updateTshockSettings } from '../controllers/configController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/status', getConfigStatus)
router.get('/tshock', verifyToken, getTshockSettings)
router.post('/tshock', verifyToken, requireRole('admin'), updateTshockSettings)

export default router
import { Router } from 'express'
import { getConfigStatus, getTshockSettings, updateTshockSettings, getConfigFile, saveConfigFile, getTsWebConfig, setTsWebConfig, getAllowList, addAllowIP, clearAllowList } from '../controllers/configController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/status', getConfigStatus)
router.get('/tshock', verifyToken, getTshockSettings)
router.post('/tshock', verifyToken, requireRole('admin'), updateTshockSettings)
router.get('/file', verifyToken, requireRole('admin'), getConfigFile)
router.post('/file', verifyToken, requireRole('admin'), saveConfigFile)
router.get('/tsweb', verifyToken, requireRole('admin'), getTsWebConfig)
router.post('/tsweb', verifyToken, requireRole('admin'), setTsWebConfig)
router.get('/tsweb/allowlist', verifyToken, requireRole('admin'), getAllowList)
router.post('/tsweb/allow', verifyToken, requireRole('admin'), addAllowIP)
router.post('/tsweb/allowclear', verifyToken, requireRole('admin'), clearAllowList)

export default router

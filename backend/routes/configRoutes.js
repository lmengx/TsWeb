import { Router } from 'express'
import { getConfigFile, saveConfigFile, getTsWebConfig, setTsWebConfig, getLicenseCheck, postLicenseClose, getBossLimitStatus, getPromotionConfig, setPromotionConfig, getLogWebhookConfig, setLogWebhookConfig } from '../controllers/configController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/file', verifyToken, requireRole('admin'), getConfigFile)
router.post('/file', verifyToken, requireRole('admin'), saveConfigFile)
router.get('/tsweb', verifyToken, requireRole('admin'), getTsWebConfig)
router.post('/tsweb', verifyToken, requireRole('admin'), setTsWebConfig)
router.get('/bosslimit/status', verifyToken, requireRole('admin'), getBossLimitStatus)
router.get('/license-check', getLicenseCheck)
router.post('/license-close', verifyToken, requireRole('admin'), postLicenseClose)
router.get('/promotion', verifyToken, requireRole('admin'), getPromotionConfig)
router.post('/promotion', verifyToken, requireRole('admin'), setPromotionConfig)
router.get('/log-webhook', verifyToken, requireRole('admin'), getLogWebhookConfig)
router.post('/log-webhook', verifyToken, requireRole('admin'), setLogWebhookConfig)

export default router

import { Router } from 'express'
import { getConfigFile, saveConfigFile, getTsWebConfig, setTsWebConfig, getLicenseCheck, postLicenseClose, getBossLimitStatus, getPromotionConfig, setPromotionConfig, getLogWebhookConfig, setLogWebhookConfig } from '../controllers/configController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'
import { validateSetupToken } from '../setupToken.js'

// 允许通过 Setup Token（URL 上的 ?token=xxx）绕过 JWT 认证
const allowSetupToken = (req, res, next) => {
  const setupToken = req.query.token
  if (setupToken && validateSetupToken(setupToken)) {
    req.user = { username: 'setup', usergroup: 'superadmin' }
    return next()
  }
  verifyToken(req, res, next)
}

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
router.get('/log-webhook', allowSetupToken, requireRole('admin'), getLogWebhookConfig)
router.post('/log-webhook', allowSetupToken, requireRole('admin'), setLogWebhookConfig)

export default router

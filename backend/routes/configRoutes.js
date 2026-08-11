import { Router } from 'express'
import { getConfigFile, saveConfigFile, getTsWebConfig, setTsWebConfig, getBossConfig, setBossConfig, getBackupConfig, setBackupConfig, getLicenseCheck, postLicenseClose, getBossLimitStatus, getPromotionConfig, setPromotionConfig, getLogWebhookConfig, setLogWebhookConfig, getListenConfig, saveListenConfig } from '../controllers/configController.js'
import { verifyToken, requireRole, requireAdmin, requireManager } from '../middlewares/authMiddleware.js'
import { validateSetupToken } from '../setupToken.js'

// 允许通过 Setup Token（URL 上的 ?token=xxx）绕过 JWT 认证
const allowSetupToken = (req, res, next) => {
  const setupToken = req.query.token
  if (setupToken && validateSetupToken(setupToken)) {
    req.user = { username: 'setup', usergroup: 'admin' }
    return next()
  }
  verifyToken(req, res, next)
}

const router = Router()

// 后端监听设置：仅 admin
router.get('/listen', verifyToken, requireAdmin, getListenConfig)
router.post('/listen', verifyToken, requireAdmin, saveListenConfig)
// 后端配置文件：仅 admin
router.get('/file', verifyToken, requireAdmin, getConfigFile)
router.post('/file', verifyToken, requireAdmin, saveConfigFile)
// 插件级配置（单服务器）：admin + subadmin
router.get('/tsweb', verifyToken, requireManager, getTsWebConfig)
router.post('/tsweb', verifyToken, requireManager, setTsWebConfig)
router.get('/boss', verifyToken, requireManager, getBossConfig)
router.post('/boss', verifyToken, requireManager, setBossConfig)
router.get('/backup', verifyToken, requireManager, getBackupConfig)
router.post('/backup', verifyToken, requireManager, setBackupConfig)
router.get('/bosslimit/status', verifyToken, requireManager, getBossLimitStatus)
router.get('/license-check', getLicenseCheck)
router.post('/license-close', verifyToken, requireAdmin, postLicenseClose)
router.get('/promotion', verifyToken, requireManager, getPromotionConfig)
router.post('/promotion', verifyToken, requireManager, setPromotionConfig)
// Webhook 回传配置：仅 admin
router.get('/log-webhook', allowSetupToken, requireAdmin, getLogWebhookConfig)
router.post('/log-webhook', allowSetupToken, requireAdmin, setLogWebhookConfig)

export default router

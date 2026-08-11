import { Router } from 'express'
import { getConfigFile, saveConfigFile, getTsWebConfig, setTsWebConfig, getBossConfig, setBossConfig, getBackupConfig, setBackupConfig, getLicenseCheck, postLicenseClose, getBossLimitStatus, getPromotionConfig, setPromotionConfig, getListenConfig, saveListenConfig } from '../controllers/configController.js'
import { verifyToken, requireRole, requireAdmin, requireManager } from '../middlewares/authMiddleware.js'

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

export default router

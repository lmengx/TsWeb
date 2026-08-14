import { Router } from 'express'
import { getConfigFile, saveConfigFile, getTsWebConfig, setTsWebConfig, getBossConfig, setBossConfig, getBackupConfig, setBackupConfig, postLicenseClose, getBossLimitStatus, getPromotionConfig, setPromotionConfig, getListenConfig, saveListenConfig, getSingleLoginConfig, setSingleLoginConfig, getStatusPanelConfig, setStatusPanelConfig, getShopUIConfig, setShopUIConfig } from '../controllers/configController.js'
import { verifyToken, requireRole, requireAdmin, requireManager } from '../middlewares/authMiddleware.js'

const router = Router()

// 后端监听设置：仅 admin
router.get('/listen', verifyToken, requireAdmin, getListenConfig)
router.post('/listen', verifyToken, requireAdmin, saveListenConfig)
// 禁止多服登录（全局）：仅 admin
router.get('/single-login', verifyToken, requireAdmin, getSingleLoginConfig)
router.post('/single-login', verifyToken, requireAdmin, setSingleLoginConfig)
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
router.post('/license-close', verifyToken, requireAdmin, postLicenseClose)
router.get('/promotion', verifyToken, requireManager, getPromotionConfig)
router.post('/promotion', verifyToken, requireManager, setPromotionConfig)
router.get('/statuspanel', verifyToken, requireManager, getStatusPanelConfig)
router.post('/statuspanel', verifyToken, requireManager, setStatusPanelConfig)
router.get('/shopui', verifyToken, requireManager, getShopUIConfig)
router.post('/shopui', verifyToken, requireManager, setShopUIConfig)

export default router

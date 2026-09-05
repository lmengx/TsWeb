import { Router } from 'express'
import { getConfigFile, saveConfigFile, getTsWebConfig, setTsWebConfig, getBossConfig, setBossConfig, getBackupConfig, setBackupConfig, postLicenseClose, getBossLimitStatus, getPromotionConfig, setPromotionConfig, getListenConfig, saveListenConfig, getSingleLoginConfig, setSingleLoginConfig, getStatusPanelConfig, setStatusPanelConfig, getShopUIConfig, setShopUIConfig, getRiskControlConfig, setRiskControlConfig, riskControlAction, riskControlPlayers, riskControlProxyRefresh, getCurfewConfig, setCurfewConfig } from '../controllers/configController.js'
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
// 实时风控：admin + subadmin
router.get('/risk-control', verifyToken, requireManager, getRiskControlConfig)
router.post('/risk-control', verifyToken, requireManager, setRiskControlConfig)
router.post('/risk-control/action', verifyToken, requireManager, riskControlAction)
// 风控：在线玩家特征（群体命中计算 + 代理判定列表）
router.get('/risk-control/players', verifyToken, requireManager, riskControlPlayers)
// 风控：强制刷新代理检测缓存（ip 可选，缺省清空全部）
router.post('/risk-control/proxy/refresh', verifyToken, requireManager, riskControlProxyRefresh)
// 宵禁（禁止进服）：条目化排期 + 豁免组 + 模板消息
router.get('/curfew', verifyToken, requireManager, getCurfewConfig)
router.post('/curfew', verifyToken, requireManager, setCurfewConfig)

export default router

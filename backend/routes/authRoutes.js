import { Router } from 'express'
import {
  getServerKey, login, getUserInfo, setupLogin, changePassword,
  getAccounts, addAccount, removeAccount, resetAccountPassword,
  changeAccountRole, getInitStatus
} from '../controllers/authController.js'
import { verifyToken, requireAdmin } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/get-server-key', getServerKey)
router.post('/login', login)
router.get('/setup-login', setupLogin)
router.get('/init-status', getInitStatus)
router.get('/user-info', verifyToken, getUserInfo)

// 自助改密（admin/subadmin 均可，JWT + 旧密码校验）
router.post('/change-password', verifyToken, changePassword)

// 账户管理（仅 admin）
router.get('/accounts', verifyToken, requireAdmin, getAccounts)
router.post('/accounts', verifyToken, requireAdmin, addAccount)
router.delete('/accounts/:username', verifyToken, requireAdmin, removeAccount)
router.post('/accounts/:username/reset-password', verifyToken, requireAdmin, resetAccountPassword)
router.post('/accounts/:username/role', verifyToken, requireAdmin, changeAccountRole)

export default router

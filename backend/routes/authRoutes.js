import { Router } from 'express'
import {
  getServerKey, login, setupLogin, changePassword,
  playerLogin, playerMe,
  getAccounts, addAccount, removeAccount, resetAccountPassword,
  changeAccountRole
} from '../controllers/authController.js'
import { verifyToken, requireAdmin, requirePlayer } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/get-server-key', getServerKey)
router.post('/login', login)
router.get('/setup-login', setupLogin)

// 玩家（QQ 台账）登录 / 信息：独立于管理端账号体系，只查后端本地 qq_accounts.json
router.post('/player-login', playerLogin)
router.get('/player/me', verifyToken, requirePlayer, playerMe)

// 自助改密（admin/subadmin 均可，JWT + 旧密码校验）
router.post('/change-password', verifyToken, changePassword)

// 账户管理（仅 admin）
router.get('/accounts', verifyToken, requireAdmin, getAccounts)
router.post('/accounts', verifyToken, requireAdmin, addAccount)
router.delete('/accounts/:username', verifyToken, requireAdmin, removeAccount)
router.post('/accounts/:username/reset-password', verifyToken, requireAdmin, resetAccountPassword)
router.post('/accounts/:username/role', verifyToken, requireAdmin, changeAccountRole)

export default router

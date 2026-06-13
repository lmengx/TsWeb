import { Router } from 'express'
import { getServerKey, login, getUserInfo } from '../controllers/authController.js'
import { verifyToken } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/get-server-key', getServerKey)
router.post('/login', login)
router.get('/user-info', verifyToken, getUserInfo)

export default router

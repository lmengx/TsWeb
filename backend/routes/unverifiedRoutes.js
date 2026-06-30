import { Router } from 'express'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'
import * as unverified from '../controllers/unverifiedController.js'

const router = Router()

router.get('/list', verifyToken, requireRole('admin'), unverified.list)
router.get('/detail', verifyToken, requireRole('admin'), unverified.detail)
router.post('/register', verifyToken, requireRole('admin'), unverified.register)
router.post('/force-login', verifyToken, requireRole('admin'), unverified.forceLogin)
router.post('/kick', verifyToken, requireRole('admin'), unverified.kick)
router.post('/ban', verifyToken, requireRole('admin'), unverified.ban)

export default router

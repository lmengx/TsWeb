import { Router } from 'express'
import { getHourlyOnline, getRanking, getPlayerCalendar } from '../controllers/onlineController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'

const router = Router()

router.get('/hourly', verifyToken, requireRole('admin'), getHourlyOnline)
router.get('/ranking', verifyToken, requireRole('admin'), getRanking)
router.get('/player', verifyToken, requireRole('admin'), getPlayerCalendar)

export default router

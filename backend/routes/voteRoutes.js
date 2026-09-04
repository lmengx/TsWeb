import { Router } from 'express'
import { verifyToken, requireAdmin, requirePlayer } from '../middlewares/authMiddleware.js'
import * as vote from '../controllers/voteController.js'

const router = Router()

// ── 公开：轮次 + 结果（匿名可看，不含个人状态）──
router.get('/rounds', vote.listPublicRounds)

// ── 玩家（QQ 台账登录）──
router.get('/rounds/mine', verifyToken, requirePlayer, vote.listMyState)
router.post('/rounds/:id/cast', verifyToken, requirePlayer, vote.castVote)
router.post('/rounds/:id/propose', verifyToken, requirePlayer, vote.propose)

// ── 管理（admin）──
router.post('/rounds', verifyToken, requireAdmin, vote.createRound)
router.patch('/rounds/:id', verifyToken, requireAdmin, vote.updateRound)
router.post('/rounds/:id/options', verifyToken, requireAdmin, vote.addOption)
router.delete('/rounds/:id/options/:optionId', verifyToken, requireAdmin, vote.removeOption)
router.get('/admin/rounds', verifyToken, requireAdmin, vote.listAdminRounds)
router.get('/admin/rounds/:id/detail', verifyToken, requireAdmin, vote.getRoundDetail)
router.post('/rounds/:id/close', verifyToken, requireAdmin, vote.closeRound)
router.post('/rounds/:id/archive', verifyToken, requireAdmin, vote.archiveRound)
router.post('/rounds/:id/unarchive', verifyToken, requireAdmin, vote.unarchiveRound)
router.delete('/rounds/:id', verifyToken, requireAdmin, vote.deleteRound)

export default router

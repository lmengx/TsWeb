import { Router } from 'express'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'
import { rename, remove, uuid, batch } from '../controllers/userAdminController.js'

const router = Router()

// 玩家账号管理（改名/删除/UUID/批量）：admin + subadmin（requireManager）
router.use(verifyToken, requireManager)

router.post('/rename', rename)
router.post('/delete', remove)
router.post('/uuid', uuid)
router.post('/batch', batch)

export default router

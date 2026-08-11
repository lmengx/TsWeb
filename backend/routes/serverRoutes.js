import { Router } from 'express'
import { verifyToken, requireAdmin } from '../middlewares/authMiddleware.js'
import {
  list, getOne, create, update, remove, testConnection, testOnly
} from '../controllers/serverController.js'

const router = Router()

// 服务器管理仅 admin
router.use(verifyToken, requireAdmin)

router.get('/', list)
router.post('/test', testOnly)
router.get('/:id', getOne)
router.post('/', create)
router.put('/:id', update)
router.delete('/:id', remove)
router.post('/:id/test', testConnection)

export default router

import { Router } from 'express'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'
import * as playerTransfer from '../controllers/playerTransferController.js'

const router = Router()

// 玩家角色（.plr）导入导出：admin + subadmin（子管理员）均可用（与角色清空/背包编辑同权限级）
router.get('/export', verifyToken, requireManager, playerTransfer.exportPlayer)
router.get('/export-all', verifyToken, requireManager, playerTransfer.exportAll)
router.get('/export-list', verifyToken, requireManager, playerTransfer.listServerExports)
router.get('/has-character', verifyToken, requireManager, playerTransfer.hasCharacter)
router.post('/import', verifyToken, requireManager, playerTransfer.importPlayer)
router.post('/import-from-server', verifyToken, requireManager, playerTransfer.importFromServer)

// 后端目录（data/transfer/{serverId}/plr/）保存与管理
router.post('/backend-save', verifyToken, requireManager, playerTransfer.saveToBackend)
router.get('/backend-list', verifyToken, requireManager, playerTransfer.listBackend)
router.get('/backend-download', verifyToken, requireManager, playerTransfer.downloadFromBackend)
router.post('/backend-delete', verifyToken, requireManager, playerTransfer.deleteFromBackend)

export default router

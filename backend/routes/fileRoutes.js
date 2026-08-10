import { Router } from 'express'
import { verifyToken, requireAdmin } from '../middlewares/authMiddleware.js'
import * as fileController from '../controllers/fileController.js'

const router = Router()

// 文件管理：仅 admin（用户明确 subadmin 禁用）
router.get('/list', verifyToken, requireAdmin, fileController.listDir)
router.get('/read', verifyToken, requireAdmin, fileController.readFile)
router.post('/write', verifyToken, requireAdmin, fileController.writeFile)
router.post('/delete', verifyToken, requireAdmin, fileController.deleteFile)
router.post('/upload', verifyToken, requireAdmin, fileController.uploadFile)
// SSE 下载（fetch 流式读取，带 Authorization + x-server-id header，不走 EventSource）
router.get('/download', verifyToken, requireAdmin, fileController.downloadFile)
// 保存到后端（data/transfer/{serverId}/）
router.post('/save', verifyToken, requireAdmin, fileController.saveFile)
router.get('/saved', verifyToken, requireAdmin, fileController.listSavedFiles)
router.get('/saved/download', verifyToken, requireAdmin, fileController.downloadSavedFile)
router.post('/saved/delete', verifyToken, requireAdmin, fileController.deleteSavedFile)

export default router

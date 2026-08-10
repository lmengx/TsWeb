import { Router } from 'express'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'
import * as buildingController from '../controllers/buildingController.js'

const router = Router()

// 建筑存档（房屋导入导出）：与房屋管理页一致，admin + subadmin（manager）可用
router.get('/list', verifyToken, requireManager, buildingController.listBuildings)
router.post('/send', verifyToken, requireManager, buildingController.sendToBackend)
router.post('/export-to-backend', verifyToken, requireManager, buildingController.exportToBackend)
router.post('/upload', verifyToken, requireManager, buildingController.uploadToPlugin)
router.post('/import', verifyToken, requireManager, buildingController.importToWorld)
router.post('/delete', verifyToken, requireManager, buildingController.deleteBuilding)
router.get('/download', verifyToken, requireManager, buildingController.downloadBuilding)

export default router

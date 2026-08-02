import { Router } from 'express'
import { executeCommand, testCommand, getUsers, getActiveUsers, getInventory, getUserData, checkDuplicateIPs, getAllDuplicateIPs, editInventory, batchEdit, getGroups, getGroup, createGroup, updateGroup, deleteGroup, addGroupPermission, removeGroupPermission, banPlayer, unbanPlayer, createUser, getSelfInfo, getBossProgress, getBanList, scanItems, scanItemById, getPlayerStats, setPlayerStats, clearCharacter, clearAllCharacter } from '../controllers/tshockController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'
import tshockService from '../services/tshockService.js'

const router = Router()

router.post('/command', verifyToken, requireRole('admin'), executeCommand)
router.get('/rawcmd', verifyToken, requireRole('admin'), testCommand)
router.get('/users', verifyToken, requireRole('admin'), getUsers)
router.get('/activeusers', verifyToken, requireRole('admin'), getActiveUsers)
router.get('/invsee', verifyToken, requireRole('admin'), getInventory)
router.get('/userdata', verifyToken, requireRole('admin'), getUserData)
router.post('/user/create', verifyToken, requireRole('admin'), createUser)
router.get('/duplicateips', verifyToken, requireRole('admin'), checkDuplicateIPs)
router.get('/allduplicateips', verifyToken, requireRole('admin'), getAllDuplicateIPs)
router.post('/editinv', verifyToken, requireRole('admin'), editInventory)
router.post('/batch-edit', verifyToken, requireRole('admin'), batchEdit)
router.get('/groups', verifyToken, getGroups)
router.get('/groups/get', verifyToken, getGroup)
router.post('/groups/create', verifyToken, requireRole('admin'), createGroup)
router.post('/groups/update', verifyToken, requireRole('admin'), updateGroup)
router.post('/groups/delete', verifyToken, requireRole('admin'), deleteGroup)
router.post('/groups/permission/add', verifyToken, requireRole('admin'), addGroupPermission)
router.post('/groups/permission/remove', verifyToken, requireRole('admin'), removeGroupPermission)
router.post('/ban', verifyToken, requireRole('admin'), banPlayer)
router.post('/unban', verifyToken, requireRole('admin'), unbanPlayer)
router.get('/banlist', verifyToken, requireRole('admin'), getBanList)
router.get('/self', verifyToken, getSelfInfo)
router.get('/boss/progress', verifyToken, getBossProgress)
router.post('/itemscan', verifyToken, requireRole('admin'), scanItems)
router.post('/itemscan-by-id', verifyToken, requireRole('admin'), scanItemById)
router.get('/stats', verifyToken, requireRole('admin'), getPlayerStats)
router.post('/stats/set', verifyToken, requireRole('admin'), setPlayerStats)
router.post('/clearcharacter', verifyToken, requireRole('admin'), clearCharacter)
router.post('/clearallcharacter', verifyToken, requireRole('admin'), clearAllCharacter)

// ===== 通用代理：TSWeb 自定义 /data/* 端点（自动任务等） =====
router.use('/data', verifyToken, requireRole('admin'), async (req, res) => {
  // router.use('/data') 挂载后，req.path 已剥离 /data 前缀，如 /tasks/list
  const subPath = req.path.replace(/^\//, '')
  const method = req.method

  // 合并 query 与 POST body 参数（TShock REST 通过 query 收参）
  const params = { ...req.query }
  if (req.method === 'POST' && req.body && typeof req.body === 'object') {
    Object.assign(params, req.body)
  }

  const result = await tshockService.proxyDataRequest(subPath, method, params)
  if (result.error && !result.response) {
    return res.status(502).json({ status: '500', error: result.error })
  }
  res.json(result)
})

export default router

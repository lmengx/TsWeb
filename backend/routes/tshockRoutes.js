import { Router } from 'express'
import { executeCommand, testCommand, getUsers, getActiveUsers, getInventory, getUserData, checkDuplicateIPs, getAllDuplicateIPs, editInventory, batchEdit, getGroups, getGroup, createGroup, updateGroup, deleteGroup, addGroupPermission, removeGroupPermission, banPlayer, unbanPlayer, createUser, getSelfInfo, getBossProgress, getBanList, scanItems, scanItemById, getPlayerStats, setPlayerStats, clearCharacter, clearAllCharacter } from '../controllers/tshockController.js'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'
import tshockService from '../services/tshockService.js'

const router = Router()

// 服务器内操作：admin + subadmin（子管理员）均可用
router.post('/command', verifyToken, requireManager, executeCommand)
router.get('/rawcmd', verifyToken, requireManager, testCommand)
router.get('/users', verifyToken, requireManager, getUsers)
router.get('/activeusers', verifyToken, requireManager, getActiveUsers)
router.get('/invsee', verifyToken, requireManager, getInventory)
router.get('/userdata', verifyToken, requireManager, getUserData)
router.post('/user/create', verifyToken, requireManager, createUser)
router.get('/duplicateips', verifyToken, requireManager, checkDuplicateIPs)
router.get('/allduplicateips', verifyToken, requireManager, getAllDuplicateIPs)
router.post('/editinv', verifyToken, requireManager, editInventory)
router.post('/batch-edit', verifyToken, requireManager, batchEdit)
router.get('/groups', verifyToken, getGroups)
router.get('/groups/get', verifyToken, getGroup)
router.post('/groups/create', verifyToken, requireManager, createGroup)
router.post('/groups/update', verifyToken, requireManager, updateGroup)
router.post('/groups/delete', verifyToken, requireManager, deleteGroup)
router.post('/groups/permission/add', verifyToken, requireManager, addGroupPermission)
router.post('/groups/permission/remove', verifyToken, requireManager, removeGroupPermission)
router.post('/ban', verifyToken, requireManager, banPlayer)
router.post('/unban', verifyToken, requireManager, unbanPlayer)
router.get('/banlist', verifyToken, requireManager, getBanList)
router.get('/self', verifyToken, getSelfInfo)
router.get('/boss/progress', verifyToken, getBossProgress)
router.post('/itemscan', verifyToken, requireManager, scanItems)
router.post('/itemscan-by-id', verifyToken, requireManager, scanItemById)
router.get('/stats', verifyToken, requireManager, getPlayerStats)
router.post('/stats/set', verifyToken, requireManager, setPlayerStats)
router.post('/clearcharacter', verifyToken, requireManager, clearCharacter)
router.post('/clearallcharacter', verifyToken, requireManager, clearAllCharacter)

// ===== 通用代理：TSWeb 自定义 /data/* 端点（自动任务等） =====
router.use('/data', verifyToken, requireManager, async (req, res) => {
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

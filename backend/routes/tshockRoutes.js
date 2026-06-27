import { Router } from 'express'
import { executeCommand, testCommand, getUsers, getActiveUsers, getInventory, getUserList, checkDuplicateIPs, getAllDuplicateIPs, editInventory, getGroups, getGroup, createGroup, updateGroup, deleteGroup, addGroupPermission, removeGroupPermission, banPlayer, unbanPlayer, createUser, getSelfInfo, getBossProgress, getBanList, scanItems, getPlayerStats, setPlayerStats } from '../controllers/tshockController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'

const router = Router()

router.post('/command', verifyToken, requireRole('admin'), executeCommand)
router.get('/rawcmd', verifyToken, testCommand)
router.get('/users', verifyToken, requireRole('admin'), getUsers)
router.get('/activeusers', verifyToken, requireRole('admin'), getActiveUsers)
router.get('/invsee', verifyToken, requireRole('admin'), getInventory)
router.get('/userlist', verifyToken, requireRole('admin'), getUserList)
router.post('/user/create', verifyToken, requireRole('admin'), createUser)
router.get('/duplicateips', verifyToken, requireRole('admin'), checkDuplicateIPs)
router.get('/allduplicateips', verifyToken, requireRole('admin'), getAllDuplicateIPs)
router.post('/editinv', verifyToken, requireRole('admin'), editInventory)
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
router.get('/stats', verifyToken, requireRole('admin'), getPlayerStats)
router.post('/stats/set', verifyToken, requireRole('admin'), setPlayerStats)

export default router

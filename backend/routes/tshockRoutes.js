import { Router } from 'express'
import { executeCommand, testCommand, getUsers, getActiveUsers, getInventory, getUserList, checkDuplicateIPs, editInventory, getGroups, getGroup, createGroup, updateGroup, deleteGroup, addGroupPermission, removeGroupPermission, banPlayer } from '../controllers/tshockController.js'
import { verifyToken, requireRole } from '../middlewares/authMiddleware.js'

const router = Router()

router.post('/command', verifyToken, requireRole('admin'), executeCommand)
router.get('/rawcmd', verifyToken, testCommand)
router.get('/users', verifyToken, getUsers)
router.get('/activeusers', verifyToken, getActiveUsers)
router.get('/invsee', verifyToken, requireRole('admin'), getInventory)
router.get('/userlist', verifyToken, getUserList)
router.get('/duplicateips', verifyToken, checkDuplicateIPs)
router.post('/editinv', verifyToken, requireRole('admin'), editInventory)
router.get('/groups', verifyToken, getGroups)
router.get('/groups/get', verifyToken, getGroup)
router.post('/groups/create', verifyToken, requireRole('admin'), createGroup)
router.post('/groups/update', verifyToken, requireRole('admin'), updateGroup)
router.post('/groups/delete', verifyToken, requireRole('admin'), deleteGroup)
router.post('/groups/permission/add', verifyToken, requireRole('admin'), addGroupPermission)
router.post('/groups/permission/remove', verifyToken, requireRole('admin'), removeGroupPermission)
router.post('/ban', verifyToken, requireRole('admin'), banPlayer)

export default router

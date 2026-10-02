import { Router } from 'express'
import { executeCommand, getUsers, getActiveUsers, getInventory, getUserData, checkDuplicateIPs, getAllDuplicateIPs, editInventory, batchEdit, getGroups, createGroup, updateGroup, deleteGroup, addGroupPermission, removeGroupPermission, banPlayer, unbanPlayer, createUser, getBossProgress, getBanList, scanItems, scanItemById, getPlayerStats, setPlayerStats, clearCharacter, clearAllCharacter } from '../controllers/tshockController.js'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'
import tshockService, { getCurrentServerId } from '../services/tshockService.js'
import audit from '../services/auditLogger.js'

// ═══════════════════════════════════════════════════════════
// 通用代理路径白名单
//
// 本代理以「后端 API Key」的身份转发请求，插件端 SecureRestCommand 的权限项
// 因此恒为通过；唯一真实边界就是本路由的 requireManager（admin + subadmin）。
// 若不限制路径，会出现两个问题：
//   1) 越权：子管理员可经 /api/tshock/data/permissions/grant 签发任意权限
//      （该操作的专用路由 /api/permissions/grant 要求 requireAdmin）；
//   2) 漏审计：/data/users/{clearallcharacter,clearcharacter,rename,delete,uuid}
//      等破坏性端点可绕过各自在专用路由上写入的审计。
// 故只放行确有前端调用的功能族；新增路径必须在此登记。
// ═══════════════════════════════════════════════════════════
const PROXY_PATH_ALLOWLIST = [
  'tasks/',      // 自动任务 — frontend/src/api/tasksApi.js
  'house/',      // 房屋系统 — frontend/src/api/houseApi.js
  'buildings/',  // 建筑存档 — frontend/src/api/houseApi.js
  'emoji/'       // 聊天表情 — frontend/src/api/emojiApi.js
]

/**
 * 子路径是否在白名单内。
 *
 * 额外拒绝 `..` 与 `%`：前缀匹配本身挡不住 "tasks/../users/getpassword"
 * 这类构造——它以 "tasks/" 开头能通过 startsWith，却可能被下游解码成别的端点。
 */
export function isProxyPathAllowed(subPath) {
  if (!subPath) return false
  if (subPath.includes('..') || subPath.includes('%')) return false
  return PROXY_PATH_ALLOWLIST.some(prefix => subPath.startsWith(prefix))
}

const router = Router()

// 服务器内操作：admin + subadmin（子管理员）均可用
router.post('/command', verifyToken, requireManager, executeCommand)
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
router.post('/groups/create', verifyToken, requireManager, createGroup)
router.post('/groups/update', verifyToken, requireManager, updateGroup)
router.post('/groups/delete', verifyToken, requireManager, deleteGroup)
router.post('/groups/permission/add', verifyToken, requireManager, addGroupPermission)
router.post('/groups/permission/remove', verifyToken, requireManager, removeGroupPermission)
router.post('/ban', verifyToken, requireManager, banPlayer)
router.post('/unban', verifyToken, requireManager, unbanPlayer)
router.get('/banlist', verifyToken, requireManager, getBanList)
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

  // 白名单之外一律拒绝，且不转发给插件（权限边界见上方注释）
  if (!isProxyPathAllowed(subPath)) {
    console.warn(`[Proxy] 拒绝未登记的 /data 路径: ${subPath}（actor=${req.user?.username || 'unknown'}）`)
    return res.status(403).json({
      status: '403',
      error: `通用代理仅放行登记过的路径，已拒绝: ${subPath}。如确需放行，请在 tshockRoutes.js 的 PROXY_PATH_ALLOWLIST 中登记。`
    })
  }

  const method = req.method

  // 合并 query 与 POST body 参数（TShock REST 通过 query 收参）
  const params = { ...req.query }
  if (req.method === 'POST' && req.body && typeof req.body === 'object') {
    Object.assign(params, req.body)
  }

  const result = await tshockService.proxyDataRequest(subPath, method, params)

  // 注意：user.password_query 的审计只在此分支，而 `users/` 前缀已被上面的白名单拒绝，
  // 故本分支当前不可达——/data/users/getpassword 原先可经代理透传并返回 Users.Password（bcrypt 哈希）。
  // 保留它的原因：若将来重新放行该路径，审计不会跟着丢。若要让"查询玩家密码"恢复可用，
  // 应新增一条专用路由并置于 requireAdmin 之下，而不是把它放回通用代理。
  if (/^users\/getpassword$/i.test(subPath)) {
    audit.safeRecord('user.password_query', {
      serverId: getCurrentServerId() || '',
      actor: req.user?.username || 'unknown',
      ip: req.ip,
      player: params.username || '',
      ok: !result.error,
      ...(result.error ? { detail: { error: result.error } } : {})
    })
  }

  if (result.error && !result.response) {
    return res.status(502).json({ status: '500', error: result.error })
  }
  res.json(result)
})

export default router

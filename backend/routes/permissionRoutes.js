import { Router } from 'express'
import tshockService from '../services/tshockService.js'
import { verifyToken, requireAdmin, requireManager } from '../middlewares/authMiddleware.js'
import audit from '../services/auditLogger.js'

// ═══════════════════════════════════════════════════════════
// 个人独立权限管理（插件端 /data/permissions/* 代理）
//   - 查看：manager 级（summary / list）
//   - 签发/回收/清理：admin 级（grant / revoke 系列）
//   - 批量操作只记录后端一次审计（含 serverId + 数组明细），
//     批量签发请求体为 { players: [...], permissions: [...] } 矩阵
// ═══════════════════════════════════════════════════════════

const router = Router()

const getServerId = (req) => String(req.headers['x-server-id'] || '')

// ── 聚合统计（玩家/权限两个维度 + 数量） ──
router.get('/summary', verifyToken, requireManager, async (req, res) => {
  try {
    const data = await tshockService.proxyDataRequest('permissions/summary', 'GET', {})
    if (data.error && !data.response) return res.status(502).json({ status: '500', error: data.error })
    res.json(data)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

// ── 明细列表（全量返回，排序/筛选由前端完成） ──
router.get('/list', verifyToken, requireManager, async (req, res) => {
  try {
    const params = {}
    for (const k of ['player', 'permission', 'grantedBy', 'status']) {
      if (req.query[k]) params[k] = req.query[k]
    }
    const data = await tshockService.proxyDataRequest('permissions/list', 'GET', params)
    if (data.error && !data.response) return res.status(502).json({ status: '500', error: data.error })
    res.json(data)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

// ── 单条快速签发 ──
router.post('/grant', verifyToken, requireAdmin, async (req, res) => {
  try {
    const { player, permission, note, expireAt, expiresIn } = req.body || {}
    if (!player || !permission) {
      return res.status(400).json({ status: '400', error: 'player 与 permission 参数必填' })
    }
    const actor = req.user?.username || 'unknown'
    const serverId = getServerId(req)

    const params = { player, permission, grantedBy: actor }
    if (note) params.note = note
    if (expireAt) params.expireAt = expireAt
    if (expiresIn) params.expiresIn = expiresIn

    const data = await tshockService.proxyDataRequest('permissions/grant', 'POST', params)
    if (data.error && !data.response) return res.status(400).json({ status: '400', error: data.error })

    audit.record('permission.grant', {
      player, permission,
      note: note || '',
      expireAt: expireAt || '',
      serverId, actor
    })
    res.json(data)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

// ── 批量签发（多玩家 × 多权限矩阵，一次操作一条审计） ──
router.post('/grant-batch', verifyToken, requireAdmin, async (req, res) => {
  try {
    const { players, permissions, note, expireAt, expiresIn } = req.body || {}
    const playersArr = Array.isArray(players) ? players.filter(Boolean) : [players].filter(Boolean)
    const permsArr = Array.isArray(permissions) ? permissions.filter(Boolean) : [permissions].filter(Boolean)
    if (playersArr.length === 0 || permsArr.length === 0) {
      return res.status(400).json({ status: '400', error: 'players 与 permissions 必填且不能为空' })
    }
    const actor = req.user?.username || 'unknown'
    const serverId = getServerId(req)

    const params = { players: playersArr, permissions: permsArr, grantedBy: actor }
    if (note) params.note = note
    if (expireAt) params.expireAt = expireAt
    if (expiresIn) params.expiresIn = expiresIn

    const data = await tshockService.proxyDataRequest('permissions/grant-batch', 'POST', params)
    if (data.error && !data.response) return res.status(400).json({ status: '400', error: data.error })

    audit.record('permission.grant_batch', {
      players: playersArr,
      permissions: permsArr,
      note: note || '',
      expireAt: expireAt || '',
      serverId, actor
    })
    res.json(data)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

// ── 回收单条 ──
router.post('/revoke', verifyToken, requireAdmin, async (req, res) => {
  try {
    const { player, permission } = req.body || {}
    if (!player || !permission) {
      return res.status(400).json({ status: '400', error: 'player 与 permission 参数必填' })
    }
    const actor = req.user?.username || 'unknown'
    const serverId = getServerId(req)

    const data = await tshockService.proxyDataRequest('permissions/revoke', 'POST', {
      player, permission, grantedBy: actor
    })
    if (data.error && !data.response) return res.status(400).json({ status: '400', error: data.error })

    audit.record('permission.revoke', { player, permission, serverId, actor })
    res.json(data)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

// ── 批量回收（players × permissions 全组合，一次操作一条审计） ──
router.post('/revoke-batch', verifyToken, requireAdmin, async (req, res) => {
  try {
    const { players, permissions } = req.body || {}
    const playersArr = Array.isArray(players) ? players.filter(Boolean) : [players].filter(Boolean)
    const permsArr = Array.isArray(permissions) ? permissions.filter(Boolean) : [permissions].filter(Boolean)
    if (playersArr.length === 0 || permsArr.length === 0) {
      return res.status(400).json({ status: '400', error: 'players 与 permissions 必填且不能为空' })
    }
    const actor = req.user?.username || 'unknown'
    const serverId = getServerId(req)

    const data = await tshockService.proxyDataRequest('permissions/revoke-batch', 'POST', {
      players: playersArr, permissions: permsArr, grantedBy: actor
    })
    if (data.error && !data.response) return res.status(400).json({ status: '400', error: data.error })

    audit.record('permission.revoke_batch', {
      players: playersArr, permissions: permsArr, serverId, actor
    })
    res.json(data)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

// ── 手动清理过期权限 ──
router.post('/cleanup', verifyToken, requireAdmin, async (req, res) => {
  try {
    const actor = req.user?.username || 'unknown'
    const serverId = getServerId(req)

    const data = await tshockService.proxyDataRequest('permissions/cleanup', 'POST', { grantedBy: actor })
    if (data.error && !data.response) return res.status(502).json({ status: '500', error: data.error })

    audit.record('permission.cleanup', { cleaned: data.cleaned || 0, serverId, actor })
    res.json(data)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

export default router

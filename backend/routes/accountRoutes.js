import { Router } from 'express'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'
import audit from '../services/auditLogger.js'
import {
  aggregateAttributes,
  getAllFiltered,
  getRuleMeta,
  attrLabel,
  attrList
} from '../services/accountAttributeService.js'
import { findAltPurgeCandidates, executeAltPurge } from '../services/altPurgeService.js'

// ═══════════════════════════════════════════════════════════
// 账号属性判定（多服聚合）
//   GET /api/account/attributes  聚合明细 + 概览统计（筛选/分页/排序）
//   GET /api/account/meta        判定规则说明（语义明确，前端规则面板用）
//   GET /api/account/export      当前筛选结果 CSV 导出
//   GET /api/account/attrs       属性字典（key -> 中文标签）
//   GET /api/account/purge/preview   小号清理候选扫描
//   POST /api/account/purge/execute  小号清理执行（删除）
// 权限：manager 级（admin + subadmin）
// ═══════════════════════════════════════════════════════════

const router = Router()

router.get('/attrs', verifyToken, requireManager, (req, res) => {
  res.json({ attrs: attrList() })
})

router.get('/meta', verifyToken, requireManager, async (req, res) => {
  try {
    const meta = await getRuleMeta()
    res.json({ meta })
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

router.get('/attributes', verifyToken, requireManager, async (req, res) => {
  try {
    const filters = {
      serverId: req.query.serverId || '',
      attr: req.query.attr || '',
      keyword: req.query.keyword || '',
      minMinutes: req.query.minMinutes,
      maxMinutes: req.query.maxMinutes,
      activeDays14Min: req.query.activeDays14Min,
      sortBy: req.query.sortBy || 'totalMinutes',
      sortDir: req.query.sortDir || 'desc',
      page: req.query.page || '1',
      pageSize: req.query.pageSize || '100'
    }
    const result = await aggregateAttributes(filters)
    audit.record('account.attributes.view', {
      serverId: filters.serverId || 'all',
      attr: filters.attr || '',
      keyword: filters.keyword || '',
      total: result.total,
      actor: req.user?.username || 'unknown'
    })
    res.json(result)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

router.get('/export', verifyToken, requireManager, async (req, res) => {
  try {
    const filters = {
      serverId: req.query.serverId || '',
      attr: req.query.attr || '',
      keyword: req.query.keyword || '',
      minMinutes: req.query.minMinutes,
      maxMinutes: req.query.maxMinutes,
      activeDays14Min: req.query.activeDays14Min,
      sortBy: req.query.sortBy || 'totalMinutes',
      sortDir: req.query.sortDir || 'desc'
    }
    const result = await getAllFiltered(filters)

    const columns = [
      { key: 'serverName', label: '服务器' },
      { key: 'username', label: '账号' },
      { key: 'qq', label: 'QQ' },
      { key: 'group', label: '用户组' },
      { key: 'registered', label: '注册时间' },
      { key: 'lastAccess', label: '最后登录' },
      { key: 'totalMinutes', label: '累计时长(分)' },
      { key: 'recent7dMinutes', label: '近7天(分)' },
      { key: 'recent14dMinutes', label: '近14天(分)' },
      { key: 'recent30dMinutes', label: '近30天(分)' },
      { key: 'activeDays7', label: '近7天活跃天数' },
      { key: 'activeDays14', label: '近14天活跃天数' },
      { key: 'activeDays30', label: '近30天活跃天数' },
      { key: 'relGroupSize', label: '关联组大小' },
      { key: 'attributes', label: '属性' }
    ]

    const esc = (v) => {
      const s = String(v ?? '')
      return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
    }

    const lines = [columns.map(c => esc(c.label)).join(',')]
    for (const a of result.accounts) {
      const row = {}
      for (const c of columns) {
        if (c.key === 'attributes') {
          row[c.key] = (a.attributes || []).map(k => attrLabel(k)).join('|')
        } else {
          row[c.key] = a[c.key]
        }
      }
      lines.push(columns.map(c => esc(row[c.key])).join(','))
    }

    const csv = '\uFEFF' + lines.join('\r\n') // BOM 保证 Excel 打开中文不乱码
    const filename = `account-attributes-${new Date().toISOString().slice(0, 10)}.csv`

    audit.record('account.attributes.export', {
      serverId: filters.serverId || 'all',
      attr: filters.attr || '',
      rows: result.accounts.length,
      actor: req.user?.username || 'unknown'
    })

    res.setHeader('Content-Type', 'text/csv; charset=utf-8')
    res.setHeader('Content-Disposition', `attachment; filename="${filename}"`)
    res.send(csv)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

// ═══════════════════════════════════════════════════════════
// 小号清理（手动触发 → 候选名单 → 确认删除）
//   候选：判定为小号(alt) 且 最后登录距今 >= days 天（默认 30）
//   保护：管理组账号 + 已绑定 QQ 的账号（不进名单）
//   删除：仅该账号所在那一台服；只删 TShock 账号行，保留角色存档与封禁记录
// ═══════════════════════════════════════════════════════════

router.get('/purge/preview', verifyToken, requireManager, async (req, res) => {
  try {
    const days = req.query.days || 30
    const serverId = req.query.serverId || ''
    const result = await findAltPurgeCandidates({ days, serverId })
    audit.record('account.purge.preview', {
      days: result.inactiveDays,
      serverId: serverId || 'all',
      scanned: result.scanned,
      candidates: result.total,
      actor: req.user?.username || 'unknown'
    })
    res.json(result)
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

router.post('/purge/execute', verifyToken, requireManager, async (req, res) => {
  try {
    const { items, days } = req.body || {}
    const result = await executeAltPurge({ items, days: days || 30 })
    audit.record('account.purge.execute', {
      days: result.inactiveDays,
      requested: result.requested,
      deleted: result.deletedCount,
      skipped: result.skippedCount,
      // 只记录实际删除的账号（跳过的不记，避免日志被未执行项淹没）
      usernames: result.deleted.map(d => `${d.serverName}:${d.username}`),
      actor: req.user?.username || 'unknown'
    })
    res.json({ status: 'ok', ...result })
  } catch (err) {
    res.status(400).json({ status: '400', error: err.message })
  }
})

export default router

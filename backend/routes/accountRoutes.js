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

// ═══════════════════════════════════════════════════════════
// 账号属性判定（多服聚合）
//   GET /api/account/attributes  聚合明细 + 概览统计（筛选/分页/排序）
//   GET /api/account/meta        判定规则说明（语义明确，前端规则面板用）
//   GET /api/account/export      当前筛选结果 CSV 导出
//   GET /api/account/attrs       属性字典（key -> 中文标签）
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

export default router

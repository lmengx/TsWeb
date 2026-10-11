import { getAllFiltered } from './accountAttributeService.js'
import { deleteUser } from './userAdminService.js'

// ═══════════════════════════════════════════════════════════
// 小号清理（alt purge）
//   候选 = 判定为小号(alt) 且 最后登录距今 >= N 天（默认 30 天）
//   保护 = 管理组账号（组名含 admin / owner / superadmin）+ 已绑定 QQ 的账号
//   删除 = 仅该账号所在那一台服；只删 TShock 账号行，保留角色存档与封禁记录
//
// 两个刻意的设计：
//   1) 执行前必须复检：名单可能在「扫描 → 确认」之间过期（账号刚登录过、
//      刚绑定了 QQ、刚被提权）。复检不通过的账号一律不删，且逐条给出跳过原因
//      —— 依据项目既有的「失效必须可见」原则，绝不静默丢弃。
//   2) 服间并行、服内串行：避免同一台服被连续请求压垮。
// ═══════════════════════════════════════════════════════════

export const DEFAULT_INACTIVE_DAYS = 30
export const MAX_EXECUTE_ITEMS = 500

/** 管理组判定：沿用前端 Console.vue / Home.vue 既有约定（组名含 admin / owner / superadmin） */
export function isProtectedGroup(group) {
  const g = String(group || '').toLowerCase()
  return g.includes('admin') || g.includes('owner') || g.includes('superadmin')
}

/**
 * 距今天数（按本地日历日差，避免时分秒造成的 29/30 天抖动）
 * 插件输出的 lastAccess 是 yyyy-MM-dd；空值或非法格式返回 null（无法判定 → 不进候选）
 */
export function daysSinceLastAccess(lastAccess, now = new Date()) {
  const s = String(lastAccess || '').trim()
  if (!/^\d{4}-\d{2}-\d{2}$/.test(s)) return null
  const [y, m, d] = s.split('-').map(Number)
  const then = new Date(y, m - 1, d)
  if (Number.isNaN(then.getTime())) return null
  // Date 会把非法日期自动进位（2026-13-45 -> 2027-02-14），
  // 这条删除路径上的错误日期会直接导致误判，故逐字段核对回读值
  if (then.getFullYear() !== y || then.getMonth() !== m - 1 || then.getDate() !== d) return null
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  return Math.floor((today - then) / 86400000)
}

/** 天数归一化：显式传入且为有效非负数才采用，否则回落默认值（0 是合法值，不能用 || 兜底） */
function normalizeDays(days) {
  const n = parseInt(days, 10)
  if (Number.isNaN(n) || n < 0) return DEFAULT_INACTIVE_DAYS
  return n
}

/** (服, 账号) 复合键；用 JSON 编码避免用户名里出现分隔符导致歧义 */
const keyOf = (serverId, username) => JSON.stringify([String(serverId), String(username)])

/**
 * 扫描（内部）：返回候选 + 每个账号未入选的原因，供预览与执行复检共用。
 * @returns {{ inactiveDays, servers, scanned, candidates, reasons: Map, excluded: object }}
 */
async function scanAltPurge({ days, serverId = '' }) {
  const inactiveDays = normalizeDays(days)
  // 复用账号属性聚合（attr=alt 已在插件侧按「关联组内 且 累计时长 <= 30 分钟」判定）
  const agg = await getAllFiltered({ attr: 'alt', serverId })
  const accounts = agg.accounts || []

  const candidates = []
  const reasons = new Map()
  // key -> 账号概要：执行复检时给「跳过」项补全可读信息（服名/组/最后登录）
  const index = new Map()
  const excluded = { adminGroup: 0, qqBound: 0, unknownLastAccess: 0, recentlyActive: 0 }

  for (const a of accounts) {
    const key = keyOf(a.serverId, a.username)
    const item = {
      serverId: a.serverId,
      serverName: a.serverName,
      username: a.username,
      group: a.group || '',
      qq: a.qq || '',
      registered: a.registered || '',
      lastAccess: a.lastAccess || '',
      inactiveDays: daysSinceLastAccess(a.lastAccess),
      totalMinutes: a.totalMinutes || 0,
      recent30dMinutes: a.recent30dMinutes || 0,
      relGroupSize: a.relGroupSize || 1
    }
    index.set(key, item)

    if (isProtectedGroup(item.group)) {
      excluded.adminGroup++
      reasons.set(key, '管理组账号（保护）')
      continue
    }
    if (item.qq) {
      excluded.qqBound++
      reasons.set(key, '已绑定 QQ（保护）')
      continue
    }
    if (item.inactiveDays === null) {
      excluded.unknownLastAccess++
      reasons.set(key, '无最后登录记录，无法判定')
      continue
    }
    if (item.inactiveDays < inactiveDays) {
      excluded.recentlyActive++
      reasons.set(key, `最后登录距今仅 ${item.inactiveDays} 天（未满 ${inactiveDays} 天）`)
      continue
    }

    candidates.push(item)
  }

  // 默认按「最久未登录」优先
  candidates.sort((x, y) => y.inactiveDays - x.inactiveDays)

  return { inactiveDays, servers: agg.servers || [], scanned: accounts.length, candidates, reasons, index, excluded }
}

/**
 * 清理候选预览（给前端展示名单用）
 * @param {{ days?: number|string, serverId?: string }} opts
 */
export async function findAltPurgeCandidates({ days, serverId = '' } = {}) {
  const s = await scanAltPurge({ days, serverId })
  return {
    generatedAt: new Date().toISOString(),
    inactiveDays: s.inactiveDays,
    servers: s.servers,
    scanned: s.scanned,
    total: s.candidates.length,
    candidates: s.candidates,
    excluded: s.excluded,
    limits: { maxExecuteItems: MAX_EXECUTE_ITEMS },
    rule: {
      attribute: 'alt',
      description: `判定为小号（在关联组内 且 累计时长 <= 30 分钟）且 最后登录距今 >= ${s.inactiveDays} 天`,
      protectedGroups: '组名含 admin / owner / superadmin 的管理组账号',
      qqBound: '已绑定 QQ 的账号',
      scope: '仅删除该账号所在的那一台服；只删 TShock 账号行，保留角色存档与封禁记录',
      recheck: '执行前会按同样的条件复检一次，不再满足条件的账号不会删除',
      // 删除会改变剩余账号的属性：小号判定依赖「在关联组内」，组内成员被删光后会解散，
      // 残留账号因此失去 alt 属性（仍在账号表中，只是变成游客）。名单在两次扫描间变化
      // 是正常现象，不是删除失败，必须显式说明，否则用户会反复重试并撞上 404。
      note: '小号判定依赖「在关联组内」。删除组内其他账号后，关联组可能解散，残留账号会失去小号属性（账号仍在，属性变为游客），因此重新扫描时名单会变化，这是正常现象'
    }
  }
}

/**
 * 执行清理（删除）
 * @param {{ items?: Array<{serverId, username}>, days?: number|string }} opts
 * @returns {{ executedAt, inactiveDays, requested, deletedCount, skippedCount, deleted[], skipped[] }}
 */
export async function executeAltPurge({ items = [], days } = {}) {
  const list = Array.isArray(items) ? items : []
  if (list.length === 0) throw new Error('缺少参数: items')
  if (list.length > MAX_EXECUTE_ITEMS) {
    throw new Error(`单次清理上限 ${MAX_EXECUTE_ITEMS} 个账号，请分批执行`)
  }

  // 归一化 + 去重（同一「服 + 账号」只删一次）
  // 注意：账号名是精确标识符，可能含首尾空白（全角空格 / 不换行空格等），
  // 绝不能 trim——裁剪后名字就变了，复检与删除都会定位失败。
  const wanted = new Map()
  for (const it of list) {
    const serverId = String(it?.serverId ?? '').trim()
    const username = String(it?.username ?? '')
    if (!serverId || !username.trim()) continue
    wanted.set(keyOf(serverId, username), { serverId, username })
  }
  if (wanted.size === 0) throw new Error('items 中没有有效的 (serverId, username)')

  // 执行前复检（不限定 serverId：名单可能跨服）
  const fresh = await scanAltPurge({ days })
  const freshByKey = new Map(fresh.candidates.map(c => [keyOf(c.serverId, c.username), c]))

  const targets = []
  const skipped = []
  // 复检时账号是否还存在于账号表：用于区分「已被删掉」和「还在、只是属性变了」。
  // 该查询要拉全量账号（跨服），只在真正需要时执行一次，并缓存结果复用：
  // 大多数情况下 deleted 会让 why 有值或全部命中候选，用不到它，不该无条件付出这个开销。
  let presentNowPromise = null
  const getPresentNow = () => {
    if (!presentNowPromise) {
      presentNowPromise = (async () => {
        const set = new Set()
        try {
          const all = await getAllFiltered({})
          for (const a of all.accounts || []) set.add(keyOf(a.serverId, a.username))
        } catch {
          // 查不到 → 无法判定存在性，措辞回落到保守那一种
        }
        return set
      })()
    }
    return presentNowPromise
  }

  for (const [key, item] of wanted) {
    const hit = freshByKey.get(key)
    if (!hit) {
      const why = fresh.reasons.get(key)
      const info = fresh.index.get(key)
      // 关键区分：账号还在、只是不再满足 alt 条件 —— 关联组被删散后会大量出现这种情况。
      // 若笼统说成「已不存在」，用户会以为自己删失败了，反复重试（而重试必然 404）。
      let reason
      if (why) {
        reason = `复检未通过：${why}`
      } else {
        const presentNow = await getPresentNow()
        reason = presentNow.has(key)
          ? '复检未通过：账号仍存在，但已不再满足小号条件（关联组被删除后解散，属性已改变）'
          : '复检未通过：账号已不存在（可能已被本轮或此前的清理删除）'
      }
      skipped.push({
        ...item,
        serverName: info?.serverName || '',
        group: info?.group || '',
        lastAccess: info?.lastAccess || '',
        reason
      })
      continue
    }
    // 服名与未登录天数取复检结果（客户端只上报 serverId + username）
    targets.push({ ...item, serverName: hit.serverName || '', inactiveDays: hit.inactiveDays })
  }

  // 按服分组 → 服间并行、服内串行
  const byServer = new Map()
  for (const t of targets) {
    if (!byServer.has(t.serverId)) byServer.set(t.serverId, [])
    byServer.get(t.serverId).push(t)
  }

  let deleted = []
  const results = await Promise.allSettled([...byServer.entries()].map(async ([serverId, group]) => {
    const out = []
    for (const t of group) {
      try {
        // 只删账号行：角色存档 / 封禁记录 / QQ 台账全部保留
        const r = await deleteUser({
          username: t.username,
          serverId,
          deleteCharacter: false,
          deleteBans: false,
          unbindQq: false,
          deleteBackendAccount: false
        })
        if (r.ok > 0) {
          out.push({ ...t, ok: true })
        } else {
          const err = r.failed?.[0]?.error || (r.total === 0 ? '目标服务器不可用（未启用或缺少 apiKey）' : '插件未确认删除')
          out.push({ ...t, ok: false, error: err })
        }
      } catch (err) {
        out.push({ ...t, ok: false, error: err.message })
      }
    }
    return out
  }))

  for (const r of results) {
    if (r.status === 'fulfilled') {
      for (const x of r.value) {
        if (x.ok) deleted.push(x)
        else skipped.push({ ...x, reason: x.error || '删除失败' })
      }
    } else {
      skipped.push({ serverId: '', username: '', reason: r.reason?.message || '异常' })
    }
  }

  // 4) 删除后复核：重扫一次全量账号表，确认提交的账号确实已经消失。
  //    为什么不能用「是否还在候选里」复核——组内其他账号被删掉后，残留账号会因关联组
  //    解散而失去 alt 属性、同样退出候选，那并不代表它被删了；必须查「账号是否还存在」。
  //    复核本身失败时保留插件的成功结论，但如实回报 verifyError，不谎报「已核实」。
  let stillPresent = []
  let verifyError = ''
  if (deleted.length > 0) {
    try {
      const all = await getAllFiltered({})
      const present = new Set((all.accounts || []).map(a => keyOf(a.serverId, a.username)))
      const kept = []
      for (const d of deleted) {
        if (present.has(keyOf(d.serverId, d.username))) {
          stillPresent.push({
            ...d,
            ok: false,
            reason: '删除后复核仍存在：删除未生效（请检查该服插件版本与 TShock 日志）'
          })
        } else {
          kept.push(d)
        }
      }
      deleted = kept
      for (const s of stillPresent) skipped.push(s)
    } catch (e) {
      verifyError = e.message
    }
  }

  return {
    executedAt: new Date().toISOString(),
    inactiveDays: fresh.inactiveDays,
    requested: wanted.size,
    deletedCount: deleted.length,
    skippedCount: skipped.length,
    deleted,
    skipped,
    stillPresent,
    verifyError
  }
}

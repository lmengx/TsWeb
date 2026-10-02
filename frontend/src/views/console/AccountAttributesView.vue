<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { get, post, apiRequest } from '../../utils/api.js'
import { getServers, fetchServers } from '../../utils/serverStore.js'
import Loading from '../../components/Loading.vue'

// ═══════════════════════════════════════════════════════════
// 账号属性判定 组合视图（方案A·重构版）
//   顶部：账号总数 / 实际玩家数（关联组合并后）
//   筛选：属性 chips 默认全选，可取消单项，可完全反选
//   图表：主属性占比饼图（当前选中项内） + 属性标签分布（可重叠，保留）
//   名单：明细表（行展开/分页/导出/规则说明）
//   控件：统一美化（自定义 select 箭头 / 聚焦高亮 / chips 色彩）
// ═══════════════════════════════════════════════════════════

const loading = ref(false)
const error = ref('')
const data = ref(null)

// ── 属性字典（key -> 中文标签/色值/说明），分布图/饼图/表格标签统一取色 ──
const ATTRS = [
  { key: 'alt', label: '小号', color: '#f43f5e', desc: '在关联组内 且 累计时长 <= 30 分钟' },
  { key: 'guest', label: '游客账号', color: '#64748b', desc: '非关联账号 且 累计时长 <= 30 分钟' },
  { key: 'churn', label: '流失玩家', color: '#8b5cf6', desc: '累计时长 > 10 小时 且 最后访问距今 >= 10 天' },
  { key: 'new_active', label: '近期新增活跃', color: '#10b981', desc: '注册 <= 14 天 且 最后访问距今 <= 7 天 且 累计时长 > 30 分钟' },
  { key: 'returning', label: '回流玩家', color: '#22d3ee', desc: '曾活跃 且 活跃段间空档 >= 14 天 且 最后访问距今 <= 3 天' },
  { key: 'sustained', label: '持续活跃', color: '#06b6d4', desc: '近 14 天活跃 >= 7 天 且 最后访问距今 <= 3 天' },
  { key: 'dormant', label: '长期沉睡', color: '#a16207', desc: '累计时长 > 10 小时 且 最后访问距今 >= 30 天' },
  { key: 'high_risk_group', label: '高风险关联组', color: '#b91c1c', desc: '所在关联组账号数 >= 3（仅标签，不参与筛选）' },
  { key: 'normal', label: '普通账号', color: '#475569', desc: '未命中任何属性的账号' }
]
const ATTR_MAP = Object.fromEntries(ATTRS.map(a => [a.key, a]))

// 可筛选属性（高风险关联组只作可重叠标签，不参与筛选）
const FILTER_ATTRS = ATTRS.filter(a => a.key !== 'high_risk_group')
const ALL_ATTR_KEYS = FILTER_ATTRS.map(a => a.key)

// ── 筛选状态 ──
const servers = ref([])
const filters = reactive({
  serverId: '',
  attrs: [...ALL_ATTR_KEYS], // 默认全选
  keyword: '',
  minMinutes: '',
  maxMinutes: '',
  activeDays14Min: '',
  sortBy: 'totalMinutes',
  sortDir: 'desc',
  page: 1,
  pageSize: 50
})

// 全选状态（9 项全选 = 不过滤，语义为"全部"）
const allSelected = computed(() => filters.attrs.length === ALL_ATTR_KEYS.length)

// 发送给后端的 attr 参数：
//   全选   -> ''（不过滤，性能好）
//   部分选 -> 逗号连接选中项
//   全不选 -> __none__（后端返回空结果）
const queryParams = computed(() => {
  const p = {
    serverId: filters.serverId,
    keyword: filters.keyword.trim(),
    sortBy: filters.sortBy,
    sortDir: filters.sortDir,
    page: filters.page,
    pageSize: filters.pageSize
  }
  if (filters.attrs.length === 0) p.attr = '__none__'
  else if (!allSelected.value) p.attr = filters.attrs.join(',')
  if (filters.minMinutes !== '') p.minMinutes = filters.minMinutes
  if (filters.maxMinutes !== '') p.maxMinutes = filters.maxMinutes
  if (filters.activeDays14Min !== '') p.activeDays14Min = filters.activeDays14Min
  return p
})

const totalPages = computed(() => {
  if (!data.value) return 1
  return Math.max(1, Math.ceil(data.value.total / filters.pageSize))
})

// ── 顶部统计 ──
const summary = computed(() => data.value?.summary || null)
const filteredSummary = computed(() => data.value?.filteredSummary || null)

const statCards = computed(() => {
  if (!summary.value) return []
  const avgH = (summary.value.avgMinutesExclAlt || 0) / 60
  return [
    { label: '账号总数', value: summary.value.total, sub: `${summary.value.qqBound} 个已绑定 QQ`, color: '#22d3ee' },
    { label: '实际玩家数', value: summary.value.actualPlayers, sub: '关联账号组合并后', color: '#10b981' },
    { label: '实际平均时长（去小号）', value: avgH.toFixed(1), sub: '小时 · 排除小号账号', color: '#f59e0b' },
    { label: '关联账号组', value: summary.value.altGroupCount, sub: `${summary.value.highRiskGroupCount} 个高风险组(>=3账号)`, color: '#8b5cf6' }
  ]
})

// ── 全局属性标签分布（可重叠 · 不受筛选影响；含高风险关联组标签）──
const globalBars = computed(() => {
  const by = summary.value?.byAttribute || {}
  const normalCount = summary.value?.byPrimary?.normal || 0
  const total = Math.max(1, summary.value?.total || 0)
  const items = ATTRS.map(a => ({
    key: a.key,
    label: a.label,
    color: a.color,
    count: a.key === 'normal' ? normalCount : (by[a.key] || 0)
  })).filter(x => x.count > 0)
  for (const x of items) {
    x.pct = Math.round((x.count / total) * 1000) / 10
  }
  return items.sort((a, b) => b.count - a.count)
})

// ── 饼图：选中属性的标签命中占比（未选中属性不占比例；normal 用主属性 normal 计数）──
const pieSlices = computed(() => {
  const byAttr = filteredSummary.value?.byAttribute || {}
  const byPrimary = filteredSummary.value?.byPrimary || {}
  // 选中属性集合；全选 = 全部可筛选属性
  const keys = filters.attrs.length === 0 ? [] : (allSelected.value ? ALL_ATTR_KEYS : filters.attrs)
  const slices = keys
    .map(k => ({
      key: k,
      label: ATTR_MAP[k]?.label || k,
      color: ATTR_MAP[k]?.color || '#475569',
      count: k === 'normal' ? (byPrimary.normal || 0) : (byAttr[k] || 0)
    }))
    .filter(s => s.count > 0)
  const totalHits = slices.reduce((s, x) => s + x.count, 0)
  for (const s of slices) {
    s.pct = totalHits > 0 ? Math.round((s.count / totalHits) * 1000) / 10 : 0
  }
  return slices.sort((a, b) => b.count - a.count)
})

// 选中属性命中总数（饼图中心显示；扇区可重叠，总和可 > 100%）
const pieTotalHits = computed(() => pieSlices.value.reduce((s, x) => s + x.count, 0))

// ── SVG 饼图（每扇区一个 path；单扇区时用 circle 画整圆；hover 等比缩放突出）──
const PIE_CX = 100, PIE_CY = 100, PIE_R = 80

const piePaths = computed(() => {
  const slices = pieSlices.value
  // 单选：整圆（arc 画不出完整圆，改用 circle）
  if (slices.length === 1) {
    const s = slices[0]
    return [{ ...s, isFull: true }]
  }
  let acc = 0
  return slices.map(s => {
    const startPct = acc
    acc += s.pct
    const start = (startPct / 100) * 2 * Math.PI - Math.PI / 2
    const end = (acc / 100) * 2 * Math.PI - Math.PI / 2
    const x1 = PIE_CX + PIE_R * Math.cos(start)
    const y1 = PIE_CY + PIE_R * Math.sin(start)
    const x2 = PIE_CX + PIE_R * Math.cos(end)
    const y2 = PIE_CY + PIE_R * Math.sin(end)
    const largeArc = s.pct > 50 ? 1 : 0
    return {
      ...s,
      isFull: false,
      d: `M ${PIE_CX} ${PIE_CY} L ${x1} ${y1} A ${PIE_R} ${PIE_R} 0 ${largeArc} 1 ${x2} ${y2} Z`
    }
  })
})

// hover 状态：扇区外扩 + 中心详情 + 气泡
const hoveredKey = ref('')
const hovered = computed(() => piePaths.value.find(s => s.key === hoveredKey.value) || null)

// 气泡定位（相对 .pie-wrap 容器，百分比坐标，中心 50%/50% + 扇区方向偏移）
const hoverTip = computed(() => {
  if (!hovered.value) return null
  const s = hovered.value
  const midPct = (() => {
    let acc = 0
    for (const x of piePaths.value) {
      if (x.key === s.key) return (acc + x.pct / 2) / 100
      acc += x.pct
    }
    return 0.5
  })()
  const rad = midPct * 2 * Math.PI - Math.PI / 2
  // 距中心 62% 半径处（饼图外缘略外），气泡锚点
  const x = 50 + 66 * Math.cos(rad)
  const y = 50 + 66 * Math.sin(rad)
  return { x, y, ...s }
})

// ── 服务器 ──
const loadServers = async () => {
  try {
    await fetchServers()
    servers.value = getServers()
  } catch { servers.value = [] }
}

// ── 数据加载 ──
const loadData = async () => {
  loading.value = true
  error.value = ''
  try {
    const qs = new URLSearchParams(queryParams.value).toString()
    const res = await get(`/api/account/attributes?${qs}`)
    const json = await res.json()
    if (json.error) throw new Error(json.error)
    data.value = json
  } catch (err) {
    error.value = err.message
  } finally {
    loading.value = false
  }
}

// ── 筛选交互 ──
const toggleAttr = (key) => {
  const i = filters.attrs.indexOf(key)
  if (i >= 0) filters.attrs.splice(i, 1)
  else filters.attrs.push(key)
  filters.page = 1
  loadData()
}

// 完全反选：选中 ↔ 未选中 反转
const invertAttrs = () => {
  const current = new Set(filters.attrs)
  filters.attrs = ALL_ATTR_KEYS.filter(k => !current.has(k))
  filters.page = 1
  loadData()
}

// 全选 / 清空
const selectAll = () => {
  filters.attrs = [...ALL_ATTR_KEYS]
  filters.page = 1
  loadData()
}
const clearAll = () => {
  filters.attrs = []
  filters.page = 1
  loadData()
}

const clearFilters = () => {
  filters.serverId = ''
  filters.attrs = [...ALL_ATTR_KEYS]
  filters.keyword = ''
  filters.minMinutes = ''
  filters.maxMinutes = ''
  filters.activeDays14Min = ''
  filters.sortBy = 'totalMinutes'
  filters.sortDir = 'desc'
  filters.page = 1
  loadData()
}

const changePage = (p) => {
  if (p < 1 || p > totalPages.value) return
  filters.page = p
  loadData()
}

const setSort = (field) => {
  if (filters.sortBy === field) {
    filters.sortDir = filters.sortDir === 'desc' ? 'asc' : 'desc'
  } else {
    filters.sortBy = field
    filters.sortDir = 'desc'
  }
  loadData()
}

const sortIcon = (field) => {
  if (filters.sortBy !== field) return ''
  return filters.sortDir === 'desc' ? '▼' : '▲'
}

// ── 格式化 ──
const fmtMinutes = (min) => {
  const m = Number(min) || 0
  if (m <= 0) return '0'
  const h = Math.floor(m / 60)
  const mm = m % 60
  return h > 0 ? `${h}h${mm > 0 ? ` ${mm}m` : ''}` : `${mm}m`
}

const fmtDate = (d) => d || '-'

// ── 行展开 ──
const expanded = ref(new Set())
const toggleExpand = (rowKey) => {
  const s = new Set(expanded.value)
  if (s.has(rowKey)) s.delete(rowKey)
  else s.add(rowKey)
  expanded.value = s
}

// ── 规则说明模态框 ──
const showRules = ref(false)
const ruleMeta = ref(null)
const rulesLoading = ref(false)
const openRules = async () => {
  showRules.value = true
  if (ruleMeta.value) return
  rulesLoading.value = true
  try {
    const res = await get('/api/account/meta')
    const json = await res.json()
    ruleMeta.value = json.meta || null
  } catch { ruleMeta.value = null }
  finally { rulesLoading.value = false }
}

// ── CSV 导出 ──
const exporting = ref(false)
const exportCsv = async () => {
  exporting.value = true
  try {
    const qs = new URLSearchParams(queryParams.value).toString()
    const res = await apiRequest(`/api/account/export?${qs}`, { method: 'GET' })
    if (!res.ok) throw new Error('导出失败')
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `account-attributes-${new Date().toISOString().slice(0, 10)}.csv`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  } catch (err) {
    error.value = err.message
  } finally {
    exporting.value = false
  }
}

// ── 清理小号（手动触发 → 名单 → 确认删除）──
//   候选：判定为小号(alt) 且 最后登录距今 >= purgeDays 天
//   保护：管理组账号 + 已绑定 QQ 的账号（后端剔除，不进名单）
//   删除：仅该账号所在那一台服；只删 TShock 账号行，保留角色存档与封禁记录
const showPurge = ref(false)
const purgeScanning = ref(false)
const purgeExecuting = ref(false)
const purgeData = ref(null)
const purgeError = ref('')
const purgeResult = ref(null)
const purgeConfirmed = ref(false)
const purgeSelected = ref(new Set())
const purgeDays = ref(30)

const purgeRows = computed(() => purgeData.value?.candidates || [])
const purgeKey = (c) => `${c.serverId}-${c.username}`
const purgeSelCount = computed(() => purgeRows.value.filter(c => purgeSelected.value.has(purgeKey(c))).length)
const purgeAllSelected = computed(() => purgeRows.value.length > 0 && purgeSelCount.value === purgeRows.value.length)
const purgeMaxItems = computed(() => purgeData.value?.limits?.maxExecuteItems || 500)

// 账号名可能含首尾空白（全角空格 U+3000 / 不换行空格 U+00A0 等）。这类字符在表格里
// 完全看不见，会让人无法理解「名字明明在这儿却删不掉」，因此显性标注出来。
const visName = (n) => String(n ?? '').replace(/^\s+|\s+$/g, m => '·'.repeat(m.length))

const openPurge = () => {
  showPurge.value = true
  purgeResult.value = null
  purgeError.value = ''
  purgeConfirmed.value = false
  scanPurge()
}

// 拉取候选并默认全选（保留 purgeResult，供执行后回显结果）
const fetchPurge = async () => {
  purgeScanning.value = true
  purgeError.value = ''
  try {
    const res = await get(`/api/account/purge/preview?days=${purgeDays.value}`)
    const json = await res.json()
    if (json.error) throw new Error(json.error)
    purgeData.value = json
    purgeSelected.value = new Set((json.candidates || []).map(purgeKey))
  } catch (err) {
    purgeError.value = err.message
    purgeData.value = null
    purgeSelected.value = new Set()
  } finally {
    purgeScanning.value = false
  }
}

// 手动（重新）扫描：先清掉上一次的执行结果
const scanPurge = async () => {
  purgeResult.value = null
  purgeConfirmed.value = false
  await fetchPurge()
}

const togglePurgeRow = (key) => {
  const s = new Set(purgeSelected.value)
  if (s.has(key)) s.delete(key)
  else s.add(key)
  purgeSelected.value = s
  purgeConfirmed.value = false
}

const toggleAllPurge = () => {
  purgeSelected.value = purgeAllSelected.value ? new Set() : new Set(purgeRows.value.map(purgeKey))
  purgeConfirmed.value = false
}

const executePurge = async () => {
  const items = purgeRows.value
    .filter(c => purgeSelected.value.has(purgeKey(c)))
    .map(c => ({ serverId: c.serverId, username: c.username }))
  if (items.length === 0) { purgeError.value = '请先勾选要删除的账号'; return }
  if (items.length > purgeMaxItems.value) {
    purgeError.value = `单次最多删除 ${purgeMaxItems.value} 个账号，请分批执行`
    return
  }
  purgeExecuting.value = true
  purgeError.value = ''
  try {
    const res = await post('/api/account/purge/execute', { days: purgeDays.value, items })
    const json = await res.json()
    if (json.error) throw new Error(json.error)
    purgeResult.value = json
    purgeConfirmed.value = false
    await fetchPurge()  // 重新扫描：名单随删除结果收敛
    await loadData()    // 主列表同步刷新
  } catch (err) {
    purgeError.value = err.message
  } finally {
    purgeExecuting.value = false
  }
}

// ── 初始化 ──
onMounted(async () => {
  await loadServers()
  loadData()
})

const rowAccounts = computed(() => data.value?.accounts || [])
const rowKey = (a) => `${a.serverId}-${a.username}`

const jumpToPlayer = (username) => {
  window.open(`/console/users/${encodeURIComponent(username)}`, '_blank')
}
</script>

<template>
  <div class="account-attr-view">
    <!-- 页头 -->
    <div class="page-header">
      <div>
        <h2>账号属性判定</h2>
        <p class="page-sub">基于关联账号 + 游玩时长的账号分类统计（每服独立计算，后端聚合）</p>
      </div>
      <div class="header-actions">
        <button class="btn ghost" @click="openRules">
          <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>
          规则说明
        </button>
        <button class="btn ghost" @click="loadData" :disabled="loading">
          <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="23 4 23 10 17 10"></polyline><polyline points="1 20 1 14 7 14"></polyline><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path></svg>
          {{ loading ? '刷新中...' : '刷新' }}
        </button>
        <button class="btn primary" @click="exportCsv" :disabled="exporting">
          <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>
          {{ exporting ? '导出中...' : '导出 CSV' }}
        </button>
        <button class="btn purge-btn" @click="openPurge">
          <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"></path><path d="M10 11v6M14 11v6"></path><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"></path></svg>
          清理小号
        </button>
      </div>
    </div>

    <div v-if="error" class="error-message">{{ error }}</div>

    <!-- 顶部统计卡片 -->
    <div v-if="summary" class="stat-grid">
      <div v-for="c in statCards" :key="c.label" class="stat-card">
        <div class="stat-value" :style="{ color: c.color }">{{ c.value }}</div>
        <div class="stat-label">{{ c.label }}</div>
        <div class="stat-sub">{{ c.sub }}</div>
      </div>
    </div>

    <!-- 全局属性标签分布（可重叠 · 不受筛选影响） -->
    <div v-if="summary" class="chart-card global-bars">
      <div class="chart-title">
        属性标签分布（可重叠）
        <span class="chart-tip">一个账号可命中多个属性，占比基于全量账号，不受筛选影响</span>
      </div>
      <div v-if="globalBars.length" class="bar-list">
        <div v-for="b in globalBars" :key="b.key" class="bar-row" :title="ATTR_MAP[b.key]?.desc || ''">
          <span class="bar-label">{{ b.label }}</span>
          <div class="bar-track">
            <div class="bar-fill" :style="{ width: b.pct + '%', background: b.color }"></div>
          </div>
          <span class="bar-count">{{ b.count }}（{{ b.pct }}%）</span>
        </div>
      </div>
      <div v-else class="bar-empty">暂无数据</div>
    </div>

    <!-- 筛选区：属性 chips 默认全选 -->
    <div class="filter-panel">
      <div class="filter-head">
        <span class="filter-title">筛选属性</span>
        <span class="filter-tip">默认全选 = 显示全部；点击取消单项；可完全反选</span>
        <div class="filter-bulk">
          <button class="btn mini" @click="selectAll">全选</button>
          <button class="btn mini" @click="invertAttrs">反选</button>
          <button class="btn mini" @click="clearAll">清空</button>
        </div>
      </div>
      <div class="attr-chips">
        <button
          v-for="a in FILTER_ATTRS"
          :key="a.key"
          class="chip"
          :class="{ active: filters.attrs.includes(a.key) }"
          :style="filters.attrs.includes(a.key) ? { background: a.color + '26', borderColor: a.color, color: '#fff' } : {}"
          :title="a.desc"
          @click="toggleAttr(a.key)"
        >
          <span class="chip-dot" :class="{ active: filters.attrs.includes(a.key) }" :style="{ background: a.color }"></span>
          {{ a.label }}
        </button>
      </div>
      <div class="filter-row">
        <div class="filter-item">
          <label>服务器</label>
          <div class="select-wrap">
            <select v-model="filters.serverId" @change="filters.page = 1; loadData()">
              <option value="">全部服务器</option>
              <option v-for="s in servers" :key="s.id" :value="s.id">{{ s.name }}</option>
            </select>
          </div>
        </div>
        <div class="filter-item">
          <label>关键词</label>
          <input
            v-model="filters.keyword"
            placeholder="账号名搜索"
            @keyup.enter="filters.page = 1; loadData()"
          />
        </div>
        <div class="filter-item">
          <label>累计时长区间(分)</label>
          <div class="range-inputs">
            <input v-model="filters.minMinutes" type="number" placeholder="最小" min="0" />
            <span class="range-sep">-</span>
            <input v-model="filters.maxMinutes" type="number" placeholder="最大" min="0" />
          </div>
        </div>
        <div class="filter-item">
          <label>近14天活跃天数 ≥</label>
          <input v-model="filters.activeDays14Min" type="number" placeholder="如 7" min="0" max="14" />
        </div>
        <div class="filter-item">
          <label>排序</label>
          <div class="select-wrap">
            <select v-model="filters.sortBy" @change="loadData()">
              <option value="totalMinutes">累计时长</option>
              <option value="lastAccess">最后登录</option>
              <option value="registered">注册时间</option>
              <option value="activeDays14">近14天活跃天数</option>
              <option value="relGroupSize">关联组大小</option>
            </select>
          </div>
        </div>
        <div class="filter-item">
          <label>方向</label>
          <div class="select-wrap">
            <select v-model="filters.sortDir" @change="loadData()">
              <option value="desc">降序</option>
              <option value="asc">升序</option>
            </select>
          </div>
        </div>
        <div class="filter-item actions">
          <button class="btn ghost" @click="clearFilters">重置</button>
          <button class="btn primary" @click="filters.page = 1; loadData()">查询</button>
        </div>
      </div>
    </div>

    <!-- 饼图：选中属性占比（未选中属性不占比例） -->
    <div v-if="filteredSummary" class="chart-card pie-card">
      <div class="chart-title">
        选中属性占比
        <span class="chart-tip">仅统计当前选中的属性标签命中占比（命中数 / 选中属性命中总数）；未选中属性不占比例</span>
      </div>
      <div class="pie-wrap">
        <div class="pie">
          <svg viewBox="0 0 200 200" class="pie-svg">
            <g v-for="s in piePaths" :key="s.key">
              <circle
                v-if="s.isFull"
                class="pie-slice"
                :cx="100" :cy="100" :r="80"
                :fill="s.color"
                :class="{ active: hoveredKey === s.key }"
                @mouseenter="hoveredKey = s.key"
                @mouseleave="hoveredKey = ''"
              ></circle>
              <path
                v-else
                class="pie-slice"
                :d="s.d"
                :fill="s.color"
                :class="{ active: hoveredKey === s.key }"
                @mouseenter="hoveredKey = s.key"
                @mouseleave="hoveredKey = ''"
              ></path>
            </g>
          </svg>
          <div class="pie-hole">
            <template v-if="hovered">
              <div class="pie-total" :style="{ color: hovered.color }">{{ hovered.label }}</div>
              <div class="pie-total-sub">{{ hovered.count }} 个 · {{ hovered.pct }}%</div>
            </template>
            <template v-else>
              <div class="pie-total">{{ pieTotalHits }}</div>
              <div class="pie-total-label">属性命中</div>
            </template>
          </div>
          <!-- hover 气泡：显示比例 -->
          <div v-if="hoverTip" class="pie-tip" :style="{ left: hoverTip.x + '%', top: hoverTip.y + '%' }">
            <span class="tip-dot" :style="{ background: hoverTip.color }"></span>
            {{ hoverTip.label }}
            <b>{{ hoverTip.pct }}%</b>
          </div>
        </div>
        <div class="pie-legend">
          <div
            v-for="s in pieSlices"
            :key="s.key"
            class="legend-row"
            :class="{ active: hoveredKey === s.key }"
            @mouseenter="hoveredKey = s.key"
            @mouseleave="hoveredKey = ''"
          >
            <span class="legend-dot" :style="{ background: s.color }"></span>
            <span class="legend-label">{{ s.label }}</span>
            <div class="legend-bar">
              <div class="legend-bar-fill" :style="{ width: s.pct + '%', background: s.color }"></div>
            </div>
            <span class="legend-pct">{{ s.pct }}%</span>
          </div>
          <div v-if="pieSlices.length === 0" class="legend-empty">当前筛选无数据</div>
        </div>
      </div>
    </div>

    <!-- 结果计数 -->
    <div class="result-bar">
      <span v-if="data">
        共 <b>{{ data.total }}</b> 个账号
        <span v-if="data.servers?.length" class="server-status">
          <span v-for="s in data.servers" :key="s.id" class="server-pill" :class="s.status === 'ok' ? 'ok' : 'err'">
            {{ s.name }} {{ s.status === 'ok' ? `(${s.total})` : '离线' }}
          </span>
        </span>
      </span>
      <span v-if="data?.generatedAt" class="generated-at">统计时间 {{ data.generatedAt }}</span>
    </div>

    <!-- 具体名单 -->
    <div class="table-card">
      <Loading v-if="loading" text="加载中..." />
      <div v-else-if="rowAccounts.length === 0" class="empty-state">无匹配账号</div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th class="col-expand"></th>
            <th>账号</th>
            <th>服务器</th>
            <th>QQ</th>
            <th>用户组</th>
            <th class="sortable" @click="setSort('registered')">注册 {{ sortIcon('registered') }}</th>
            <th class="sortable" @click="setSort('lastAccess')">最后登录 {{ sortIcon('lastAccess') }}</th>
            <th class="sortable" @click="setSort('totalMinutes')">累计时长 {{ sortIcon('totalMinutes') }}</th>
            <th>近14天</th>
            <th class="sortable" @click="setSort('activeDays14')">活跃/14天 {{ sortIcon('activeDays14') }}</th>
            <th class="sortable" @click="setSort('relGroupSize')">关联组 {{ sortIcon('relGroupSize') }}</th>
            <th>属性</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="a in rowAccounts" :key="rowKey(a)">
            <tr class="row-main" @click="toggleExpand(rowKey(a))">
              <td class="col-expand">
                <span class="expand-arrow" :class="{ open: expanded.has(rowKey(a)) }">›</span>
              </td>
              <td>
                <a class="user-link" @click.stop="jumpToPlayer(a.username)">{{ a.username }}</a>
              </td>
              <td><span class="server-name">{{ a.serverName }}</span></td>
              <td>{{ a.qq || '-' }}</td>
              <td>{{ a.group || '-' }}</td>
              <td>{{ fmtDate(a.registered) }}</td>
              <td>{{ fmtDate(a.lastAccess) }}</td>
              <td class="num">{{ fmtMinutes(a.totalMinutes) }}</td>
              <td class="num">{{ fmtMinutes(a.recent14dMinutes) }}</td>
              <td class="num">{{ a.activeDays14 }} 天</td>
              <td class="num">{{ a.relGroupSize > 1 ? a.relGroupSize : '-' }}</td>
              <td>
                <div class="attr-tags">
                  <span
                    v-for="k in a.attributes"
                    :key="k"
                    class="attr-tag"
                    :style="{ background: ATTR_MAP[k]?.color || '#475569' }"
                    :title="ATTR_MAP[k]?.desc || ''"
                  >{{ ATTR_MAP[k]?.label || k }}</span>
                  <span v-if="!a.attributes || a.attributes.length === 0" class="attr-tag" :style="{ background: '#475569' }" title="未命中任何属性">普通账号</span>
                </div>
              </td>
            </tr>
            <tr v-if="expanded.has(rowKey(a))" class="row-detail">
              <td colspan="12">
                <div class="detail-grid">
                  <div class="detail-block">
                    <div class="detail-title">游玩时长</div>
                    <div class="detail-items">
                      <span>累计：{{ fmtMinutes(a.totalMinutes) }}</span>
                      <span>近7天：{{ fmtMinutes(a.recent7dMinutes) }}</span>
                      <span>近14天：{{ fmtMinutes(a.recent14dMinutes) }}</span>
                      <span>近30天：{{ fmtMinutes(a.recent30dMinutes) }}</span>
                    </div>
                  </div>
                  <div class="detail-block">
                    <div class="detail-title">活跃天数</div>
                    <div class="detail-items">
                      <span>近7天：{{ a.activeDays7 }} 天</span>
                      <span>近14天：{{ a.activeDays14 }} 天</span>
                      <span>近30天：{{ a.activeDays30 }} 天</span>
                    </div>
                  </div>
                  <div class="detail-block">
                    <div class="detail-title">账号信息</div>
                    <div class="detail-items">
                      <span>ID：{{ a.id }}</span>
                      <span>注册：{{ fmtDate(a.registered) }}</span>
                      <span>最后登录：{{ fmtDate(a.lastAccess) }}</span>
                      <span v-if="a.relGroupSize > 1">关联组 #{{ a.relGroupIndex }}（共 {{ a.relGroupSize }} 个账号）</span>
                      <span v-else>无关联账号</span>
                    </div>
                  </div>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>

      <!-- 分页 -->
      <div v-if="data && data.total > 0" class="pagination">
        <button :disabled="filters.page <= 1" @click="changePage(filters.page - 1)">上一页</button>
        <span>第 {{ filters.page }} / {{ totalPages }} 页（共 {{ data.total }} 条）</span>
        <button :disabled="filters.page >= totalPages" @click="changePage(filters.page + 1)">下一页</button>
      </div>
    </div>

    <!-- 规则说明模态框 -->
    <div v-if="showRules" class="modal-mask" @click.self="showRules = false">
      <div class="modal">
        <div class="modal-header">
          <h3>判定规则说明</h3>
          <button class="modal-close" @click="showRules = false">×</button>
        </div>
        <div class="modal-body">
          <div v-if="rulesLoading" class="modal-loading">加载中...</div>
          <div v-else-if="ruleMeta">
            <div class="meta-line">规则版本：{{ ruleMeta.version }} · 生成时间：{{ ruleMeta.generatedAt }}</div>
            <div v-if="ruleMeta.rules" class="rule-list">
              <div v-for="(desc, key) in ruleMeta.rules" :key="key" class="rule-item">
                <span class="rule-key" :style="{ background: ATTR_MAP[key]?.color || '#475569' }">{{ ATTR_MAP[key]?.label || key }}</span>
                <span class="rule-desc">{{ desc }}</span>
              </div>
            </div>
          </div>
          <div v-else class="modal-loading">无法获取规则（插件未连接）</div>
        </div>
      </div>
    </div>

    <!-- 清理小号模态框（手动触发扫描 → 展示名单 → 勾选确认 → 删除） -->
    <div v-if="showPurge" class="modal-mask" @click.self="showPurge = false">
      <div class="modal purge-modal">
        <div class="modal-header">
          <h3>清理小号</h3>
          <button class="modal-close" @click="showPurge = false">×</button>
        </div>
        <div class="modal-body">
          <div class="purge-rule">
            <div class="purge-rule-line"><b>候选条件：</b>{{ purgeData?.rule?.description || `判定为小号 且 最后登录距今 >= ${purgeDays} 天` }}</div>
            <div class="purge-rule-line"><b>已排除：</b>{{ purgeData?.rule?.protectedGroups || '管理组账号' }}；{{ purgeData?.rule?.qqBound || '已绑定 QQ 的账号' }}</div>
            <div class="purge-rule-line"><b>删除范围：</b>{{ purgeData?.rule?.scope || '仅该账号所在的那一台服；只删 TShock 账号行，保留角色存档与封禁记录' }}</div>
            <div class="purge-rule-line"><b>执行前复检：</b>{{ purgeData?.rule?.recheck || '执行前会按同样的条件复检一次，不再满足条件的账号不会删除' }}</div>
          </div>

          <div class="purge-toolbar">
            <label class="purge-days">
              未登录天数
              <input
                v-model.number="purgeDays"
                type="number"
                min="0"
                step="1"
                class="purge-days-input"
                :disabled="purgeScanning || purgeExecuting"
              />
            </label>
            <button class="btn mini" @click="scanPurge" :disabled="purgeScanning || purgeExecuting">
              {{ purgeScanning ? '扫描中...' : '重新扫描' }}
            </button>
            <span v-if="purgeData" class="purge-stat">
              扫描 {{ purgeData.scanned }} 个小号账号，命中 <b>{{ purgeData.total }}</b> 个候选
            </span>
            <span v-if="purgeData" class="purge-excluded">
              已排除：管理组 {{ purgeData.excluded?.adminGroup || 0 }} · QQ 绑定 {{ purgeData.excluded?.qqBound || 0 }} · 无登录记录 {{ purgeData.excluded?.unknownLastAccess || 0 }} · 未满天数 {{ purgeData.excluded?.recentlyActive || 0 }}
            </span>
          </div>

          <div v-if="purgeError" class="error-message">{{ purgeError }}</div>
          <div v-if="purgeScanning" class="modal-loading">正在扫描候选账号...</div>

          <template v-else-if="purgeData">
            <div v-if="purgeRows.length === 0" class="purge-empty">
              没有符合条件的账号（判定为小号 且 近 {{ purgeData.inactiveDays }} 天未登录）
            </div>
            <template v-else>
              <div v-if="purgeRows.length > purgeMaxItems" class="purge-warn">
                候选 {{ purgeRows.length }} 个，超过单次上限 {{ purgeMaxItems }} 个，请分批勾选执行。
              </div>
              <div class="purge-table-wrap">
                <table class="purge-table">
                  <thead>
                    <tr>
                      <th class="purge-check">
                        <input type="checkbox" :checked="purgeAllSelected" @change="toggleAllPurge" />
                      </th>
                      <th>服务器</th>
                      <th>账号</th>
                      <th>用户组</th>
                      <th>最后登录</th>
                      <th>未登录</th>
                      <th>累计时长</th>
                      <th>近30天</th>
                      <th>关联组</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr
                      v-for="c in purgeRows"
                      :key="purgeKey(c)"
                      :class="{ picked: purgeSelected.has(purgeKey(c)) }"
                    >
                      <td class="purge-check">
                        <input
                          type="checkbox"
                          :checked="purgeSelected.has(purgeKey(c))"
                          @change="togglePurgeRow(purgeKey(c))"
                        />
                      </td>
                      <td>{{ c.serverName }}</td>
                      <td class="purge-name">{{ visName(c.username) }}</td>
                      <td>{{ c.group || '-' }}</td>
                      <td>{{ c.lastAccess || '-' }}</td>
                      <td>{{ c.inactiveDays }} 天</td>
                      <td>{{ fmtMinutes(c.totalMinutes) }}</td>
                      <td>{{ fmtMinutes(c.recent30dMinutes) }}</td>
                      <td>{{ c.relGroupSize || 1 }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>

              <div class="purge-actions">
                <label class="purge-confirm">
                  <input type="checkbox" v-model="purgeConfirmed" :disabled="purgeExecuting" />
                  我确认删除以上勾选的 {{ purgeSelCount }} 个账号（不可撤销）
                </label>
                <button
                  class="btn purge-danger"
                  :disabled="!purgeConfirmed || purgeSelCount === 0 || purgeExecuting"
                  @click="executePurge"
                >
                  {{ purgeExecuting ? '删除中...' : `确认删除 ${purgeSelCount} 个账号` }}
                </button>
              </div>
            </template>
          </template>

          <!-- 执行结果（删除成功与跳过逐条列出，跳过必须给出原因） -->
          <div v-if="purgeResult" class="purge-result">
            <div class="purge-result-head">
              执行完成：请求 {{ purgeResult.requested }} 个，已删除 <b>{{ purgeResult.deletedCount }}</b> 个，跳过 {{ purgeResult.skippedCount }} 个
            </div>
            <!-- 删除后复核：报告了成功但账号其实还在，属于必须显性暴露的异常 -->
            <div v-if="purgeResult.stillPresent?.length" class="purge-verify-warn">
              警告：以下 {{ purgeResult.stillPresent.length }} 个账号插件报告删除成功，但复核后仍存在于账号表中（删除未生效），已从「已删除」中剔除。请检查该服插件版本与 TShock 日志。
            </div>
            <div v-if="purgeResult.verifyError" class="purge-verify-warn">
              复核未完成（{{ purgeResult.verifyError }}）：以上「已删除」仅依据插件返回，未能二次核实。
            </div>
            <div v-if="purgeResult.deleted?.length" class="purge-result-block">
              <div class="purge-result-title">已删除</div>
              <div v-for="(d, i) in purgeResult.deleted" :key="'del' + i" class="purge-result-line ok">
                {{ d.serverName }} · {{ visName(d.username) }}
              </div>
            </div>
            <div v-if="purgeResult.skipped?.length" class="purge-result-block">
              <div class="purge-result-title">已跳过</div>
              <div v-for="(s, i) in purgeResult.skipped" :key="'skip' + i" class="purge-result-line skip">
                {{ s.serverName || '-' }} · {{ visName(s.username) || '-' }}：{{ s.reason }}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.account-attr-view {
  padding: 20px;
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

/* ── 页头 ── */
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: 12px;
}
.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
}
.page-sub {
  margin: 4px 0 0;
  color: var(--text-secondary);
  font-size: 0.85rem;
}
.header-actions {
  display: flex;
  gap: 8px;
}

.error-message {
  background: rgba(244, 63, 94, 0.12);
  color: var(--accent-error);
  padding: 10px 14px;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  border: 1px solid rgba(244, 63, 94, 0.25);
}

/* ── 按钮 ── */
.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 0.88rem;
  cursor: pointer;
  transition: all 0.15s var(--ease-out);
}
.btn:hover:not(:disabled) {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
  box-shadow: 0 0 12px rgba(99, 102, 241, 0.2);
}
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn.primary {
  background: var(--gradient-primary);
  border: none;
  color: #fff;
  font-weight: 500;
}
.btn.primary:hover:not(:disabled) {
  box-shadow: var(--glow-primary);
  color: #fff;
}
.btn.ghost { background: transparent; }
.btn.mini { padding: 4px 12px; font-size: 0.8rem; }

/* ── 顶部统计卡片 ── */
.stat-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 14px;
}
@media (max-width: 1100px) { .stat-grid { grid-template-columns: repeat(2, 1fr); } }
.stat-card {
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  padding: 18px;
  box-shadow: var(--shadow-md);
  transition: all 0.2s var(--ease-out);
}
.stat-card:hover { border-color: var(--border-glow); transform: translateY(-2px); }
.stat-value {
  font-size: 2.2rem;
  font-weight: 700;
  line-height: 1.1;
  font-variant-numeric: tabular-nums;
}
.stat-label {
  color: var(--text-secondary);
  font-size: 0.88rem;
  margin-top: 6px;
  font-weight: 500;
}
.stat-sub {
  color: var(--text-muted);
  font-size: 0.78rem;
  margin-top: 2px;
}

/* ── 筛选区 ── */
.filter-panel {
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-shadow: var(--shadow-md);
}
.filter-head {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.filter-title {
  font-weight: 600;
  color: var(--text-primary);
  font-size: 0.92rem;
}
.filter-tip {
  color: var(--text-muted);
  font-size: 0.78rem;
  flex: 1;
  min-width: 200px;
}
.filter-bulk {
  display: flex;
  gap: 6px;
}

.attr-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 13px;
  border-radius: 999px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-secondary);
  font-size: 0.82rem;
  cursor: pointer;
  transition: all 0.15s var(--ease-out);
}
.chip:hover {
  border-color: var(--accent-primary);
  color: var(--text-primary);
  transform: translateY(-1px);
}
.chip.active {
  color: #fff;
  font-weight: 500;
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.25);
}
.chip-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
  transition: box-shadow 0.15s var(--ease-out);
}
/* 选中时色点加白色描边，避免与同色背景重合 */
.chip-dot.active {
  box-shadow: 0 0 0 2px var(--bg-secondary), 0 0 0 3.5px rgba(255, 255, 255, 0.85);
}

.filter-row {
  display: flex;
  gap: 14px;
  flex-wrap: wrap;
  align-items: flex-end;
  border-top: 1px solid var(--border-light);
  padding-top: 12px;
}
.filter-item {
  display: flex;
  flex-direction: column;
  gap: 5px;
}
.filter-item label {
  font-size: 0.76rem;
  color: var(--text-secondary);
  letter-spacing: 0.02em;
}
.filter-item.actions {
  flex-direction: row;
  align-items: center;
  gap: 8px;
  margin-left: auto;
}

/* ── 美化后的 select / input（统一风格）── */
.select-wrap {
  position: relative;
  display: inline-flex;
}
.select-wrap select {
  appearance: none;
  -webkit-appearance: none;
  padding: 8px 32px 8px 12px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 0.85rem;
  cursor: pointer;
  min-width: 120px;
  transition: all 0.15s var(--ease-out);
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%239aa8c0' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'%3E%3C/polyline%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 10px center;
}
.select-wrap select:hover { border-color: var(--border-glow); }
.select-wrap select:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.2);
}
.select-wrap select option {
  background: var(--bg-secondary);
  color: var(--text-primary);
}

.filter-item input {
  padding: 8px 12px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 0.85rem;
  min-width: 110px;
  transition: all 0.15s var(--ease-out);
}
.filter-item input::placeholder { color: var(--text-muted); }
.filter-item input:hover { border-color: var(--border-glow); }
.filter-item input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.2);
}
.filter-item input[type="number"]::-webkit-outer-spin-button,
.filter-item input[type="number"]::-webkit-inner-spin-button {
  -webkit-appearance: none;
  margin: 0;
}

.range-inputs {
  display: flex;
  align-items: center;
  gap: 6px;
}
.range-inputs input { width: 84px; }
.range-sep { color: var(--text-muted); }

/* ── 图表区 ── */
.chart-card {
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  padding: 16px 18px;
  box-shadow: var(--shadow-md);
}
.chart-title {
  font-weight: 600;
  color: var(--text-primary);
  font-size: 0.9rem;
  margin-bottom: 14px;
}
.chart-tip {
  font-weight: 400;
  color: var(--text-muted);
  font-size: 0.76rem;
  margin-left: 10px;
}
/* 全局可重叠分布（顶部全宽卡片） */
.global-bars { width: 100%; }
/* 饼图卡片 */
.pie-card { width: 100%; }

/* 饼图 */
.pie-wrap {
  display: flex;
  align-items: center;
  gap: 24px;
  flex-wrap: wrap;
}
.pie {
  width: 200px;
  height: 200px;
  flex-shrink: 0;
  position: relative;
  box-shadow: var(--glow-primary);
  border-radius: 50%;
}
.pie-svg {
  width: 100%;
  height: 100%;
  display: block;
}
.pie-slice {
  transition: transform 0.22s var(--ease-out), filter 0.22s var(--ease-out);
  transform-origin: 100px 100px;
  cursor: pointer;
}
.pie-slice:hover,
.pie-slice.active {
  transform: scale(1.04);
  filter: brightness(1.1) saturate(1.05);
}
/* 非 hover 时其它扇区轻微降饱和，突出 hover 块（不位移） */
.pie:hover .pie-slice:not(:hover):not(.active) {
  filter: saturate(0.75);
}
.pie-hole {
  position: absolute;
  inset: 34px;
  border-radius: 50%;
  background: var(--bg-primary);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  pointer-events: none;
  text-align: center;
  padding: 6px;
  overflow: hidden;
}
.pie-total {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
  max-width: 100%;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.pie-total-sub {
  font-size: 0.72rem;
  color: var(--text-secondary);
  margin-top: 3px;
  white-space: nowrap;
}
.pie-total-label {
  font-size: 0.72rem;
  color: var(--text-muted);
  margin-top: 4px;
}
/* hover 气泡 */
.pie-tip {
  position: absolute;
  transform: translate(-50%, -50%);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg);
  padding: 5px 10px;
  font-size: 0.78rem;
  color: var(--text-primary);
  display: flex;
  align-items: center;
  gap: 6px;
  white-space: nowrap;
  pointer-events: none;
  z-index: 5;
  animation: tip-in 0.15s var(--ease-out);
}
.pie-tip b {
  color: var(--accent-cyan);
  font-weight: 600;
}
.tip-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}
@keyframes tip-in {
  from { opacity: 0; transform: translate(-50%, -50%) scale(0.85); }
  to { opacity: 1; transform: translate(-50%, -50%) scale(1); }
}
.pie-legend {
  display: flex;
  flex-direction: column;
  gap: 7px;
  flex: 1;
  min-width: 190px;
}
.legend-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.82rem;
  padding: 3px 6px;
  border-radius: var(--radius-sm);
  cursor: default;
  transition: background 0.15s var(--ease-out);
}
.legend-row.active {
  background: var(--bg-hover);
}
.legend-dot {
  width: 10px;
  height: 10px;
  border-radius: 3px;
  flex-shrink: 0;
}
.legend-label {
  color: var(--text-primary);
  width: 78px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* 比例条 */
.legend-bar {
  flex: 1;
  height: 8px;
  background: var(--bg-tertiary);
  border-radius: 4px;
  overflow: hidden;
  min-width: 40px;
}
.legend-bar-fill {
  height: 100%;
  border-radius: 4px;
  transition: width 0.4s var(--ease-out);
}
.legend-pct {
  color: var(--text-secondary);
  width: 46px;
  text-align: right;
  font-variant-numeric: tabular-nums;
  font-size: 0.78rem;
}
.legend-empty { color: var(--text-muted); font-size: 0.82rem; padding: 12px 0; }

/* 条形图 */
.bar-list { display: flex; flex-direction: column; gap: 7px; }
.bar-row { display: flex; align-items: center; gap: 8px; font-size: 0.8rem; }
.bar-label { width: 86px; text-align: right; color: var(--text-secondary); white-space: nowrap; }
.bar-track {
  flex: 1;
  height: 12px;
  background: var(--bg-tertiary);
  border-radius: 6px;
  overflow: hidden;
}
.bar-fill {
  height: 100%;
  border-radius: 6px;
  transition: width 0.4s var(--ease-out);
  box-shadow: 0 0 8px rgba(0, 0, 0, 0.3);
}
.bar-count {
  width: 96px;
  color: var(--text-secondary);
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
}
.bar-empty { color: var(--text-muted); font-size: 0.82rem; padding: 12px 0; }

/* ── 结果栏 ── */
.result-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 0.85rem;
  color: var(--text-secondary);
}
.server-status { display: inline-flex; gap: 6px; margin-left: 10px; flex-wrap: wrap; }
.server-pill {
  padding: 2px 9px;
  border-radius: 999px;
  font-size: 0.75rem;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
}
.server-pill.ok { color: #10b981; border-color: rgba(16, 185, 129, 0.4); }
.server-pill.err { color: var(--accent-error); border-color: rgba(244, 63, 94, 0.4); }
.generated-at { font-size: 0.75rem; color: var(--text-muted); }

/* ── 表格 ── */
.table-card {
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  padding: 16px;
  box-shadow: var(--shadow-md);
  overflow-x: auto;
}
.data-table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
.data-table th {
  text-align: left;
  padding: 9px 10px;
  color: var(--text-secondary);
  font-weight: 600;
  border-bottom: 1px solid var(--border-color);
  white-space: nowrap;
  background: rgba(99, 102, 241, 0.05);
}
.data-table th.sortable { cursor: pointer; user-select: none; }
.data-table th.sortable:hover { color: var(--accent-primary); }
.data-table td {
  padding: 9px 10px;
  border-bottom: 1px solid var(--border-light);
  white-space: nowrap;
  color: var(--text-primary);
}
.data-table .num { text-align: right; font-variant-numeric: tabular-nums; }
.row-main { cursor: pointer; }
.row-main:hover td { background: var(--bg-hover); }
.row-detail td {
  background: rgba(99, 102, 241, 0.04);
  padding: 14px 16px;
}

.col-expand { width: 28px; }
.expand-arrow {
  display: inline-block;
  transition: transform 0.15s var(--ease-out);
  color: var(--text-muted);
  font-size: 1.1rem;
}
.expand-arrow.open { transform: rotate(90deg); }

.user-link { color: var(--accent-cyan); cursor: pointer; text-decoration: none; }
.user-link:hover { text-decoration: underline; }
.server-name { color: var(--text-secondary); font-size: 0.8rem; }

.attr-tags { display: inline-flex; gap: 4px; flex-wrap: wrap; }
.attr-tag {
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.72rem;
  color: #fff;
  white-space: nowrap;
}

.detail-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; }
@media (max-width: 900px) { .detail-grid { grid-template-columns: 1fr; } }
.detail-title {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--text-secondary);
  margin-bottom: 6px;
}
.detail-items { display: flex; flex-direction: column; gap: 4px; font-size: 0.82rem; }

/* ── 空态 / 分页 ── */
.empty-state {
  padding: 40px;
  text-align: center;
  color: var(--text-muted);
}
.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 14px;
  padding-top: 14px;
  font-size: 0.85rem;
  color: var(--text-secondary);
}
.pagination button {
  padding: 6px 14px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  background: var(--bg-secondary);
  color: var(--text-primary);
  cursor: pointer;
  transition: all 0.15s var(--ease-out);
}
.pagination button:hover:not(:disabled) { border-color: var(--accent-primary); }
.pagination button:disabled { opacity: 0.4; cursor: not-allowed; }

/* ── 模态框 ── */
.modal-mask {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  /* 遮罩留边，配合 .modal 的 max-height 保证模态框永远完整落在视口内 */
  padding: 24px;
  z-index: 1000;
}
.modal {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  width: min(640px, 92vw);
  /* 用 calc 而不是 vh 百分比：不依赖 box-sizing，且与遮罩 24px 内边距严格对应 */
  max-height: calc(100vh - 48px);
  display: flex;
  flex-direction: column;
  /* 关键：把过高的内容收敛到 .modal-body 内部滚动，否则内容会溢出到视口外 */
  overflow: hidden;
  box-shadow: var(--shadow-lg);
}
.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid var(--border-color);
}
.modal-header h3 { margin: 0; font-size: 1.05rem; color: var(--text-primary); }
.modal-close {
  background: none;
  border: none;
  font-size: 1.4rem;
  color: var(--text-secondary);
  cursor: pointer;
  line-height: 1;
}
/* min-height:0 是必须的：flex 子项默认 min-height:auto 会拒绝收缩到内容高度以下，
   于是内容一多就把模态框顶出 max-height，导致模态框被挤出屏幕且无法滚动到位 */
.modal-body { padding: 16px 18px; overflow-y: auto; min-height: 0; flex: 1 1 auto; }
.modal-loading { color: var(--text-secondary); padding: 20px 0; text-align: center; }
.meta-line { font-size: 0.8rem; color: var(--text-muted); margin-bottom: 12px; }
.rule-list { display: flex; flex-direction: column; gap: 10px; }
.rule-item { display: flex; gap: 10px; align-items: flex-start; font-size: 0.85rem; }
.rule-key {
  padding: 2px 10px;
  border-radius: 999px;
  color: #fff;
  font-size: 0.75rem;
  white-space: nowrap;
  flex-shrink: 0;
}
.rule-desc { color: var(--text-primary); line-height: 1.5; }

/* ── 清理小号 ── */
.btn.purge-btn {
  background: transparent;
  border-color: #b91c1c;
  color: #f87171;
}
.btn.purge-btn:hover:not(:disabled) {
  border-color: #ef4444;
  color: #fca5a5;
  box-shadow: 0 0 12px rgba(239, 68, 68, 0.25);
}
.btn.purge-danger {
  background: #b91c1c;
  border-color: #b91c1c;
  color: #fff;
}
.btn.purge-danger:hover:not(:disabled) {
  background: #dc2626;
  border-color: #dc2626;
  color: #fff;
  box-shadow: 0 0 12px rgba(239, 68, 68, 0.35);
}
.modal.purge-modal { width: min(1000px, 95vw); }

.purge-rule {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 12px;
  margin-bottom: 12px;
  border: 1px solid var(--border-color);
  border-left: 3px solid #b91c1c;
  border-radius: var(--radius-md);
  background: var(--bg-tertiary, rgba(148, 163, 184, 0.06));
  font-size: 0.8rem;
  color: var(--text-secondary);
  line-height: 1.5;
}
.purge-rule-line b { color: var(--text-primary); font-weight: 600; }

.purge-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 12px;
  font-size: 0.82rem;
  color: var(--text-secondary);
}
.purge-days { display: inline-flex; align-items: center; gap: 6px; }
.purge-days-input {
  width: 72px;
  padding: 4px 8px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm, 6px);
  background: var(--bg-primary);
  color: var(--text-primary);
  font-size: 0.82rem;
}
.purge-stat b { color: #f87171; }
.purge-excluded { color: var(--text-muted); }

.purge-empty {
  padding: 24px 0;
  text-align: center;
  color: var(--text-muted);
  font-size: 0.88rem;
}
.purge-warn {
  padding: 8px 12px;
  margin-bottom: 10px;
  border-radius: var(--radius-md);
  background: rgba(245, 158, 11, 0.12);
  border: 1px solid rgba(245, 158, 11, 0.35);
  color: #fbbf24;
  font-size: 0.82rem;
}

.purge-table-wrap {
  /* 配合 .modal 的 max-height(100vh-48px)：40vh 给表头/规则/按钮留足空间，避免外层再出滚动条 */
  max-height: 40vh;
  overflow: auto;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
}
.purge-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
}
.purge-table th,
.purge-table td {
  padding: 6px 10px;
  text-align: left;
  white-space: nowrap;
  border-bottom: 1px solid var(--border-color);
}
.purge-table thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--bg-secondary);
  color: var(--text-secondary);
  font-weight: 600;
}
.purge-table tbody tr:hover { background: rgba(148, 163, 184, 0.08); }
.purge-table tbody tr.picked { background: rgba(185, 28, 28, 0.1); }
.purge-name { color: var(--text-primary); font-weight: 500; }
.purge-check { width: 36px; text-align: center; }
.purge-check input { cursor: pointer; }

.purge-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 12px;
}
.purge-confirm {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 0.82rem;
  color: var(--text-secondary);
  cursor: pointer;
}
.purge-confirm input { cursor: pointer; }

.purge-result {
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid var(--border-color);
}
.purge-result-head { font-size: 0.86rem; color: var(--text-primary); margin-bottom: 10px; }
.purge-result-head b { color: #f87171; }
.purge-result-block { margin-bottom: 10px; }
.purge-result-title {
  font-size: 0.78rem;
  color: var(--text-muted);
  margin-bottom: 4px;
}
.purge-result-line {
  font-size: 0.8rem;
  line-height: 1.6;
  padding-left: 10px;
}
.purge-result-line.ok { color: #34d399; }
.purge-result-line.skip { color: #fbbf24; }
.purge-verify-warn {
  margin-bottom: 10px;
  padding: 8px 10px;
  border: 1px solid rgba(248, 113, 113, 0.45);
  border-radius: var(--radius-sm);
  background: rgba(248, 113, 113, 0.1);
  color: #fca5a5;
  font-size: 0.82rem;
  line-height: 1.5;
}
</style>

<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { get, apiRequest } from '../../utils/api.js'
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

// ── 属性字典（key -> 中文标签/色值/说明），chips/饼图/条形图/表格标签统一取色 ──
const ATTRS = [
  { key: 'alt', label: '小号', color: '#f43f5e', desc: '在关联组内 且 累计时长 <= 30 分钟' },
  { key: 'guest', label: '游客账号', color: '#64748b', desc: '非关联账号 且 累计时长 <= 30 分钟' },
  { key: 'churn', label: '流失玩家', color: '#8b5cf6', desc: '累计时长 > 10 小时 且 最后访问距今 >= 10 天' },
  { key: 'new_active', label: '近期新增活跃', color: '#10b981', desc: '注册 <= 14 天 且 最后访问距今 <= 7 天 且 累计时长 > 30 分钟' },
  { key: 'returning', label: '回流玩家', color: '#22d3ee', desc: '曾活跃 且 活跃段间空档 >= 14 天 且 最后访问距今 <= 3 天' },
  { key: 'sustained', label: '持续活跃', color: '#06b6d4', desc: '近 14 天活跃 >= 7 天 且 最后访问距今 <= 3 天' },
  { key: 'dormant', label: '长期沉睡', color: '#a16207', desc: '累计时长 > 10 小时 且 最后访问距今 >= 30 天' },
  { key: 'high_risk_group', label: '高风险关联组', color: '#b91c1c', desc: '所在关联组账号数 >= 3' },
  { key: 'normal', label: '普通账号', color: '#475569', desc: '未命中任何属性的账号' }
]
const ATTR_MAP = Object.fromEntries(ATTRS.map(a => [a.key, a]))
const ALL_ATTR_KEYS = ATTRS.map(a => a.key)

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
  return [
    { label: '账号总数', value: summary.value.total, sub: `${summary.value.qqBound} 个已绑定 QQ`, color: '#22d3ee' },
    { label: '实际玩家数', value: summary.value.actualPlayers, sub: '关联账号组合并后', color: '#10b981' },
    { label: '关联账号组', value: summary.value.altGroupCount, sub: `${summary.value.highRiskGroupCount} 个高风险组(>=3账号)`, color: '#8b5cf6' },
    { label: '当前筛选', value: filteredSummary.value?.total ?? 0, sub: '选中项目内账号数', color: '#f59e0b' }
  ]
})

// ── 饼图（主属性占比，互斥分区）──
const pieSlices = computed(() => {
  const by = filteredSummary.value?.byPrimary || {}
  const total = Math.max(1, filteredSummary.value?.total || 0)
  // 只显示当前选中的主属性类别；normal 仅在全选或选中 normal 时显示
  const keys = filters.attrs.length === 0 ? [] : (allSelected.value ? ALL_ATTR_KEYS : filters.attrs)
  const slices = keys
    .filter(k => by[k])
    .map(k => ({
      key: k,
      label: ATTR_MAP[k]?.label || k,
      color: ATTR_MAP[k]?.color || '#475569',
      count: by[k],
      pct: Math.round((by[k] / total) * 1000) / 10
    }))
    .sort((a, b) => b.count - a.count)
  return slices
})

const pieStyle = computed(() => {
  const slices = pieSlices.value
  if (slices.length === 0) return {}
  let acc = 0
  const stops = slices.map(s => {
    const from = acc
    acc += s.pct
    return `${s.color} ${from}% ${acc}%`
  })
  // 不满 100% 时补满底色（理论上 byPrimary 覆盖全部，防御）
  if (acc < 100) stops.push(`#1e293b ${acc}% 100%`)
  return { background: `conic-gradient(${stops.join(', ')})` }
})

// ── 属性标签分布（可重叠，保留）──
const tagBars = computed(() => {
  const by = filteredSummary.value?.byAttribute || {}
  const total = Math.max(1, filteredSummary.value?.total || 0)
  const keys = filters.attrs.length === 0 ? [] : (allSelected.value ? ALL_ATTR_KEYS : filters.attrs)
  return keys
    .filter(k => by[k])
    .map(k => ({
      key: k,
      label: ATTR_MAP[k]?.label || k,
      color: ATTR_MAP[k]?.color || '#475569',
      count: by[k],
      pct: Math.round((by[k] / total) * 1000) / 10
    }))
    .sort((a, b) => b.count - a.count)
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
          v-for="a in ATTRS"
          :key="a.key"
          class="chip"
          :class="{ active: filters.attrs.includes(a.key) }"
          :style="filters.attrs.includes(a.key) ? { background: a.color, borderColor: a.color } : {}"
          :title="a.desc"
          @click="toggleAttr(a.key)"
        >
          <span class="chip-dot" :style="{ background: a.color }"></span>
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

    <!-- 图表区：饼图 + 可重叠分布 -->
    <div v-if="filteredSummary" class="chart-grid">
      <div class="chart-card">
        <div class="chart-title">主属性占比（当前选中项内）</div>
        <div class="pie-wrap">
          <div class="pie" :style="pieStyle">
            <div class="pie-hole">
              <div class="pie-total">{{ filteredSummary.total }}</div>
              <div class="pie-total-label">账号</div>
            </div>
          </div>
          <div class="pie-legend">
            <div v-for="s in pieSlices" :key="s.key" class="legend-row">
              <span class="legend-dot" :style="{ background: s.color }"></span>
              <span class="legend-label">{{ s.label }}</span>
              <span class="legend-count">{{ s.count }}</span>
              <span class="legend-pct">{{ s.pct }}%</span>
            </div>
            <div v-if="pieSlices.length === 0" class="legend-empty">当前筛选无数据</div>
          </div>
        </div>
      </div>
      <div class="chart-card">
        <div class="chart-title">属性标签分布（可重叠）</div>
        <div v-if="tagBars.length" class="bar-list">
          <div v-for="b in tagBars" :key="b.key" class="bar-row" :title="ATTR_MAP[b.key]?.desc || ''">
            <span class="bar-label">{{ b.label }}</span>
            <div class="bar-track">
              <div class="bar-fill" :style="{ width: b.pct + '%', background: b.color }"></div>
            </div>
            <span class="bar-count">{{ b.count }}（{{ b.pct }}%）</span>
          </div>
        </div>
        <div v-else class="bar-empty">当前筛选无数据</div>
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
.chart-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
}
@media (max-width: 900px) { .chart-grid { grid-template-columns: 1fr; } }
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

/* 饼图 */
.pie-wrap {
  display: flex;
  align-items: center;
  gap: 24px;
  flex-wrap: wrap;
}
.pie {
  width: 180px;
  height: 180px;
  border-radius: 50%;
  flex-shrink: 0;
  position: relative;
  box-shadow: var(--glow-primary);
}
.pie-hole {
  position: absolute;
  inset: 32px;
  border-radius: 50%;
  background: var(--bg-primary);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
}
.pie-total {
  font-size: 1.8rem;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1;
}
.pie-total-label {
  font-size: 0.72rem;
  color: var(--text-muted);
  margin-top: 4px;
}
.pie-legend {
  display: flex;
  flex-direction: column;
  gap: 7px;
  flex: 1;
  min-width: 180px;
}
.legend-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.82rem;
}
.legend-dot {
  width: 10px;
  height: 10px;
  border-radius: 3px;
  flex-shrink: 0;
}
.legend-label { color: var(--text-primary); flex: 1; }
.legend-count { color: var(--text-secondary); font-variant-numeric: tabular-nums; }
.legend-pct {
  color: var(--text-muted);
  width: 48px;
  text-align: right;
  font-variant-numeric: tabular-nums;
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
  z-index: 1000;
}
.modal {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  width: min(640px, 92vw);
  max-height: 80vh;
  display: flex;
  flex-direction: column;
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
.modal-body { padding: 16px 18px; overflow-y: auto; }
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
</style>

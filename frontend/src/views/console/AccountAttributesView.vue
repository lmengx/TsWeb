<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { get, apiRequest } from '../../utils/api.js'
import { getServers, fetchServers } from '../../utils/serverStore.js'
import Loading from '../../components/Loading.vue'

// ═══════════════════════════════════════════════════════════
// 账号属性判定 组合视图（方案A）
//   顶部概览卡片 → 多维筛选 → 属性 Tab 明细表（可展开行）→ CSV 导出 / 规则说明
//   多维可重复：服务器 × 属性多选 × 关键词 × 累计时长区间 × 近14天活跃天数
//   语义明确：判定规则说明模态框 + 属性标签颜色编码 + 列 tooltip
// ═══════════════════════════════════════════════════════════

const loading = ref(false)
const error = ref('')
const data = ref(null)

// ── 属性字典（key -> 中文标签/颜色/说明）──
const ATTRS = [
  { key: 'alt', label: '小号', color: 'red', desc: '在关联组内 且 累计时长 <= 30 分钟' },
  { key: 'guest', label: '游客账号', color: 'gray', desc: '非关联账号 且 累计时长 <= 30 分钟' },
  { key: 'churn', label: '流失玩家', color: 'purple', desc: '累计时长 > 10 小时 且 最后访问距今 >= 10 天' },
  { key: 'new_active', label: '近期新增活跃', color: 'green', desc: '注册 <= 14 天 且 最后访问距今 <= 7 天 且 累计时长 > 30 分钟' },
  { key: 'returning', label: '回流玩家', color: 'blue', desc: '曾活跃 且 活跃段间空档 >= 14 天 且 最后访问距今 <= 3 天' },
  { key: 'sustained', label: '持续活跃', color: 'teal', desc: '近 14 天活跃 >= 7 天 且 最后访问距今 <= 3 天' },
  { key: 'dormant', label: '长期沉睡', color: 'brown', desc: '累计时长 > 10 小时 且 最后访问距今 >= 30 天' },
  { key: 'high_risk_group', label: '高风险关联组', color: 'darkred', desc: '所在关联组账号数 >= 3' }
]
const ATTR_MAP = Object.fromEntries(ATTRS.map(a => [a.key, a]))

// ── 筛选状态 ──
const servers = ref([])
const filters = reactive({
  serverId: '',
  attrs: [],          // 多选属性（空 = 全部）
  keyword: '',
  minMinutes: '',
  maxMinutes: '',
  activeDays14Min: '',
  sortBy: 'totalMinutes',
  sortDir: 'desc',
  page: 1,
  pageSize: 50
})

const queryParams = computed(() => {
  const p = {
    serverId: filters.serverId,
    attr: filters.attrs.join(','),
    keyword: filters.keyword.trim(),
    sortBy: filters.sortBy,
    sortDir: filters.sortDir,
    page: filters.page,
    pageSize: filters.pageSize
  }
  if (filters.minMinutes !== '') p.minMinutes = filters.minMinutes
  if (filters.maxMinutes !== '') p.maxMinutes = filters.maxMinutes
  if (filters.activeDays14Min !== '') p.activeDays14Min = filters.activeDays14Min
  return p
})

const totalPages = computed(() => {
  if (!data.value) return 1
  return Math.max(1, Math.ceil(data.value.total / filters.pageSize))
})

// ── 概览统计 ──
const summary = computed(() => data.value?.summary || null)
const summaryCards = computed(() => {
  if (!summary.value) return []
  return [
    { label: '账号总数', value: summary.value.total, sub: `${summary.value.qqBound} 个已绑定 QQ` },
    { label: '关联账号组', value: summary.value.altGroupCount, sub: `${summary.value.highRiskGroupCount} 个高风险组(>=3账号)` },
    { label: '主属性分布', value: '', bars: primaryBars.value },
    { label: '多标签分布', value: '', bars: attributeBars.value }
  ]
})

const primaryBars = computed(() => {
  if (!summary.value) return []
  const by = summary.value.byPrimary || {}
  const total = Math.max(1, summary.value.total)
  return ATTRS.filter(a => by[a.key]).map(a => ({
    label: a.label,
    key: a.key,
    count: by[a.key],
    pct: Math.round((by[a.key] / total) * 1000) / 10
  })).sort((x, y) => y.count - x.count)
})

const attributeBars = computed(() => {
  if (!summary.value) return []
  const by = summary.value.byAttribute || {}
  const total = Math.max(1, summary.value.total)
  return ATTRS.filter(a => by[a.key]).map(a => ({
    label: a.label,
    key: a.key,
    count: by[a.key],
    pct: Math.round((by[a.key] / total) * 1000) / 10
  })).sort((x, y) => y.count - x.count)
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

const clearFilters = () => {
  filters.serverId = ''
  filters.attrs = []
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

    <!-- 概览统计 -->
    <div v-if="summary" class="summary-grid">
      <div class="summary-card">
        <div class="summary-value">{{ summary.total }}</div>
        <div class="summary-label">账号总数</div>
        <div class="summary-sub">{{ summary.qqBound }} 个已绑定 QQ</div>
      </div>
      <div class="summary-card">
        <div class="summary-value">{{ summary.altGroupCount }}</div>
        <div class="summary-label">关联账号组</div>
        <div class="summary-sub">{{ summary.highRiskGroupCount }} 个高风险组（>=3账号）</div>
      </div>
      <div class="summary-card wide">
        <div class="summary-label">主属性分布</div>
        <div v-if="primaryBars.length" class="bar-list">
          <div v-for="b in primaryBars" :key="b.key" class="bar-row" :title="ATTR_MAP[b.key]?.desc || ''">
            <span class="bar-label">{{ b.label }}</span>
            <div class="bar-track">
              <div class="bar-fill" :class="`fill-${b.key}`" :style="{ width: b.pct + '%' }"></div>
            </div>
            <span class="bar-count">{{ b.count }}（{{ b.pct }}%）</span>
          </div>
        </div>
        <div v-else class="bar-empty">暂无数据</div>
      </div>
      <div class="summary-card wide">
        <div class="summary-label">属性标签分布（可重叠）</div>
        <div v-if="attributeBars.length" class="bar-list">
          <div v-for="b in attributeBars" :key="b.key" class="bar-row" :title="ATTR_MAP[b.key]?.desc || ''">
            <span class="bar-label">{{ b.label }}</span>
            <div class="bar-track">
              <div class="bar-fill" :class="`fill-${b.key}`" :style="{ width: b.pct + '%' }"></div>
            </div>
            <span class="bar-count">{{ b.count }}（{{ b.pct }}%）</span>
          </div>
        </div>
        <div v-else class="bar-empty">暂无数据</div>
      </div>
    </div>

    <!-- 筛选区 -->
    <div class="filter-panel">
      <div class="filter-row">
        <div class="filter-item">
          <label>服务器</label>
          <select v-model="filters.serverId" @change="filters.page = 1; loadData()">
            <option value="">全部服务器</option>
            <option v-for="s in servers" :key="s.id" :value="s.id">{{ s.name }}</option>
          </select>
        </div>
        <div class="filter-item grow">
          <label>属性</label>
          <div class="attr-chips">
            <button
              v-for="a in ATTRS"
              :key="a.key"
              class="chip"
              :class="['chip-' + a.color, { active: filters.attrs.includes(a.key) }]"
              :title="a.desc"
              @click="toggleAttr(a.key)"
            >{{ a.label }}</button>
            <button v-if="filters.attrs.length" class="chip chip-clear" @click="filters.attrs = []; filters.page = 1; loadData()">清空</button>
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
      </div>
      <div class="filter-row">
        <div class="filter-item">
          <label>累计时长区间(分)</label>
          <div class="range-inputs">
            <input v-model="filters.minMinutes" type="number" placeholder="最小" min="0" />
            <span>-</span>
            <input v-model="filters.maxMinutes" type="number" placeholder="最大" min="0" />
          </div>
        </div>
        <div class="filter-item">
          <label>近14天活跃天数 >=</label>
          <input v-model="filters.activeDays14Min" type="number" placeholder="如 7" min="0" max="14" />
        </div>
        <div class="filter-item">
          <label>排序</label>
          <select v-model="filters.sortBy" @change="loadData()">
            <option value="totalMinutes">累计时长</option>
            <option value="lastAccess">最后登录</option>
            <option value="registered">注册时间</option>
            <option value="activeDays14">近14天活跃天数</option>
            <option value="relGroupSize">关联组大小</option>
          </select>
          <select v-model="filters.sortDir" @change="loadData()" class="sort-dir">
            <option value="desc">降序</option>
            <option value="asc">升序</option>
          </select>
        </div>
        <div class="filter-item actions">
          <button class="btn ghost" @click="clearFilters">重置</button>
          <button class="btn primary" @click="filters.page = 1; loadData()">查询</button>
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

    <!-- 明细表 -->
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
                    :class="`tag-${k}`"
                    :title="ATTR_MAP[k]?.desc || ''"
                  >{{ ATTR_MAP[k]?.label || k }}</span>
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
                <span class="rule-key" :class="`tag-${key}`">{{ ATTR_MAP[key]?.label || key }}</span>
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
  background: var(--bg-danger, rgba(220, 60, 60, 0.12));
  color: var(--text-danger, #e05555);
  padding: 10px 14px;
  border-radius: var(--radius-md, 8px);
  font-size: 0.9rem;
}

/* ── 按钮 ── */
.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md, 8px);
  background: var(--bg-card);
  color: var(--text-primary);
  font-size: 0.88rem;
  cursor: pointer;
  transition: all 0.15s;
}
.btn:hover:not(:disabled) { border-color: var(--accent, #4a9eff); color: var(--accent, #4a9eff); }
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn.primary { background: var(--accent, #4a9eff); border-color: var(--accent, #4a9eff); color: #fff; }
.btn.primary:hover:not(:disabled) { background: var(--accent-hover, #3a8eef); color: #fff; }
.btn.ghost { background: transparent; }

/* ── 概览 ── */
.summary-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
@media (max-width: 1100px) {
  .summary-grid { grid-template-columns: repeat(2, 1fr); }
}
.summary-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-xl, 14px);
  padding: 16px;
  box-shadow: var(--shadow-md);
  min-width: 0;
}
.summary-card.wide { grid-column: span 1; }
@media (min-width: 1100px) {
  .summary-card.wide { grid-column: span 1; }
}
.summary-value {
  font-size: 2rem;
  font-weight: 700;
  color: var(--accent, #4a9eff);
  line-height: 1.1;
}
.summary-label {
  color: var(--text-secondary);
  font-size: 0.85rem;
  margin-top: 4px;
}
.summary-sub {
  color: var(--text-tertiary, #888);
  font-size: 0.78rem;
  margin-top: 2px;
}
.bar-list { display: flex; flex-direction: column; gap: 5px; margin-top: 8px; }
.bar-row { display: flex; align-items: center; gap: 8px; font-size: 0.78rem; }
.bar-label { width: 72px; text-align: right; color: var(--text-secondary); white-space: nowrap; }
.bar-track { flex: 1; height: 12px; background: var(--bg-elevated, rgba(128,128,128,0.15)); border-radius: 6px; overflow: hidden; }
.bar-fill { height: 100%; border-radius: 6px; transition: width 0.4s; }
.bar-count { width: 88px; color: var(--text-secondary); white-space: nowrap; }
.bar-empty { color: var(--text-tertiary, #888); font-size: 0.8rem; padding: 8px 0; }

.fill-alt { background: #e05555; }
.fill-guest { background: #8a8a8a; }
.fill-churn { background: #9a5cd6; }
.fill-new_active { background: #3fae5a; }
.fill-returning { background: #4a9eff; }
.fill-sustained { background: #2bb8a8; }
.fill-dormant { background: #8a6d3b; }
.fill-high_risk_group { background: #b32020; }

/* ── 筛选区 ── */
.filter-panel {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-xl, 14px);
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  box-shadow: var(--shadow-md);
}
.filter-row { display: flex; gap: 16px; flex-wrap: wrap; align-items: flex-end; }
.filter-item { display: flex; flex-direction: column; gap: 4px; }
.filter-item.grow { flex: 1; min-width: 260px; }
.filter-item label { font-size: 0.78rem; color: var(--text-secondary); }
.filter-item select, .filter-item input {
  padding: 7px 10px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md, 8px);
  background: var(--bg-input, var(--bg-elevated, #fff));
  color: var(--text-primary);
  font-size: 0.85rem;
  min-width: 110px;
}
.filter-item .sort-dir { min-width: 76px; }
.range-inputs { display: flex; align-items: center; gap: 6px; }
.range-inputs input { width: 84px; }
.filter-item.actions { flex-direction: row; align-items: center; gap: 8px; margin-left: auto; }

.attr-chips { display: flex; flex-wrap: wrap; gap: 6px; }
.chip {
  padding: 4px 10px;
  border-radius: 20px;
  border: 1px solid var(--border-light);
  background: var(--bg-elevated, transparent);
  color: var(--text-secondary);
  font-size: 0.8rem;
  cursor: pointer;
  transition: all 0.15s;
}
.chip.active { color: #fff; border-color: transparent; }
.chip-red.active { background: #e05555; }
.chip-gray.active { background: #8a8a8a; }
.chip-purple.active { background: #9a5cd6; }
.chip-green.active { background: #3fae5a; }
.chip-blue.active { background: #4a9eff; }
.chip-teal.active { background: #2bb8a8; }
.chip-brown.active { background: #8a6d3b; }
.chip-darkred.active { background: #b32020; }
.chip-clear { border-style: dashed; }

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
  padding: 2px 8px;
  border-radius: 12px;
  font-size: 0.75rem;
  border: 1px solid var(--border-light);
}
.server-pill.ok { color: #3fae5a; border-color: rgba(63,174,90,0.4); }
.server-pill.err { color: #e05555; border-color: rgba(224,85,85,0.4); }
.generated-at { font-size: 0.75rem; color: var(--text-tertiary, #888); }

/* ── 表格 ── */
.table-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-xl, 14px);
  padding: 16px;
  box-shadow: var(--shadow-md);
  overflow-x: auto;
}
.data-table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
.data-table th {
  text-align: left;
  padding: 8px 10px;
  color: var(--text-secondary);
  font-weight: 600;
  border-bottom: 1px solid var(--border-light);
  white-space: nowrap;
}
.data-table th.sortable { cursor: pointer; user-select: none; }
.data-table th.sortable:hover { color: var(--accent, #4a9eff); }
.data-table td { padding: 8px 10px; border-bottom: 1px solid var(--border-light); white-space: nowrap; }
.data-table .num { text-align: right; font-variant-numeric: tabular-nums; }
.row-main { cursor: pointer; }
.row-main:hover td { background: var(--bg-elevated, rgba(128,128,128,0.06)); }
.row-detail td { background: var(--bg-elevated, rgba(128,128,128,0.04)); padding: 14px 16px; }

.col-expand { width: 28px; }
.expand-arrow {
  display: inline-block;
  transition: transform 0.15s;
  color: var(--text-tertiary, #888);
  font-size: 1.1rem;
}
.expand-arrow.open { transform: rotate(90deg); }

.user-link { color: var(--accent, #4a9eff); cursor: pointer; text-decoration: none; }
.user-link:hover { text-decoration: underline; }
.server-name { color: var(--text-secondary); font-size: 0.8rem; }

.attr-tags { display: inline-flex; gap: 4px; flex-wrap: wrap; }
.attr-tag {
  padding: 2px 7px;
  border-radius: 10px;
  font-size: 0.72rem;
  color: #fff;
  white-space: nowrap;
}
.tag-alt { background: #e05555; }
.tag-guest { background: #8a8a8a; }
.tag-churn { background: #9a5cd6; }
.tag-new_active { background: #3fae5a; }
.tag-returning { background: #4a9eff; }
.tag-sustained { background: #2bb8a8; }
.tag-dormant { background: #8a6d3b; }
.tag-high_risk_group { background: #b32020; }
.tag-normal { background: #666; }

.detail-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; }
@media (max-width: 900px) { .detail-grid { grid-template-columns: 1fr; } }
.detail-title { font-size: 0.8rem; font-weight: 600; color: var(--text-secondary); margin-bottom: 6px; }
.detail-items { display: flex; flex-direction: column; gap: 4px; font-size: 0.82rem; }

/* ── 空态 / 分页 ── */
.empty-state {
  padding: 40px;
  text-align: center;
  color: var(--text-tertiary, #888);
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
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md, 8px);
  background: var(--bg-card);
  color: var(--text-primary);
  cursor: pointer;
}
.pagination button:disabled { opacity: 0.4; cursor: not-allowed; }

/* ── 模态框 ── */
.modal-mask {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}
.modal {
  background: var(--bg-card);
  border-radius: var(--radius-xl, 14px);
  width: min(640px, 92vw);
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  box-shadow: var(--shadow-lg, 0 12px 40px rgba(0,0,0,0.3));
}
.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid var(--border-light);
}
.modal-header h3 { margin: 0; font-size: 1.05rem; }
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
.meta-line { font-size: 0.8rem; color: var(--text-tertiary, #888); margin-bottom: 12px; }
.rule-list { display: flex; flex-direction: column; gap: 10px; }
.rule-item { display: flex; gap: 10px; align-items: flex-start; font-size: 0.85rem; }
.rule-key { padding: 2px 8px; border-radius: 10px; color: #fff; font-size: 0.75rem; white-space: nowrap; }
.rule-desc { color: var(--text-primary); line-height: 1.5; }
</style>

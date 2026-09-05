<script setup>
import { ref, onMounted, computed } from 'vue'
import { apiRequest } from '../../utils/api.js'
import Loading from '../../components/Loading.vue'

// ═══════════════════════════════════════════════════════════
// 状态
// ═══════════════════════════════════════════════════════════
const loading = ref(false)
const error = ref('')

const stats = ref({ byLevel: {}, byCategory: {}, byEvent: {}, todayTotal: 0, recentAlerts: 0 })
const eventDict = ref([])

const rows = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(50)
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

// 筛选条件
const filters = ref({
  level: '',
  event: '',
  category: '',
  actor: '',
  timeFrom: '',
  timeTo: '',
  q: ''
})

const levelColor = (level) => {
  if (level === 'error') return '#ef4444'
  if (level === 'warn') return '#f59e0b'
  return '#22c55e'
}

const formatTime = (ts) => {
  if (!ts) return ''
  const d = new Date(ts)
  return d.toLocaleString('zh-CN', { hour12: false })
}

const detailText = (entry) => {
  if (!entry.detail) return ''
  try { return JSON.stringify(entry.detail) } catch { return '' }
}

// ═══════════════════════════════════════════════════════════
// 数据加载
// ═══════════════════════════════════════════════════════════
const buildQuery = () => {
  const p = new URLSearchParams()
  const f = filters.value
  if (f.level) p.set('level', f.level)
  if (f.event) p.set('event', f.event)
  if (f.category) p.set('category', f.category)
  if (f.actor) p.set('actor', f.actor)
  if (f.timeFrom) p.set('timeFrom', new Date(f.timeFrom).toISOString())
  if (f.timeTo) p.set('timeTo', new Date(new Date(f.timeTo).getTime() + 86400000).toISOString())
  if (f.q) p.set('q', f.q)
  p.set('page', page.value)
  p.set('pageSize', pageSize.value)
  return p.toString()
}

const loadLogs = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await apiRequest(`/api/audit/logs?${buildQuery()}`, { method: 'GET' })
    if (res.ok) {
      const data = await res.json()
      rows.value = data.rows || []
      total.value = data.total || 0
    } else {
      error.value = '加载失败'
    }
  } catch (e) { error.value = e.message } finally { loading.value = false }
}

const loadStats = async () => {
  try {
    const res = await apiRequest('/api/audit/stats', { method: 'GET' })
    if (res.ok) stats.value = await res.json()
  } catch { /* 静默 */ }
}

const loadEvents = async () => {
  try {
    const res = await apiRequest('/api/audit/events', { method: 'GET' })
    if (res.ok) eventDict.value = (await res.json()).events || []
  } catch { /* 静默 */ }
}

const search = () => { page.value = 1; loadLogs() }
const resetFilters = () => {
  filters.value = { level: '', event: '', category: '', actor: '', timeFrom: '', timeTo: '', q: '' }
  search()
}
const gotoPage = (p) => { page.value = p; loadLogs() }

const categories = computed(() => [...new Set(eventDict.value.map(e => e.category))])

onMounted(() => {
  loadLogs()
  loadStats()
  loadEvents()
})
</script>

<template>
  <div class="audit-content">
    <div class="section-header">
      <h2>系统日志（审计）</h2>
      <span class="audit-hint">仅记录后端操作，日志永久保留且不可删除</span>
    </div>

    <div v-if="error" class="flash error">{{ error }}</div>

    <!-- ══════════ 统计卡片 ══════════ -->
    <div class="stat-grid">
      <div class="stat-card">
        <div class="stat-num">{{ stats.todayTotal || 0 }}</div>
        <div class="stat-label">近 24h 事件</div>
      </div>
      <div class="stat-card warn">
        <div class="stat-num">{{ stats.byLevel?.warn || 0 }}</div>
        <div class="stat-label">警告</div>
      </div>
      <div class="stat-card error">
        <div class="stat-num">{{ stats.byLevel?.error || 0 }}</div>
        <div class="stat-label">错误</div>
      </div>
      <div class="stat-card alert">
        <div class="stat-num">{{ stats.recentAlerts || 0 }}</div>
        <div class="stat-label">近 24h 告警</div>
      </div>
    </div>

    <!-- ══════════ 筛选栏 ══════════ -->
    <div class="filter-bar">
      <select v-model="filters.level">
        <option value="">全部级别</option>
        <option value="info">info</option>
        <option value="warn">warn</option>
        <option value="error">error</option>
      </select>
      <select v-model="filters.category">
        <option value="">全部分类</option>
        <option v-for="c in categories" :key="c" :value="c">{{ c }}</option>
      </select>
      <select v-model="filters.event">
        <option value="">全部事件</option>
        <option v-for="e in eventDict" :key="e.event" :value="e.event">{{ e.event }}（{{ e.title }}）</option>
      </select>
      <input v-model="filters.actor" placeholder="操作者" />
      <input v-model="filters.timeFrom" type="date" />
      <span class="date-sep">~</span>
      <input v-model="filters.timeTo" type="date" />
      <input v-model="filters.q" placeholder="关键字搜索" @keyup.enter="search" />
      <button class="search-btn" @click="search">查询</button>
      <button class="reset-btn" @click="resetFilters">重置</button>
    </div>

    <!-- ══════════ 日志表格（只读，无删除） ══════════ -->
    <div class="table-wrap">
      <table class="audit-table">
        <thead>
          <tr>
            <th>时间</th>
            <th>级别</th>
            <th>事件</th>
            <th>分类</th>
            <th>操作者</th>
            <th>服务器</th>
            <th>详情</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="loading"><td colspan="7" class="td-center"><Loading size="sm" text="" /></td></tr>
          <tr v-else-if="rows.length === 0"><td colspan="7" class="td-center">无匹配日志</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="td-time">{{ formatTime(r.ts) }}</td>
            <td>
              <span class="level-tag" :style="{ color: levelColor(r.level), borderColor: levelColor(r.level) }">{{ r.level }}</span>
            </td>
            <td class="td-event" :title="r.title">{{ r.event }}</td>
            <td class="td-cat">{{ r.category }}</td>
            <td class="td-actor">{{ r.actor }}</td>
            <td class="td-svr">{{ r.serverId || '-' }}</td>
            <td class="td-detail">
              <span class="detail-text">{{ detailText(r) || (r.target ? '目标: ' + r.target : '-') }}</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- ══════════ 分页 ══════════ -->
    <div class="pager">
      <span>共 {{ total }} 条</span>
      <button :disabled="page <= 1" @click="gotoPage(page - 1)">‹ 上一页</button>
      <span class="page-info">{{ page }} / {{ totalPages }}</span>
      <button :disabled="page >= totalPages" @click="gotoPage(page + 1)">下一页 ›</button>
      <select v-model="pageSize" @change="search">
        <option :value="20">20/页</option>
        <option :value="50">50/页</option>
        <option :value="100">100/页</option>
      </select>
    </div>
  </div>
</template>

<style scoped>
.audit-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: auto;
  padding: 0 20px 20px;
}
.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-top: 16px;
  margin-bottom: 14px;
}
.section-header h2 { margin: 0; color: var(--text-primary); font-size: 1.4rem; }
.audit-hint { font-size: 0.8rem; color: var(--text-muted); }
.flash.error { padding: 10px 14px; border-radius: 8px; background: rgba(239,68,68,.12); color: #ef4444; margin-bottom: 12px; }

.stat-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; margin-bottom: 16px; }
.stat-card {
  background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 12px;
  padding: 16px; text-align: center;
}
.stat-num { font-size: 1.7rem; font-weight: 800; color: var(--accent-primary); }
.stat-card.warn .stat-num { color: #f59e0b; }
.stat-card.error .stat-num { color: #ef4444; }
.stat-card.alert .stat-num { color: #f59e0b; }
.stat-label { font-size: 0.78rem; color: var(--text-muted); margin-top: 4px; }

.filter-bar {
  display: flex; gap: 8px; flex-wrap: wrap; align-items: center;
  background: var(--bg-card); border: 1px solid var(--border-color);
  border-radius: 12px; padding: 12px; margin-bottom: 14px;
}
.filter-bar select, .filter-bar input {
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 7px 10px; border-radius: 8px; font-size: 0.85rem;
}
.filter-bar input[type="date"] { width: 140px; }
.date-sep { color: var(--text-muted); }
.search-btn {
  background: var(--accent-primary); color: #fff; border: none;
  padding: 7px 14px; border-radius: 8px; cursor: pointer; font-size: 0.85rem; font-weight: 600;
}
.reset-btn {
  background: transparent; border: 1px solid var(--border-color); color: var(--text-muted);
  padding: 7px 12px; border-radius: 8px; cursor: pointer; font-size: 0.85rem;
}

.table-wrap {
  background: var(--bg-card); border: 1px solid var(--border-color);
  border-radius: 12px; overflow: auto; flex: 1;
}
.audit-table { width: 100%; border-collapse: collapse; font-size: 0.84rem; }
.audit-table th {
  text-align: left; padding: 10px 12px; color: var(--text-muted); font-weight: 600;
  border-bottom: 1px solid var(--border-color); position: sticky; top: 0;
  background: var(--bg-card); white-space: nowrap;
}
.audit-table td { padding: 9px 12px; border-bottom: 1px solid var(--border-color); color: var(--text-primary); vertical-align: top; }
.audit-table tr:hover td { background: var(--bg-hover); }
.td-center { text-align: center; color: var(--text-muted); padding: 30px !important; }
.td-time { white-space: nowrap; font-size: 0.78rem; color: var(--text-muted); }
.level-tag { border: 1px solid; border-radius: 20px; padding: 1px 9px; font-size: 0.72rem; font-weight: 700; }
.td-event { font-family: monospace; white-space: nowrap; }
.td-actor { white-space: nowrap; }
.td-detail { max-width: 260px; }
.detail-text {
  font-family: monospace; font-size: 0.75rem; color: var(--text-muted);
  display: block; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}

.pager { display: flex; align-items: center; gap: 10px; justify-content: flex-end; margin-top: 12px; font-size: 0.85rem; color: var(--text-muted); }
.pager button {
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 6px 12px; border-radius: 7px; cursor: pointer; font-size: 0.82rem;
}
.pager button:disabled { opacity: 0.4; cursor: not-allowed; }
.pager select { background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary); padding: 5px 8px; border-radius: 7px; }
</style>

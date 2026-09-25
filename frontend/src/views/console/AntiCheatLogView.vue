<script setup>
import { ref, onMounted, computed } from 'vue'
import { apiRequest } from '../../utils/api.js'
import { loadProjectileData } from '../../api/projectileDataApi.js'
import Loading from '../../components/Loading.vue'

// ═══════════════════════════════════════════════════════════
// 状态
// ═══════════════════════════════════════════════════════════
const loading = ref(false)
const error = ref('')

// 弹幕名称字典（前端本地 ProjectileData.json；日志只记录弹幕 ID，此处由前端转名称）
const projData = ref({ list: [], dict: {} })
const projNameById = (id) => {
  if (id === undefined || id === null || id === '' || id <= 0) return ''
  return projData.value.dict[String(id)]?.chinese || ''
}

const initProjData = async () => {
  try {
    projData.value = await loadProjectileData()
  } catch { /* 字典加载失败时仅显示弹幕 ID */ }
}

const stats = ref({ total: 0, today: 0, byCategory: {}, byMethod: {}, recent: [] })

const rows = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(50)
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

// 筛选条件
const filters = ref({
  player: '',
  category: '',
  method: '',
  timeFrom: '',
  timeTo: '',
  q: ''
})

// 分类 / 处理方式展示映射
const categoryLabels = {
  item: '物品',
  proj: '弹幕',
  particle: '粒子'
}

const methodLabels = {
  log: '记录',
  kick: '踢出',
  ban: '封禁',
  drop: '丢弃',
  exempt: '豁免',
  query: '查询'
}

const categoryColor = (cat) => {
  if (cat === 'item') return '#6366f1'
  if (cat === 'proj') return '#f59e0b'
  if (cat === 'particle') return '#ec4899'
  return '#94a3b8'
}

const methodColor = (method) => {
  if (method === 'ban') return '#ef4444'
  if (method === 'kick') return '#f97316'
  if (method === 'drop') return '#eab308'
  if (method === 'exempt') return '#22c55e'
  return '#64748b'
}

const formatTime = (t) => {
  if (!t) return ''
  return t
}

// 日志行主展示文本：分类 + 物品/弹幕 + 处理方式 + 详情
// 物品名随日志落库（ItemName）；弹幕日志只记录 ID，此处用前端 ProjectileData.json 转名称
const rowTitle = (r) => {
  const parts = []
  if (r.itemName) parts.push(`[i:${r.itemId}] ${r.itemName}`)
  if (r.projId > 0) {
    const pName = projNameById(r.projId)
    parts.push(pName ? `弹幕#${r.projId} ${pName}` : `弹幕#${r.projId}`)
  }
  if (parts.length === 0) parts.push(r.category === 'particle' ? '粒子请求' : '检测事件')
  return parts.join(' / ')
}

// ═══════════════════════════════════════════════════════════
// 数据加载
// ═══════════════════════════════════════════════════════════
const buildQuery = () => {
  const p = new URLSearchParams()
  const f = filters.value
  const fmtDate = (d) => {
    if (!d) return ''
    const dt = new Date(d)
    if (isNaN(dt.getTime())) return ''
    const y = dt.getFullYear()
    const m = String(dt.getMonth() + 1).padStart(2, '0')
    const day = String(dt.getDate()).padStart(2, '0')
    return `${y}-${m}-${day}`
  }
  if (f.player) p.set('player', f.player)
  if (f.category) p.set('category', f.category)
  if (f.method) p.set('method', f.method)
  if (f.timeFrom) p.set('timeFrom', fmtDate(f.timeFrom) + ' 00:00:00')
  if (f.timeTo) p.set('timeTo', fmtDate(f.timeTo) + ' 23:59:59')
  if (f.q) p.set('q', f.q)
  p.set('page', page.value)
  p.set('pageSize', pageSize.value)
  return p.toString()
}

const loadLogs = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await apiRequest(`/api/anticheat/logs?${buildQuery()}`, { method: 'GET' })
    if (res.ok) {
      const data = await res.json()
      if (data.status === 200 || data.status === '200') {
        rows.value = data.rows || []
        total.value = data.total || 0
      } else {
        error.value = data.error || '加载失败'
      }
    } else {
      error.value = '加载失败'
    }
  } catch (e) { error.value = e.message } finally { loading.value = false }
}

const loadStats = async () => {
  try {
    const res = await apiRequest('/api/anticheat/logs/stats', { method: 'GET' })
    if (res.ok) {
      const data = await res.json()
      if (data.status === 200 || data.status === '200') {
        stats.value = data
      }
    }
  } catch { /* 静默 */ }
}

const search = () => { page.value = 1; loadLogs() }
const resetFilters = () => {
  filters.value = { player: '', category: '', method: '', timeFrom: '', timeTo: '', q: '' }
  search()
}
const gotoPage = (p) => { page.value = p; loadLogs() }

const refresh = () => { loadLogs(); loadStats() }

onMounted(() => {
  loadLogs()
  loadStats()
  initProjData()
})
</script>

<template>
  <div class="aclog-content">
    <div class="section-header">
      <h2>反作弊日志</h2>
      <span class="aclog-hint">每次反作弊检测命中自动记录（物品 / 弹幕 / 粒子），存储于服务器 AntiCheatLog.sqlite</span>
    </div>

    <div v-if="error" class="flash error">{{ error }}</div>

    <!-- ══════════ 统计卡片 ══════════ -->
    <div class="stat-grid">
      <div class="stat-card">
        <div class="stat-num">{{ stats.total || 0 }}</div>
        <div class="stat-label">累计检测</div>
      </div>
      <div class="stat-card today">
        <div class="stat-num">{{ stats.today || 0 }}</div>
        <div class="stat-label">今日检测</div>
      </div>
      <div class="stat-card item">
        <div class="stat-num">{{ stats.byCategory?.item || 0 }}</div>
        <div class="stat-label">物品违禁</div>
      </div>
      <div class="stat-card proj">
        <div class="stat-num">{{ stats.byCategory?.proj || 0 }}</div>
        <div class="stat-label">弹幕违禁</div>
      </div>
      <div class="stat-card particle">
        <div class="stat-num">{{ stats.byCategory?.particle || 0 }}</div>
        <div class="stat-label">粒子拦截</div>
      </div>
      <div class="stat-card ban">
        <div class="stat-num">{{ stats.byMethod?.ban || 0 }}</div>
        <div class="stat-label">封禁</div>
      </div>
      <div class="stat-card kick">
        <div class="stat-num">{{ stats.byMethod?.kick || 0 }}</div>
        <div class="stat-label">踢出</div>
      </div>
      <div class="stat-card drop">
        <div class="stat-num">{{ stats.byMethod?.drop || 0 }}</div>
        <div class="stat-label">丢弃拦截</div>
      </div>
    </div>

    <!-- ══════════ 筛选栏 ══════════ -->
    <div class="filter-bar">
      <select v-model="filters.category">
        <option value="">全部分类</option>
        <option value="item">物品</option>
        <option value="proj">弹幕</option>
        <option value="particle">粒子</option>
      </select>
      <select v-model="filters.method">
        <option value="">全部处理方式</option>
        <option value="log">记录</option>
        <option value="kick">踢出</option>
        <option value="ban">封禁</option>
        <option value="drop">丢弃</option>
        <option value="exempt">豁免</option>
      </select>
      <input v-model="filters.player" placeholder="玩家名" @keyup.enter="search" />
      <input v-model="filters.timeFrom" type="date" />
      <span class="date-sep">~</span>
      <input v-model="filters.timeTo" type="date" />
      <input v-model="filters.q" placeholder="关键字（物品/详情）" @keyup.enter="search" />
      <button class="search-btn" @click="search">查询</button>
      <button class="reset-btn" @click="resetFilters">重置</button>
      <button class="refresh-btn" @click="refresh" title="刷新">刷新</button>
    </div>

    <!-- ══════════ 日志表格 ══════════ -->
    <div class="table-wrap">
      <table class="aclog-table">
        <thead>
          <tr>
            <th>时间</th>
            <th>玩家</th>
            <th>分类</th>
            <th>目标</th>
            <th>处理</th>
            <th>详情</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="loading"><td colspan="6" class="td-center"><Loading size="sm" text="" /></td></tr>
          <tr v-else-if="rows.length === 0"><td colspan="6" class="td-center">暂无反作弊检测日志</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="td-time">{{ formatTime(r.time) }}</td>
            <td class="td-player">{{ r.playerName || '-' }}</td>
            <td>
              <span class="cat-tag" :style="{ color: categoryColor(r.category), borderColor: categoryColor(r.category) }">{{ categoryLabels[r.category] || r.category }}</span>
            </td>
            <td class="td-title" :title="rowTitle(r)">{{ rowTitle(r) }}</td>
            <td>
              <span class="method-tag" :style="{ color: methodColor(r.method), borderColor: methodColor(r.method) }">{{ methodLabels[r.method] || r.method }}</span>
            </td>
            <td class="td-detail">
              <span class="detail-text" :title="r.detail">{{ r.detail || '-' }}</span>
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
.aclog-content {
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
.aclog-hint { font-size: 0.8rem; color: var(--text-muted); }
.flash.error { padding: 10px 14px; border-radius: 8px; background: rgba(239,68,68,.12); color: #ef4444; margin-bottom: 12px; }

.stat-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: 12px; margin-bottom: 16px; }
.stat-card {
  background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 12px;
  padding: 14px; text-align: center;
}
.stat-num { font-size: 1.5rem; font-weight: 800; color: var(--accent-primary); }
.stat-card.today .stat-num { color: #22c55e; }
.stat-card.item .stat-num { color: #6366f1; }
.stat-card.proj .stat-num { color: #f59e0b; }
.stat-card.particle .stat-num { color: #ec4899; }
.stat-card.ban .stat-num { color: #ef4444; }
.stat-card.kick .stat-num { color: #f97316; }
.stat-card.drop .stat-num { color: #eab308; }
.stat-label { font-size: 0.76rem; color: var(--text-muted); margin-top: 4px; }

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
.filter-bar input[placeholder="玩家名"] { width: 120px; }
.filter-bar input[placeholder="关键字（物品/详情）"] { width: 170px; }
.date-sep { color: var(--text-muted); }
.search-btn {
  background: var(--accent-primary); color: #fff; border: none;
  padding: 7px 14px; border-radius: 8px; cursor: pointer; font-size: 0.85rem; font-weight: 600;
}
.reset-btn {
  background: transparent; border: 1px solid var(--border-color); color: var(--text-muted);
  padding: 7px 12px; border-radius: 8px; cursor: pointer; font-size: 0.85rem;
}
.refresh-btn {
  background: transparent; border: 1px solid var(--border-color); color: var(--text-secondary);
  padding: 7px 12px; border-radius: 8px; cursor: pointer; font-size: 0.85rem;
}

.table-wrap {
  background: var(--bg-card); border: 1px solid var(--border-color);
  border-radius: 12px; overflow: auto; flex: 1;
}
.aclog-table { width: 100%; border-collapse: collapse; font-size: 0.84rem; }
.aclog-table th {
  text-align: left; padding: 10px 12px; color: var(--text-muted); font-weight: 600;
  border-bottom: 1px solid var(--border-color); position: sticky; top: 0;
  background: var(--bg-card); white-space: nowrap;
}
.aclog-table td { padding: 9px 12px; border-bottom: 1px solid var(--border-color); color: var(--text-primary); vertical-align: top; }
.aclog-table tr:hover td { background: var(--bg-hover); }
.td-center { text-align: center; color: var(--text-muted); padding: 30px !important; }
.td-time { white-space: nowrap; font-size: 0.78rem; color: var(--text-muted); }
.td-player { white-space: nowrap; font-weight: 600; }
.cat-tag { border: 1px solid; border-radius: 20px; padding: 1px 9px; font-size: 0.72rem; font-weight: 700; white-space: nowrap; }
.method-tag { border: 1px solid; border-radius: 20px; padding: 1px 9px; font-size: 0.72rem; font-weight: 700; white-space: nowrap; }
.td-title { max-width: 200px; }
.td-title, .detail-text {
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.detail-text {
  font-family: monospace; font-size: 0.75rem; color: var(--text-muted);
  display: block; max-width: 280px;
}

.pager { display: flex; align-items: center; gap: 10px; justify-content: flex-end; margin-top: 12px; font-size: 0.85rem; color: var(--text-muted); }
.pager button {
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 6px 12px; border-radius: 7px; cursor: pointer; font-size: 0.82rem;
}
.pager button:disabled { opacity: 0.4; cursor: not-allowed; }
.pager select { background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary); padding: 5px 8px; border-radius: 7px; }
</style>

<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { get } from '../../utils/api.js'

const toLocalDateString = (date) => {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

const today = toLocalDateString(new Date())
const currentHour = new Date().getHours()

// ==================== 顶部统计卡片 ====================
const statsOnline = ref(0)
const statsTotal = ref(0)
const statsQQ = ref(0)
const statsBans = ref(0)
const statsLoading = ref(true)

const fetchStats = async () => {
  const [activeRes, userRes, banRes] = await Promise.allSettled([
    get('/api/tshock/activeusers'),
    get('/api/tshock/userlist'),
    get('/api/tshock/banlist'),
  ])

  try {
    const a = await activeRes.value.json()
    statsOnline.value = a.activeusers ? a.activeusers.split('\t').filter(n => n.trim()).length : 0
  } catch { statsOnline.value = 0 }

  try {
    const u = await userRes.value.json()
    if (u.users) {
      statsTotal.value = u.users.length
      statsQQ.value = u.users.filter(x => x.QQ && x.QQ.trim()).length
    }
  } catch { statsTotal.value = 0; statsQQ.value = 0 }

  try {
    const b = await banRes.value.json()
    statsBans.value = b.bans ? b.bans.length : (b.banslist ? b.banslist.length : 0)
  } catch { statsBans.value = 0 }

  statsLoading.value = false
}

// ==================== 当前在线玩家 ====================
const activePlayers = ref([])
const activeLoading = ref(false)

const fetchActivePlayers = async () => {
  activeLoading.value = true
  try {
    const res = await get('/api/tshock/activeusers')
    const json = await res.json()
    activePlayers.value = json.activeusers
      ? json.activeusers.split('\t').filter(n => n.trim())
      : []
  } catch { activePlayers.value = [] }
  activeLoading.value = false
}

// ==================== 排行榜 ====================
const rankingMode = ref('today')
const rankingData = ref([])
const rankingLoading = ref(false)

const fetchRanking = async () => {
  rankingLoading.value = true
  try {
    const res = await get(`/api/online/ranking?mode=${rankingMode.value}`)
    const json = await res.json()
    rankingData.value = json.ranking || []
  } catch { rankingData.value = [] }
  rankingLoading.value = false
}

const formatDuration = (min) => {
  const h = Math.floor(min / 60)
  const m = min % 60
  if (h > 0 && m > 0) return `${h}h ${m}m`
  if (h > 0) return `${h}h`
  return `${m}m`
}

watch(rankingMode, fetchRanking)

// ==================== 逐时在线 ====================
const hourlyDate = ref(today)
const hourlyData = ref([])
const hourlyLoading = ref(false)
const hourlyTooltip = ref({ show: false, hour: null, x: 0, y: 0, names: [] })
const maxHourlyCount = ref(0)
const maxVisibleHour = ref(currentHour)

const fetchHourly = async () => {
  hourlyLoading.value = true
  hourlyTooltip.value.show = false
  try {
    const res = await get(`/api/online/hourly?date=${hourlyDate.value}`)
    const json = await res.json()
    hourlyData.value = json.hours || []
    const max = Math.max(1, ...hourlyData.value.map(h => (h.online_players || []).length))
    maxHourlyCount.value = max
    maxVisibleHour.value = hourlyDate.value === today ? currentHour : 23
  } catch { hourlyData.value = []; maxHourlyCount.value = 1; maxVisibleHour.value = currentHour }
  hourlyLoading.value = false
}

const barHeights = computed(() => {
  const bars = new Array(24).fill(0)
  hourlyData.value.forEach(h => {
    const hour = h.hour_ts % 100
    if (hour >= 0 && hour < 24) bars[hour] = (h.online_players || []).length
  })
  return bars
})

const showHourlyTooltip = (hour, event) => {
  const h = hourlyData.value.find(h => (h.hour_ts % 100) === hour)
  hourlyTooltip.value = { show: true, hour, x: event.clientX, y: event.clientY, names: h ? (h.online_players || []) : [] }
}

const hideHourlyTooltip = () => { hourlyTooltip.value.show = false }

watch(hourlyDate, fetchHourly)

onMounted(() => {
  fetchStats()
  fetchActivePlayers()
  fetchRanking()
  fetchHourly()
})
</script>

<template>
  <div class="dashboard">
    <!-- 顶部统计卡片 -->
    <section class="stat-row">
      <div class="stat-card" v-for="s in [
        { label: '当前在线', value: statsOnline, icon: 'online', color: '#22c55e' },
        { label: '总注册用户', value: statsTotal, icon: 'users', color: '#6366f1' },
        { label: 'QQ 绑定', value: statsQQ, icon: 'qq', color: '#3b82f6' },
        { label: '封禁数量', value: statsBans, icon: 'ban', color: '#ef4444' },
      ]" :key="s.label">
        <div class="stat-icon" :style="{ background: s.color + '18', color: s.color }">
          <!-- online -->
          <svg v-if="s.icon === 'online'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect><line x1="8" y1="21" x2="16" y2="21"></line><line x1="12" y1="17" x2="12" y2="21"></line>
          </svg>
          <!-- users -->
          <svg v-else-if="s.icon === 'users'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M23 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path>
          </svg>
          <!-- qq -->
          <svg v-else-if="s.icon === 'qq'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 2a5 5 0 0 0-5 5v1a5 5 0 0 0 10 0V7a5 5 0 0 0-5-5Z"></path><path d="M5 13c-1.5 2-2 4-2 6 3 1 6.5 1.5 9 1.5S18 20 21 19c0-2-.5-4-2-6"></path><path d="M8 11c0 2 1.5 3 4 3s4-1 4-3"></path>
          </svg>
          <!-- ban -->
          <svg v-else width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="10"></circle><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"></line>
          </svg>
        </div>
        <div class="stat-body">
          <span class="stat-value" v-if="!statsLoading">{{ s.value }}</span>
          <span class="stat-value loading-pulse" v-else></span>
          <span class="stat-label">{{ s.label }}</span>
        </div>
      </div>
    </section>

    <div class="dashboard-grid">
      <!-- 当前在线玩家 -->
      <section class="card active-card">
        <div class="card-header">
          <h3>当前在线</h3>
          <span class="badge" :class="activePlayers.length > 0 ? 'badge-online' : 'badge-offline'">
            {{ activePlayers.length }}
          </span>
        </div>
        <div class="card-body">
          <div v-if="activeLoading" class="loading">加载中...</div>
          <div v-else-if="activePlayers.length === 0" class="empty">暂无玩家在线</div>
          <ul v-else class="player-list">
            <li v-for="name in activePlayers" :key="name" class="player-item">
              <span class="player-dot"></span>
              <span class="player-name">{{ name }}</span>
            </li>
          </ul>
        </div>
      </section>

      <!-- 逐时在线 -->
      <section class="card hourly-card">
        <div class="card-header">
          <h3>每小时在线</h3>
          <input type="date" v-model="hourlyDate" class="filter-date" />
        </div>
        <div class="card-body">
          <div v-if="hourlyLoading" class="loading">加载中...</div>
          <div v-else class="bar-chart">
            <div v-for="h in 24" :key="h - 1" class="bar-col" :class="{ future: h - 1 > maxVisibleHour }"
              @mouseenter="h - 1 <= maxVisibleHour && showHourlyTooltip(h - 1, $event)"
              @mouseleave="hideHourlyTooltip">
              <div v-if="h - 1 <= maxVisibleHour" class="bar"
                :style="{ height: maxHourlyCount > 0 ? Math.max(4, (barHeights[h - 1] / maxHourlyCount) * 140) + 'px' : '4px' }">
              </div>
              <div v-else class="bar future-bar"></div>
              <span class="bar-label">{{ String(h - 1).padStart(2, '0') }}</span>
            </div>
          </div>
        </div>
      </section>
    </div>

    <!-- 排行榜 -->
    <section class="card ranking-card">
      <div class="card-header">
        <h3>在线时长排行</h3>
        <div class="select-wrapper">
          <select v-model="rankingMode" class="filter-select">
            <option value="today">今天</option>
            <option value="24h">24小时内</option>
            <option value="7d">最近7天</option>
            <option value="30d">最近30天</option>
            <option value="all">累计</option>
          </select>
        </div>
      </div>
      <div class="card-body">
        <div v-if="rankingLoading" class="loading">加载中...</div>
        <div v-else-if="rankingData.length === 0" class="empty">暂无数据</div>
        <table v-else class="ranking-table">
          <thead>
            <tr>
              <th class="col-rank">#</th>
              <th class="col-name">玩家</th>
              <th class="col-time">时长</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(item, idx) in rankingData" :key="item.uid">
              <td class="col-rank">{{ idx + 1 }}</td>
              <td class="col-name">{{ item.uid }}</td>
              <td class="col-time">{{ formatDuration(item.total_min) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- Tooltip -->
    <Teleport to="body">
      <div v-if="hourlyTooltip.show" class="hourly-tooltip"
        :style="{ left: hourlyTooltip.x + 12 + 'px', top: hourlyTooltip.y - 10 + 'px' }">
        <div class="tooltip-hour">{{ String(hourlyTooltip.hour).padStart(2, '0') }}:00</div>
        <div class="tooltip-count">{{ hourlyTooltip.names.length }}人在线</div>
        <div v-if="hourlyTooltip.names.length > 0" class="tooltip-ids">{{ hourlyTooltip.names.join(', ') }}</div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.dashboard {
  padding: 20px;
  max-width: 1200px;
  margin: 0 auto;
}

/* ===== 顶部统计卡片 ===== */
.stat-row {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 14px;
  margin-bottom: 20px;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 20px;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 14px;
  transition: transform 0.25s cubic-bezier(0.34, 1.56, 0.64, 1), box-shadow 0.25s ease;
}

.stat-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.08);
}

.stat-icon {
  width: 48px;
  height: 48px;
  border-radius: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.stat-body {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.stat-value {
  font-size: 1.8rem;
  font-weight: 800;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
  line-height: 1.2;
}

.stat-value.loading-pulse {
  width: 48px;
  height: 32px;
  border-radius: 6px;
  background: var(--bg-tertiary);
  animation: pulse 1.5s ease-in-out infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 0.5; }
  50% { opacity: 0.2; }
}

.stat-label {
  font-size: 0.82rem;
  color: var(--text-secondary);
  font-weight: 500;
}

/* ===== 网格布局 ===== */
.dashboard-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 16px;
}

/* ===== 通用卡片 ===== */
.card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 14px;
  overflow: hidden;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border-light);
}

.card-header h3 {
  font-size: 1.05rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}

.card-body {
  padding: 16px 20px;
}

/* ===== 在线玩家列表 ===== */
.active-card .card-body {
  max-height: 320px;
  overflow-y: auto;
}

.player-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.player-item {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  background: var(--bg-tertiary);
  border-radius: 10px;
  border: 1px solid var(--border-light);
  transition: background 0.2s;
}

.player-item:hover {
  background: var(--bg-hover);
}

.player-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #22c55e;
  box-shadow: 0 0 8px rgba(34, 197, 94, 0.5);
  flex-shrink: 0;
}

.player-name {
  font-size: 0.9rem;
  font-weight: 500;
  color: var(--text-primary);
}

.badge {
  padding: 2px 10px;
  border-radius: 20px;
  font-size: 0.78rem;
  font-weight: 700;
}

.badge-online {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
}

.badge-offline {
  background: rgba(107, 114, 128, 0.15);
  color: #6b7280;
}

/* ===== 逐时柱状图 ===== */
.bar-chart {
  display: flex;
  align-items: flex-end;
  gap: 2px;
  height: 180px;
  padding-top: 8px;
}

.bar-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-end;
  height: 100%;
  cursor: pointer;
}

.bar-col.future { cursor: default; }

.bar {
  width: 100%;
  max-width: 22px;
  border-radius: 3px 3px 0 0;
  background: linear-gradient(180deg, #6366f1, #4f46e5);
  min-height: 4px;
  transition: opacity 0.15s;
}

.bar-col:hover .bar { opacity: 0.75; }

.future-bar {
  width: 100%;
  max-width: 22px;
  border-radius: 3px 3px 0 0;
  background: var(--border-light);
  min-height: 4px;
}

.bar-label {
  margin-top: 4px;
  font-size: 0.6rem;
  color: var(--text-secondary);
}

/* ===== 排行榜 ===== */
.ranking-table {
  width: 100%;
  border-collapse: collapse;
}

.ranking-table th {
  text-align: left;
  padding: 8px 12px;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--text-secondary);
  border-bottom: 1px solid var(--border-light);
  text-transform: uppercase;
}

.ranking-table td {
  padding: 8px 12px;
  font-size: 0.9rem;
  border-bottom: 1px solid var(--border-light);
  color: var(--text-primary);
}

.ranking-table tr:hover td {
  background: var(--bg-hover);
}

.col-rank { width: 40px; color: var(--text-secondary); }
.col-time { width: 80px; text-align: right; font-variant-numeric: tabular-nums; }

/* ===== 筛选 ===== */
.filter-select, .filter-date {
  padding: 7px 32px 7px 14px;
  border: 1px solid var(--border-light);
  border-radius: 6px;
  background: var(--bg-input);
  color: var(--text-primary);
  font-size: 0.875rem;
  cursor: pointer;
  outline: none;
  transition: border-color 0.2s, box-shadow 0.2s;
  -webkit-appearance: none;
  -moz-appearance: none;
  appearance: none;
  min-width: 120px;
}

.filter-select:hover, .filter-date:hover { border-color: var(--accent-primary); }
.filter-select:focus, .filter-date:focus {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent);
}

.filter-select option {
  background: var(--bg-card);
  color: var(--text-primary);
  padding: 6px 12px;
}

.select-wrapper {
  position: relative;
  display: inline-flex;
}

.select-wrapper::after {
  content: '';
  position: absolute;
  right: 12px;
  top: 50%;
  transform: translateY(-50%);
  width: 0;
  height: 0;
  border-left: 5px solid transparent;
  border-right: 5px solid transparent;
  border-top: 5px solid var(--text-secondary);
  pointer-events: none;
}

/* ===== Tooltip ===== */
.hourly-tooltip {
  position: fixed;
  z-index: 9999;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 8px;
  padding: 10px 14px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
  pointer-events: none;
  font-size: 0.8rem;
  color: var(--text-primary);
}

.tooltip-hour { font-weight: 600; margin-bottom: 2px; }
.tooltip-count { color: var(--accent-primary); }

.tooltip-ids {
  margin-top: 4px;
  color: var(--text-secondary);
  font-size: 0.7rem;
  max-width: 200px;
  word-break: break-all;
}

/* ===== 通用 ===== */
.loading, .empty {
  text-align: center;
  padding: 24px 0;
  color: var(--text-secondary);
  font-size: 0.9rem;
}

@media (max-width: 860px) {
  .stat-row { grid-template-columns: repeat(2, 1fr); }
  .dashboard-grid { grid-template-columns: 1fr; }
}

@media (max-width: 480px) {
  .stat-row { grid-template-columns: 1fr; }
}
</style>

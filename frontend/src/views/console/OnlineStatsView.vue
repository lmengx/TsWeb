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

// ==================== 区域1：排行榜 ====================
const rankingMode = ref('today')
const rankingData = ref([])
const rankingLoading = ref(false)

const fetchRanking = async () => {
  rankingLoading.value = true
  try {
    const res = await get(`/api/online/ranking?mode=${rankingMode.value}`)
    const json = await res.json()
    rankingData.value = json.ranking || []
  } catch (e) {
    rankingData.value = []
  }
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

// ==================== 区域2：逐时在线 ====================
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
  } catch (e) {
    hourlyData.value = []
    maxHourlyCount.value = 1
    maxVisibleHour.value = currentHour
  }
  hourlyLoading.value = false
}

const barHeights = computed(() => {
  const bars = new Array(24).fill(0)
  hourlyData.value.forEach(h => {
    const hour = h.hour_ts % 100
    if (hour >= 0 && hour < 24) {
      bars[hour] = (h.online_players || []).length
    }
  })
  return bars
})

const showHourlyTooltip = (hour, event) => {
  const h = hourlyData.value.find(h => (h.hour_ts % 100) === hour)
  hourlyTooltip.value = {
    show: true,
    hour,
    x: event.clientX,
    y: event.clientY,
    names: h ? (h.online_players || []) : []
  }
}

const hideHourlyTooltip = () => {
  hourlyTooltip.value.show = false
}

watch(hourlyDate, fetchHourly)

onMounted(() => {
  fetchRanking()
  fetchHourly()
})
</script>

<template>
  <div class="online-stats">
    <h2 class="page-title">在线时长统计</h2>
    <div class="top-row">
      <!-- 排行榜 -->
      <section class="card ranking-section">
        <div class="card-header">
          <h3>累计时长排行</h3>
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

      <!-- 逐时在线 -->
      <section class="card hourly-section">
        <div class="card-header">
          <h3>每日逐时在线</h3>
          <input type="date" v-model="hourlyDate" class="filter-date" />
        </div>
        <div class="card-body">
          <div v-if="hourlyLoading" class="loading">加载中...</div>
          <div v-else class="bar-chart">
            <div
              v-for="h in 24"
              :key="h - 1"
              class="bar-col"
              :class="{ future: h - 1 > maxVisibleHour }"
              @mouseenter="h - 1 <= maxVisibleHour && showHourlyTooltip(h - 1, $event)"
              @mouseleave="hideHourlyTooltip"
            >
              <div
                v-if="h - 1 <= maxVisibleHour"
                class="bar"
                :style="{
                  height: maxHourlyCount > 0 ? Math.max(4, (barHeights[h - 1] / maxHourlyCount) * 160) + 'px' : '4px'
                }"
                :title="`${String(h - 1).padStart(2, '0')}:00 - ${barHeights[h - 1]}人在线`"
              ></div>
              <div v-else class="bar future-bar"></div>
              <span class="bar-label">{{ String(h - 1).padStart(2, '0') }}</span>
            </div>
          </div>
        </div>
      </section>
    </div>

    <!-- Tooltip -->
    <Teleport to="body">
      <div
        v-if="hourlyTooltip.show"
        class="hourly-tooltip"
        :style="{ left: hourlyTooltip.x + 12 + 'px', top: hourlyTooltip.y - 10 + 'px' }"
      >
        <div class="tooltip-hour">{{ String(hourlyTooltip.hour).padStart(2, '0') }}:00</div>
        <div class="tooltip-count">{{ hourlyTooltip.names.length }}人在线</div>
        <div v-if="hourlyTooltip.names.length > 0" class="tooltip-ids">
          {{ hourlyTooltip.names.join(', ') }}
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.online-stats {
  padding: 24px;
  max-width: 1200px;
  margin: 0 auto;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--text-primary);
  margin-bottom: 24px;
  padding-bottom: 12px;
  border-bottom: 2px solid var(--accent-primary);
}

.top-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

.card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-lg, 12px);
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

.filter-select, .filter-date {
  padding: 7px 32px 7px 14px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-sm, 6px);
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

.filter-select:hover {
  border-color: var(--accent-primary);
}

.filter-select:focus {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent);
}

.filter-select option {
  background: var(--bg-card);
  color: var(--text-primary);
  padding: 6px 12px;
}

.loading, .empty {
  text-align: center;
  padding: 32px 0;
  color: var(--text-secondary);
  font-size: 0.9rem;
}

/* 排行榜表格 */
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

/* 逐时柱状图 */
.bar-chart {
  display: flex;
  align-items: flex-end;
  gap: 2px;
  height: 200px;
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
  max-width: 24px;
  border-radius: 3px 3px 0 0;
  background: linear-gradient(180deg, #40c463, #30a14e);
  min-height: 4px;
  transition: opacity 0.15s;
}

.bar-col:hover .bar { opacity: 0.8; }
.bar-col.future:hover .bar { opacity: 1; }

.future-bar {
  width: 100%;
  max-width: 24px;
  border-radius: 3px 3px 0 0;
  background: var(--border-light);
  min-height: 4px;
}

.bar-label {
  margin-top: 4px;
  font-size: 0.65rem;
  color: var(--text-secondary);
}

/* Tooltip */
.hourly-tooltip {
  position: fixed;
  z-index: 9999;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 8px;
  padding: 10px 14px;
  box-shadow: var(--shadow-lg);
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

@media (max-width: 768px) {
  .top-row { grid-template-columns: 1fr; }
}
</style>

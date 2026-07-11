<script setup>
import { ref, computed, watch, onMounted, onUnmounted, Transition } from 'vue'
import { useRouter } from 'vue-router'
import { get } from '../../utils/api.js'
import BanListView from './BanListView.vue'

const isMobile = ref(false)
let mql = null

onMounted(() => {
  mql = window.matchMedia('(max-width: 767px)')
  isMobile.value = mql.matches
  mql.addEventListener('change', onMediaChange)
})

onUnmounted(() => {
  if (mql) mql.removeEventListener('change', onMediaChange)
  stopAutoCycle()
})

const onMediaChange = (e) => { isMobile.value = e.matches }
import ConsoleTerminal from '../../components/ConsoleTerminal.vue'

const router = useRouter()

const openPlayersPage = () => {
  window.open(router.resolve({ name: 'Players' }).href, '_blank')
}

const openPlayerDetail = (name) => {
  window.open(router.resolve({ name: 'UserDetail', params: { username: name } }).href, '_blank')
}

const toLocalDateString = (date) => {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

const today = toLocalDateString(new Date())
const currentHour = new Date().getHours()

// ── 数字动画 ──
const animateValue = (targetRef, end, duration = 800) => {
  if (targetRef.value === end) return
  const start = targetRef.value
  const startTime = performance.now()
  const diff = end - start
  const tick = (now) => {
    const elapsed = now - startTime
    const progress = Math.min(elapsed / duration, 1)
    const eased = 1 - Math.pow(1 - progress, 3)
    targetRef.value = Math.round(start + diff * eased)
    if (progress < 1) requestAnimationFrame(tick)
  }
  requestAnimationFrame(tick)
}

// ── 统计数据 ──
const onlineCount = ref(0)
const totalUsers = ref(0)
const qqCount = ref(0)
const banCount = ref(0)
const activePlayers = ref([])
const recentBans = ref([])

const fetchStats = async () => {
  const [activeRes, userRes, banRes] = await Promise.allSettled([
    get('/api/tshock/activeusers'),
    get('/api/tshock/userdata'),
    get('/api/tshock/banlist'),
  ])

  let online = 0
  try {
    const a = await activeRes.value.json()
    const list = a.activeusers ? a.activeusers.split('\t').filter(n => n.trim()) : []
    online = list.length
    activePlayers.value = list
  } catch {}
  animateValue(onlineCount, online)

  try {
    const u = await userRes.value.json()
    if (u.users) {
      animateValue(totalUsers, u.users.length)
      const qq = u.users.filter(x => x.QQ && x.QQ.trim()).length
      animateValue(qqCount, qq)
    }
  } catch {}

  try {
    const b = await banRes.value.json()
    let bans = []
    if (b.bans) bans = b.bans
    else if (b.banslist) bans = b.banslist
    animateValue(banCount, bans.length)
    recentBans.value = bans.slice(0, 3)
  } catch {}
}

// ── 封禁模态框 ──
const showBanModal = ref(false)
const openBanModal = () => { showBanModal.value = true }
const closeBanModal = () => { showBanModal.value = false }

// ── 排行榜切换 ──
const rankType = ref('online')
const rankTypes = [
  { key: 'online', label: '在线时长', unit: 'min', fmt: (v) => { const h = Math.floor(v / 60); const m = v % 60; return h > 0 ? `${h}h ${m}m` : `${m}m` } },
  { key: 'deaths', label: '死亡', unit: '次', fmt: (v) => `${v}次` },
  { key: 'fishing', label: '钓鱼任务', unit: '个', fmt: (v) => `${v}个` },
]
const rankLabel = computed(() => rankTypes.find(t => t.key === rankType.value)?.label ?? '在线时长')
const rankFmt = computed(() => rankTypes.find(t => t.key === rankType.value)?.fmt ?? ((v) => v))

// ── 模态框独立格式化器 ──
const rankModalFmt = computed(() => rankTypes.find(t => t.key === rankModalType.value)?.fmt ?? ((v) => v))

const rankData = ref([])
const rankLoading = ref(false)
const rankKey = ref(0)

// 请求序列号，用于忽略过期响应
let rankFetchSeq = 0

// 模态框分页状态
const showRankModal = ref(false)
const rankModalType = ref('online')
const rankModalData = ref([])
const rankModalPage = ref(1)
const rankModalPageSize = ref(10)
const rankModalTotal = ref(0)

const fetchRank = async (type, page = 1, pageSize = 5) => {
  const seq = ++rankFetchSeq
  try {
    const res = await get(`/api/online/ranking/stats?type=${type}&page=${page}&pageSize=${pageSize}`)
    const json = await res.json()
    if (seq !== rankFetchSeq) return json // 已过期，忽略
    if (pageSize <= 5 && page === 1) {
      rankData.value = json.ranking || []
      rankType.value = type
      rankKey.value++
    }
    return json
  } catch { return { ranking: [], total: 0 } }
}

const openRankModal = async () => {
  stopAutoCycle()
  rankModalType.value = rankType.value
  rankModalPage.value = 1
  rankModalPageSize.value = 10
  const json = await fetchRank(rankType.value, 1, 10)
  rankModalData.value = json.ranking || []
  rankModalTotal.value = json.total || 0
  showRankModal.value = true
}

const closeRankModal = () => { showRankModal.value = false }

const rankModalTotalPages = computed(() => Math.max(1, Math.ceil(rankModalTotal.value / rankModalPageSize.value)))

const goRankModalPage = async (p) => {
  if (p < 1 || p > rankModalTotalPages.value) return
  rankModalPage.value = p
  const json = await fetchRank(rankModalType.value, p, rankModalPageSize.value)
  rankModalData.value = json.ranking || []
}

const setRankModalPageSize = async (size) => {
  rankModalPageSize.value = size
  rankModalPage.value = 1
  const json = await fetchRank(rankModalType.value, 1, size)
  rankModalData.value = json.ranking || []
  rankModalTotal.value = json.total || 0
}

// ── 排行榜自动切换 ──
let autoCycleTimer = null
const autoCycleEnabled = ref(true)
const rankHovering = ref(false)

const startAutoCycle = () => {
  stopAutoCycle()
  if (!autoCycleEnabled.value) return
  autoCycleTimer = setInterval(() => {
    if (rankHovering.value) return
    const keys = rankTypes.map(t => t.key)
    const idx = keys.indexOf(rankType.value)
    const nextType = keys[(idx + 1) % keys.length]
    fetchRank(nextType, 1, 5)
  }, 5000)
}

const stopAutoCycle = () => {
  if (autoCycleTimer) {
    clearInterval(autoCycleTimer)
    autoCycleTimer = null
  }
}

const onRankMouseEnter = () => {
  rankHovering.value = true
}

const onRankMouseLeave = () => {
  rankHovering.value = false
  if (autoCycleEnabled.value) {
    stopAutoCycle()
    startAutoCycle()
  }
}

const toggleRankType = () => {
  stopAutoCycle()
  autoCycleEnabled.value = false
  const keys = rankTypes.map(t => t.key)
  const idx = keys.indexOf(rankType.value)
  const nextType = keys[(idx + 1) % keys.length]
  fetchRank(nextType, 1, 5)
}

// ── 逐时在线 ──
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
  hourlyData.value.forEach(h => { const hour = h.hour_ts % 100; if (hour >= 0 && hour < 24) bars[hour] = (h.online_players || []).length })
  return bars
})

const showHourlyTooltip = (hour, event) => {
  const h = hourlyData.value.find(h => (h.hour_ts % 100) === hour)
  hourlyTooltip.value = { show: true, hour, x: event.clientX, y: event.clientY, names: h ? (h.online_players || []) : [] }
}
const hideHourlyTooltip = () => { hourlyTooltip.value.show = false }

watch(hourlyDate, fetchHourly)

// ── 工具函数 ──
const ticksToDate = (ticks) => { try { return new Date((ticks - 621355968000000000) / 10000).toLocaleString('zh-CN') } catch { return '-' } }
const parseIdentifier = (v) => {
  if (!v) return { type: '未知', value: '-' }
  if (v.startsWith('ip:')) return { type: 'IP', value: v.substring(3) }
  if (v.startsWith('uuid:')) { const u = v.substring(5); return { type: 'UUID', value: u.length > 12 ? u.substring(0, 12) + '...' : u } }
  if (v.startsWith('acc:')) return { type: '账户', value: v.substring(4) }
  return { type: '未知', value: v }
}

onMounted(() => {
  fetchStats()
  fetchRank('online', 1, 5)
  fetchHourly()
  startAutoCycle()
})
</script>

<template>
  <div class="dashboard">
    <!-- 四个等高大框 -->
    <div class="stat-cards">
      <!-- Box 1: 当前在线 -->
      <div class="stat-card clickable" @click="openPlayersPage">
        <div class="card-header-row">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#22c55e" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect><line x1="8" y1="21" x2="16" y2="21"></line><line x1="12" y1="17" x2="12" y2="21"></line>
          </svg>
          <span class="card-title">当前在线</span>
          <span class="card-badge green-badge">{{ onlineCount }}</span>
        </div>
        <div class="card-body-scroll">
          <div v-if="activePlayers.length === 0" class="card-empty">暂无玩家在线</div>
          <div v-else class="online-tags">
            <span v-for="name in activePlayers" :key="name" class="online-tag clickable-tag" @click.stop="openPlayerDetail(name)" title="查看玩家详情">
              <span class="dot"></span>{{ name }}
            </span>
          </div>
        </div>
      </div>

      <!-- Box 2: 注册 & 绑定 -->
      <div class="stat-card">
        <div class="card-header-row">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M23 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path>
          </svg>
          <span class="card-title">注册 & 绑定</span>
        </div>
        <div class="card-body-scroll" style="display:flex;flex-direction:column;justify-content:center;gap:16px;padding:10px 0;">
          <div class="big-stat-row">
            <span class="big-stat-label">注册总数</span>
            <span class="big-stat-value">{{ totalUsers }}</span>
          </div>
          <div class="big-stat-row">
            <span class="big-stat-label">QQ 绑定</span>
            <span class="big-stat-value qq-val">{{ qqCount }}</span>
          </div>
        </div>
      </div>

      <!-- Box 3: 排行榜 -->
      <div class="stat-card clickable" @click="openRankModal" @mouseenter="onRankMouseEnter" @mouseleave="onRankMouseLeave">
        <div class="card-header-row">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="6 9 12 4 18 9"></polyline><path d="M18 9l3-3v2a9 9 0 0 1-9 9c-4.97 0-9-4.03-9-9V6l3 3"></path>
          </svg>
          <span class="card-title">{{ rankLabel }}</span>
          <button class="switch-btn" @click.stop="toggleRankType" title="切换排行类型">⟳</button>
        </div>
        <div class="card-body-scroll">
          <div v-if="rankData.length === 0" class="card-empty">暂无数据</div>
          <Transition v-else mode="out-in" name="rank-fade">
            <div class="rank-mini-list" :key="rankKey">
              <div v-for="(item, idx) in rankData" :key="item.name" class="rank-mini-item clickable-tag" @click.stop="openPlayerDetail(item.name)" title="查看玩家详情">
                <span class="rank-num" :class="{ 'gold-1': idx === 0, silver: idx === 1, bronze: idx === 2 }">{{ idx + 1 }}</span>
                <span class="rank-name" :class="{ 'gold-1': idx === 0, silver: idx === 1, bronze: idx === 2 }">{{ item.name }}</span>
                <span class="rank-val">{{ rankFmt(item.value) }}</span>
              </div>
            </div>
          </Transition>
        </div>
      </div>

      <!-- Box 4: 封禁 -->
      <div class="stat-card clickable" @click="openBanModal">
        <div class="card-header-row">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="10"></circle><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"></line>
          </svg>
          <span class="card-title">封禁记录</span>
          <span class="card-badge red-badge">{{ banCount }}</span>
        </div>
        <div class="card-body-scroll">
          <div v-if="recentBans.length === 0" class="card-empty">暂无封禁记录</div>
          <div v-else class="ban-mini-list">
            <div v-for="ban in recentBans" :key="ban.ticket_number" class="ban-mini-item">
              <span class="ban-type" :class="parseIdentifier(ban.identifier).type.toLowerCase()">{{ parseIdentifier(ban.identifier).type }}</span>
              <span class="ban-target">{{ parseIdentifier(ban.identifier).value }}</span>
              <span class="ban-date">{{ ticksToDate(ban.start_date_ticks) }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 底部双栏：终端 + 每小时在线 -->
    <div class="bottom-split" :class="{ mobile: isMobile }">
      <section v-if="!isMobile" class="card terminal-wrapper">
        <ConsoleTerminal />
      </section>
      <section class="card hourly-card">
        <div class="card-header">
          <h3>每小时在线</h3>
          <input type="date" v-model="hourlyDate" class="filter-date" />
        </div>
        <div class="card-body">
          <div v-if="hourlyLoading" class="loading">加载中...</div>
          <div v-else class="bar-chart">
            <div v-for="h in 24" :key="h - 1" class="bar-col" :class="{ future: h - 1 > maxVisibleHour }"
              @mouseenter="h - 1 <= maxVisibleHour && showHourlyTooltip(h - 1, $event)" @mouseleave="hideHourlyTooltip">
              <div v-if="h - 1 <= maxVisibleHour" class="bar" :style="{ height: maxHourlyCount > 0 ? Math.max(4, (barHeights[h - 1] / maxHourlyCount) * 140) + 'px' : '4px' }"></div>
              <div v-else class="bar future-bar"></div>
              <span class="bar-label">{{ String(h - 1).padStart(2, '0') }}</span>
            </div>
          </div>
        </div>
      </section>
    </div>

    <!-- Tooltip -->
    <Teleport to="body">
      <div v-if="hourlyTooltip.show" class="hourly-tooltip" :style="{ left: hourlyTooltip.x + 12 + 'px', top: hourlyTooltip.y - 10 + 'px' }">
        <div class="tooltip-hour">{{ String(hourlyTooltip.hour).padStart(2, '0') }}:00</div>
        <div class="tooltip-count">{{ hourlyTooltip.names.length }}人在线</div>
        <div v-if="hourlyTooltip.names.length > 0" class="tooltip-ids">{{ hourlyTooltip.names.join(', ') }}</div>
      </div>
    </Teleport>

    <!-- 封禁模态框 -->
    <Teleport to="body">
      <div v-if="showBanModal" class="modal-overlay" @click.self="closeBanModal">
        <div class="modal-container" @click.self.stop>
          <div class="modal-header-bar">
            <h2>封禁列表</h2>
            <button class="modal-close-btn" @click="closeBanModal">✕</button>
          </div>
          <div class="modal-body-area"><BanListView /></div>
        </div>
      </div>
    </Teleport>

    <!-- 排行榜模态框 -->
    <Teleport to="body">
      <div v-if="showRankModal" class="modal-overlay" @click.self="closeRankModal">
        <div class="modal-container modal-rank-container" @click.self.stop>
          <div class="modal-header-bar">
            <h2>排行榜 — {{ rankTypes.find(t => t.key === rankModalType)?.label }}</h2>
            <button class="modal-close-btn" @click="closeRankModal">✕</button>
          </div>
          <div class="modal-body-area">
            <div v-if="rankModalData.length === 0" class="loading">暂无数据</div>
            <template v-else>
              <!-- 表头统计信息 -->
              <div class="rank-modal-stats">
                <span class="rms-label">{{ rankTypes.find(t => t.key === rankModalType)?.label }}排行</span>
                <span class="rms-total">共 {{ rankModalTotal }} 人</span>
              </div>
              <div class="rank-modal-table-wrap">
                <table class="rank-modal-table">
                  <thead>
                    <tr><th class="col-r">#</th><th class="col-n">玩家</th><th class="col-v">数值</th></tr>
                  </thead>
                  <tbody>
                    <tr v-for="(item, idx) in rankModalData" :key="item.name"
                      :class="{ 'row-gold': (rankModalPage-1)*rankModalPageSize+idx === 0, 'row-silver': (rankModalPage-1)*rankModalPageSize+idx === 1, 'row-bronze': (rankModalPage-1)*rankModalPageSize+idx === 2 }">
                      <td class="col-r"><span class="rank-num" :class="{ 'gold-1': (rankModalPage-1)*rankModalPageSize+idx === 0, silver: (rankModalPage-1)*rankModalPageSize+idx === 1, bronze: (rankModalPage-1)*rankModalPageSize+idx === 2 }">{{ (rankModalPage-1)*rankModalPageSize + idx + 1 }}</span></td>
                      <td class="col-n clickable-cell" :class="{ 'gold-1': (rankModalPage-1)*rankModalPageSize+idx === 0, silver: (rankModalPage-1)*rankModalPageSize+idx === 1, bronze: (rankModalPage-1)*rankModalPageSize+idx === 2 }" @click.stop="openPlayerDetail(item.name)" title="查看玩家详情">{{ item.name }}</td>
                      <td class="col-v">{{ rankModalFmt(item.value) }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <!-- 分页栏 -->
              <div class="pagination-bar">
                <div class="pagination-info">共 {{ rankModalTotal }} 条，{{ rankModalTotalPages }} 页</div>
                <div class="pagination-controls">
                  <button class="page-btn" :disabled="rankModalPage <= 1" @click="goRankModalPage(1)">«</button>
                  <button class="page-btn" :disabled="rankModalPage <= 1" @click="goRankModalPage(rankModalPage - 1)">‹</button>
                  <span class="page-indicator">{{ rankModalPage }} / {{ rankModalTotalPages }}</span>
                  <button class="page-btn" :disabled="rankModalPage >= rankModalTotalPages" @click="goRankModalPage(rankModalPage + 1)">›</button>
                  <button class="page-btn" :disabled="rankModalPage >= rankModalTotalPages" @click="goRankModalPage(rankModalTotalPages)">»</button>
                </div>
                <div class="pagination-size">
                  <label>每页</label>
                  <select v-model="rankModalPageSize" @change="setRankModalPageSize(Number(rankModalPageSize))">
                    <option :value="10">10</option>
                    <option :value="20">20</option>
                    <option :value="50">50</option>
                    <option :value="100">100</option>
                  </select>
                  <label>条</label>
                </div>
              </div>
            </template>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.dashboard { padding: 20px 32px; max-width: 1920px; margin: 0 auto; }

/* ── 四个等高卡片 ── */
.stat-cards {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
  margin-bottom: 18px;
}
.stat-cards > * { min-width: 0; }

.stat-card {
  display: flex; flex-direction: column;
  background: var(--bg-card); border: 1px solid var(--border-light); border-radius: 14px;
  padding: 20px;
  min-height: 300px;
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
  overflow: hidden;
  transition: transform 0.25s cubic-bezier(0.34, 1.56, 0.64, 1), box-shadow 0.25s ease;
}
.stat-card:hover { transform: translateY(-3px); box-shadow: 0 8px 28px rgba(0, 0, 0, 0.07); }
.stat-card.clickable { cursor: pointer; }
.stat-card.clickable:hover { border-color: rgba(99, 102, 241, 0.3); }

.card-header-row { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; flex-shrink: 0; }
.card-title { font-size: 0.95rem; font-weight: 700; color: var(--text-primary); flex: 1; }
.card-badge { padding: 2px 10px; border-radius: 20px; font-size: 0.75rem; font-weight: 700; }
.green-badge { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
.red-badge { background: rgba(239, 68, 68, 0.15); color: #ef4444; }

.card-body-scroll { flex: 1; overflow-y: auto; overflow-x: hidden; min-width: 0; }
.card-empty { color: var(--text-muted); font-size: 0.85rem; padding: 10px 0; }
.card-footer { flex-shrink: 0; padding-top: 12px; margin-top: 12px; border-top: 1px solid var(--border-light); font-size: 0.82rem; color: var(--text-secondary); }
.card-footer strong { color: var(--text-primary); font-variant-numeric: tabular-nums; }
.card-footer-action { color: var(--accent-primary); font-weight: 600; font-size: 0.82rem; }
.dual-footer { display: flex; justify-content: space-between; align-items: center; }
.dual-footer .qq-val { background: linear-gradient(135deg, #3b82f6, #6366f1); -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text; }

/* ── 大号统计行 ── */
.big-stat-row { display: flex; flex-direction: column; align-items: center; gap: 4px; }
.big-stat-label { font-size: 0.82rem; color: var(--text-secondary); }
.big-stat-value { font-size: 2.2rem; font-weight: 800; color: var(--text-primary); font-variant-numeric: tabular-nums; line-height: 1.1; }
.big-stat-value.qq-val { background: linear-gradient(135deg, #3b82f6, #6366f1); -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text; }

/* ── 在线玩家 ── */
.online-tags { display: flex; flex-wrap: wrap; gap: 6px; min-width: 0; }
.online-tag { display: inline-flex; align-items: center; gap: 5px; padding: 5px 10px; background: var(--bg-tertiary); border-radius: 8px; font-size: 0.82rem; font-weight: 500; color: var(--text-primary); border: 1px solid var(--border-light); max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.online-tag.clickable-tag:hover { border-color: #22c55e !important; background: rgba(34, 197, 94, 0.06); }
.dot { width: 6px; height: 6px; border-radius: 50%; background: #22c55e; box-shadow: 0 0 6px rgba(34, 197, 94, 0.5); }

/* ── 排行榜 ── */
.switch-btn {
  background: var(--bg-tertiary); border: 1px solid var(--border-light); border-radius: 6px;
  width: 28px; height: 28px; font-size: 1rem; cursor: pointer; color: var(--text-secondary);
  display: flex; align-items: center; justify-content: center; transition: all 0.2s;
}
.switch-btn:hover { background: rgba(245, 158, 11, 0.12); border-color: #f59e0b; color: #f59e0b; }

.rank-mini-list { display: flex; flex-direction: column; gap: 5px; }
.rank-mini-item { display: flex; align-items: center; gap: 8px; padding: 6px 8px; background: var(--bg-tertiary); border-radius: 8px; font-size: 0.82rem; border: 1px solid var(--border-light); }

/* ── 可点击标签 ── */
.clickable-tag { cursor: pointer; transition: all 0.2s ease; }
.clickable-tag:hover { border-color: var(--accent-primary) !important; background: rgba(99, 102, 241, 0.06); }

/* ── 排名序号：纯数字，无背景圆 ── */
.rank-num { 
  width: 18px; text-align: center; 
  font-size: 0.78rem; font-weight: 700; 
  color: var(--text-muted); flex-shrink: 0; 
  font-variant-numeric: tabular-nums;
}
.rank-name { flex: 1; font-weight: 500; color: var(--text-primary); min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

/* ── 第一名：烈焰金·极光（最终版）── */
.rank-num.gold-1, .rank-name.gold-1 {
  font-weight: 800;
  background-image: linear-gradient(90deg, #f97316, #ef4444, #e879f9, #818cf8, #e879f9, #ef4444, #f97316);
  background-size: 400% auto;
  background-clip: text;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  animation: shimmer-smooth 3s ease-in-out infinite alternate;
}

/* ── 银：双高光扫光 ── */
.rank-num.silver, .rank-name.silver {
  font-weight: 800;
  background-image: linear-gradient(90deg,
    #9ca3af 0%, #e5e7eb 10%, #ffffff 18%, #e5e7eb 25%, #9ca3af 35%,
    #9ca3af 45%, #e5e7eb 55%, #ffffff 63%, #e5e7eb 70%, #9ca3af 80%
  );
  background-size: 300% auto;
  background-clip: text;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  animation: shimmer 2s linear infinite;
}

/* ── 铜：古朴铜色流光 ── */
.rank-num.bronze, .rank-name.bronze {
  font-weight: 800;
  background-image: linear-gradient(90deg, #cd7f32, #f59e0b, #cd7f32, #a0522d, #cd7f32);
  background-size: 200% auto;
  background-clip: text;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  animation: shimmer 2.5s linear infinite;
}

@keyframes shimmer-smooth {
  0% { background-position: 0% center; }
  100% { background-position: 100% center; }
}

@keyframes shimmer {
  0% { background-position: 0% center; }
  100% { background-position: 200% center; }
}

@keyframes glow-pulse {
  0%, 100% { filter: brightness(1); }
  50% { filter: brightness(1.4); }
}

.rank-val { color: var(--text-secondary); font-weight: 600; font-size: 0.78rem; font-variant-numeric: tabular-nums; flex-shrink: 0; }

.rank-fade-enter-active,
.rank-fade-leave-active {
  transition: opacity 0.25s ease;
}
.rank-fade-enter-from,
.rank-fade-leave-to {
  opacity: 0;
}

/* ── 封禁迷你列表 ── */
.ban-mini-list { display: flex; flex-direction: column; gap: 5px; }
.ban-mini-item { display: flex; align-items: center; gap: 8px; padding: 6px 8px; background: var(--bg-tertiary); border-radius: 8px; font-size: 0.8rem; border: 1px solid var(--border-light); }
.ban-type { padding: 1px 6px; border-radius: 5px; font-size: 0.65rem; font-weight: 700; flex-shrink: 0; }
.ban-type.ip { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
.ban-type.uuid { background: rgba(139, 92, 246, 0.15); color: #8b5cf6; }
.ban-type.账户 { background: rgba(22, 163, 74, 0.15); color: #16a34a; }
.ban-type.未知 { background: rgba(156, 163, 175, 0.15); color: #6b7280; }
.ban-target { flex: 1; font-weight: 500; color: var(--text-primary); min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ban-date { color: var(--text-muted); font-size: 0.72rem; flex-shrink: 0; }

/* ── 底部双栏：终端（固定10行）+ 每小时在线 ── */
.bottom-split {
  display: grid; grid-template-columns: 1.2fr 1.8fr; gap: 14px;
  height: 280px; min-height: 0;
}
.bottom-split > * { min-width: 0; }
.terminal-wrapper { display: flex; flex-direction: column; padding: 0; overflow: hidden; }
.terminal-wrapper .terminal-card { border: none; border-radius: 0; height: 100%; }

/* ── 柱状图 ── */
.hourly-card { background: var(--bg-card); border: 1px solid var(--border-light); border-radius: 14px; overflow: hidden; display: flex; flex-direction: column; }
.card-header { display: flex; align-items: center; justify-content: space-between; padding: 14px 18px; border-bottom: 1px solid var(--border-light); }
.card-header h3 { font-size: 1rem; font-weight: 600; color: var(--text-primary); margin: 0; }
.card-body { padding: 14px 18px; }
.bar-chart { display: flex; align-items: flex-end; gap: 2px; height: 170px; padding-top: 6px; }
.bar-col { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: flex-end; height: 100%; cursor: pointer; }
.bar-col.future { cursor: default; }
.bar { width: 100%; max-width: 20px; border-radius: 3px 3px 0 0; background: linear-gradient(180deg, #6366f1, #4f46e5); min-height: 4px; transition: opacity 0.15s; }
.bar-col:hover .bar { opacity: 0.75; }
.future-bar { width: 100%; max-width: 20px; border-radius: 3px 3px 0 0; background: var(--border-light); min-height: 4px; }
.bar-label { margin-top: 3px; font-size: 0.55rem; color: var(--text-secondary); }

/* ── 模态框 ── */
.modal-overlay { position: fixed; inset: 0; background: rgba(0, 0, 0, 0.5); display: flex; align-items: center; justify-content: center; z-index: 10000; animation: fadeIn 0.2s ease; }
@keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
.modal-container { background: var(--bg-primary); border-radius: 16px; width: 90vw; height: 85vh; display: flex; flex-direction: column; overflow: hidden; box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3); animation: scaleIn 0.25s cubic-bezier(0.34, 1.56, 0.64, 1); }
.modal-rank-container { width: 70vw; height: 80vh; }
@keyframes scaleIn { from { transform: scale(0.85); opacity: 0; } to { transform: scale(1); opacity: 1; } }
.modal-header-bar { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid var(--border-light); flex-shrink: 0; }
.modal-header-bar h2 { margin: 0; font-size: 1.2rem; color: var(--text-primary); }
.modal-close-btn { width: 34px; height: 34px; border-radius: 10px; border: 1px solid var(--border-light); background: var(--bg-tertiary); color: var(--text-secondary); font-size: 1rem; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.2s; }
.modal-close-btn:hover { background: rgba(239, 68, 68, 0.1); border-color: #ef4444; color: #ef4444; }
.modal-body-area { flex: 1; overflow-y: auto; padding: 0; display: flex; flex-direction: column; }

/* ── 模态框表头统计 ── */
.rank-modal-stats {
  display: flex; align-items: center; gap: 12px;
  padding: 12px 20px;
  border-bottom: 1px solid var(--border-light);
  flex-shrink: 0;
}
.rms-label { font-weight: 700; font-size: 0.9rem; color: var(--text-primary); }
.rms-total { font-size: 0.78rem; color: var(--text-secondary); background: var(--bg-tertiary); padding: 2px 10px; border-radius: 10px; }

/* ── 排行榜模态表格 ── */
.rank-modal-table-wrap { flex: 1; overflow-y: auto; }
.rank-modal-table { width: 100%; border-collapse: collapse; }
.rank-modal-table thead { position: sticky; top: 0; z-index: 1; }
.rank-modal-table thead tr { background: var(--bg-secondary); }
.rank-modal-table th {
  text-align: left; padding: 10px 16px;
  font-size: 0.75rem; font-weight: 700; color: var(--text-muted);
  text-transform: uppercase; letter-spacing: 0.5px;
  border-bottom: 2px solid var(--border-light);
}
.rank-modal-table td { padding: 9px 16px; font-size: 0.88rem; border-bottom: 1px solid var(--border-light); color: var(--text-primary); }
.rank-modal-table tbody tr:nth-child(even) { background: var(--bg-tertiary); }
.rank-modal-table tbody tr:hover td { background-color: var(--bg-hover); }
.rank-modal-table tbody tr.row-gold { background: rgba(245, 158, 11, 0.06); }
.rank-modal-table tbody tr.row-silver { background: rgba(156, 163, 175, 0.06); }
.rank-modal-table tbody tr.row-bronze { background: rgba(205, 127, 50, 0.06); }
.col-r { width: 48px; text-align: center; }
.col-n { font-weight: 500; }
.clickable-cell { cursor: pointer; transition: box-shadow 0.15s; border-radius: 4px; }
.clickable-cell:hover { box-shadow: inset 0 0 0 200px rgba(99, 102, 241, 0.08); }
.col-n.gold-1, .col-n.silver, .col-n.bronze {
  font-weight: 800;
  background-size: 200% auto;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  animation: shimmer 2.5s linear infinite;
}
.col-n.gold-1 {
  background-image: linear-gradient(90deg, #f97316, #ef4444, #e879f9, #818cf8, #e879f9, #ef4444, #f97316);
  background-size: 400% auto;
  animation: shimmer-smooth 3s ease-in-out infinite alternate;
}
.col-n.silver {
  background-image: linear-gradient(90deg,
    #9ca3af 0%, #e5e7eb 10%, #ffffff 18%, #e5e7eb 25%, #9ca3af 35%,
    #9ca3af 45%, #e5e7eb 55%, #ffffff 63%, #e5e7eb 70%, #9ca3af 80%
  );
  background-size: 300% auto;
  animation: shimmer 2s linear infinite;
}
.col-n.bronze { background-image: linear-gradient(90deg, #cd7f32, #f59e0b, #cd7f32, #a0522d, #cd7f32); }
.col-v {
  width: 90px; text-align: right;
  font-variant-numeric: tabular-nums;
  font-weight: 600; color: var(--text-secondary);
}

/* ── 筛选 ── */
.filter-date { padding: 6px 28px 6px 12px; border: 1px solid var(--border-light); border-radius: 6px; background: var(--bg-input); color: var(--text-primary); font-size: 0.85rem; cursor: pointer; outline: none; transition: border-color 0.2s; -webkit-appearance: none; -moz-appearance: none; appearance: none; min-width: 110px; }
.filter-date:hover { border-color: var(--accent-primary); }
.filter-date:focus { border-color: var(--accent-primary); box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent); }

/* ── Tooltip ── */
.hourly-tooltip { position: fixed; z-index: 9999; background: var(--bg-card); border: 1px solid var(--border-light); border-radius: 8px; padding: 10px 14px; box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12); pointer-events: none; font-size: 0.8rem; color: var(--text-primary); }
.tooltip-hour { font-weight: 600; margin-bottom: 2px; }
.tooltip-count { color: var(--accent-primary); }
.tooltip-ids { margin-top: 4px; color: var(--text-secondary); font-size: 0.7rem; max-width: 200px; word-break: break-all; }

.loading { text-align: center; padding: 20px 0; color: var(--text-secondary); font-size: 0.85rem; }

/* ── 分页栏 ── */
.pagination-bar {
  display: flex; align-items: center; justify-content: space-between;
  flex-wrap: wrap; gap: 10px;
  padding: 14px 12px 6px; border-top: 1px solid var(--border-light);
  font-size: 0.82rem; color: var(--text-secondary);
}
.pagination-info { white-space: nowrap; }
.pagination-controls { display: flex; align-items: center; gap: 4px; }
.page-btn {
  width: 30px; height: 30px; border-radius: 6px;
  border: 1px solid var(--border-light); background: var(--bg-tertiary);
  color: var(--text-primary); font-size: 0.85rem; cursor: pointer;
  display: flex; align-items: center; justify-content: center;
  transition: all 0.15s;
}
.page-btn:hover:not(:disabled) { background: var(--accent-primary); color: #fff; border-color: var(--accent-primary); }
.page-btn:disabled { opacity: 0.3; cursor: default; }
.page-indicator { min-width: 60px; text-align: center; font-weight: 600; color: var(--text-primary); }
.pagination-size {
  display: flex; align-items: center; gap: 6px;
}
.pagination-size select {
  padding: 4px 6px; border: 1px solid var(--border-light); border-radius: 6px;
  background: var(--bg-input); color: var(--text-primary); font-size: 0.8rem;
  outline: none; cursor: pointer;
}
.pagination-size select:hover { border-color: var(--accent-primary); }

@media (max-width: 960px) {
  .stat-cards { grid-template-columns: repeat(2, 1fr); }
  .bottom-split { grid-template-columns: 1fr; }
  .modal-rank-container { width: 90vw; }
}
@media (max-width: 767px) {
  .dashboard { padding: 8px 8px 0; }
  .stat-cards { gap: 8px; }
  .stat-card { min-height: 200px; padding: 12px; }
  .bottom-split.mobile { grid-template-columns: 1fr; height: auto; min-height: 220px; }
  .bottom-split.mobile .hourly-card { border-radius: 10px; }
  .bottom-split.mobile .bar-chart { height: 130px; }
  .big-stat-value { font-size: 1.6rem; }
  .card-body-scroll { overflow-y: visible; }
}
@media (max-width: 540px) {
  .stat-cards { grid-template-columns: 1fr; }
}
</style>

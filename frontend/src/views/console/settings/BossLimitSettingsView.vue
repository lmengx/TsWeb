<script setup>
import { ref, onMounted, watch, onUnmounted } from 'vue'
import { get, post } from '../../../utils/api.js'

const loading = ref(true)
const error = ref('')
const success = ref('')
let saveTimer = null
let ready = false

const bossLimitMode = ref('disabled')
const bossLimitMinPlayers = ref(7)
const quitLimitEnabled = ref(false)
const lateCompEnabled = ref(false)

// BossLimit 活跃追踪状态
const bossLimitStatus = ref(null)
const statusLoading = ref(false)
let statusTimer = null

const bossModeOptions = [
  { value: 'disabled', label: '不做任何限制' },
  { value: 'playerlimit', label: '按最低人数限制' },
  { value: 'killrequired', label: '不允许召唤未击败的 Boss' }
]

const autoSave = () => {
  if (!ready) return
  clearTimeout(saveTimer)
  saveTimer = setTimeout(async () => {
    error.value = ''
    success.value = ''
    try {
      const res = await post('/api/config/boss', {
        bossLimitMode: bossLimitMode.value,
        bossLimitEnabled: bossLimitMode.value !== 'disabled',
        bossLimitMinPlayers: bossLimitMinPlayers.value,
        quitLimitEnabled: quitLimitEnabled.value,
        lateCompEnabled: lateCompEnabled.value
      })
      const data = await res.json()
      if (data.status === '200') {
        success.value = '已保存'
        setTimeout(() => { success.value = '' }, 1500)
      } else {
        error.value = data.error || '保存失败'
      }
    } catch (err) {
      error.value = '保存失败: ' + err.message
    }
  }, 500)
}

watch(bossLimitMode, autoSave)
watch(bossLimitMinPlayers, autoSave)
watch(quitLimitEnabled, autoSave)
watch(lateCompEnabled, autoSave)

const fetchBossLimitStatus = async () => {
  statusLoading.value = true
  try {
    const res = await get('/api/config/bosslimit/status')
    const data = await res.json()
    if (data.status === '200') {
      bossLimitStatus.value = data
    }
  } catch {
    // 静默失败，不阻塞页面
  }
  statusLoading.value = false
}

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/boss')
    const data = await res.json()
    if (data.bossLimitMode !== undefined) bossLimitMode.value = data.bossLimitMode
    if (data.bossLimitMinPlayers !== undefined) bossLimitMinPlayers.value = data.bossLimitMinPlayers
    if (data.quitLimitEnabled !== undefined) quitLimitEnabled.value = data.quitLimitEnabled
    if (data.lateCompEnabled !== undefined) lateCompEnabled.value = data.lateCompEnabled
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  ready = true
  loading.value = false

  // 加载 bosslimit 活跃追踪状态
  fetchBossLimitStatus()
}

onMounted(() => {
  fetchConfig()
  // 每 10 秒刷新一次活跃追踪状态
  statusTimer = setInterval(fetchBossLimitStatus, 10000)
})

onUnmounted(() => {
  if (statusTimer) clearInterval(statusTimer)
})
</script>

<template>
  <div class="settings-page">
    <div v-if="loading" class="loading-state">
      <p>加载中...</p>
    </div>

    <div v-else class="settings-content">
      <div class="settings-grid">
        <!-- Boss 限制 -->
        <div class="section-card">
          <h3>Boss 召唤限制</h3>
          <p class="section-desc">控制 Boss 召唤的拦截策略</p>
          <div class="radio-group">
            <label
              v-for="opt in bossModeOptions"
              :key="opt.value"
              class="radio-item"
              :class="{ active: bossLimitMode === opt.value }"
            >
              <input
                type="radio"
                v-model="bossLimitMode"
                :value="opt.value"
                class="radio-input"
              />
              <span class="radio-label">{{ opt.label }}</span>
            </label>
          </div>
          <div v-if="bossLimitMode === 'playerlimit'" class="toggle-row">
            <span class="toggle-label">最低在线人数</span>
            <div class="number-control">
              <button class="num-btn" @click="bossLimitMinPlayers = Math.max(1, bossLimitMinPlayers - 1)">−</button>
              <span class="num-value">{{ bossLimitMinPlayers }}</span>
              <button class="num-btn" @click="bossLimitMinPlayers = Math.min(999, bossLimitMinPlayers + 1)">+</button>
            </div>
          </div>
        </div>

        <!-- Boss 退出惩罚 + 晚入补偿 -->
        <div class="section-card">
          <h3>Boss 退出惩罚 & 晚入补偿</h3>
          <p class="section-desc">控制玩家在 Boss 战中退出或晚入的行为处理</p>

          <div class="toggle-row">
            <span class="toggle-label">退出惩罚</span>
            <span class="toggle-hint">战斗中退出的玩家上线后将被击杀</span>
            <label class="switch">
              <input type="checkbox" v-model="quitLimitEnabled" />
              <span class="slider"></span>
            </label>
          </div>

          <div class="toggle-row">
            <span class="toggle-label">晚入补偿</span>
            <span class="toggle-hint">新加入的玩家按比例增加 Boss 血量</span>
            <label class="switch">
              <input type="checkbox" v-model="lateCompEnabled" />
              <span class="slider"></span>
            </label>
          </div>
        </div>

        <!-- 活跃追踪状态 -->
        <div v-if="bossLimitStatus" class="section-card">
          <div class="tracking-header">
            <h3 class="tracking-title">当前活跃追踪</h3>
            <span v-if="statusLoading" class="tracking-refresh">刷新中...</span>
          </div>
          <div class="tracking-grid">
            <div class="tracking-stat">
              <span class="stat-number">{{ bossLimitStatus.trackedBosses ?? 0 }}</span>
              <span class="stat-label">追踪 BOSS</span>
            </div>
            <div class="tracking-stat">
              <span class="stat-number">{{ bossLimitStatus.trackedPlayers ?? 0 }}</span>
              <span class="stat-label">伤害者</span>
            </div>
          </div>
          <div v-if="bossLimitStatus.activeBosses && bossLimitStatus.activeBosses.length > 0" class="tracking-detail">
            <div v-for="(boss, idx) in bossLimitStatus.activeBosses" :key="idx" class="boss-track-row">
              <span class="boss-track-name">{{ boss.bossName }}</span>
              <span class="boss-track-dmg">{{ boss.damagerCount }} 人伤害</span>
              <span class="boss-track-spawn">出现时 {{ boss.onlineOnSpawn }}人在线</span>
            </div>
          </div>
          <div v-else class="tracking-idle">
            当前无活跃 Boss 战
          </div>
        </div>
      </div>

      <!-- Toast 通知 -->
      <Transition name="toast">
        <div v-if="success" class="toast toast-success">
          <svg class="toast-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18">
            <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/>
          </svg>
          <span>{{ success }}</span>
        </div>
      </Transition>
      <Transition name="toast">
        <div v-if="error" class="toast toast-error">
          <svg class="toast-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18">
            <path fill-rule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clip-rule="evenodd"/>
          </svg>
          <span>{{ error }}</span>
        </div>
      </Transition>
    </div>
  </div>
</template>

<style scoped>
.settings-page {
  padding: 20px;
  width: 100%;
}

.settings-content {
  max-width: 700px;
}

.loading-state {
  text-align: center;
  padding: 60px;
  color: var(--text-muted);
}

.settings-grid {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.section-card {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.section-card h3 {
  margin: 0 0 4px 0;
  color: var(--text-primary);
  font-size: 1.1rem;
  font-weight: 600;
}

.section-desc {
  margin: 0 0 20px 0;
  color: var(--text-muted);
  font-size: 0.85rem;
}

.radio-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.radio-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: all 0.2s ease;
}

.radio-item:hover {
  border-color: var(--accent-primary);
}

.radio-item.active {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.08);
}

.radio-input {
  accent-color: var(--accent-primary);
}

.radio-label {
  color: var(--text-primary);
  font-size: 0.95rem;
}

.toggle-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 0;
  border-bottom: 1px solid var(--border-light);
}

.toggle-row:last-child {
  border-bottom: none;
}

.toggle-label {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.95rem;
}

.toggle-hint {
  flex: 1;
  color: var(--text-muted);
  font-size: 0.8rem;
}

.switch {
  position: relative;
  display: inline-block;
  width: 48px;
  height: 26px;
  flex-shrink: 0;
}

.switch input {
  opacity: 0;
  width: 0;
  height: 0;
}

.slider {
  position: absolute;
  cursor: pointer;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: var(--bg-hover);
  border: 2px solid var(--border-color);
  border-radius: 26px;
  transition: all 0.3s ease;
}

.slider::before {
  content: '';
  position: absolute;
  height: 18px;
  width: 18px;
  left: 2px;
  bottom: 2px;
  background: var(--text-muted);
  border-radius: 50%;
  transition: all 0.3s ease;
}

.switch input:checked + .slider {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
}

.switch input:checked + .slider::before {
  transform: translateX(22px);
  background: white;
}

.number-control {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-left: auto;
}

.num-btn {
  width: 32px;
  height: 32px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 1rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.num-btn:hover {
  border-color: var(--accent-primary);
}

.num-value {
  min-width: 32px;
  text-align: center;
  color: var(--text-primary);
  font-weight: 600;
  font-size: 1rem;
}

.tracking-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.tracking-title {
  margin: 0 !important;
}

.tracking-refresh {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.tracking-grid {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
}

.tracking-stat {
  flex: 1;
  text-align: center;
  padding: 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-light);
}

.stat-number {
  display: block;
  font-size: 1.6rem;
  font-weight: 700;
  color: var(--accent-primary);
}

.stat-label {
  display: block;
  margin-top: 4px;
  font-size: 0.8rem;
  color: var(--text-muted);
}

.tracking-detail {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.boss-track-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-light);
  font-size: 0.85rem;
}

.boss-track-name {
  color: var(--text-primary);
  font-weight: 500;
}

.boss-track-dmg,
.boss-track-spawn {
  color: var(--text-muted);
}

.tracking-idle {
  text-align: center;
  padding: 20px;
  color: var(--text-muted);
  font-size: 0.9rem;
}

.toast {
  position: fixed;
  top: 20px;
  right: 20px;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 18px;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  z-index: 2000;
  box-shadow: var(--shadow-lg);
}

.toast-success {
  background: rgba(34, 197, 94, 0.15);
  color: var(--accent-secondary);
  border: 1px solid rgba(34, 197, 94, 0.3);
}

.toast-error {
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.toast-enter-active,
.toast-leave-active {
  transition: all 0.3s ease;
}

.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(-10px);
}
</style>

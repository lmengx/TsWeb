<script setup>
import { ref, onMounted, watch, nextTick, onUnmounted } from 'vue'
import { get, post } from '../utils/api.js'
import { useRouter } from 'vue-router'

const router = useRouter()
const loading = ref(true)
const error = ref('')
const success = ref('')
let saveTimer = null
let ready = false

const registerMode = ref('default')
const bossLimitMode = ref('disabled')
const bossLimitMinPlayers = ref(7)
const quitLimitEnabled = ref(false)
const lateCompEnabled = ref(false)

// BossLimit 活跃追踪状态
const bossLimitStatus = ref(null)
const statusLoading = ref(false)
let statusTimer = null

const registerModeOptions = [
  { value: 'default', label: '默认模式 - 允许手动注册' },
  { value: 'auto', label: '自动注册 - 新玩家自动创建账户' },
  { value: 'block', label: '白名单模式 - 仅已注册玩家可进入' }
]

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
      const res = await post('/api/config/tsweb', {
        mode: registerMode.value,
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

watch(registerMode, autoSave)
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
    const res = await get('/api/config/tsweb')
    const data = await res.json()
    if (data.mode !== undefined) registerMode.value = data.mode
    if (data.bossLimitMode !== undefined) bossLimitMode.value = data.bossLimitMode
    if (data.bossLimitMinPlayers !== undefined) bossLimitMinPlayers.value = data.bossLimitMinPlayers
    if (data.quitLimitEnabled !== undefined) quitLimitEnabled.value = data.quitLimitEnabled
    if (data.lateCompEnabled !== undefined) lateCompEnabled.value = data.lateCompEnabled
    await nextTick()
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  ready = true
  loading.value = false

  // 加载 bosslimit 活跃追踪状态
  fetchBossLimitStatus()
}

const goToAppConfig = () => {
  router.push('/console/settings/app')
}

const goToPromotionConfig = () => {
  router.push('/console/settings/promotion')
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
    <div class="page-header">
      <h2>服务器设置</h2>
    </div>

    <div class="settings-content">
      <div v-if="loading" class="loading-state">
        <p>加载中...</p>
      </div>

      <div v-else class="settings-grid">
        <!-- 注册模式 -->
        <div class="section-card">
          <h3>注册模式</h3>
          <p class="section-desc">控制新玩家的注册方式</p>
          <div class="radio-group">
            <label
              v-for="opt in registerModeOptions"
              :key="opt.value"
              class="radio-item"
              :class="{ active: registerMode === opt.value }"
            >
              <input
                type="radio"
                v-model="registerMode"
                :value="opt.value"
                class="radio-input"
              />
              <span class="radio-label">{{ opt.label }}</span>
            </label>
      </div>

      <!-- 导航到权限提升配置 -->
      <div class="section-card">
        <h3>权限提升配置</h3>
        <p class="section-desc">配置 QQ 绑定和游玩时长的自动权限提升规则</p>
        <button class="nav-btn" @click="goToPromotionConfig">
          前往配置 →
        </button>
      </div>
    </div>

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

          <!-- 活跃追踪状态 -->
          <div v-if="bossLimitStatus" class="tracking-status">
            <div class="tracking-header">
              <span class="tracking-title">当前活跃追踪</span>
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

.page-header {
  margin-bottom: 20px;
}

.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
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
  font-size: 0.9rem;
}

.toggle-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 0;
}

.toggle-label {
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
}

.switch {
  position: relative;
  display: inline-block;
  width: 50px;
  height: 28px;
}

.switch input {
  opacity: 0;
  width: 0;
  height: 0;
}

.slider {
  position: absolute;
  cursor: pointer;
  top: 0; left: 0; right: 0; bottom: 0;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 28px;
  transition: all 0.3s ease;
}

.slider::before {
  content: '';
  position: absolute;
  height: 18px; width: 18px;
  left: 3px; bottom: 3px;
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

.nav-btn {
  padding: 12px 24px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.25s ease;
}

.nav-btn:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.status-bar {
  margin-top: 16px;
  min-height: 24px;
}

.number-control {
  display: flex;
  align-items: center;
  gap: 12px;
}

.num-btn {
  width: 32px;
  height: 32px;
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  background: var(--bg-tertiary);
  color: var(--text-primary);
  font-size: 1.1rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;
}

.num-btn:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.num-value {
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
  min-width: 32px;
  text-align: center;
}

/* ====== Toast 通知 ====== */
.toast {
  position: fixed;
  top: 20px;
  right: 24px;
  z-index: 9999;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 18px;
  border-radius: var(--radius-md, 8px);
  font-size: 0.88rem;
  font-weight: 500;
  box-shadow: 0 4px 16px rgba(0,0,0,0.15);
  pointer-events: none;
  max-width: 360px;
}

.toast-success {
  color: #065f46;
  background: #d1fae5;
  border: 1px solid #6ee7b7;
}

.toast-error {
  color: #991b1b;
  background: #fee2e2;
  border: 1px solid #fca5a5;
}

.toast-icon {
  flex-shrink: 0;
}

.toast-enter-active {
  transition: all 0.3s ease-out;
}
.toast-leave-active {
  transition: all 0.25s ease-in;
}
.toast-enter-from {
  opacity: 0;
  transform: translateX(40px);
}
.toast-leave-to {
  opacity: 0;
  transform: translateX(40px);
}

.status-bar {
  margin-top: 16px;
  min-height: 24px;
}

.success-msg {
  color: var(--accent-secondary);
  font-size: 0.85rem;
}

.error-msg {
  color: var(--accent-error);
  font-size: 0.85rem;
  padding: 8px 12px;
  background: rgba(239, 68, 68, 0.1);
  border-radius: var(--radius-md);
  display: inline-block;
}

.toggle-hint {
  color: var(--text-muted);
  font-size: 0.78rem;
  flex: 1;
  margin-left: 8px;
}

/* ====== 活跃追踪状态 ====== */
.tracking-status {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid var(--border-color);
}

.tracking-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.tracking-title {
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--text-primary);
}

.tracking-refresh {
  font-size: 0.78rem;
  color: var(--text-muted);
}

.tracking-grid {
  display: flex;
  gap: 12px;
  margin-bottom: 12px;
}

.tracking-stat {
  flex: 1;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  padding: 12px;
  text-align: center;
}

.stat-number {
  display: block;
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--accent-primary);
}

.stat-label {
  display: block;
  font-size: 0.8rem;
  color: var(--text-muted);
  margin-top: 4px;
}

.tracking-detail {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.boss-track-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 12px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-sm);
  font-size: 0.85rem;
}

.boss-track-name {
  font-weight: 600;
  color: var(--text-primary);
  min-width: 0;
  flex: 1;
}

.boss-track-dmg {
  color: var(--accent-error);
  font-size: 0.8rem;
}

.boss-track-spawn {
  color: var(--text-muted);
  font-size: 0.78rem;
}

.tracking-idle {
  text-align: center;
  padding: 16px;
  color: var(--text-muted);
  font-size: 0.85rem;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
}
</style>

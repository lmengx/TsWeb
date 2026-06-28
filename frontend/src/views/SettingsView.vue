<script setup>
import { ref, onMounted, watch, nextTick } from 'vue'
import { get, post } from '../utils/api.js'
import { useRouter } from 'vue-router'

const router = useRouter()
const loading = ref(true)
const error = ref('')
const success = ref('')
let saveTimer = null
let ready = false

const registerMode = ref('default')
const bossLimitEnabled = ref(false)
const bossLimitMinPlayers = ref(7)

const modeOptions = [
  { value: 'default', label: '默认模式 - 允许手动注册' },
  { value: 'auto', label: '自动注册 - 新玩家自动创建账户' },
  { value: 'disable', label: '禁用注册 - 完全禁止注册' },
  { value: 'block', label: '阻止模式 - 未注册/UUID不匹配玩家断联' }
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
        bossLimitEnabled: bossLimitEnabled.value,
        bossLimitMinPlayers: bossLimitMinPlayers.value
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
watch(bossLimitEnabled, autoSave)
watch(bossLimitMinPlayers, autoSave)

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/tsweb')
    const data = await res.json()
    if (data.mode !== undefined) registerMode.value = data.mode
    if (data.bossLimitEnabled !== undefined) bossLimitEnabled.value = data.bossLimitEnabled
    if (data.bossLimitMinPlayers !== undefined) bossLimitMinPlayers.value = data.bossLimitMinPlayers
    await nextTick()
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  ready = true
  loading.value = false
}

const goToAppConfig = () => {
  router.push('/console/settings/app')
}

onMounted(() => {
  fetchConfig()
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
              v-for="opt in modeOptions"
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
        </div>

        <!-- Boss 限制 -->
        <div class="section-card">
          <h3>Boss 召唤限制</h3>
          <p class="section-desc">未击败的 Boss 需要达到最低在线人数方可召唤（无 spawnboss 权限者）</p>
          <div class="toggle-row">
            <span class="toggle-label">开启限制</span>
            <label class="switch">
              <input type="checkbox" v-model="bossLimitEnabled" />
              <span class="slider"></span>
            </label>
          </div>
          <div class="toggle-row">
            <span class="toggle-label">最低在线人数</span>
            <div class="number-control">
              <button class="num-btn" @click="bossLimitMinPlayers = Math.max(1, bossLimitMinPlayers - 1)">−</button>
              <span class="num-value">{{ bossLimitMinPlayers }}</span>
              <button class="num-btn" @click="bossLimitMinPlayers = Math.min(999, bossLimitMinPlayers + 1)">+</button>
            </div>
          </div>
        </div>

        <!-- Boss 限制 -->
        <div class="section-card">
          <h3>服务器连接配置</h3>
          <p class="section-desc">管理服务器连接配置</p>
          <button @click="goToAppConfig" class="nav-btn">
            前往应用配置
          </button>
        </div>
      </div>

      <div class="status-bar">
        <span v-if="success" class="success-msg">{{ success }}</span>
        <span v-if="error" class="error-msg">{{ error }}</span>
      </div>
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
</style>

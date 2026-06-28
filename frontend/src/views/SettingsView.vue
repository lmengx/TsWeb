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
const allowIP = ref('')
const allowIPs = ref([])
const allowMsg = ref('')

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
watch(allowIP, () => { allowMsg.value = '' })

const fetchAllowList = async () => {
  try {
    const res = await get('/api/config/tsweb/allowlist')
    const data = await res.json()
    if (data.status === '200' && Array.isArray(data.ips)) {
      allowIPs.value = data.ips
    }
  } catch {}
}

const addAllowIPHandler = async () => {
  const ip = allowIP.value.trim()
  if (!ip) { allowMsg.value = '请输入IP'; return }
  allowMsg.value = ''
  try {
    const res = await post('/api/config/tsweb/allow', { ip })
    const data = await res.json()
    if (data.status === '200') {
      allowIP.value = ''
      allowMsg.value = data.message
      fetchAllowList()
    } else {
      allowMsg.value = data.error || '添加失败'
    }
  } catch (err) {
    allowMsg.value = '添加失败: ' + err.message
  }
}

const clearAllowListHandler = async () => {
  try {
    const res = await post('/api/config/tsweb/allowclear')
    const data = await res.json()
    if (data.status === '200') {
      allowIPs.value = []
      allowMsg.value = data.message
    } else {
      allowMsg.value = data.error || '清空失败'
    }
  } catch (err) {
    allowMsg.value = '清空失败: ' + err.message
  }
}

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/tsweb')
    const data = await res.json()
    if (data.mode !== undefined) registerMode.value = data.mode
    if (data.bossLimitEnabled !== undefined) bossLimitEnabled.value = data.bossLimitEnabled
    if (data.bossLimitMinPlayers !== undefined) bossLimitMinPlayers.value = data.bossLimitMinPlayers
    fetchAllowList()
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

          <!-- 特许IP - 仅阻止模式可见 -->
          <div v-if="registerMode === 'block'" class="allow-section">
            <div class="allow-divider"></div>
            <h4 class="allow-title">特许IP</h4>
            <p class="allow-desc">添加一次性特许IP，绕过阻止模式进入服务器，用完即失效</p>
            <div class="allow-input-row">
              <input
                v-model="allowIP"
                type="text"
                placeholder="输入IP地址"
                class="allow-input"
                @keyup.enter="addAllowIPHandler"
              />
              <button @click="addAllowIPHandler" class="btn btn-primary">添加</button>
            </div>
            <div v-if="allowMsg" class="allow-msg">{{ allowMsg }}</div>
            <div v-if="allowIPs.length > 0" class="allow-list">
              <div class="allow-list-header">
                <span>当前特许IP（{{ allowIPs.length }} 个）</span>
                <button @click="clearAllowListHandler" class="btn-link-danger">清空全部</button>
              </div>
              <div v-for="ip in allowIPs" :key="ip" class="allow-item">
                <span class="ip-text">{{ ip }}</span>
              </div>
            </div>
            <div v-else class="allow-empty">当前没有特许IP</div>
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

.allow-section {
  margin-top: 8px;
}

.allow-divider {
  height: 1px;
  background: var(--border-color);
  margin: 16px 0;
}

.allow-title {
  margin: 0 0 2px 0;
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 600;
}

.allow-desc {
  margin: 0 0 14px 0;
  color: var(--text-muted);
  font-size: 0.8rem;
}

.allow-input-row {
  display: flex;
  gap: 10px;
  margin-bottom: 12px;
}

.allow-input {
  flex: 1;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  outline: none;
  transition: border-color 0.2s;
}

.allow-input:focus {
  border-color: var(--accent-primary);
}

.btn {
  padding: 10px 20px;
  border: none;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  white-space: nowrap;
}

.btn-primary {
  background: var(--accent-primary);
  color: #fff;
}

.btn-primary:hover {
  opacity: 0.85;
}

.btn-link-danger {
  background: none;
  border: none;
  color: var(--accent-error);
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  padding: 0;
  transition: opacity 0.2s;
}

.btn-link-danger:hover {
  opacity: 0.7;
}

.allow-msg {
  color: var(--accent-secondary);
  font-size: 0.85rem;
  margin-bottom: 10px;
}

.allow-list-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
  font-size: 0.85rem;
  color: var(--text-muted);
}

.allow-item {
  padding: 8px 12px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-sm);
  margin-bottom: 4px;
  font-family: monospace;
  font-size: 0.9rem;
  color: var(--text-primary);
}

.allow-empty {
  color: var(--text-muted);
  font-size: 0.85rem;
  padding: 12px 0;
}
</style>

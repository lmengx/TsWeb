<script setup>
import { ref, onMounted, watch } from 'vue'
import { get, post } from '../../../utils/api.js'

const loading = ref(true)
const error = ref('')
const success = ref('')
let saveTimer = null
let ready = false

const enabled = ref(false)
const intervalSeconds = ref(3600)
const pushToBackend = ref(true)

const autoSave = () => {
  if (!ready) return
  // 规范化间隔：非法输入回退 3600，限制 60~86400
  const raw = Number(intervalSeconds.value)
  const secs = Number.isFinite(raw) ? Math.max(60, Math.min(86400, Math.round(raw))) : 3600
  intervalSeconds.value = secs
  clearTimeout(saveTimer)
  saveTimer = setTimeout(async () => {
    error.value = ''
    success.value = ''
    try {
      const res = await post('/api/config/backup', {
        enabled: enabled.value,
        intervalSeconds: secs,
        pushToBackend: pushToBackend.value
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

watch(enabled, autoSave)
watch(intervalSeconds, autoSave)
watch(pushToBackend, autoSave)

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/backup')
    const data = await res.json()
    if (data.enabled !== undefined) enabled.value = data.enabled
    if (data.intervalSeconds !== undefined) intervalSeconds.value = data.intervalSeconds
    if (data.pushToBackend !== undefined) pushToBackend.value = data.pushToBackend
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  ready = true
  loading.value = false
}

onMounted(fetchConfig)
</script>

<template>
  <div class="settings-page">
    <div v-if="loading" class="loading-state">
      <p>加载中...</p>
    </div>

    <div v-else class="settings-content">
      <div class="settings-grid">
        <!-- 自动备份总开关 -->
        <div class="section-card">
          <h3>自动备份</h3>
          <p class="section-desc">
            定时将「世界地图 + tshock.sqlite + 房屋存档」打包为 zip 保存在服务器本地
            <code>TSWeb/Backup/</code> 目录（默认关闭）。
          </p>

          <div class="toggle-row">
            <span class="toggle-label">启用自动备份</span>
            <span class="toggle-hint">开启后按下方间隔周期执行</span>
            <label class="switch">
              <input type="checkbox" v-model="enabled" />
              <span class="slider"></span>
            </label>
          </div>
        </div>

        <!-- 间隔配置 -->
        <div class="section-card">
          <h3>备份间隔</h3>
          <p class="section-desc">两次备份之间的间隔时间，单位：秒（默认 3600 秒 = 1 小时）</p>

          <div class="interval-row">
            <div class="number-control">
              <button class="num-btn" @click="intervalSeconds = Math.max(60, (Number(intervalSeconds) || 60) - 60)">−</button>
              <input
                type="number"
                v-model.number="intervalSeconds"
                min="60"
                max="86400"
                step="60"
                class="num-input"
                title="可直接输入，范围 60~86400 秒"
              />
              <button class="num-btn" @click="intervalSeconds = Math.min(86400, (Number(intervalSeconds) || 60) + 60)">+</button>
            </div>
            <span class="interval-desc">
              约 {{ Math.round((Number(intervalSeconds) || 3600) / 60) }} 分钟
            </span>
          </div>
        </div>

        <!-- 后端推送 -->
        <div class="section-card">
          <h3>推送后端</h3>
          <p class="section-desc">
            备份完成后推送到后端的
            <code>data/backup/{服务器}/</code> 专门目录（需后端已连接并注册 webhook）。
            推送失败仅记录日志，本地备份包始终保留。
          </p>

          <div class="toggle-row">
            <span class="toggle-label">推送后端</span>
            <span class="toggle-hint">后端未连接时自动跳过</span>
            <label class="switch">
              <input type="checkbox" v-model="pushToBackend" />
              <span class="slider"></span>
            </label>
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
  line-height: 1.6;
}

.section-desc code {
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
  border-radius: 4px;
  padding: 1px 6px;
  font-size: 0.8rem;
  color: var(--accent-primary);
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

.interval-row {
  display: flex;
  align-items: center;
  gap: 16px;
}

.number-control {
  display: flex;
  align-items: center;
  gap: 12px;
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
  min-width: 64px;
  text-align: center;
  color: var(--text-primary);
  font-weight: 600;
  font-size: 1rem;
}

.num-input {
  width: 84px;
  height: 32px;
  text-align: center;
  font-size: 1rem;
  font-weight: 700;
  border: 2px solid var(--border-color);
  border-radius: var(--radius-sm);
  outline: none;
  color: var(--text-primary);
  background: var(--bg-tertiary);
  transition: border-color 0.15s ease;
  -moz-appearance: textfield;
  font-family: inherit;
}

.num-input::-webkit-outer-spin-button,
.num-input::-webkit-inner-spin-button {
  -webkit-appearance: none;
  margin: 0;
}

.num-input:focus {
  border-color: var(--accent-primary);
}

.interval-desc {
  color: var(--text-muted);
  font-size: 0.85rem;
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

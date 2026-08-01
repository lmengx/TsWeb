<script setup>
import { ref, onMounted, watch } from 'vue'
import { get, post } from '../../../utils/api.js'

const loading = ref(true)
const error = ref('')
const success = ref('')
let saveTimer = null
let ready = false

const registerMode = ref('default')
const kickPasswordMessage = ref('')

const registerModeOptions = [
  { value: 'default', label: '默认模式 - 允许手动注册' },
  { value: 'auto', label: '自动注册 - 新玩家自动创建账户' },
  { value: 'block', label: '白名单模式 - 仅已注册玩家可进入' }
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
        kickPasswordMessage: kickPasswordMessage.value
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
watch(kickPasswordMessage, autoSave)

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/tsweb')
    const data = await res.json()
    if (data.mode !== undefined) registerMode.value = data.mode
    if (data.kickPasswordMessage !== undefined) kickPasswordMessage.value = data.kickPasswordMessage
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
        </div>

        <!-- 密码不匹配踢出文本 -->
        <div class="section-card">
          <h3>密码不匹配踢出文本</h3>
          <p class="section-desc">
            玩家使用已有账号但密码验证失败（UUID 变更触发密码挑战）时，踢出时显示的提示文本。<br />
            每一行即为提示中的一行，修改后自动保存。
          </p>
          <textarea
            v-model="kickPasswordMessage"
            class="kick-textarea"
            rows="6"
            placeholder="输入踢出时显示的文本，每行一条提示"
          ></textarea>
          <div class="textarea-hint">
            支持换行。可用变量：无（纯文本提示）。
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

.kick-textarea {
  width: 100%;
  box-sizing: border-box;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  line-height: 1.6;
  resize: vertical;
  font-family: inherit;
  transition: all 0.25s ease;
}

.kick-textarea:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.textarea-hint {
  margin-top: 8px;
  color: var(--text-muted);
  font-size: 0.8rem;
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

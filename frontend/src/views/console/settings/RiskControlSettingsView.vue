<script setup>
import { ref, onMounted, watch, onUnmounted } from 'vue'
import { get, post } from '../../../utils/api.js'

const loading = ref(true)
const error = ref('')
const success = ref('')
let saveTimer = null
let ready = false

// ── 持久开关 ──
const blockAllEnter = ref(false)
const blockUnder1hEnter = ref(false)
const blockAllChat = ref(false)
const blockUnder1hChat = ref(false)

// ── 一次性动作 ──
const kickingAll = ref(false)
const kickingUnder1h = ref(false)

const autoSave = () => {
  if (!ready) return
  clearTimeout(saveTimer)
  saveTimer = setTimeout(async () => {
    error.value = ''
    success.value = ''
    try {
      const res = await post('/api/config/risk-control', {
        blockAllEnter: blockAllEnter.value,
        blockUnder1hEnter: blockUnder1hEnter.value,
        blockAllChat: blockAllChat.value,
        blockUnder1hChat: blockUnder1hChat.value,
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

watch(blockAllEnter, autoSave)
watch(blockUnder1hEnter, autoSave)
watch(blockAllChat, autoSave)
watch(blockUnder1hChat, autoSave)

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/risk-control')
    const data = await res.json()
    if (data.blockAllEnter !== undefined) blockAllEnter.value = data.blockAllEnter
    if (data.blockUnder1hEnter !== undefined) blockUnder1hEnter.value = data.blockUnder1hEnter
    if (data.blockAllChat !== undefined) blockAllChat.value = data.blockAllChat
    if (data.blockUnder1hChat !== undefined) blockUnder1hChat.value = data.blockUnder1hChat
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  ready = true
  loading.value = false
}

const executeKick = async (action, label) => {
  if (!confirm(`确定要${label}吗？此操作不可撤销。`)) return
  const kickRef = action === 'kick-all' ? kickingAll : kickingUnder1h
  kickRef.value = true
  error.value = ''
  success.value = ''
  try {
    const res = await post('/api/config/risk-control/action', { action })
    const data = await res.json()
    if (data.status === '200') {
      success.value = `已执行：${label}（${data.kicked ?? '未知'} 人）`
      setTimeout(() => { success.value = '' }, 3000)
    } else {
      error.value = data.error || `${label}失败`
    }
  } catch (err) {
    error.value = `${label}失败: ` + err.message
  } finally {
    kickRef.value = false
  }
}

onMounted(fetchConfig)
onUnmounted(() => { clearTimeout(saveTimer) })
</script>

<template>
  <div class="settings-page">
    <div v-if="loading" class="loading-state"><p>加载中...</p></div>

    <div v-else class="settings-content">
      <div class="settings-grid">

        <!-- ═══ 紧急踢出操作 ═══ -->
        <div class="section-card section-card-danger">
          <h3>⚡ 紧急踢出操作</h3>
          <p class="section-desc">立即执行一次性的踢出操作，不会持久保存状态</p>

          <div class="action-row">
            <div class="action-info">
              <span class="action-label">踢出所有在线玩家</span>
              <span class="action-hint">立即将当前所有在线玩家踢出服务器</span>
            </div>
            <button
              class="action-btn btn-danger"
              :disabled="kickingAll"
              @click="executeKick('kick-all', '踢出所有玩家')"
            >
              {{ kickingAll ? '执行中...' : '执行踢出' }}
            </button>
          </div>

          <div class="action-row">
            <div class="action-info">
              <span class="action-label">踢出游玩时间不足1小时玩家</span>
              <span class="action-hint">将所有累计游玩时间小于1小时的玩家踢出服务器</span>
            </div>
            <button
              class="action-btn btn-warning"
              :disabled="kickingUnder1h"
              @click="executeKick('kick-under-1h', '踢出不足1小时玩家')"
            >
              {{ kickingUnder1h ? '执行中...' : '执行踢出' }}
            </button>
          </div>
        </div>

        <!-- ═══ 进服限制 ═══ -->
        <div class="section-card">
          <h3>🚫 进服限制</h3>
          <p class="section-desc">拦截新玩家进入服务器，优先级最高（NetGetData int.MaxValue）</p>

          <div class="toggle-row">
            <div class="toggle-label-wrap">
              <span class="toggle-label">禁止所有玩家进入</span>
              <span class="toggle-hint">开启后所有玩家无法进入服务器，管理员同样受影响</span>
            </div>
            <label class="switch">
              <input type="checkbox" v-model="blockAllEnter" />
              <span class="slider"></span>
            </label>
          </div>

          <div class="toggle-row">
            <div class="toggle-label-wrap">
              <span class="toggle-label">禁止游玩时间不足1小时玩家进入</span>
              <span class="toggle-hint">累计游玩时间＜1小时的玩家进入时将被踢出</span>
            </div>
            <label class="switch">
              <input type="checkbox" v-model="blockUnder1hEnter" />
              <span class="slider"></span>
            </label>
          </div>
        </div>

        <!-- ═══ 发言/命令限制 ═══ -->
        <div class="section-card">
          <h3>💬 发言 & 命令限制</h3>
          <p class="section-desc">在数据包层面直接丢包，TShock 命令解析器永远不会收到该消息，含 /command 格式指令</p>

          <div class="toggle-row">
            <div class="toggle-label-wrap">
              <span class="toggle-label">禁止所有玩家发言</span>
              <span class="toggle-hint">所有玩家无法聊天，也无法执行任何命令（含管理员）</span>
            </div>
            <label class="switch">
              <input type="checkbox" v-model="blockAllChat" />
              <span class="slider"></span>
            </label>
          </div>

          <div class="toggle-row">
            <div class="toggle-label-wrap">
              <span class="toggle-label">禁止游玩时间不足1小时玩家发言</span>
              <span class="toggle-hint">累计游玩时间＜1小时的玩家无法聊天或执行命令</span>
            </div>
            <label class="switch">
              <input type="checkbox" v-model="blockUnder1hChat" />
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

.section-card-danger {
  border-color: rgba(239, 68, 68, 0.3);
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

/* ── 踢出操作行 ── */
.action-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 14px 0;
  border-bottom: 1px solid var(--border-light);
}

.action-row:last-child {
  border-bottom: none;
}

.action-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
  min-width: 0;
}

.action-label {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.95rem;
}

.action-hint {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.action-btn {
  flex-shrink: 0;
  padding: 8px 20px;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  border: none;
  transition: all 0.2s ease;
}

.action-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-danger {
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.btn-danger:hover:not(:disabled) {
  background: rgba(239, 68, 68, 0.25);
  border-color: rgba(239, 68, 68, 0.5);
}

.btn-warning {
  background: rgba(245, 158, 11, 0.15);
  color: #f59e0b;
  border: 1px solid rgba(245, 158, 11, 0.3);
}

.btn-warning:hover:not(:disabled) {
  background: rgba(245, 158, 11, 0.25);
  border-color: rgba(245, 158, 11, 0.5);
}

/* ── 开关行 ── */
.toggle-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 14px 0;
  border-bottom: 1px solid var(--border-light);
}

.toggle-row:last-child {
  border-bottom: none;
}

.toggle-label-wrap {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
  min-width: 0;
}

.toggle-label {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.95rem;
}

.toggle-hint {
  color: var(--text-muted);
  font-size: 0.8rem;
  line-height: 1.4;
}

.switch {
  position: relative;
  display: inline-block;
  width: 48px;
  height: 26px;
  flex-shrink: 0;
  margin-top: 2px;
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

/* ── Toast ── */
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

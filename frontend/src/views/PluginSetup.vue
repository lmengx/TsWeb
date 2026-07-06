<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const token = route.query.token || ''
const loading = ref(true)
const statusLoading = ref(false)
const setupCompleted = ref(false)
const configExists = ref(false)
const error = ref('')
const success = ref('')

const strategies = [
  {
    id: 'default',
    title: '默认模式',
    desc: '玩家需使用 /register 手动注册',
    details: [
      '玩家进入服务器后使用 /register 命令注册账户',
      '注册后使用 /login 命令登录',
      '标准的 TShock 注册流程'
    ],
    risk: '任意玩家均可随意创建账户，且手动输入指令会劝退部分玩家'
  },
  {
    id: 'auto',
    title: '自动注册',
    desc: '玩家进入时自动创建账户',
    details: [
      '玩家首次进入服务器时自动创建账户',
      '自动使用随机密码注册，玩家无需手动操作'
    ],
    risk: '为玩家省略手敲命令的过程，可以在服务器未受攻击时开放'
  },
  {
    id: 'block',
    title: '白名单模式',
    desc: '仅允许已注册玩家进入',
    details: [],
    risk: '最安全，可拓展',
    features: [],
    tags: ['QQ注册，接入机器人', '管理员手动过白']
  }
]

const selectedMode = ref('')
const showConfirm = computed(() => selectedMode.value !== '')

const selectMode = (mode) => {
  selectedMode.value = mode
}

const submitInit = async () => {
  if (!selectedMode.value) return
  statusLoading.value = true
  error.value = ''
  success.value = ''
  try {
    const res = await fetch('/api/setup/plugin-init', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token, mode: selectedMode.value })
    })
    const data = await res.json()
    if (data.error) {
      error.value = data.error
    } else {
      success.value = '插件初始化完成！'
      setupCompleted.value = true
    }
  } catch (err) {
    error.value = '请求失败: ' + err.message
  }
  statusLoading.value = false
}

const goToConsole = () => {
  router.push('/console')
}

onMounted(async () => {
  if (!token) {
    error.value = '缺少 Setup Token，请从初始化页面进入'
    loading.value = false
    return
  }
  try {
    const res = await fetch(`/api/setup/plugin-status?token=${encodeURIComponent(token)}`)
    const data = await res.json()
    if (data.setupCompleted) {
      setupCompleted.value = true
      configExists.value = true
    } else if (data.configExists) {
      configExists.value = true
    }
  } catch (err) {
    error.value = '检测失败: ' + err.message
  }
  loading.value = false
})
</script>

<template>
  <div class="plugin-setup-page">
    <div class="setup-card">
      <div class="setup-header">
        <h1>插件初始化</h1>
        <p class="setup-subtitle">配置 TSWeb 插件的进服策略</p>
      </div>

      <div v-if="loading" class="loading-state">
        <div class="spinner"></div>
        <p>正在检测插件状态...</p>
      </div>

      <div v-else-if="error && !setupCompleted" class="error-state">
        <p>❌ {{ error }}</p>
      </div>

      <template v-else-if="setupCompleted">
        <div class="completed-state">
          <div class="completed-icon">✅</div>
          <h2>已初始化完成</h2>
          <p v-if="configExists">插件配置已设置。</p>
          <button @click="goToConsole" class="primary-btn">前往控制台</button>
        </div>
      </template>

      <template v-else>
        <div class="strategy-section">
          <h3 class="section-title">选择进服策略</h3>
          <p class="section-desc">选择一个策略来控制新玩家的进服方式，策略可随时在控制台修改。</p>

          <div class="strategy-list">
            <div
              v-for="s in strategies"
              :key="s.id"
              class="strategy-card"
              :class="{ selected: selectedMode === s.id }"
              @click="selectMode(s.id)"
            >
              <div class="strategy-header">
                <div class="strategy-meta">
                  <span class="strategy-title">{{ s.title }}</span>
                  <span class="strategy-desc">{{ s.desc }}</span>
                </div>
                <span class="strategy-check" :class="{ checked: selectedMode === s.id }">
                  <svg v-if="selectedMode === s.id" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                    <polyline points="20 6 9 17 4 12"></polyline>
                  </svg>
                </span>
              </div>

              <!-- 详情 - 始终显示 -->
              <div class="strategy-body">
                <div class="risk-badge" :class="{ danger: s.id === 'default' }">
                  {{ s.risk }}
                </div>
                <div class="detail-list">
                  <div v-for="(d, i) in s.details" :key="i" class="detail-item">
                    <span class="detail-bullet">•</span>
                    <span>{{ d }}</span>
                  </div>
                </div>
                <div v-if="s.tags" class="tags-section">
                  <span v-for="(t, i) in s.tags" :key="i" class="tag-badge">{{ t }}</span>
                </div>
              </div>
            </div>
          </div>

          <Transition name="fade">
            <button
              v-if="showConfirm"
              @click="submitInit"
              :disabled="statusLoading"
              class="confirm-btn"
            >
              {{ statusLoading ? '配置中...' : '确认并完成初始化' }}
            </button>
          </Transition>

          <div v-if="error" class="error-msg">{{ error }}</div>
          <div v-if="success" class="success-msg">{{ success }}</div>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.plugin-setup-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
  background: var(--bg-primary);
}

.setup-card {
  width: 100%;
  max-width: 960px;
  background: var(--bg-card);
  border-radius: 20px;
  padding: 40px;
  box-shadow: 0 8px 40px rgba(0, 0, 0, 0.15);
  border: 1px solid var(--border-light);
}

.setup-header {
  text-align: center;
  margin-bottom: 32px;
}

.setup-header h1 {
  margin: 0;
  font-size: 1.6rem;
  color: var(--text-primary);
}

.setup-subtitle {
  margin: 8px 0 0;
  color: var(--text-muted);
  font-size: 0.95rem;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  padding: 40px;
  color: var(--text-muted);
}

.spinner {
  width: 36px;
  height: 36px;
  border: 3px solid var(--border-light);
  border-top-color: var(--accent-primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.error-state {
  text-align: center;
  padding: 40px;
  color: var(--accent-error);
}

.completed-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  padding: 40px;
  text-align: center;
}

.completed-icon {
  font-size: 3rem;
}

.completed-state h2 {
  margin: 0;
  color: var(--text-primary);
}

.completed-state p {
  color: var(--text-muted);
  margin: 0;
}

.primary-btn {
  margin-top: 8px;
  padding: 12px 32px;
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
  border: none;
  border-radius: 10px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
}

.primary-btn:hover {
  transform: translateY(-2px);
  box-shadow: 0 6px 20px rgba(99, 102, 241, 0.4);
}

.section-title {
  margin: 0 0 4px;
  font-size: 1.1rem;
  color: var(--text-primary);
}

.section-desc {
  margin: 0 0 20px;
  font-size: 0.85rem;
  color: var(--text-muted);
}

/* 横向卡片布局 */
.strategy-list {
  display: flex;
  flex-direction: row;
  gap: 12px;
}

.strategy-card {
  flex: 1;
  min-width: 0;
  background: var(--bg-primary);
  border: 2px solid var(--border-light);
  border-radius: 14px;
  padding: 20px 18px;
  cursor: pointer;
  transition: all 0.25s ease;
  display: flex;
  flex-direction: column;
}

.strategy-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
}

.strategy-card.selected {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.05);
}

.strategy-header {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.strategy-meta {
  flex: 1;
  min-width: 0;
}

.strategy-title {
  display: block;
  font-weight: 700;
  color: var(--text-primary);
  font-size: 1rem;
  margin-bottom: 4px;
}

.strategy-desc {
  display: block;
  font-size: 0.8rem;
  color: var(--text-muted);
  line-height: 1.3;
}

.strategy-check {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  border: 2px solid var(--border-light);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  transition: all 0.25s ease;
  margin-top: 2px;
}

.strategy-check.checked {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
  color: white;
}

/* 详情 - 不修改父元素大小 */
/* 详情 - 浮动显示，不影响父元素大小 */
/* 详情 - 在卡片内部，不改变父元素大小（父元素已固定高度） */
.strategy-body {
  margin-top: 14px;
  padding-top: 14px;
  border-top: 1px solid var(--border-light);
  flex-shrink: 0;
}

.detail-list {
  display: flex;
  flex-direction: column;
  gap: 5px;
  margin-bottom: 10px;
}

.detail-item {
  display: flex;
  gap: 6px;
  font-size: 0.8rem;
  color: var(--text-secondary);
  line-height: 1.4;
}

.detail-bullet {
  color: var(--accent-primary);
  flex-shrink: 0;
}

.risk-badge {
  padding: 8px 12px;
  border-radius: 8px;
  font-size: 0.85rem;
  background: rgba(22, 163, 74, 0.1);
  color: #16a34a;
  line-height: 1.4;
  margin-bottom: 10px;
  font-weight: 600;
}

.risk-badge.warning {
  background: rgba(234, 179, 8, 0.12);
  color: #ca8a04;
}

.risk-badge.danger {
  background: rgba(239, 68, 68, 0.1);
  color: #ef4444;
}

.features-section {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.features-label {
  font-weight: 700;
  font-size: 0.78rem;
  background: linear-gradient(90deg, #eab308, #f59e0b, #eab308);
  background-size: 300% 100%;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  animation: featureFlow 3s linear infinite;
}

@keyframes featureFlow {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

@keyframes tagFlow {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

.feature-item {
  font-size: 0.8rem;
  color: var(--text-secondary);
  line-height: 1.4;
}

.tags-section {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 8px;
}

.tag-badge {
  display: inline-block;
  padding: 5px 12px;
  border-radius: 8px;
  font-size: 0.78rem;
  font-weight: 700;
  color: #fff;
  background: linear-gradient(90deg, #6366f1, #8b5cf6, #06b6d4, #6366f1);
  background-size: 300% 100%;
  animation: tagFlow 3s linear infinite;
  border: none;
}

.confirm-btn {
  display: block;
  width: 100%;
  margin-top: 20px;
  padding: 14px;
  background: linear-gradient(135deg, #16a34a, #22c55e);
  color: white;
  border: none;
  border-radius: 12px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
}

.confirm-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 6px 20px rgba(22, 163, 74, 0.4);
}

.confirm-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.error-msg {
  margin-top: 12px;
  padding: 10px;
  background: rgba(239, 68, 68, 0.1);
  border-radius: 8px;
  color: var(--accent-error);
  text-align: center;
  font-size: 0.85rem;
}

.success-msg {
  margin-top: 12px;
  padding: 10px;
  background: rgba(22, 163, 74, 0.1);
  border-radius: 8px;
  color: #16a34a;
  text-align: center;
  font-size: 0.85rem;
  font-weight: 500;
}

.fade-enter-active {
  transition: opacity 0.3s ease;
}
.fade-leave-active {
  transition: opacity 0.1s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>

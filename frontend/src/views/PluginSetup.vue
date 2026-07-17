<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const token = route.query.token || ''
const statusLoading = ref(false)
const step = ref('strategy')
const error = ref('')
const success = ref('')
const loading = ref(false)
const setupCompleted = ref(false)
const configExists = ref(false)

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
    risk: '最安全，但复杂，建议搭配QQ扩展',
    features: [],
    tags: ['QQ注册，接入机器人', '管理员手动过白']
  }
]

const selectedMode = ref('')
const showConfirm = computed(() => selectedMode.value !== '')

const sscConfig = ref(null)
const sscEnabled = ref(false)
const sscLoading = ref(false)

const bossLimitMode = ref('disabled')
const bossLimitMinPlayers = ref(7)

const bossLimitOptions = [
  {
    id: 'disabled',
    title: '不限制',
    desc: '不对 Boss 召唤做任何限制'
  },
  {
    id: 'playerlimit',
    title: '人数限制',
    desc: '未击败的 Boss 需要达到最低在线人数才能召唤'
  },
  {
    id: 'killrequired',
    title: '需击败过',
    desc: '不允许召唤从未击败过的 Boss'
  }
]

const selectMode = (mode) => {
  selectedMode.value = mode
}

const goToSsc = async () => {
  if (!selectedMode.value) return
  step.value = 'ssc'
  sscLoading.value = true
  try {
    const res = await fetch(`/api/setup/ssc-config?token=${encodeURIComponent(token)}`)
    const data = await res.json()
    if (data.content) {
      try {
        const parsed = JSON.parse(data.content)
        sscConfig.value = parsed
        sscEnabled.value = parsed.Settings?.Enabled ?? false
      } catch {}
    }
  } catch {}
  sscLoading.value = false
}

const goToBossLimit = () => {
  step.value = 'bosslimit'
}

const submitAll = async () => {
  statusLoading.value = true
  error.value = ''
  success.value = ''

  // 保存 SSC 配置
  if (sscConfig.value) {
    sscConfig.value.Settings.Enabled = sscEnabled.value
    try {
      await fetch('/api/setup/ssc-config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token, content: JSON.stringify(sscConfig.value, null, 2) })
      })
    } catch {}
  }

  // 完成插件初始化
  try {
    const res = await fetch('/api/setup/plugin-init', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        token,
        mode: selectedMode.value,
        bossLimitMode: bossLimitMode.value,
        bossLimitMinPlayers: bossLimitMinPlayers.value
      })
    })
    const data = await res.json()
    if (data.error) {
      error.value = data.error
    } else {
      // 自动登录：用 setup token 换取 JWT
      try {
        const loginRes = await fetch('/api/auth/setup-login?token=' + encodeURIComponent(token))
        const loginData = await loginRes.json()
        if (loginData.success) {
          localStorage.setItem('user', JSON.stringify({
            username: 'admin',
            usergroup: 'superadmin',
            token: loginData.token
          }))
        }
      } catch {}
      success.value = '插件初始化完成！'
      step.value = 'done'
    }
  } catch (err) {
    error.value = '请求失败: ' + err.message
  }
  statusLoading.value = false
}

const goToConsole = () => {
  router.push('/backend?token=' + encodeURIComponent(route.query.token || ''))
}

onMounted(async () => {
  if (!token) {
    error.value = '缺少 Setup Token，请从初始化页面进入'
    loading.value = false
    return
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

      <template v-else-if="step === 'done'">
        <div class="completed-state">
          <div class="completed-icon">✅</div>
          <h2>已初始化完成</h2>
          <p v-if="configExists">插件配置已设置。</p>
          <button @click="goToConsole" class="primary-btn">前往控制台</button>
        </div>
      </template>

      <template v-else>
        <!-- 进服策略选择 -->
        <div v-if="step === 'strategy'" class="strategy-section">
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

          <div class="action-bar">
            <div v-if="error" class="error-msg">{{ error }}</div>
            <div v-if="success" class="success-msg">{{ success }}</div>
            <button
              @click="goToSsc"
              :disabled="!showConfirm || statusLoading"
              class="next-btn"
            >
              下一步
            </button>
          </div>
        </div>

        <!-- SSC 开荒配置 -->
        <div v-if="step === 'ssc'" class="ssc-section">
          <button class="back-btn" @click="step = 'strategy'">← 返回</button>
          <h3 class="section-title">SSC配置</h3>
          <p class="section-desc">配置服务器角色开荒（SSC）相关设置，决定玩家进服后是否使用统一角色数据。</p>

          <div v-if="sscLoading" class="loading-state">
            <div class="spinner"></div>
            <p>正在读取配置文件...</p>
          </div>

          <div v-else-if="sscConfig" class="ssc-card">
            <div class="ssc-row">
              <div class="ssc-info">
                <span class="ssc-name">启用强制开荒</span>
                <span class="ssc-desc">开启后所有玩家进服后将使用服务器统一设定的初始角色数据（背包、血量、魔力等）</span>
              </div>
              <label class="ssc-toggle-wrap">
                <input type="checkbox" v-model="sscEnabled" class="ssc-check" />
                <span class="ssc-toggle"></span>
              </label>
            </div>
            <div class="ssc-detail-row">
              <span class="ssc-detail-label">初始生命</span>
              <span class="ssc-detail-value">{{ sscConfig.Settings?.StartingHealth ?? 100 }}</span>
            </div>
            <div class="ssc-detail-row">
              <span class="ssc-detail-label">初始魔力</span>
              <span class="ssc-detail-value">{{ sscConfig.Settings?.StartingMana ?? 20 }}</span>
            </div>
            <div class="ssc-detail-row">
              <span class="ssc-detail-label">自动保存间隔</span>
              <span class="ssc-detail-value">{{ sscConfig.Settings?.ServerSideCharacterSave ?? 5 }} 秒</span>
            </div>
          </div>

          <div v-else class="error-state">
            <p>无法读取 SSC 配置文件，请确认 TShock 已正确安装。</p>
          </div>

          <div class="action-bar">
            <button @click="goToBossLimit" :disabled="statusLoading" class="next-btn">
              下一步
            </button>
          </div>
        </div>

        <!-- BossLimit 配置 -->
        <div v-if="step === 'bosslimit'" class="bosslimit-section">
          <button class="back-btn" @click="step = 'ssc'">← 返回</button>
          <h3 class="section-title">Boss 召唤限制</h3>
          <p class="section-desc">配置 Boss 召唤的限制策略，防止低人数时 Boss 被误召或恶意召唤。</p>

          <div class="bosslimit-list">
            <div
              v-for="b in bossLimitOptions"
              :key="b.id"
              class="bosslimit-card"
              :class="{ selected: bossLimitMode === b.id }"
              @click="bossLimitMode = b.id"
            >
              <div class="bosslimit-header">
                <span class="bosslimit-check" :class="{ checked: bossLimitMode === b.id }">
                  <svg v-if="bossLimitMode === b.id" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                    <polyline points="20 6 9 17 4 12"></polyline>
                  </svg>
                </span>
                <div class="bosslimit-meta">
                  <span class="bosslimit-title">{{ b.title }}</span>
                  <span class="bosslimit-desc">{{ b.desc }}</span>
                </div>
              </div>
            </div>
          </div>

          <!-- 人数输入（仅 playerlimit 模式） -->
          <div v-if="bossLimitMode === 'playerlimit'" class="playerlimit-input-wrap">
            <label class="playerlimit-label">最低在线人数</label>
            <div class="playerlimit-control">
              <button class="num-btn" @click="bossLimitMinPlayers = Math.max(1, bossLimitMinPlayers - 1)">−</button>
              <input type="number" v-model.number="bossLimitMinPlayers" min="1" class="num-input" />
              <button class="num-btn" @click="bossLimitMinPlayers++">+</button>
            </div>
            <p class="playerlimit-hint">未击败的 Boss 需要至少 {{ bossLimitMinPlayers }} 名在线玩家才能召唤</p>
          </div>

          <div class="action-bar">
            <div v-if="error" class="error-msg">{{ error }}</div>
            <div v-if="success" class="success-msg">{{ success }}</div>
            <button @click="submitAll" :disabled="statusLoading" class="next-btn finish-btn">
              {{ statusLoading ? '保存中...' : '确认并完成' }}
            </button>
          </div>
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
  background: linear-gradient(135deg, #e0e7ff, #c7d2fe, #a5b4fc, #c7d2fe, #e0e7ff);
  background-size: 400% 400%;
  animation: bgFlow 8s ease infinite;
}

@keyframes bgFlow {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

.setup-card {
  width: 100%;
  max-width: 960px;
  background: rgba(255, 255, 255, 0.85);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border-radius: 24px;
  padding: 40px;
  box-shadow: 0 8px 40px rgba(99, 102, 241, 0.12);
  border: 1px solid rgba(255, 255, 255, 0.6);
}

.setup-header {
  text-align: center;
  margin-bottom: 32px;
}

.setup-header h1 {
  margin: 0;
  font-size: 1.6rem;
  color: #1e1b4b;
}

.setup-subtitle {
  margin: 8px 0 0;
  color: #6b7280;
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
  color: #0f0a3a;
  font-size: 1.5rem;
  font-weight: 800;
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
  color: #1e1b4b;
}

.section-desc {
  margin: 0 0 20px;
  font-size: 0.85rem;
  color: #6b7280;
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
  background: rgba(255, 255, 255, 0.7);
  border: 2px solid rgba(0, 0, 0, 0.06);
  border-radius: 16px;
  padding: 20px 18px;
  cursor: pointer;
  transition: border-color 0.25s ease, background 0.25s ease, box-shadow 0.25s ease, transform 0.3s cubic-bezier(0.34, 1.56, 0.64, 1);
  display: flex;
  flex-direction: column;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.04);
  animation: cardEnter 0.5s ease both;
  opacity: 0;
}

.strategy-card:nth-child(1) { animation-delay: 0s; }
.strategy-card:nth-child(2) { animation-delay: 0.1s; }
.strategy-card:nth-child(3) { animation-delay: 0.2s; }

@keyframes cardEnter {
  from { opacity: 0; transform: translateY(12px); }
  to { opacity: 1; transform: translateY(0); }
}

.strategy-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
  box-shadow: 0 6px 20px rgba(99, 102, 241, 0.1);
}

.strategy-card.selected {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.08);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.2), 0 8px 24px rgba(99, 102, 241, 0.15);
  transform: scale(1.03);
}

.strategy-card:active {
  transform: scale(0.97);
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
  color: #0f0a3a;
  font-size: 1rem;
  margin-bottom: 4px;
}

.strategy-desc {
  display: block;
  font-size: 0.8rem;
  color: #4b5563;
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

/* 详情 - 在卡片内部 */
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
  padding: 8px 14px;
  border-radius: 8px;
  font-size: 0.85rem;
  line-height: 1.4;
  margin-bottom: 10px;
  font-weight: 600;
}

.risk-badge.danger {
  background: transparent;
  color: #ef4444;
  border: 1.5px solid #ef4444;
}

.risk-badge.warning {
  background: transparent;
  color: #eab308;
  border: 1.5px solid #eab308;
}

.risk-badge:not(.danger):not(.warning) {
  background: transparent;
  color: #22c55e;
  border: 1.5px solid #22c55e;
  box-shadow: 0 0 8px rgba(34, 197, 94, 0.2);
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

/* SSC 配置 */
.ssc-section {
  width: 100%;
}

.ssc-section .back-btn {
  background: none;
  border: none;
  color: var(--accent-primary);
  cursor: pointer;
  font-size: 0.9rem;
  padding: 0;
  margin-bottom: 16px;
}

.ssc-section .back-btn:hover {
  text-decoration: underline;
}

.ssc-card {
  background: rgba(255, 255, 255, 0.7);
  border: 1px solid rgba(0, 0, 0, 0.08);
  border-radius: 14px;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.ssc-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  padding-bottom: 14px;
  border-bottom: 1px solid rgba(0, 0, 0, 0.06);
}

.ssc-info {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.ssc-name {
  font-weight: 700;
  font-size: 0.95rem;
  color: #0f0a3a;
}

.ssc-desc {
  font-size: 0.8rem;
  color: #4b5563;
  line-height: 1.4;
}

.ssc-toggle-wrap {
  position: relative;
  display: inline-block;
  width: 44px;
  height: 24px;
  flex-shrink: 0;
  margin-top: 2px;
  cursor: pointer;
}

.ssc-check {
  display: none;
}

.ssc-toggle {
  position: absolute;
  inset: 0;
  border-radius: 12px;
  background: #d1d5db;
  transition: all 0.25s ease;
}

.ssc-toggle::after {
  content: '';
  position: absolute;
  top: 2px;
  left: 2px;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: white;
  transition: all 0.25s ease;
  box-shadow: 0 1px 3px rgba(0,0,0,0.15);
}

.ssc-check:checked + .ssc-toggle {
  background: var(--accent-primary);
}

.ssc-check:checked + .ssc-toggle::after {
  left: 22px;
}

.ssc-detail-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.ssc-detail-label {
  font-size: 0.85rem;
  color: #4b5563;
}

.ssc-detail-value {
  font-size: 0.85rem;
  font-weight: 600;
  color: #0f0a3a;
}

.finish-btn {
  background: linear-gradient(135deg, #16a34a, #22c55e) !important;
}

.finish-btn:hover:not(:disabled) {
  box-shadow: 0 6px 20px rgba(22, 163, 74, 0.4) !important;
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

/* 底部操作栏 */
.action-bar {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 12px;
  margin-top: 20px;
}

.next-btn {
  padding: 12px 32px;
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
  border: none;
  border-radius: 10px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.25s ease, transform 0.25s ease, box-shadow 0.25s ease;
}

.next-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 6px 20px rgba(99, 102, 241, 0.4);
}

.next-btn:disabled {
  opacity: 0.35;
  cursor: not-allowed;
  transform: none;
  box-shadow: none;
}

.error-msg {
  padding: 10px 16px;
  background: rgba(239, 68, 68, 0.1);
  border-radius: 8px;
  color: var(--accent-error);
  font-size: 0.85rem;
  margin-right: auto;
}

.success-msg {
  padding: 10px 16px;
  background: rgba(22, 163, 74, 0.1);
  border-radius: 8px;
  color: #16a34a;
  font-size: 0.85rem;
  font-weight: 500;
  margin-right: auto;
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

/* BossLimit 配置 */
.bosslimit-section {
  width: 100%;
}

.bosslimit-section .back-btn {
  background: none;
  border: none;
  color: var(--accent-primary);
  cursor: pointer;
  font-size: 0.9rem;
  padding: 0;
  margin-bottom: 16px;
}

.bosslimit-section .back-btn:hover {
  text-decoration: underline;
}

.bosslimit-list {
  display: flex;
  flex-direction: row;
  gap: 12px;
}

.bosslimit-card {
  flex: 1;
  background: rgba(255, 255, 255, 0.7);
  border: 2px solid rgba(0, 0, 0, 0.06);
  border-radius: 14px;
  padding: 16px;
  cursor: pointer;
  transition: all 0.25s ease;
}

.bosslimit-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
}

.bosslimit-card.selected {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.08);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.2);
}

.bosslimit-header {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}

.bosslimit-check {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  border: 2px solid var(--border-light);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-top: 1px;
  transition: all 0.25s ease;
}

.bosslimit-check.checked {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
  color: white;
}

.bosslimit-meta {
  flex: 1;
}

.bosslimit-title {
  display: block;
  font-weight: 700;
  color: #0f0a3a;
  font-size: 0.95rem;
  margin-bottom: 3px;
}

.bosslimit-desc {
  display: block;
  font-size: 0.8rem;
  color: #4b5563;
  line-height: 1.3;
}

.playerlimit-input-wrap {
  margin-top: 20px;
  padding: 18px 20px;
  background: rgba(255, 255, 255, 0.7);
  border: 1px solid rgba(0, 0, 0, 0.08);
  border-radius: 14px;
  animation: fadeIn 0.25s ease;
}

@keyframes fadeIn {
  from { opacity: 0; transform: translateY(-6px); }
  to { opacity: 1; transform: translateY(0); }
}

.playerlimit-label {
  display: block;
  font-weight: 700;
  font-size: 0.9rem;
  color: #0f0a3a;
  margin-bottom: 10px;
}

.playerlimit-control {
  display: flex;
  align-items: center;
  gap: 8px;
}

.num-btn {
  width: 34px;
  height: 34px;
  border-radius: 8px;
  border: 1.5px solid rgba(0, 0, 0, 0.12);
  background: white;
  font-size: 1.1rem;
  font-weight: 700;
  color: #0f0a3a;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s ease;
}

.num-btn:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.num-input {
  width: 72px;
  height: 36px;
  text-align: center;
  font-size: 1rem;
  font-weight: 700;
  border: 1.5px solid rgba(0, 0, 0, 0.12);
  border-radius: 8px;
  outline: none;
  color: #0f0a3a;
  background: white;
  transition: border-color 0.15s ease;
  -moz-appearance: textfield;
}

.num-input::-webkit-outer-spin-button,
.num-input::-webkit-inner-spin-button {
  -webkit-appearance: none;
  margin: 0;
}

.num-input:focus {
  border-color: var(--accent-primary);
}

.playerlimit-hint {
  margin: 10px 0 0;
  font-size: 0.8rem;
  color: #6b7280;
}

</style>

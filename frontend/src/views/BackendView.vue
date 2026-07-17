<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const setupToken = route.query.token || ''
const activeTab = ref('rest')

const tabs = [
  { id: 'rest', label: 'REST 连接设置' },
  { id: 'webhook', label: '日志推流' },
  { id: 'plugin', label: '插件初始化' }
]

// ═══════════════════════════════════════════════
// 通用状态
// ═══════════════════════════════════════════════
const tokenOk = ref(false)
const error = ref('')
const success = ref('')

// ═══════════════════════════════════════════════
// REST 连接设置（只读展示，配置在 init 页面完成）
// ═══════════════════════════════════════════════
const connected = ref(false)
const restHost = ref('127.0.0.1')
const restPort = ref('7878')
const restApiKey = ref('')
const showKey = ref(false)

// ═══════════════════════════════════════════════
// 日志推流设置
// ═══════════════════════════════════════════════
const webhookEnabled = ref(false)
const webhookUrl = ref('http://127.0.0.1:3000/api/online/log-webhook')
const webhookLoading = ref(false)
const webhookSaving = ref(false)
const webhookStatus = ref('')

// ═══════════════════════════════════════════════
// 插件初始化
// ═══════════════════════════════════════════════
const pluginStep = ref('strategy')
const selectedMode = ref('')
const strategies = [
  { id: 'default', title: '默认模式', desc: '玩家需使用 /register 手动注册' },
  { id: 'auto', title: '自动注册', desc: '玩家进入时自动创建账户' },
  { id: 'block', title: '白名单模式', desc: '仅允许已注册玩家进入' }
]
const sscConfig = ref(null)
const sscEnabled = ref(false)
const sscLoading = ref(false)
const bossLimitMode = ref('disabled')
const bossLimitMinPlayers = ref(7)
const bossLimitOptions = [
  { id: 'disabled', title: '不限制', desc: '不对 Boss 召唤做任何限制' },
  { id: 'playerlimit', title: '人数限制', desc: '未击败的 Boss 需要达到最低在线人数才能召唤' },
  { id: 'killrequired', title: '需击败过', desc: '不允许召唤从未击败过的 Boss' }
]
const pluginSaving = ref(false)

// Token 认证
const tokenMissing = ref(false)
const manualToken = ref('')
const authError = ref('')

// ═══════════════════════════════════════════════
// 初始化
// ═══════════════════════════════════════════════
onMounted(async () => {
  if (!setupToken) {
    tokenMissing.value = true
    return
  }
  try {
    const res = await fetch('/api/setup/check?token=' + encodeURIComponent(setupToken))
    const data = await res.json()
    if (data.needToken) {
      tokenMissing.value = true
      return
    }
    tokenOk.value = true
    // 未配置 → 跳转到初始化页面
    if (!data.configured) {
      router.replace('/backend/init?token=' + encodeURIComponent(setupToken))
      return
    }
    // 已配置：填充显示值
    if (data.config) {
      restHost.value = data.config.host || '127.0.0.1'
      restPort.value = String(data.config.port || 7878)
      restApiKey.value = data.config.apiKey || ''
    }
    // 检查连接状态
    const statusRes = await fetch('/api/status')
    const statusData = await statusRes.json()
    connected.value = statusData.connected
    if (connected.value) {
      loadWebhookConfig()
    }
  } catch {
    tokenMissing.value = true
  }
})

// ═══════════════════════════════════════════════
// 通用 API 包装
// ═══════════════════════════════════════════════
async function apiPost(url, body) {
  return fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...body, token: setupToken })
  })
}

function getAuthHeaders() {
  try {
    const user = JSON.parse(localStorage.getItem('user') || '{}')
    return user.token ? { 'Authorization': 'Bearer ' + user.token } : {}
  } catch {
    return {}
  }
}

async function submitTokenAuth() {
  const t = manualToken.value.trim()
  if (!t) return
  authError.value = ''
  try {
    const res = await fetch('/api/setup/check?token=' + encodeURIComponent(t))
    const data = await res.json()
    if (data.needToken || !data.setupToken) {
      authError.value = 'Token 无效'
      return
    }
    const url = new URL(window.location.href)
    url.searchParams.set('token', t)
    window.location.href = url.toString()
  } catch (err) {
    authError.value = err.message
  }
}

function goToInit() {
  router.push('/backend/init?token=' + encodeURIComponent(setupToken))
}

// ═══════════════════════════════════════════════
// 日志推流
// ═══════════════════════════════════════════════
async function loadWebhookConfig() {
  webhookLoading.value = true
  try {
    const res = await fetch('/api/config/log-webhook?token=' + encodeURIComponent(setupToken), {
      headers: getAuthHeaders()
    })
    const data = await res.json()
    if (data.status === '200' || !data.error) {
      webhookEnabled.value = data.enabled ?? false
      webhookUrl.value = data.publicUrl || `http://127.0.0.1:3000/api/online/log-webhook`
    }
  } catch {}
  webhookLoading.value = false
}

async function saveWebhookConfig() {
  webhookSaving.value = true
  webhookStatus.value = ''
  try {
    const res = await fetch('/api/config/log-webhook?token=' + encodeURIComponent(setupToken), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...getAuthHeaders() },
      body: JSON.stringify({ enabled: webhookEnabled.value, publicUrl: webhookUrl.value })
    })
    const data = await res.json()
    if (data.status === '200' || !data.error) {
      webhookStatus.value = '✅ 配置已保存'
    } else {
      webhookStatus.value = '❌ ' + (data.error || '保存失败')
    }
  } catch (err) {
    webhookStatus.value = '❌ ' + err.message
  }
  webhookSaving.value = false
  setTimeout(() => { webhookStatus.value = '' }, 3000)
}

// ═══════════════════════════════════════════════
// 插件初始化
// ═══════════════════════════════════════════════
function selectMode(mode) {
  selectedMode.value = mode
}

async function goToSsc() {
  if (!selectedMode.value) return
  pluginStep.value = 'ssc'
  sscLoading.value = true
  try {
    const res = await fetch('/api/setup/ssc-config?token=' + encodeURIComponent(setupToken))
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

function goToBossLimit() {
  pluginStep.value = 'bosslimit'
}

async function submitPlugin() {
  pluginSaving.value = true
  error.value = ''
  success.value = ''
  try {
    if (sscConfig.value) {
      sscConfig.value.Settings.Enabled = sscEnabled.value
      await apiPost('/api/setup/ssc-config', {
        content: JSON.stringify(sscConfig.value, null, 2)
      })
    }
    const res = await apiPost('/api/setup/plugin-init', {
      mode: selectedMode.value,
      bossLimitMode: bossLimitMode.value,
      bossLimitMinPlayers: bossLimitMinPlayers.value
    })
    const data = await res.json()
    if (data.error) {
      error.value = data.error
    } else {
      success.value = '插件初始化完成！'
      pluginStep.value = 'done'
    }
  } catch (err) {
    error.value = '请求失败: ' + err.message
  }
  pluginSaving.value = false
}

function goBackToRest() {
  activeTab.value = 'rest'
}

onUnmounted(() => {})
</script>

<template>
  <div class="backend-page">
    <!-- ========== 顶部：标题 + 居中分页栏 ========== -->
    <div class="backend-header">
      <div class="header-inner">
        <h1 class="header-title">TSWeb 后台管理</h1>
        <div class="tab-bar">
          <button v-for="tab in tabs" :key="tab.id"
            :class="['tab-btn', { active: activeTab === tab.id }]"
            @click="activeTab = tab.id">
            {{ tab.label }}
          </button>
        </div>
      </div>
    </div>

    <!-- ========== 主体 ========== -->
    <div class="backend-body">

      <!-- Token 缺失 — 显示输入框 -->
      <div v-if="tokenMissing" class="token-auth-overlay">
        <div class="token-auth-card">
          <div class="auth-icon">🔐</div>
          <h2>需要授权</h2>
          <p class="auth-desc">请输入服务端控制台提供的 Token 以访问后台管理</p>
          <div class="auth-input-row">
            <input v-model="manualToken" type="text" class="auth-input"
              placeholder="输入 Token..."
              @keydown="e => e.key === 'Enter' && submitTokenAuth()" />
            <button class="auth-btn" @click="submitTokenAuth" :disabled="!manualToken.trim()">
              验证
            </button>
          </div>
          <p v-if="authError" class="auth-error">❌ {{ authError }}</p>
        </div>
      </div>

      <!-- Token 有效 -->
      <template v-else>

        <!-- ═══════════════════════════════════════ -->
        <!-- TAB 1: REST 连接设置（只读展示） -->
        <!-- ═══════════════════════════════════════ -->
        <div v-if="activeTab === 'rest'" class="tab-content">
          <div class="section-card">
            <div class="section-header"><h3>连接属性</h3></div>

            <div class="form-row">
              <div class="form-group flex-1">
                <label class="form-label">服务器地址</label>
                <input :value="restHost" type="text" class="form-input" disabled readonly />
              </div>
              <div class="form-group port-input">
                <label class="form-label">REST 端口</label>
                <input :value="restPort" type="text" class="form-input" disabled readonly />
              </div>
            </div>

            <div class="form-group">
              <label class="form-label">API 密钥</label>
              <div class="input-with-btn">
                <input :value="showKey ? restApiKey : (restApiKey ? restApiKey.substring(0, 16) + '......' : '未设置')"
                  type="text" class="form-input" disabled readonly />
                <button class="toggle-btn" @click="showKey = !showKey" type="button">{{ showKey ? '隐藏' : '显示' }}</button>
              </div>
            </div>

            <div class="section-actions" style="margin-top:24px">
              <button class="btn primary" @click="goToInit">更新配置</button>
            </div>
            <p class="auto-link" @click="goToInit">
              我看不懂，跳转到自动配置 →
            </p>
          </div>
        </div>

        <!-- ═══════════════════════════════════════ -->
        <!-- TAB 2: 日志推流 -->
        <!-- ═══════════════════════════════════════ -->
        <div v-if="activeTab === 'webhook'" class="tab-content">
          <div class="section-card">
            <div class="section-header"><h3>日志推流</h3></div>
            <p class="section-desc">
              配置后，TSWeb 插件端会将控制台日志通过 Webhook HTTP POST 推送到此地址，
              后端再转发到前端控制台终端。<strong>启用后需重启服务器生效。</strong>
            </p>

            <div v-if="webhookLoading" class="loading-text">加载中...</div>

            <template v-else>
              <!-- 开关 -->
              <div class="toggle-row">
                <div class="toggle-info">
                  <span class="toggle-label">启用日志推流</span>
                  <span class="toggle-desc">开启后插件端将日志推送到后端，前端控制台可实时查看</span>
                </div>
                <label class="toggle-wrap">
                  <input type="checkbox" v-model="webhookEnabled" class="toggle-input" />
                  <span class="toggle-slider"></span>
                </label>
              </div>

              <!-- 地址输入 -->
              <div class="form-group" style="margin-top:16px">
                <label class="form-label">推流地址</label>
                <input v-model="webhookUrl" type="text" class="form-input"
                  placeholder="http://127.0.0.1:3000/api/online/log-webhook"
                  :disabled="!webhookEnabled" />
                <p class="field-hint" style="margin-top:6px;font-size:0.78rem;color:#64748b">
                  本机默认自动填充，公网服务器请填写可被插件访问到的完整 URL
                </p>
              </div>

              <div v-if="webhookStatus" :class="['msg-box', webhookStatus.includes('✅') ? 'success' : 'error']" style="margin:12px 0 0">
                {{ webhookStatus }}
              </div>

              <div class="section-actions">
                <button class="btn primary" @click="saveWebhookConfig" :disabled="webhookSaving || webhookLoading">
                  {{ webhookSaving ? '保存中...' : '保存配置' }}
                </button>
              </div>
            </template>
          </div>
        </div>

        <!-- ═══════════════════════════════════════ -->
        <!-- TAB 3: 插件初始化 -->
        <!-- ═══════════════════════════════════════ -->
        <div v-if="activeTab === 'plugin'" class="tab-content">

          <div v-if="pluginStep === 'done'" class="done-section">
            <div class="done-icon">✅</div>
            <h2>初始化完成</h2>
            <p>插件配置已保存。</p>
            <div class="section-actions">
              <button class="btn primary" @click="activeTab = 'rest'">返回连接设置</button>
            </div>
          </div>

          <template v-else>
            <!-- 策略选择 -->
            <div v-if="pluginStep === 'strategy'" class="section-card">
              <div class="section-header"><h3>选择进服策略</h3></div>
              <p class="section-desc">控制新玩家的注册方式，策略可随时修改。</p>
              <div class="strategy-grid">
                <div v-for="s in strategies" :key="s.id"
                  :class="['strategy-card', { selected: selectedMode === s.id }]"
                  @click="selectMode(s.id)">
                  <div class="strategy-top">
                    <span class="strategy-title">{{ s.title }}</span>
                    <span :class="['check-circle', { checked: selectedMode === s.id }]">
                      <svg v-if="selectedMode === s.id" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><polyline points="20 6 9 17 4 12"></polyline></svg>
                    </span>
                  </div>
                  <span class="strategy-desc-text">{{ s.desc }}</span>
                </div>
              </div>
              <div class="section-actions">
                <button class="btn primary" @click="goToSsc" :disabled="!selectedMode">下一步</button>
              </div>
            </div>

            <!-- SSC 配置 -->
            <div v-if="pluginStep === 'ssc'" class="section-card">
              <div class="section-header">
                <h3>SSC 开荒配置</h3>
                <button class="btn-text" @click="pluginStep = 'strategy'">← 返回</button>
              </div>
              <div v-if="sscLoading" class="loading-text">读取配置中...</div>
              <div v-else-if="sscConfig">
                <div class="toggle-row">
                  <div class="toggle-info">
                    <span class="toggle-label">启用强制开荒</span>
                    <span class="toggle-desc">玩家进服后使用统一初始角色数据</span>
                  </div>
                  <label class="toggle-wrap">
                    <input type="checkbox" v-model="sscEnabled" class="toggle-input" />
                    <span class="toggle-slider"></span>
                  </label>
                </div>
                <div class="ssc-details">
                  <div class="ssc-detail"><span>初始生命</span><span>{{ sscConfig.Settings?.StartingHealth ?? 100 }}</span></div>
                  <div class="ssc-detail"><span>初始魔力</span><span>{{ sscConfig.Settings?.StartingMana ?? 20 }}</span></div>
                  <div class="ssc-detail"><span>自动保存间隔</span><span>{{ sscConfig.Settings?.ServerSideCharacterSave ?? 5 }} 秒</span></div>
                </div>
              </div>
              <div v-else class="msg-box warn">无法读取 SSC 配置文件，请确认 TShock 已正确安装。</div>
              <div class="section-actions">
                <button class="btn primary" @click="goToBossLimit">下一步</button>
              </div>
            </div>

            <!-- Boss 限制 -->
            <div v-if="pluginStep === 'bosslimit'" class="section-card">
              <div class="section-header">
                <h3>Boss 召唤限制</h3>
                <button class="btn-text" @click="pluginStep = 'ssc'">← 返回</button>
              </div>
              <div class="bosslimit-grid">
                <div v-for="b in bossLimitOptions" :key="b.id"
                  :class="['bosslimit-card', { selected: bossLimitMode === b.id }]"
                  @click="bossLimitMode = b.id">
                  <div class="bosslimit-top">
                    <span class="strategy-title">{{ b.title }}</span>
                    <span :class="['check-circle', { checked: bossLimitMode === b.id }]">
                      <svg v-if="bossLimitMode === b.id" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><polyline points="20 6 9 17 4 12"></polyline></svg>
                    </span>
                  </div>
                  <span class="strategy-desc-text">{{ b.desc }}</span>
                </div>
              </div>
              <div v-if="bossLimitMode === 'playerlimit'" class="playerlimit-row">
                <label>最低在线人数</label>
                <div class="num-stepper">
                  <button class="num-btn" @click="bossLimitMinPlayers = Math.max(1, bossLimitMinPlayers - 1)">−</button>
                  <input type="number" v-model.number="bossLimitMinPlayers" min="1" class="num-input" />
                  <button class="num-btn" @click="bossLimitMinPlayers++">+</button>
                </div>
              </div>
              <div class="section-actions">
                <div v-if="error" class="msg-box error" style="margin:0 0 12px 0">{{ error }}</div>
                <div v-if="success" class="msg-box success" style="margin:0 0 12px 0">{{ success }}</div>
                <button class="btn primary" @click="submitPlugin" :disabled="pluginSaving">
                  {{ pluginSaving ? '保存中...' : '确认并完成' }}
                </button>
              </div>
            </div>
          </template>
        </div>

      </template>
    </div>
  </div>
</template>

<style scoped>
/* ═══════════════════════════════════════════════
   布局
   ═══════════════════════════════════════════════ */
.backend-page {
  min-height: 100vh;
  background: linear-gradient(135deg, #0f0c29, #1a1740, #242150);
  color: #e2e8f0;
}

/* ═══ 顶部页眉 ── 居中分页栏 ═══ */
.backend-header {
  background: rgba(255, 255, 255, 0.04);
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  backdrop-filter: blur(12px);
  position: sticky;
  top: 0;
  z-index: 100;
}

.header-inner {
  max-width: 960px;
  margin: 0 auto;
  padding: 20px 24px 0;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
}

.header-title {
  margin: 0 0 16px;
  font-size: 1.3rem;
  font-weight: 700;
  color: #c7d2fe;
  letter-spacing: 1px;
}

.tab-bar {
  display: flex;
  gap: 2px;
}

.tab-btn {
  padding: 8px 20px;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: #94a3b8;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
  white-space: nowrap;
}

.tab-btn:hover {
  color: #e2e8f0;
}

.tab-btn.active {
  color: #a5b4fc;
}

/* ═══ 主体 ═══ */
.backend-body {
  max-width: 800px;
  margin: 0 auto;
  padding: 32px 24px 80px;
}

/* ═══ 卡片 ═══ */
.section-card {
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  padding: 24px;
  margin-bottom: 20px;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.section-header h3 {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
  color: #c7d2fe;
}

.section-desc {
  margin: -8px 0 16px;
  font-size: 0.85rem;
  color: #64748b;
  line-height: 1.6;
}

.section-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 16px;
  flex-wrap: wrap;
}

/* ═══ 表单 ═══ */
.form-row {
  display: flex;
  gap: 12px;
  margin-bottom: 14px;
}

.form-group {
  margin-bottom: 14px;
}

.form-group:last-child {
  margin-bottom: 0;
}

.flex-1 { flex: 1; }
.port-input { max-width: 140px; }

.form-label {
  display: block;
  font-size: 0.8rem;
  font-weight: 600;
  color: #94a3b8;
  margin-bottom: 6px;
}

.form-input {
  width: 100%;
  padding: 10px 14px;
  background: rgba(0, 0, 0, 0.3);
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  color: #e2e8f0;
  font-size: 0.9rem;
  outline: none;
  transition: border-color 0.25s ease;
  box-sizing: border-box;
}

.form-input:disabled {
  opacity: 0.65;
  cursor: default;
  border-color: rgba(255, 255, 255, 0.05);
  background: rgba(0, 0, 0, 0.15);
  color: #cbd5e1;
}

.form-input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15);
}

.form-input::placeholder {
  color: #475569;
}

.input-with-btn {
  display: flex;
  gap: 8px;
}

.input-with-btn .form-input {
  flex: 1;
}

.toggle-btn {
  padding: 8px 14px;
  background: rgba(255, 255, 255, 0.06);
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  color: #94a3b8;
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 500;
  white-space: nowrap;
  transition: all 0.2s ease;
}

.toggle-btn:hover {
  border-color: #6366f1;
  color: #a5b4fc;
}

/* ═══ 按钮 ═══ */
.btn {
  padding: 10px 22px;
  border: none;
  border-radius: 10px;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn.primary {
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
}

.btn.primary:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.35);
}

.btn.secondary {
  background: rgba(255, 255, 255, 0.08);
  color: #c7d2fe;
  border: 1px solid rgba(255, 255, 255, 0.12);
}

.btn.secondary:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.12);
}

.btn-text {
  background: none;
  border: none;
  color: #818cf8;
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 600;
  padding: 0;
}

.btn-text:hover {
  color: #a5b4fc;
  text-decoration: underline;
}

/* ═══ 消息框 ═══ */
.msg-box {
  padding: 10px 14px;
  border-radius: 10px;
  font-size: 0.85rem;
  margin-bottom: 20px;
  line-height: 1.5;
}

.msg-box.error {
  background: rgba(239, 68, 68, 0.12);
  border: 1px solid rgba(239, 68, 68, 0.2);
  color: #f87171;
}

.msg-box.success {
  background: rgba(34, 197, 94, 0.12);
  border: 1px solid rgba(34, 197, 94, 0.2);
  color: #4ade80;
}

.msg-box.warn {
  background: rgba(234, 179, 8, 0.12);
  border: 1px solid rgba(234, 179, 8, 0.2);
  color: #fbbf24;
}

.msg-box.info {
  background: rgba(99, 102, 241, 0.12);
  border: 1px solid rgba(99, 102, 241, 0.2);
  color: #a5b4fc;
}

/* ═══ Token 认证弹框 ═══ */
.token-auth-overlay {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 60vh;
}

.token-auth-card {
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 20px;
  padding: 48px 40px;
  text-align: center;
  max-width: 420px;
  width: 100%;
}

.auth-icon {
  font-size: 3rem;
  margin-bottom: 16px;
}

.token-auth-card h2 {
  color: #c7d2fe;
  margin: 0 0 8px;
  font-size: 1.3rem;
}

.auth-desc {
  color: #64748b;
  margin: 0 0 24px;
  font-size: 0.9rem;
  line-height: 1.5;
}

.auth-input-row {
  display: flex;
  gap: 8px;
}

.auth-input {
  flex: 1;
  padding: 12px 16px;
  background: rgba(0, 0, 0, 0.3);
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  color: #e2e8f0;
  font-size: 0.95rem;
  font-family: 'SF Mono', 'Cascadia Code', 'Consolas', monospace;
  outline: none;
  transition: border-color 0.25s ease;
}
.auth-input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15);
}

.auth-btn {
  padding: 12px 24px;
  border: none;
  border-radius: 10px;
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: #fff;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
  transition: all 0.2s ease;
}
.auth-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.3);
}
.auth-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.auth-error {
  color: #f87171;
  margin-top: 12px;
  font-size: 0.85rem;
}

/* ═══ 自动配置链接 ═══ */
.auto-link {
  margin: 16px 0 0;
  font-size: 0.82rem;
  color: #6366f1;
  cursor: pointer;
  transition: color 0.2s;
}
.auto-link:hover {
  color: #818cf8;
  text-decoration: underline;
}

/* ═══ Toggle ═══ */
.toggle-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 12px 0;
}

.toggle-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.toggle-label {
  font-weight: 600;
  font-size: 0.9rem;
  color: #e2e8f0;
}

.toggle-desc {
  font-size: 0.8rem;
  color: #64748b;
}

.toggle-wrap {
  position: relative;
  display: inline-block;
  width: 42px;
  height: 24px;
  flex-shrink: 0;
  cursor: pointer;
}

.toggle-input {
  display: none;
}

.toggle-slider {
  position: absolute;
  inset: 0;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.15);
  transition: all 0.25s ease;
}

.toggle-slider::after {
  content: '';
  position: absolute;
  top: 2px;
  left: 2px;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: #64748b;
  transition: all 0.25s ease;
  box-shadow: 0 1px 3px rgba(0,0,0,0.3);
}

.toggle-input:checked + .toggle-slider {
  background: rgba(99, 102, 241, 0.4);
}

.toggle-input:checked + .toggle-slider::after {
  left: 20px;
  background: #818cf8;
}

.field-hint {
  font-size: 0.8rem;
  color: #64748b;
  margin: 8px 0 0;
}

/* ═══ 策略选择 ═══ */
.strategy-grid, .bosslimit-grid {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 10px;
}

.strategy-card, .bosslimit-card {
  background: rgba(255, 255, 255, 0.03);
  border: 1.5px solid rgba(255, 255, 255, 0.08);
  border-radius: 12px;
  padding: 16px;
  cursor: pointer;
  transition: all 0.25s ease;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.strategy-card:hover, .bosslimit-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
  background: rgba(255, 255, 255, 0.05);
}

.strategy-card.selected, .bosslimit-card.selected {
  border-color: #6366f1;
  background: rgba(99, 102, 241, 0.1);
  box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.2);
}

.strategy-top, .bosslimit-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.strategy-title {
  font-weight: 700;
  font-size: 0.9rem;
  color: #c7d2fe;
}

.strategy-desc-text {
  font-size: 0.78rem;
  color: #64748b;
  line-height: 1.4;
}

.check-circle {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  border: 2px solid rgba(255, 255, 255, 0.15);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  transition: all 0.25s ease;
  color: white;
}

.check-circle.checked {
  background: #6366f1;
  border-color: #6366f1;
}

/* ═══ SSC ═══ */
.ssc-details {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 12px;
}

.ssc-detail {
  display: flex;
  justify-content: space-between;
  font-size: 0.85rem;
  color: #94a3b8;
}

.ssc-detail span:last-child {
  font-weight: 600;
  color: #cbd5e1;
}

/* ═══ Player Limit ═══ */
.playerlimit-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.playerlimit-row label {
  font-size: 0.85rem;
  font-weight: 600;
  color: #94a3b8;
}

.num-stepper {
  display: flex;
  align-items: center;
  gap: 6px;
}

.num-btn {
  width: 32px;
  height: 32px;
  border-radius: 8px;
  border: 1.5px solid rgba(255, 255, 255, 0.12);
  background: rgba(255, 255, 255, 0.06);
  color: #c7d2fe;
  font-size: 1rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;
}

.num-btn:hover {
  background: rgba(255, 255, 255, 0.12);
}

.num-input {
  width: 60px;
  padding: 6px 8px;
  text-align: center;
  background: rgba(0, 0, 0, 0.3);
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  color: #e2e8f0;
  font-size: 0.9rem;
  font-weight: 700;
  outline: none;
}

.num-input:focus {
  border-color: #6366f1;
}

/* ═══ 完成 ═══ */
.done-section {
  text-align: center;
  padding: 60px 20px;
}

.done-icon {
  font-size: 3rem;
  margin-bottom: 16px;
}

.done-section h2 {
  color: #c7d2fe;
  margin: 0 0 8px;
}

.done-section p {
  color: #64748b;
  margin: 0 0 24px;
}

/* ═══ 加载 ═══ */
.loading-text {
  color: #64748b;
  font-size: 0.9rem;
  padding: 20px 0;
  text-align: center;
}

/* ═══ 响应式 ═══ */
@media (max-width: 640px) {
  .strategy-grid, .bosslimit-grid {
    grid-template-columns: 1fr;
  }
  .form-row {
    flex-direction: column;
  }
  .port-input {
    max-width: 100%;
  }
  .tab-btn {
    padding: 8px 16px;
    font-size: 0.8rem;
  }
  .header-inner {
    padding: 16px 16px 0;
  }
  .header-title {
    font-size: 1.1rem;
  }
}
</style>

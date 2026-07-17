<script setup>
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()
const setupToken = route.query.token || ''

// ═══════════════════════════════════════════════
// 通用状态
// ═══════════════════════════════════════════════
const tokenOk = ref(false)
const error = ref('')
const success = ref('')

// ═══════════════════════════════════════════════
// 渐进式选择
// ═══════════════════════════════════════════════
const selectedMode = ref(null) // 'manual' | 'auto' | null

// ═══════════════════════════════════════════════
// REST 连接设置
// ═══════════════════════════════════════════════
const connected = ref(false)
const restHost = ref('127.0.0.1')
const restPort = ref('7878')
const restApiKey = ref('')

// 手动配置
const manualHost = ref('127.0.0.1')
const manualPort = ref('7878')
const manualApiKey = ref('')
const manualLoading = ref(false)
const showKey = ref(false)

// 自动探测
const probePort = ref('7777')
const probeLoading = ref(false)
const probeResult = ref(null)
const autoReadLoading = ref(false)
const autoReadResult = ref(null)
const autoVerifyLoading = ref(false)
const autoVerifyError = ref('')
const autoStep = ref('idle') // idle | probe | done

// 远程配置
const remoteConfigRaw = ref('')
const remoteLoading = ref(false)
const remoteResult = ref(null)
const remoteHost = ref('')
const remotePort = ref('')
const remoteVerifyLoading = ref(false)
const remoteVerifyError = ref('')
const remoteStep = ref('idle') // idle | review | verify

// 自动配置机器选择（渐进式：先选 本机/远程）
const autoMachineMode = ref(null) // null | 'local' | 'remote'

// 等待连接
const waiting = ref(false)
const statusText = ref('')
let pollTimer = null

// 连接成功后的初始化提示
const initPromptShown = ref(false)

// Token 认证
const tokenMissing = ref(false)
const manualToken = ref('')
const authError = ref('')

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
    // 预填默认值（无论是否已配置，进入此页面即允许重新配置）
    if (data.config) {
      manualHost.value = data.config.host || '127.0.0.1'
      manualPort.value = String(data.config.port || 7878)
      manualApiKey.value = data.config.apiKey || ''
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

// ═══════════════════════════════════════════════
// 手动配置
// ═══════════════════════════════════════════════
async function submitManualConfig() {
  if (!manualHost.value.trim() || !manualPort.value.trim() || !manualApiKey.value.trim()) {
    error.value = '所有字段均为必填'
    return
  }
  const portNum = parseInt(manualPort.value)
  if (isNaN(portNum) || portNum < 1 || portNum > 65535) {
    error.value = '端口必须是 1-65535 之间的数字'
    return
  }
  manualLoading.value = true
  error.value = ''
  try {
    const res = await apiPost('/api/setup/init', {
      host: manualHost.value.trim(),
      port: portNum,
      apiKey: manualApiKey.value.trim()
    })
    const data = await res.json()
    if (!data.success) {
      error.value = data.error || '保存失败'
      return
    }
    restHost.value = manualHost.value.trim()
    restPort.value = String(portNum)
    restApiKey.value = manualApiKey.value.trim()
    success.value = '配置已保存，正在检测连接...'
    startPolling('连接验证成功')
  } catch (err) {
    error.value = '请求失败: ' + err.message
  } finally {
    manualLoading.value = false
  }
}

// ═══════════════════════════════════════════════
// 自动探测
// ═══════════════════════════════════════════════
async function startProbe() {
  probeLoading.value = true
  probeResult.value = null
  autoStep.value = 'idle'
  try {
    const res = await fetch('/api/setup/probe?port=' + probePort.value + '&token=' + encodeURIComponent(setupToken))
    probeResult.value = await res.json()
  } catch (err) {
    probeResult.value = { error: err.message }
  }
  probeLoading.value = false
}

async function startAutoRead() {
  if (!probeResult.value?.processes?.[0]) return
  autoReadLoading.value = true
  try {
    const res = await apiPost('/api/setup/auto-read', {
      processPath: probeResult.value.processes[0].path
    })
    autoReadResult.value = await res.json()
    if (autoReadResult.value.success) {
      autoStep.value = 'done'
      success.value = '已自动修改配置文件！请重启 TShock 服务端后点击验证'
    }
  } catch (err) {
    error.value = err.message
  }
  autoReadLoading.value = false
}

async function startAutoVerify() {
  if (!autoReadResult.value) return
  autoVerifyLoading.value = true
  autoVerifyError.value = ''
  try {
    const res = await apiPost('/api/setup/auto-verify', {
      host: '127.0.0.1',
      port: autoReadResult.value.restPort,
      apiKey: autoReadResult.value.tokenKey
    })
    const data = await res.json()
    if (data.success) {
      restHost.value = '127.0.0.1'
      restPort.value = String(autoReadResult.value.restPort)
      restApiKey.value = autoReadResult.value.tokenKey
      success.value = '验证成功！'
      startPolling('连接验证成功')
    } else {
      autoVerifyError.value = data.error || '验证失败'
    }
  } catch (err) {
    autoVerifyError.value = err.message
  }
  autoVerifyLoading.value = false
}

// ═══════════════════════════════════════════════
// 远程配置
// ═══════════════════════════════════════════════
async function submitRemoteConfig() {
  if (!remoteConfigRaw.value.trim()) {
    error.value = '请粘贴 tshock/config.json 的内容'
    return
  }
  remoteLoading.value = true
  remoteStep.value = 'idle'
  error.value = ''
  try {
    const res = await apiPost('/api/setup/auto-remote', {
      configRaw: remoteConfigRaw.value
    })
    remoteResult.value = await res.json()
    if (remoteResult.value.success) {
      remoteStep.value = 'review'
      remotePort.value = String(remoteResult.value.restPort)
    } else {
      error.value = remoteResult.value.error || '处理失败'
    }
  } catch (err) {
    error.value = err.message
  }
  remoteLoading.value = false
}

function copyRemoteConfig() {
  if (remoteResult.value?.modifiedRaw) {
    navigator.clipboard.writeText(remoteResult.value.modifiedRaw)
    success.value = '已复制到剪贴板'
  }
}

function downloadRemoteConfig() {
  if (!remoteResult.value?.modifiedRaw) return
  const blob = new Blob([remoteResult.value.modifiedRaw], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'config.json'
  a.click()
  URL.revokeObjectURL(url)
  success.value = '已下载配置文件'
}

async function verifyRemoteConnection() {
  if (!remoteResult.value || !remoteHost.value.trim()) return
  remoteVerifyLoading.value = true
  remoteVerifyError.value = ''
  const port = remotePort.value || remoteResult.value.restPort
  try {
    const res = await apiPost('/api/setup/auto-verify', {
      host: remoteHost.value.trim(),
      port: parseInt(port),
      apiKey: remoteResult.value.tokenKey
    })
    const data = await res.json()
    if (data.success) {
      restHost.value = remoteHost.value.trim()
      restPort.value = String(port)
      restApiKey.value = remoteResult.value.tokenKey
      success.value = '远程连接验证成功！'
      startPolling('连接验证成功')
    } else {
      remoteVerifyError.value = data.error || '验证失败'
    }
  } catch (err) {
    remoteVerifyError.value = err.message
  }
  remoteVerifyLoading.value = false
}

// ═══════════════════════════════════════════════
// 轮询连接状态
// ═══════════════════════════════════════════════
function startPolling(doneText) {
  waiting.value = true
  statusText.value = '正在检测连接...'
  pollTimer = setInterval(async () => {
    try {
      const res = await fetch('/api/status')
      const data = await res.json()
      if (data.connected) {
        connected.value = true
        statusText.value = doneText || '已连接'
        stopPolling()
        initPromptShown.value = true
      } else {
        statusText.value = data.message || '等待连接...'
      }
    } catch {
      statusText.value = '无法连接到后端服务'
    }
  }, 2000)
}

function stopPolling() {
  waiting.value = false
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

function goToBackend() {
  router.push('/backend?token=' + encodeURIComponent(setupToken))
}

onMounted(() => {
  // 清理定时器
  return () => {
    if (pollTimer) clearInterval(pollTimer)
  }
})
</script>

<template>
  <div class="init-page">
    <div class="init-container">

      <!-- Token 缺失 -->
      <div v-if="tokenMissing" class="token-auth-overlay">
        <div class="token-auth-card">
          <div class="auth-icon">🔐</div>
          <h2>需要授权</h2>
          <p class="auth-desc">请输入服务端控制台提供的 Token 以进行初始化配置</p>
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

        <!-- ═══ 顶部标题（无选项卡） ═══ -->
        <div class="init-header">
          <h1 class="init-title">TSWeb 后台初始化</h1>
          <p class="init-subtitle">配置 TShock REST 连接以启用后台管理功能</p>
        </div>

        <!-- 操作反馈 -->
        <div v-if="error" class="msg-box error">{{ error }}</div>
        <div v-if="success" class="msg-box success">{{ success }}</div>
        <div v-if="waiting" class="msg-box info">{{ statusText }}</div>

        <!-- ═══ 渐进式选择：两张卡片 ═══ -->
        <div class="mode-cards">
          <div :class="['mode-card', { selected: selectedMode === 'auto' }]"
            @click="selectedMode = 'auto'">
            <div class="mode-icon">⚡</div>
            <h3>自动配置</h3>
            <p>自动扫描本机 TShock 进程，<br/>或解析远程配置文件</p>
            <div class="mode-tag">推荐</div>
          </div>
          <div :class="['mode-card', { selected: selectedMode === 'manual' }]"
            @click="selectedMode = 'manual'">
            <div class="mode-icon">✏️</div>
            <h3>手动配置</h3>
            <p>手动输入服务器地址、<br/>REST 端口和 API 密钥</p>
          </div>
        </div>

        <!-- ═══ 手动配置表单 ═══ -->
        <div v-if="selectedMode === 'manual'" class="form-section">
          <div class="section-card">
            <h3 class="section-heading">手动配置</h3>
            <div class="form-row">
              <div class="form-group flex-1">
                <label class="form-label">服务器地址</label>
                <input v-model="manualHost" type="text" placeholder="127.0.0.1" class="form-input" />
              </div>
              <div class="form-group port-input">
                <label class="form-label">REST 端口</label>
                <input v-model="manualPort" type="text" placeholder="7878" class="form-input" />
              </div>
            </div>
            <div class="form-group">
              <label class="form-label">API 密钥</label>
              <div class="input-with-btn">
                <input v-model="manualApiKey" :type="showKey ? 'text' : 'password'" class="form-input" autocomplete="new-password" />
                <button class="toggle-btn" @click="showKey = !showKey" type="button">{{ showKey ? '隐藏' : '显示' }}</button>
              </div>
            </div>
            <button class="btn primary" @click="submitManualConfig" :disabled="manualLoading">
              {{ manualLoading ? '保存中...' : '测试连接并保存' }}
            </button>

            <!-- 连接成功后提示 -->
            <div v-if="initPromptShown" class="init-prompt">
              <p>✅ 连接成功！配置已保存。</p>
              <div class="prompt-actions">
                <button class="btn primary" @click="goToBackend">进入后台管理</button>
                <button class="btn secondary" @click="initPromptShown = false">继续配置</button>
              </div>
            </div>
          </div>
        </div>

        <!-- ═══ 自动配置 ═══ -->
        <div v-if="selectedMode === 'auto'" class="form-section">
          <div class="section-card">
            <h3 class="section-heading">自动配置</h3>

            <!-- 第一步：选择机器位置（双卡片） -->
            <div v-if="!autoMachineMode" class="machine-cards">
              <div class="machine-card" @click="autoMachineMode = 'local'">
                <div class="machine-icon">🖥️</div>
                <h3>本机</h3>
                <p>自动扫描本机 TShock 进程，<br/>修改配置文件并复制插件</p>
              </div>
              <div class="machine-card" @click="autoMachineMode = 'remote'">
                <div class="machine-icon">🌐</div>
                <h3>远程</h3>
                <p>远程服务器上的 TShock，<br/>粘贴配置文件并手动导入</p>
              </div>
            </div>

            <!-- 返回选择（在卡片下方，始终可见） -->
            <button class="back-link" @click="autoMachineMode ? autoMachineMode = null : selectedMode = null">← 返回选择</button>

            <!-- 第二步：已选本机 → 操作表单 -->
            <template v-if="autoMachineMode === 'local'">
              <p class="section-desc">自动扫描本机 TShock 进程，修改配置文件并复制插件</p>
              <div class="probe-bar">
                <input v-model="probePort" type="text" class="form-input probe-input" placeholder="游戏端口" />
                <button class="btn secondary" @click="startProbe" :disabled="probeLoading">
                  {{ probeLoading ? '扫描中...' : '检测进程' }}
                </button>
              </div>
              <div v-if="probeResult" class="probe-result">
                <div v-if="probeResult.error" class="msg-box error">{{ probeResult.error }}</div>
                <div v-else-if="!probeResult.found" class="msg-box warn">未在端口 {{ probeResult.port }} 找到监听进程</div>
                <div v-else>
                  <p class="probe-count">找到 {{ probeResult.processes.length }} 个监听进程</p>
                  <div v-for="p in probeResult.processes" :key="p.pid" class="probe-item">
                    <code>{{ p.pid }}</code> {{ p.path }}
                  </div>
                  <div v-if="autoStep === 'idle'" class="section-actions">
                    <button class="btn primary" @click="startAutoRead" :disabled="autoReadLoading">
                      {{ autoReadLoading ? '处理中...' : '修改配置并复制插件' }}
                    </button>
                  </div>
                </div>
              </div>
              <div v-if="autoStep === 'done' && autoReadResult" class="auto-done">
                <div class="info-grid mini">
                  <div class="info-item"><span class="info-label">REST 端口</span><span class="info-value">{{ autoReadResult.restPort }}</span></div>
                  <div class="info-item"><span class="info-label">API Key</span><span class="info-value mono">{{ autoReadResult.tokenKey }}</span></div>
                </div>
                <p class="section-desc">已修改 TShock 配置文件并复制插件 DLL，请重启 TShock 服务端后点击验证</p>
                <button class="btn primary" @click="startAutoVerify" :disabled="autoVerifyLoading">
                  {{ autoVerifyLoading ? '验证中...' : '验证连接' }}
                </button>
                <div v-if="autoVerifyError" class="msg-box error" style="margin-top:8px">{{ autoVerifyError }}</div>
                <div v-if="initPromptShown" class="init-prompt">
                  <p>✅ 连接成功！配置已保存。</p>
                  <div class="prompt-actions">
                    <button class="btn primary" @click="goToBackend">进入后台管理</button>
                    <button class="btn secondary" @click="initPromptShown = false">继续配置</button>
                  </div>
                </div>
              </div>
            </template>

            <!-- 第二步：已选远程 → 操作表单 -->
            <template v-if="autoMachineMode === 'remote'">
              <p class="section-desc">粘贴远程服务器上的 <code>tshock/config.json</code> 内容，自动修改并导出</p>
              <textarea v-model="remoteConfigRaw" class="form-textarea" rows="6" placeholder="将远程 tshock/config.json 的完整内容粘贴到这里..."></textarea>
              <button class="btn primary" @click="submitRemoteConfig" :disabled="remoteLoading" style="margin-top:8px">
                {{ remoteLoading ? '处理中...' : '解析并修改配置' }}
              </button>

              <div v-if="remoteStep === 'review' && remoteResult" class="remote-review">
                <div class="info-grid mini">
                  <div class="info-item"><span class="info-label">REST 端口</span><span class="info-value">{{ remoteResult.restPort }}</span></div>
                  <div class="info-item"><span class="info-label">API Key</span><span class="info-value mono">{{ remoteResult.tokenKey }}</span></div>
                </div>
                <p class="section-desc">将修改后的配置文件覆盖到远程服务器，然后重启远程 TShock</p>
                <div class="btn-row">
                  <button class="btn secondary" @click="copyRemoteConfig">📋 复制到剪贴板</button>
                  <button class="btn secondary" @click="downloadRemoteConfig">⬇️ 下载 config.json</button>
                </div>
                <div class="remote-verify">
                  <div class="form-row">
                    <div class="form-group flex-1">
                      <label class="form-label">远程 IP / 域名</label>
                      <input v-model="remoteHost" type="text" placeholder="192.168.1.100" class="form-input" />
                    </div>
                    <div class="form-group port-input">
                      <label class="form-label">REST 端口</label>
                      <input v-model="remotePort" type="text" :placeholder="String(remoteResult.restPort)" class="form-input" />
                    </div>
                  </div>
                  <button class="btn primary" @click="verifyRemoteConnection" :disabled="remoteVerifyLoading || !remoteHost.trim()">
                    {{ remoteVerifyLoading ? '验证中...' : '测试远程连接' }}
                  </button>
                  <div v-if="remoteVerifyError" class="msg-box error" style="margin-top:8px">{{ remoteVerifyError }}</div>
                  <div v-if="initPromptShown" class="init-prompt">
                    <p>✅ 连接成功！配置已保存。</p>
                    <div class="prompt-actions">
                      <button class="btn primary" @click="goToBackend">进入后台管理</button>
                      <button class="btn secondary" @click="initPromptShown = false">继续配置</button>
                    </div>
                  </div>
                </div>
              </div>
            </template>
          </div>
        </div>

      </template>
    </div>
  </div>
</template>

<style scoped>
/* ═══════════════════════════════════════════════
   布局
   ═══════════════════════════════════════════════ */
.init-page {
  min-height: 100vh;
  background: linear-gradient(135deg, #0f0c29, #1a1740, #242150);
  color: #e2e8f0;
  display: flex;
  justify-content: center;
}

.init-container {
  max-width: 720px;
  width: 100%;
  padding: 48px 24px 80px;
}

/* ═══ 顶部标题 ═══ */
.init-header {
  text-align: center;
  margin-bottom: 36px;
}

.init-title {
  margin: 0 0 8px;
  font-size: 1.5rem;
  font-weight: 700;
  color: #c7d2fe;
  letter-spacing: 1px;
}

.init-subtitle {
  margin: 0;
  font-size: 0.9rem;
  color: #64748b;
}

/* ═══ 渐进式选择卡片 ═══ */
.mode-cards {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 32px;
}

.mode-card {
  background: rgba(255, 255, 255, 0.04);
  border: 1.5px solid rgba(255, 255, 255, 0.08);
  border-radius: 20px;
  padding: 36px 24px;
  text-align: center;
  cursor: pointer;
  transition: all 0.3s ease;
  position: relative;
}

.mode-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
  background: rgba(255, 255, 255, 0.06);
  transform: translateY(-3px);
}

.mode-card.selected {
  border-color: #6366f1;
  background: rgba(99, 102, 241, 0.1);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.2), 0 8px 32px rgba(99, 102, 241, 0.15);
  transform: translateY(-3px);
}

.mode-icon {
  font-size: 2.5rem;
  margin-bottom: 12px;
}

.mode-card h3 {
  margin: 0 0 8px;
  font-size: 1.1rem;
  font-weight: 700;
  color: #c7d2fe;
}

.mode-card p {
  margin: 0;
  font-size: 0.82rem;
  color: #64748b;
  line-height: 1.6;
}

.mode-tag {
  position: absolute;
  top: -8px;
  right: 16px;
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
  font-size: 0.7rem;
  font-weight: 700;
  padding: 3px 10px;
  border-radius: 8px;
  letter-spacing: 0.5px;
}

/* ═══ 表单区 ═══ */
.form-section {
  animation: fadeSlideIn 0.35s ease;
}

@keyframes fadeSlideIn {
  from {
    opacity: 0;
    transform: translateY(16px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.section-card {
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  padding: 24px;
  margin-bottom: 20px;
}

.section-heading {
  margin: 0 0 16px;
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

.btn-row {
  display: flex;
  gap: 10px;
  margin: 12px 0;
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

.form-input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15);
}

.form-input::placeholder { color: #475569; }

.form-textarea {
  width: 100%;
  padding: 12px 14px;
  background: rgba(0, 0, 0, 0.3);
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  color: #e2e8f0;
  font-size: 0.85rem;
  outline: none;
  transition: border-color 0.25s ease;
  box-sizing: border-box;
  resize: vertical;
  font-family: 'SF Mono', 'Cascadia Code', 'Consolas', monospace;
  line-height: 1.5;
}

.form-textarea:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15);
}

.form-textarea::placeholder {
  color: #475569;
  font-family: inherit;
}

.input-with-btn {
  display: flex;
  gap: 8px;
}

.input-with-btn .form-input { flex: 1; }

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

/* ═══ Token 认证 ═══ */
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

/* ═══ 自动配置：机器选择卡片 ═══ */
.machine-cards {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 8px;
}

.machine-card {
  background: rgba(255, 255, 255, 0.04);
  border: 1.5px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  padding: 28px 20px;
  text-align: center;
  cursor: pointer;
  transition: all 0.3s ease;
}

.machine-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
  background: rgba(255, 255, 255, 0.06);
  transform: translateY(-2px);
}

.machine-icon {
  font-size: 2.2rem;
  margin-bottom: 10px;
}

.machine-card h3 {
  margin: 0 0 8px;
  font-size: 1.05rem;
  font-weight: 700;
  color: #c7d2fe;
}

.machine-card p {
  margin: 0;
  font-size: 0.82rem;
  color: #64748b;
  line-height: 1.6;
}

/* ═══ 返回选择链接 ═══ */
.back-link {
  display: inline-block;
  margin-bottom: 12px;
  padding: 0;
  background: none;
  border: none;
  font-size: 0.82rem;
  color: #6366f1;
  cursor: pointer;
  transition: color 0.2s;
  font-family: inherit;
}
.back-link:hover {
  color: #818cf8;
  text-decoration: underline;
}

/* ═══ 初始化提示 ═══ */
.init-prompt {
  margin-top: 20px;
  padding: 16px 20px;
  border: 1px solid rgba(34, 197, 94, 0.3);
  border-radius: 12px;
  background: rgba(34, 197, 94, 0.06);
}

.init-prompt p {
  margin: 0 0 12px;
  color: #4ade80;
  font-weight: 600;
  font-size: 0.9rem;
}

.prompt-actions {
  display: flex;
  gap: 10px;
}

/* ═══ 信息网格 ═══ */
.info-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.info-grid.mini {
  grid-template-columns: 1fr 1fr;
  margin-bottom: 12px;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.info-label {
  font-size: 0.8rem;
  color: #64748b;
  font-weight: 500;
}

.info-value {
  font-size: 0.9rem;
  color: #e2e8f0;
  font-weight: 600;
}

.info-value.mono {
  font-family: 'SF Mono', 'Cascadia Code', 'Consolas', monospace;
  font-size: 0.8rem;
}

/* ═══ 探测 ═══ */
.probe-bar {
  display: flex;
  gap: 10px;
  margin-bottom: 12px;
}

.probe-input {
  max-width: 100px;
  flex: none;
}

.probe-result {
  margin-top: 8px;
}

.probe-count {
  font-size: 0.85rem;
  color: #94a3b8;
  margin: 0 0 8px;
}

.probe-item {
  display: flex;
  gap: 10px;
  padding: 6px 0;
  font-size: 0.85rem;
  color: #cbd5e1;
  align-items: center;
}

.probe-item code {
  background: rgba(255, 255, 255, 0.06);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.8rem;
}

.auto-done {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.remote-review {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.remote-verify {
  margin-top: 12px;
}

/* ═══ 响应式 ═══ */
@media (max-width: 640px) {
  .mode-cards {
    grid-template-columns: 1fr;
  }
  .machine-cards {
    grid-template-columns: 1fr;
  }
  .form-row {
    flex-direction: column;
  }
  .port-input {
    max-width: 100%;
  }
  .init-container {
    padding: 32px 16px 60px;
  }
}
</style>

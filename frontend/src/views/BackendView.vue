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

// 等待连接
const waiting = ref(false)
const statusText = ref('')
let pollTimer = null

const tokenMissing = ref(false)

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
    if (data.configured && data.config) {
      restHost.value = data.config.host || '127.0.0.1'
      restPort.value = String(data.config.port || 7878)
      restApiKey.value = data.config.apiKey || ''
      manualHost.value = data.config.host || '127.0.0.1'
      manualPort.value = String(data.config.port || 7878)
      manualApiKey.value = data.config.apiKey || ''
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

// ═══════════════════════════════════════════════
// REST 设置 - 手动配置
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
// REST 设置 - 自动探测
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
// REST 设置 - 远程配置
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

// ═══════════════════════════════════════════════
// 日志推流
// ═══════════════════════════════════════════════
async function loadWebhookConfig() {
  webhookLoading.value = true
  try {
    const res = await fetch('/api/config/log-webhook', {
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
    const res = await fetch('/api/config/log-webhook', {
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
    // 保存 SSC
    if (sscConfig.value) {
      sscConfig.value.Settings.Enabled = sscEnabled.value
      await apiPost('/api/setup/ssc-config', {
        content: JSON.stringify(sscConfig.value, null, 2)
      })
    }
    // 初始化插件
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

onUnmounted(() => stopPolling())
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

      <!-- Token 缺失 -->
      <div v-if="tokenMissing" class="empty-state">
        <div class="empty-icon">🔒</div>
        <h2>需要 Setup Token</h2>
        <p class="empty-desc">请在服务端控制台输入 <code>setup</code> 获取访问链接</p>
      </div>

      <!-- Token 有效 -->
      <template v-else>

        <!-- ═══════════════════════════════════════ -->
        <!-- TAB 1: REST 连接设置 -->
        <!-- ═══════════════════════════════════════ -->
        <div v-if="activeTab === 'rest'" class="tab-content">

          <!-- 当前状态 -->
          <div class="section-card">
            <div class="section-header">
              <h3>当前连接</h3>
              <span :class="['status-badge', connected ? 'ok' : 'off']">
                {{ connected ? '已连接' : '未连接' }}
              </span>
            </div>
            <div class="info-grid">
              <div class="info-item">
                <span class="info-label">服务器地址</span>
                <span class="info-value">{{ restHost }}:{{ restPort }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">API Key</span>
                <span class="info-value mono">{{ restApiKey ? restApiKey.substring(0, 16) + '...' : '未设置' }}</span>
              </div>
            </div>
          </div>

          <!-- 操作反馈 -->
          <div v-if="error && activeTab === 'rest'" class="msg-box error">{{ error }}</div>
          <div v-if="success && activeTab === 'rest' && activeTab === 'rest'" class="msg-box success">{{ success }}</div>
          <div v-if="waiting && activeTab === 'rest'" class="msg-box info">{{ statusText }}</div>

          <!-- 手动配置 -->
          <div class="section-card">
            <div class="section-header"><h3>手动配置</h3></div>
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
          </div>

          <!-- 本机自动检测 -->
          <div class="section-card">
            <div class="section-header"><h3>本机自动检测</h3></div>
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
            </div>
          </div>

          <!-- 远程配置 -->
          <div class="section-card">
            <div class="section-header"><h3>远程配置</h3></div>
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
              </div>
            </div>
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
  align-items: center;
}

.header-title {
  margin: 0 0 20px;
  font-size: 1.3rem;
  font-weight: 700;
  color: #c7d2fe;
  letter-spacing: 1px;
}

.tab-bar {
  display: flex;
  gap: 4px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 12px;
  padding: 4px;
}

.tab-btn {
  padding: 10px 28px;
  border: none;
  border-radius: 10px;
  background: transparent;
  color: #94a3b8;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
  white-space: nowrap;
}

.tab-btn:hover {
  color: #c7d2fe;
  background: rgba(255, 255, 255, 0.06);
}

.tab-btn.active {
  background: rgba(99, 102, 241, 0.25);
  color: #a5b4fc;
  box-shadow: 0 0 12px rgba(99, 102, 241, 0.15);
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

.btn-row {
  display: flex;
  gap: 10px;
  margin: 12px 0;
  flex-wrap: wrap;
}

/* ═══ 状态标签 ═══ */
.status-badge {
  padding: 4px 14px;
  border-radius: 20px;
  font-size: 0.8rem;
  font-weight: 700;
}

.status-badge.ok {
  background: rgba(34, 197, 94, 0.15);
  color: #4ade80;
}

.status-badge.off {
  background: rgba(239, 68, 68, 0.15);
  color: #f87171;
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

.form-input::placeholder {
  color: #475569;
}

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

/* ═══ 空状态 ═══ */
.empty-state {
  text-align: center;
  padding: 80px 20px;
}

.empty-icon {
  font-size: 3rem;
  margin-bottom: 16px;
}

.empty-state h2 {
  color: #c7d2fe;
  margin: 0 0 8px;
}

.empty-desc {
  color: #64748b;
  font-size: 0.9rem;
}

.empty-desc code {
  background: rgba(255, 255, 255, 0.08);
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.85rem;
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

/* ═══ 远程配置 ═══ */
.remote-review {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.remote-verify {
  margin-top: 12px;
}

/* ═══ RCON ═══ */
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

.rcon-fields {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.field-hint {
  font-size: 0.8rem;
  color: #64748b;
  margin: 8px 0 0;
}

.test-result {
  font-size: 0.85rem;
  font-weight: 600;
}

.test-result.ok { color: #4ade80; }
.test-result.fail { color: #f87171; }

.rcon-disabled-hint {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.rcon-disabled-hint p {
  margin: 0;
  font-size: 0.85rem;
  color: #64748b;
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
  .info-grid {
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

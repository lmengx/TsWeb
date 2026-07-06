<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'

const router = useRouter()
const route = useRoute()

const step = ref('check')
const host = ref('127.0.0.1')
const port = ref('7878')
const apiKey = ref('')
const loading = ref(false)
const error = ref('')
const showApiKey = ref(false)

const configExists = ref(false)

const manageHost = ref('')
const managePort = ref('')
const manageApiKey = ref('')
const manageLoading = ref(false)
const manageError = ref('')
const manageSuccess = ref('')
const manageShowKey = ref(false)

const setupToken = route.query.token || ''

const statusText = ref('')
const statusOk = ref(false)
const polling = ref(false)
let pollTimer = null

// 端口探测
const probePort = ref('7777')
const probeLoading = ref(false)
const probeResult = ref(null)

const startProbe = async () => {
  probeLoading.value = true
  probeResult.value = null
  try {
    const res = await fetch('/api/setup/probe?port=' + probePort.value + '&token=' + encodeURIComponent(setupToken))
    probeResult.value = await res.json()
  } catch (err) {
    probeResult.value = { error: err.message }
  }
  probeLoading.value = false
}

// 远程配置
const remoteConfigRaw = ref('')
const remoteLoading = ref(false)
const remoteResult = ref(null)
const remoteVerifyLoading = ref(false)
const remoteVerifyError = ref('')
const remoteActionDone = ref(false)
const remoteHost = ref('')
const remotePort = ref('')
const showPortHint = ref(false)

const submitRemoteConfig = async () => {
  if (!remoteConfigRaw.value.trim()) {
    error.value = '请粘贴 tshock/config.json 的内容'
    return
  }
  remoteLoading.value = true
  remoteResult.value = null
  error.value = ''
  try {
    const res = await fetch('/api/setup/auto-remote', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ configRaw: remoteConfigRaw.value, token: setupToken })
    })
    const data = await res.json()
    if (data.success) {
      remoteResult.value = data
        step.value = 'auto-remote-review'
        remotePort.value = String(data.restPort)
    } else {
      error.value = data.error || '处理失败'
    }
  } catch (err) {
    error.value = err.message
  }
  remoteLoading.value = false
}

const copyRemoteConfig = () => {
  if (remoteResult.value?.modifiedRaw) {
    navigator.clipboard.writeText(remoteResult.value.modifiedRaw)
    remoteActionDone.value = true
  }
}

const downloadRemoteConfig = () => {
  if (!remoteResult.value?.modifiedRaw) return
  const blob = new Blob([remoteResult.value.modifiedRaw], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'config.json'
  a.click()
  URL.revokeObjectURL(url)
  remoteActionDone.value = true
}

const verifyRemoteConnection = async () => {
  if (!remoteResult.value || !remoteHost.value.trim()) return
  remoteVerifyLoading.value = true
  remoteVerifyError.value = ''
  const port = remotePort.value || remoteResult.value.restPort
  try {
    const res = await fetch('/api/setup/auto-verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        host: remoteHost.value.trim(),
        port: parseInt(port),
        apiKey: remoteResult.value.tokenKey,
        token: setupToken
      })
    })
    const data = await res.json()
    if (data.success) {
      step.value = 'waiting'
      statusText.value = '验证成功，正在检测插件状态...'
      startPolling()
    } else {
      remoteVerifyError.value = data.error || '验证失败'
    }
  } catch (err) {
    remoteVerifyError.value = err.message
  }
  remoteVerifyLoading.value = false
}
const autoReadLoading = ref(false)
const autoReadResult = ref(null)
const autoVerifyLoading = ref(false)
const autoVerifyError = ref('')
const autoVerifyDone = ref(false)

const startAutoRead = async () => {
  if (!probeResult.value?.processes?.[0]) return
  autoReadLoading.value = true
  autoReadResult.value = null
  try {
    const res = await fetch('/api/setup/auto-read', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        processPath: probeResult.value.processes[0].path,
        token: setupToken
      })
    })
    autoReadResult.value = await res.json()
    if (autoReadResult.value.success) {
      step.value = 'auto-restart'
    }
  } catch (err) {
    autoReadResult.value = { error: err.message }
  }
  autoReadLoading.value = false
}

const startAutoVerify = async () => {
  if (!autoReadResult.value) return
  autoVerifyLoading.value = true
  autoVerifyError.value = ''
  try {
    const res = await fetch('/api/setup/auto-verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        host: '127.0.0.1',
        port: autoReadResult.value.restPort,
        apiKey: autoReadResult.value.tokenKey,
        token: setupToken
      })
    })
    const data = await res.json()
    if (data.success) {
      autoVerifyDone.value = true
      step.value = 'waiting'
      statusText.value = '验证成功，正在检测插件状态...'
      startPolling()
    } else {
      autoVerifyError.value = data.error || '验证失败'
    }
  } catch (err) {
    autoVerifyError.value = err.message
  }
  autoVerifyLoading.value = false
}

onMounted(async () => {
  if (!setupToken) {
    step.value = 'no-access'
    return
  }
  try {
    const res = await fetch('/api/setup/check?token=' + encodeURIComponent(setupToken))
    const data = await res.json()
    if (data.needToken) {
      step.value = 'no-access'
      return
    }
    if (data.configured) {
      configExists.value = true
      step.value = 'manage'
      if (data.config) {
        manageHost.value = data.config.host || '127.0.0.1'
        managePort.value = String(data.config.port || 7878)
        manageApiKey.value = data.config.apiKey || ''
      }
    } else {
      step.value = 'method'
    }
  } catch {
    step.value = 'no-access'
  }
})

const updateConnection = async () => {
  if (!manageHost.value.trim() || !managePort.value.trim() || !manageApiKey.value.trim()) {
    manageError.value = '所有字段均为必填'
    return
  }
  manageLoading.value = true
  manageError.value = ''
  manageSuccess.value = ''
  try {
    const res = await fetch('/api/setup/init', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        host: manageHost.value.trim(),
        port: parseInt(managePort.value),
        apiKey: manageApiKey.value.trim(),
        token: setupToken
      })
    })
    const data = await res.json()
    if (data.success) {
      manageSuccess.value = '连接配置已更新'
      step.value = 'waiting'
      statusText.value = '配置已更新，正在连接 TShock...'
      startPolling()
    } else {
      manageError.value = data.error || '更新失败'
    }
  } catch (err) {
    manageError.value = '请求失败: ' + err.message
  }
  manageLoading.value = false
}

const reinstallPlugin = () => {
  router.push(`/setup/plugin?token=${encodeURIComponent(setupToken)}`)
}

onUnmounted(() => stopPolling())

function startPolling() {
  polling.value = true
  pollTimer = setInterval(async () => {
    try {
      const res = await fetch('/api/status')
      const data = await res.json()
      if (data.connected) {
        statusOk.value = true
        statusText.value = '已连接到 TShock 服务器'
        stopPolling()
        // 直接跳转到插件初始化页面
        setTimeout(() => router.push(`/setup/plugin?token=${encodeURIComponent(setupToken)}`), 1000)
      } else {
        statusText.value = data.message || '等待连接...'
      }
    } catch {
      statusText.value = '无法连接到后端服务'
    }
  }, 2000)
}

function stopPolling() {
  polling.value = false
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

const selectMethod = (method) => {
  if (method === 'manual') {
    step.value = 'form'
  } else if (method === 'auto') {
    step.value = 'auto-location'
  }
}

const selectAutoLocation = (location) => {
  if (location === 'local') {
    step.value = 'auto-local-probe'
    probeResult.value = null
  } else {
    step.value = 'auto-remote-paste'
  }
}

const submitConfig = async () => {
  if (!host.value.trim() || !port.value.trim() || !apiKey.value.trim()) {
    error.value = '所有字段均为必填'
    return
  }
  const portNum = parseInt(port.value)
  if (isNaN(portNum) || portNum < 1 || portNum > 65535) {
    error.value = '端口必须是 1-65535 之间的数字'
    return
  }
  loading.value = true
  error.value = ''
  try {
    const res = await fetch('/api/setup/init', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        host: host.value.trim(),
        port: portNum,
        apiKey: apiKey.value.trim(),
        token: setupToken
      })
    })
    const data = await res.json()
    if (!data.success) {
      error.value = data.error || '保存失败'
      loading.value = false
      return
    }
    step.value = 'waiting'
    statusText.value = '配置已保存，正在连接 TShock...'
    startPolling()
  } catch (err) {
    error.value = '请求失败: ' + err.message
  }
  loading.value = false
}

const goBack = () => {
  step.value = 'method'
}
</script>

<template>
  <div class="setup-page">
    <div class="setup-card">
      <div class="setup-logo">
        <h1>TsWeb</h1>
        <p class="subtitle">TShock Web 管理面板</p>
      </div>

      <!-- 检测中 -->
      <div v-if="step === 'check'" class="setup-body">
        <p>检查配置状态...</p>
      </div>

      <!-- 管理页面（已有配置时） -->
      <div v-else-if="step === 'manage'" class="setup-body">
        <h2>管理面板</h2>
        <p class="desc">后端配置已存在，可更新连接设置或重新初始化插件。</p>

        <div class="manage-section">
          <h3 class="manage-title">服务器连接</h3>
          <div class="form-group">
            <label class="form-label">服务器地址</label>
            <input v-model="manageHost" type="text" class="form-input" />
          </div>
          <div class="form-group">
            <label class="form-label">REST API 端口</label>
            <input v-model="managePort" type="text" class="form-input" />
          </div>
          <div class="form-group">
            <label class="form-label">API 密钥</label>
            <div class="input-with-btn">
              <input v-model="manageApiKey" :type="manageShowKey ? 'text' : 'password'" class="form-input" autocomplete="new-password" />
              <button class="toggle-btn" @click="manageShowKey = !manageShowKey" type="button">{{ manageShowKey ? '隐藏' : '显示' }}</button>
            </div>
          </div>
          <div v-if="manageError" class="error-msg">{{ manageError }}</div>
          <div v-if="manageSuccess" class="success-msg">{{ manageSuccess }}</div>
          <button class="submit-btn" @click="updateConnection" :disabled="manageLoading" style="margin-top:12px">
            {{ manageLoading ? '更新中...' : '更新连接配置' }}
          </button>
        </div>

        <div class="manage-section" style="margin-top:24px">
          <h3 class="manage-title">插件初始化</h3>
          <p class="desc" style="margin-top:4px">重新配置 TSWeb 插件的进服策略和开荒设置。</p>
          <button class="submit-btn" @click="reinstallPlugin" style="margin-top:12px">重新初始化插件</button>
        </div>
      </div>

      <!-- 选择配置方式 -->
      <div v-else-if="step === 'method'" class="setup-body">
        <h2>选择配置方式</h2>
        <p class="desc">请选择 TShock 服务器的连接配置方式</p>
        <div class="method-grid">
          <button class="method-card" @click="selectMethod('auto')">
            <span class="method-icon">⚡</span>
            <span class="method-title">自动配置</span>
            <span class="method-desc">自动检测本机 TShock 配置</span>
          </button>
          <button class="method-card" @click="selectMethod('manual')">
            <span class="method-icon">✏️</span>
            <span class="method-title">手动配置</span>
            <span class="method-desc">手动输入连接地址和密钥</span>
          </button>
        </div>
      </div>

      <!-- 自动配置 - 选择服务器位置 -->
      <div v-else-if="step === 'auto-location'" class="setup-body">
        <button class="back-btn" @click="goBack">← 返回</button>
        <h2>TsWeb 管理端与 TShock 服务器是否在同一台机器上？</h2><br>
        <div class="method-grid">
          <button class="method-card" @click="selectAutoLocation('local')">
            <span class="method-icon">🖥️</span>
            <span class="method-title">同一台机器</span>
            <span class="method-desc">TsWeb Node 端和 TShock 在本机</span>
          </button>
          <button class="method-card" @click="selectAutoLocation('remote')">
            <span class="method-icon">🌐</span>
            <span class="method-title">远程服务器</span>
            <span class="method-desc">TShock 运行在另一台服务器上</span>
          </button>
        </div>
      </div>

      <!-- 自动配置 - 本机探测 -->
      <div v-else-if="step === 'auto-local-probe'" class="setup-body">
        <button class="back-btn" @click="goBack">← 返回</button>
        <h2>检测 TShock 服务器</h2>
        <div class="probe-instructions">
          <div class="probe-header">
            <span class="probe-icon">🎮</span>
            <div>
              <p class="probe-title">请先启动 TShock 服务端</p>
              <p class="probe-sub">确保 TShock 已在<strong>本机</strong>运行，并监听默认端口 <strong>7777</strong></p>
            </div>
          </div>
          <div class="probe-steps">
            <div class="step-item"><span class="step-num">1</span> 启动 TShock / TerrariaServer.exe</div>
            <div class="step-item"><span class="step-num">2</span> 确认服务端已加载世界并进入游戏</div>
            <div class="step-item"><span class="step-num">3</span> 点击下方「检测」按钮扫描端口</div>
          </div>
        </div>

        <div class="probe-bar">
          <input v-model="probePort" type="text" class="form-input probe-input" placeholder="端口" />
          <button class="submit-btn probe-btn" @click="startProbe" :disabled="probeLoading">
            {{ probeLoading ? '扫描中...' : '检测' }}
          </button>
        </div>

        <div v-if="probeResult" class="probe-result">
          <div v-if="probeResult.error" class="error-msg">{{ probeResult.error }}</div>
          <div v-else-if="!probeResult.found" class="probe-empty">
            <p>未在端口 {{ probeResult.port }} 上找到监听中的进程。</p>
            <p class="hint">请确认 TShock 服务器已启动，或检查端口号是否正确。</p>
          </div>
          <div v-else class="probe-found">
            <p class="probe-count">找到 {{ probeResult.processes.length }} 个监听进程：</p>
            <div v-for="p in probeResult.processes" :key="p.pid" class="probe-item">
              <span class="probe-pid">PID: {{ p.pid }}</span>
              <span class="probe-path">{{ p.path }}</span>
            </div>
            <button class="submit-btn" style="margin-top:16px" @click="startAutoRead" :disabled="autoReadLoading">
              {{ autoReadLoading ? '处理中...' : '下一步' }}
            </button>
          </div>
        </div>
      </div>

      <!-- 远程配置 - 粘贴 JSON -->
      <div v-else-if="step === 'auto-remote-paste'" class="setup-body">
        <button class="back-btn" @click="goBack">← 返回</button>
        <h2>粘贴配置文件</h2>
        <div class="file-path-guide">
          <div class="guide-step"><span class="guide-num">1</span> 在远程服务器上找到 <strong>tshock根目录</strong> </div>
          <div class="guide-step"><span class="guide-num">2</span> 打开其中的 <strong>tshock</strong> 子文件夹</div>
          <div class="guide-step"><span class="guide-num">3</span> 找到 <strong>config.json</strong>，将其完整内容粘贴到下方</div>
        </div>
        <textarea
          v-model="remoteConfigRaw"
          class="config-textarea"
          rows="10"
          placeholder="将 config.json 的完整内容粘贴到这里...它以{“Settings”: {开头"
        ></textarea>
<br>
        <div v-if="error" class="error-msg">{{ error }}</div>
        <button class="submit-btn" @click="submitRemoteConfig" :disabled="remoteLoading">
          {{ remoteLoading ? '处理中...' : '解析并修改配置' }}
        </button>
      </div>

      <!-- 远程配置 - 查看修改结果 -->
      <div v-else-if="step === 'auto-remote-review'" class="setup-body">
        <button class="back-btn" @click="goBack">← 返回</button>
        <div class="status-icon">✏️</div>
        <h2>配置已修改</h2>
        <div class="auto-info">
          <p>已对配置做出以下更改：</p>
          <div class="info-line"><span class="info-label">REST 端口</span><span class="info-value">{{ remoteResult?.restPort }}</span></div>
          <div class="info-line"><span class="info-label">Token</span><span class="info-value token">{{ remoteResult?.tokenKey }}</span></div>
        </div>
        <p class="desc" style="margin-top:16px">请将修改后的配置文件<span class="highlight-text">任选以下一种方式</span>覆盖到远程服务器：</p>
        <div class="remote-actions">
          <div class="remote-action-card" @click="copyRemoteConfig">
            <span class="action-icon">📋</span>
            <span class="action-title">复制到剪贴板</span>
            <span class="action-desc">复制修改后的 JSON，粘贴到远程服务器上覆盖原文件</span>
          </div>
          <div class="remote-action-card" @click="downloadRemoteConfig">
            <span class="action-icon">⬇️</span>
            <span class="action-title">下载 config.json</span>
            <span class="action-desc">下载修改后的配置文件，通过 SFTP/SCP 等方式上传到远程服务器</span>
          </div>
        </div>
        <p class="hint" style="margin-top:16px">覆盖文件后，请重启远程 TShock 服务端以使配置生效。</p>
        <button class="submit-btn" style="margin-top:16px" :disabled="!remoteActionDone" @click="step = 'auto-remote-restart'">已完成覆盖，下一步</button>
      </div>

      <!-- 远程配置 - 重启验证 -->
      <div v-else-if="step === 'auto-remote-restart'" class="setup-body">
        <button class="back-btn" @click="goBack">← 返回</button>
        <div class="status-icon">🔄</div>
        <h2>连接远程服务器</h2>
        <div class="form-group">
          <label class="form-label">服务器 IP / 域名</label>
          <input v-model="remoteHost" type="text" placeholder="例如 192.168.1.100" class="form-input" />
        </div>
        <div class="form-group">
          <label class="form-label">REST API 端口</label>
          <input v-model="remotePort" type="text" :placeholder="String(remoteResult?.restPort || 7878)" class="form-input" @focus="showPortHint = true" @blur="showPortHint = false" />
          <div v-if="showPortHint" class="port-hint">
            <p>💡 如果 TShock 运行在 <strong>云服务器</strong> 上，你可能需要在云服务商的安全组/防火墙中<strong>单独放行 REST 端口</strong>（{{ remoteResult?.restPort || 7878 }}），就像放行游戏端口 7777 一样。</p>
            <p>如果使用了端口映射（NAT），请在此处填写<strong>外部映射端口</strong>。</p>
          </div>
        </div>
        <p class="desc">请确保远程 TShock 服务端已重启，然后点击下方按钮测试连接。</p>
        <button class="submit-btn" @click="verifyRemoteConnection" :disabled="remoteVerifyLoading || !remoteHost.trim()">
          {{ remoteVerifyLoading ? '验证中...' : '测试连接' }}
        </button>
        <div v-if="remoteVerifyError" class="error-msg" style="margin-top:12px">{{ remoteVerifyError }}</div>
      </div>

      <!-- 自动配置 - 重启服务端 -->
      <div v-else-if="step === 'auto-restart'" class="setup-body">
        <button class="back-btn" @click="goBack">← 返回</button>
        <div class="status-icon">🔄</div>
        <h2>重启 TShock 服务端</h2>
        <div class="auto-info">
          <p>已自动完成以下配置：</p>
          <div class="info-line"><span class="info-label">REST 端口</span><span class="info-value">{{ autoReadResult?.restPort }}</span></div>
          <div class="info-line"><span class="info-label">Token</span><span class="info-value token">{{ autoReadResult?.tokenKey }}</span></div>
          <div class="info-line"><span class="info-label">配置位置</span><span class="info-value path">{{ autoReadResult?.configPath }}</span></div>
          <p class="hint" style="margin-top:16px">请重启 TShock 服务端以加载新配置，然后点击下方按钮完成配置。</p>
        </div>
        <button class="submit-btn" @click="startAutoVerify" :disabled="autoVerifyLoading">
          {{ autoVerifyLoading ? '验证中...' : '已完成重启，验证连接' }}
        </button>
        <div v-if="autoVerifyError" class="error-msg" style="margin-top:12px">{{ autoVerifyError }}</div>
      </div>

      <!-- 自动配置 - 完成 -->
      <div v-else-if="step === 'auto-done'" class="setup-body">
        <div class="status-icon ok">✅</div>
        <h2>配置完成</h2>
        <p class="desc">已成功连接到 TShock REST 接口，配置已保存。</p>
        <button class="submit-btn" @click="router.push('/')">进入首页</button>
      </div>

      <!-- 手动配置表单 -->
      <div v-else-if="step === 'form'" class="setup-body">
        <button class="back-btn" @click="goBack">← 返回</button>
        <h2>手动配置</h2>
        <p class="desc">填写 TShock 服务器的连接信息</p>
        <div class="form-group">
          <label class="form-label">服务器地址</label>
          <input v-model="host" type="text" placeholder="例如 127.0.0.1" class="form-input" @keyup.enter="submitConfig" />
        </div>
        <div class="form-group">
          <label class="form-label">REST API 端口</label>
          <input v-model="port" type="text" placeholder="默认 7878" class="form-input" @keyup.enter="submitConfig" />
        </div>
        <div class="form-group">
          <label class="form-label">API 密钥</label>
          <div class="input-with-btn">
            <input v-model="apiKey" :type="showApiKey ? 'text' : 'password'" placeholder="输入 TShock REST API 密钥" class="form-input" autocomplete="new-password" @keyup.enter="submitConfig" />
            <button class="toggle-btn" @click="showApiKey = !showApiKey" type="button">{{ showApiKey ? '隐藏' : '显示' }}</button>
          </div>
        </div>
        <div v-if="error" class="error-msg">{{ error }}</div>
        <button class="submit-btn" @click="submitConfig" :disabled="loading">{{ loading ? '保存中...' : '测试连接并保存' }}</button>
      </div>

      <!-- 等待连接 -->
      <div v-else-if="step === 'waiting'" class="setup-body">
        <div class="status-icon" :class="{ ok: statusOk }">{{ statusOk ? '✅' : '🔄' }}</div>
        <h2>{{ statusOk ? '连接成功' : '等待连接' }}</h2>
        <p class="desc">{{ statusText }}</p>
        <p v-if="!statusOk" class="hint">后端正在自动尝试连接 TShock 服务器，连接成功后会自动跳转</p>
      </div>

      <!-- 无权限 -->
      <div v-else-if="step === 'no-access'" class="setup-body">
        <div class="status-icon">🔒</div>
        <h2>需要 Setup Token</h2>
        <p class="desc">请在服务端控制台查看 Token，以 <code>?token=xxx</code> 方式访问此页面。</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.setup-page {
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
  max-width: 540px;
  background: rgba(255, 255, 255, 0.85);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border-radius: 24px;
  padding: 40px;
  box-shadow: 0 8px 40px rgba(99, 102, 241, 0.12);
  border: 1px solid rgba(255, 255, 255, 0.6);
}

.setup-logo {
  text-align: center;
  margin-bottom: 32px;
}

.setup-logo h1 {
  margin: 0;
  font-size: 2rem;
  font-weight: 800;
  background: linear-gradient(135deg, #4f46e5, #7c3aed);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.subtitle {
  color: #6b7280;
  font-size: 0.9rem;
  margin: 4px 0 0;
}

.setup-body h2 {
  color: #1e1b4b;
  font-size: 1.2rem;
  margin: 0 0 8px;
  font-weight: 700;
}

.desc {
  color: #6b7280;
  font-size: 0.85rem;
  margin: 0 0 24px;
  line-height: 1.6;
}

.hint {
  color: #9ca3af;
  font-size: 0.8rem;
  margin: 0;
  line-height: 1.5;
}

.form-group {
  margin-bottom: 18px;
}

.form-label {
  display: block;
  color: #1e1b4b;
  font-size: 0.85rem;
  font-weight: 600;
  margin-bottom: 6px;
}

.form-input {
  width: 100%;
  padding: 12px 16px;
  background: white;
  border: 2px solid rgba(0, 0, 0, 0.1);
  border-radius: 10px;
  color: #0f0a3a;
  font-size: 0.95rem;
  transition: all 0.25s ease;
  box-sizing: border-box;
  outline: none;
}

.form-input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15);
}

.form-input::placeholder {
  color: #9ca3af;
}

.input-with-btn {
  display: flex;
  gap: 8px;
}

.input-with-btn .form-input {
  flex: 1;
}

.toggle-btn {
  padding: 10px 16px;
  background: rgba(0, 0, 0, 0.04);
  border: 2px solid rgba(0, 0, 0, 0.1);
  border-radius: 10px;
  color: #6b7280;
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 500;
  white-space: nowrap;
  transition: all 0.2s ease;
}

.toggle-btn:hover {
  border-color: #6366f1;
  color: #6366f1;
}

.error-msg {
  padding: 10px 14px;
  background: rgba(239, 68, 68, 0.1);
  color: #dc2626;
  border-radius: 8px;
  border: 1px solid rgba(239, 68, 68, 0.2);
  font-size: 0.85rem;
  margin-bottom: 16px;
}

.success-msg {
  padding: 10px 14px;
  background: rgba(22, 163, 74, 0.1);
  color: #16a34a;
  border-radius: 8px;
  border: 1px solid rgba(22, 163, 74, 0.2);
  font-size: 0.85rem;
  margin-bottom: 16px;
  font-weight: 500;
}

.submit-btn {
  width: 100%;
  padding: 14px 24px;
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
  border: none;
  border-radius: 10px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
}

.submit-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.4);
}

.submit-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.back-btn {
  background: none;
  border: none;
  color: #6366f1;
  cursor: pointer;
  font-size: 0.9rem;
  padding: 0 0 16px;
  display: block;
}

.back-btn:hover {
  color: #4f46e5;
  text-decoration: underline;
}

.status-icon {
  text-align: center;
  font-size: 3rem;
  margin-bottom: 16px;
}

.status-icon:not(.ok) {
  animation: spin 2s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* 管理页面 */
.manage-section {
  background: rgba(255, 255, 255, 0.7);
  border: 1px solid rgba(0, 0, 0, 0.06);
  border-radius: 14px;
  padding: 20px;
}

.manage-title {
  color: #0f0a3a;
  font-size: 0.95rem;
  font-weight: 700;
  margin: 0;
}

/* 选择卡片 */
.method-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-top: 8px;
}

.method-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  padding: 28px 20px;
  background: rgba(255, 255, 255, 0.7);
  border: 2px solid rgba(0, 0, 0, 0.06);
  border-radius: 14px;
  cursor: pointer;
  transition: all 0.25s ease;
  text-align: center;
}

.method-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.08);
  transform: translateY(-2px);
}

.method-icon {
  font-size: 2rem;
}

.method-title {
  color: #0f0a3a;
  font-size: 1rem;
  font-weight: 700;
}

.method-desc {
  color: #6b7280;
  font-size: 0.8rem;
  line-height: 1.4;
}

/* 探测 */
.probe-instructions {
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.1), rgba(139, 92, 246, 0.06));
  border: 1.5px solid rgba(99, 102, 241, 0.25);
  border-radius: 14px;
  padding: 20px;
  margin-bottom: 20px;
}

.probe-header {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  margin-bottom: 16px;
}

.probe-icon {
  font-size: 2.2rem;
  line-height: 1;
}

.probe-title {
  color: #0f0a3a;
  font-size: 1.1rem;
  font-weight: 700;
  margin: 0 0 4px;
}

.probe-sub {
  color: #6b7280;
  font-size: 0.9rem;
  margin: 0;
  line-height: 1.5;
}

.probe-sub strong {
  color: #6366f1;
}

.probe-steps {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.step-item {
  display: flex;
  align-items: center;
  gap: 10px;
  color: #1e1b4b;
  font-size: 0.9rem;
}

.step-num {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  background: rgba(99, 102, 241, 0.15);
  border-radius: 50%;
  color: #6366f1;
  font-size: 0.8rem;
  font-weight: 700;
  flex-shrink: 0;
}

.probe-bar {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
}

.probe-input {
  max-width: 100px;
  flex: none !important;
}

.probe-btn {
  width: auto !important;
  padding: 12px 24px;
  flex-shrink: 0;
}

.probe-result {
  margin-top: 12px;
}

.probe-empty {
  padding: 20px;
  text-align: center;
  color: #6b7280;
  font-size: 0.9rem;
}

.probe-empty p {
  margin: 4px 0;
}

.probe-count {
  color: #0f0a3a;
  font-weight: 600;
  margin-bottom: 12px;
  font-size: 0.9rem;
}

.probe-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 16px;
  background: rgba(255, 255, 255, 0.7);
  border: 1px solid rgba(0, 0, 0, 0.06);
  border-radius: 10px;
  margin-bottom: 8px;
}

.probe-pid {
  color: #6366f1;
  font-size: 0.8rem;
  font-weight: 600;
  font-family: monospace;
}

.probe-path {
  color: #1e1b4b;
  font-size: 0.85rem;
  word-break: break-all;
  font-family: monospace;
}

/* 自动配置信息 */
.auto-info {
  background: rgba(99, 102, 241, 0.06);
  border: 1px solid rgba(99, 102, 241, 0.2);
  border-radius: 10px;
  padding: 16px;
  margin-bottom: 20px;
}

.auto-info p {
  color: #1e1b4b;
  font-size: 0.85rem;
  margin: 0 0 12px;
}

.info-line {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 0;
  border-bottom: 1px solid rgba(0, 0, 0, 0.06);
}

.info-line:last-child {
  border-bottom: none;
}

.info-label {
  color: #6b7280;
  font-size: 0.8rem;
}

.info-value {
  color: #0f0a3a;
  font-size: 0.85rem;
  font-weight: 600;
}

.info-value.token {
  color: #6366f1;
  font-family: monospace;
  font-size: 0.8rem;
  word-break: break-all;
}

.info-value.path {
  color: #6b7280;
  font-family: monospace;
  font-size: 0.75rem;
  word-break: break-all;
}

/* 远程配置 */
.config-textarea {
  width: 100%;
  padding: 12px 16px;
  background: white;
  border: 2px solid rgba(0, 0, 0, 0.1);
  border-radius: 10px;
  color: #0f0a3a;
  font-size: 0.8rem;
  font-family: monospace;
  line-height: 1.5;
  transition: all 0.25s ease;
  box-sizing: border-box;
  outline: none;
  resize: vertical;
  min-height: 200px;
}

.config-textarea:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15);
}

/* 端口提示 */
.port-hint {
  margin-top: 8px;
  padding: 12px 16px;
  background: rgba(245, 158, 11, 0.08);
  border: 1px solid rgba(245, 158, 11, 0.2);
  border-radius: 10px;
}

.port-hint p {
  margin: 4px 0;
  color: #1e1b4b;
  font-size: 0.82rem;
  line-height: 1.6;
}

.port-hint strong {
  color: #f59e0b;
}

/* 路径引导 */
.file-path-guide {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 16px 20px;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.08), rgba(139, 92, 246, 0.04));
  border: 1px solid rgba(99, 102, 241, 0.2);
  border-radius: 14px;
  margin-bottom: 16px;
}

.guide-step {
  display: flex;
  align-items: center;
  gap: 12px;
  color: #1e1b4b;
  font-size: 0.95rem;
  line-height: 1.5;
}

.guide-step strong {
  color: #6366f1;
  font-weight: 700;
}

.guide-num {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  background: rgba(99, 102, 241, 0.15);
  border-radius: 50%;
  color: #6366f1;
  font-size: 0.85rem;
  font-weight: 700;
  flex-shrink: 0;
}

.remote-actions {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 16px;
}

.remote-action-card {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 20px;
  background: rgba(255, 255, 255, 0.7);
  border: 2px solid rgba(0, 0, 0, 0.06);
  border-radius: 14px;
  cursor: pointer;
  transition: all 0.25s ease;
}

.remote-action-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.08);
  transform: translateY(-2px);
}

.action-icon {
  font-size: 1.8rem;
}

.action-title {
  color: #0f0a3a;
  font-size: 1rem;
  font-weight: 700;
}

.action-desc {
  color: #6b7280;
  font-size: 0.85rem;
  line-height: 1.5;
}

/* 流光文字 */
.highlight-text {
  font-weight: 800;
  color: #6366f1;
}
</style>

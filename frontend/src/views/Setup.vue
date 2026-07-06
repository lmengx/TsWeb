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

// 从 URL 获取 setup token
const setupToken = route.query.token || ''

// 状态轮询
const statusText = ref('')
const statusOk = ref(false)
const polling = ref(false)
let pollTimer = null

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

    step.value = 'form'
  } catch {
    step.value = 'form'
  }
})

onUnmounted(() => {
  stopPolling()
})

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
        setTimeout(() => router.push('/'), 1000)
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

    // 保存成功，切换到等待页
    step.value = 'waiting'
    statusText.value = '配置已保存，正在连接 TShock...'
    startPolling()
  } catch (err) {
    error.value = '请求失败: ' + err.message
  }

  loading.value = false
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

      <!-- 配置表单 -->
      <div v-else-if="step === 'form'" class="setup-body">
        <h2>连接配置</h2>
        <p class="desc">填写 TShock 服务器的连接信息</p>

        <div class="form-group">
          <label class="form-label">服务器地址</label>
          <input
            v-model="host"
            type="text"
            placeholder="例如 127.0.0.1"
            class="form-input"
            @keyup.enter="submitConfig"
          />
        </div>

        <div class="form-group">
          <label class="form-label">REST API 端口</label>
          <input
            v-model="port"
            type="text"
            placeholder="默认 7878"
            class="form-input"
            @keyup.enter="submitConfig"
          />
        </div>

        <div class="form-group">
          <label class="form-label">API 密钥</label>
          <div class="input-with-btn">
            <input
              v-model="apiKey"
              :type="showApiKey ? 'text' : 'password'"
              placeholder="输入 TShock REST API 密钥"
              class="form-input"
              @keyup.enter="submitConfig"
            />
            <button class="toggle-btn" @click="showApiKey = !showApiKey" type="button">
              {{ showApiKey ? '隐藏' : '显示' }}
            </button>
          </div>
        </div>

        <div v-if="error" class="error-msg">{{ error }}</div>

        <button class="submit-btn" @click="submitConfig" :disabled="loading">
          {{ loading ? '保存中...' : '保存配置' }}
        </button>
      </div>

      <!-- 等待连接 -->
      <div v-else-if="step === 'waiting'" class="setup-body">
        <div class="status-icon" :class="{ ok: statusOk }">
          {{ statusOk ? '✅' : '🔄' }}
        </div>
        <h2>{{ statusOk ? '连接成功' : '等待连接' }}</h2>
        <p class="desc">{{ statusText }}</p>
        <p v-if="!statusOk" class="hint">后端正在自动尝试连接 TShock 服务器，连接成功后会自动跳转</p>
      </div>

      <!-- 无权限 -->
      <div v-else-if="step === 'no-access'" class="setup-body">
        <div class="status-icon">🔒</div>
        <h2>需要 Setup Token</h2>
        <p class="desc">服务器已配置，修改连接配置需要有效的 Setup Token。</p>
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
  background: linear-gradient(135deg, #1e1b4b, #312e81, #4338ca);
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

.setup-card {
  background: #1f2937;
  border-radius: 16px;
  padding: 48px 40px;
  width: 90%;
  max-width: 460px;
  box-shadow: 0 25px 60px rgba(0, 0, 0, 0.5);
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.setup-logo {
  text-align: center;
  margin-bottom: 32px;
}

.setup-logo h1 {
  color: #818cf8;
  font-size: 2rem;
  margin: 0;
  font-weight: 800;
  letter-spacing: -0.5px;
}

.subtitle {
  color: #9ca3af;
  font-size: 0.9rem;
  margin: 4px 0 0;
}

.setup-body h2 {
  color: #f3f4f6;
  font-size: 1.2rem;
  margin: 0 0 8px;
  font-weight: 600;
}

.desc {
  color: #9ca3af;
  font-size: 0.85rem;
  margin: 0 0 24px;
  line-height: 1.6;
}

.hint {
  color: #6b7280;
  font-size: 0.8rem;
  margin: 0;
  line-height: 1.5;
}

.form-group {
  margin-bottom: 18px;
}

.form-label {
  display: block;
  color: #d1d5db;
  font-size: 0.85rem;
  font-weight: 600;
  margin-bottom: 6px;
}

.form-input {
  width: 100%;
  padding: 12px 16px;
  background: #111827;
  border: 2px solid #374151;
  border-radius: 8px;
  color: #f3f4f6;
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
  color: #6b7280;
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
  background: #374151;
  border: 2px solid #4b5563;
  border-radius: 8px;
  color: #9ca3af;
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 500;
  white-space: nowrap;
  transition: all 0.2s ease;
}

.toggle-btn:hover {
  border-color: #6366f1;
  color: #818cf8;
}

.error-msg {
  padding: 10px 14px;
  background: rgba(239, 68, 68, 0.15);
  color: #fca5a5;
  border-radius: 8px;
  border: 1px solid rgba(239, 68, 68, 0.3);
  font-size: 0.85rem;
  margin-bottom: 16px;
}

.submit-btn {
  width: 100%;
  padding: 14px 24px;
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
  border: none;
  border-radius: 8px;
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

.status-icon {
  text-align: center;
  font-size: 3rem;
  margin-bottom: 16px;
  animation: spin 2s linear infinite;
}

.status-icon.ok {
  animation: none;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>

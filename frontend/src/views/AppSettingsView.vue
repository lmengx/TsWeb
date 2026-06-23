<script setup>
import { ref, onMounted } from 'vue'
import { get, post } from '../utils/api.js'

const loading = ref(true)
const saving = ref(false)
const configured = ref(false)
const error = ref('')
const success = ref('')

const host = ref('')
const port = ref(37878)
const apiKey = ref('')

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const statusRes = await get('/api/config/status')
    const status = await statusRes.json()
    configured.value = status.configured

    const configRes = await get('/api/config/tshock')
    const config = await configRes.json()
    host.value = (config.host || '').replace(/^https?:\/\//, '')
    port.value = config.port || 37878
    apiKey.value = config.apiKey || ''
  } catch (err) {
    error.value = '获取配置失败: ' + err.message
  }
  loading.value = false
}

const saveConfig = async () => {
  saving.value = true
  error.value = ''
  success.value = ''

  try {
    const response = await post('/api/config/tshock', {
      host: host.value.trim(),
      port: port.value,
      apiKey: apiKey.value.trim()
    })

    if (response.ok) {
      success.value = '配置保存成功'
      configured.value = true
      setTimeout(() => {
        success.value = ''
        fetchConfig()
      }, 2000)
    } else {
      const result = await response.json()
      error.value = result.error || '保存失败'
    }
  } catch (err) {
    error.value = '保存失败: ' + err.message
  }

  saving.value = false
}

onMounted(() => {
  fetchConfig()
})
</script>

<template>
  <div class="settings-page">
    <div class="page-header">
      <button @click="$router.back()" class="back-btn">
        ← 返回
      </button>
      <div class="title-section">
        <h2>应用配置</h2>
        <span class="subtitle">管理服务器连接配置</span>
      </div>
    </div>

    <div class="settings-content">
      <div class="settings-container">
        <div v-if="loading" class="loading-state">
          <p>加载中...</p>
        </div>

        <div v-else class="settings-form">
          <div class="form-section">
            <h3>TShock REST API</h3>
            <div class="form-group">
              <label>服务器地址</label>
              <div class="input-with-prefix">
                <span class="input-prefix">http://</span>
                <input
                  v-model="host"
                  type="text"
                  placeholder="43.248.3.138"
                  class="form-input prefixed"
                />
              </div>
            </div>
            <div class="form-group">
              <label>端口</label>
              <input
                v-model="port"
                type="number"
                placeholder="37878"
                class="form-input"
              />
            </div>
            <div class="form-group">
              <label>API Key</label>
              <input
                v-model="apiKey"
                type="text"
                placeholder="输入 API Key"
                class="form-input"
              />
            </div>
          </div>

          <button
            @click="saveConfig"
            :disabled="saving || !host || !port || !apiKey"
            class="save-btn"
          >
            {{ saving ? '保存中...' : '保存配置' }}
          </button>

          <div v-if="error" class="error-message">{{ error }}</div>
          <div v-if="success" class="success-message">{{ success }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-page {
  padding: 20px;
  width: 100%;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 24px;
}

.back-btn {
  padding: 8px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  cursor: pointer;
  transition: all 0.25s ease;
}

.back-btn:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.title-section h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
}

.subtitle {
  display: block;
  margin-top: 4px;
  color: var(--text-muted);
  font-size: 0.9rem;
}

.settings-content {
  display: flex;
  justify-content: center;
  padding: 20px;
}

.settings-container {
  width: 100%;
  max-width: 600px;
}

.loading-state {
  text-align: center;
  padding: 60px;
  color: var(--text-muted);
}

.settings-form {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.form-section {
  margin-bottom: 24px;
}

.form-section h3 {
  margin: 0 0 16px 0;
  color: var(--text-primary);
  font-size: 1.1rem;
  font-weight: 600;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--border-light);
}

.form-group {
  margin-bottom: 16px;
}

.form-group label {
  display: block;
  margin-bottom: 8px;
  color: var(--text-secondary);
  font-weight: 500;
}

.input-with-prefix {
  display: flex;
  align-items: center;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  overflow: hidden;
  transition: all 0.25s ease;
}

.input-with-prefix:focus-within {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.input-prefix {
  padding: 12px 12px;
  background: var(--bg-secondary);
  color: var(--text-muted);
  font-size: 0.95rem;
  border-right: 2px solid var(--border-color);
  white-space: nowrap;
}

.form-input.prefixed {
  flex: 1;
  border: none;
  border-radius: 0;
  background: transparent;
}

.form-input.prefixed:focus {
  box-shadow: none;
}

.form-input {
  width: 100%;
  padding: 12px 14px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  transition: all 0.25s ease;
  box-sizing: border-box;
}

.form-input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.save-btn {
  width: 100%;
  padding: 14px 24px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
  margin-top: 8px;
}

.save-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: var(--shadow-md);
}

.save-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.error-message {
  margin-top: 16px;
  padding: 12px;
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border-radius: var(--radius-md);
  text-align: center;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.success-message {
  margin-top: 16px;
  padding: 12px;
  background: rgba(34, 197, 94, 0.15);
  color: var(--accent-secondary);
  border-radius: var(--radius-md);
  text-align: center;
  border: 1px solid rgba(34, 197, 94, 0.3);
}
</style>
<script setup>
import { ref, reactive, onMounted, computed } from 'vue'
import { getCurrentServer } from '../../utils/serverStore.js'
import { getCrossTransferConfig, saveCrossTransferConfig, probeCrossTransfer } from '../../utils/crossTransferApi.js'
import Loading from '../../components/Loading.vue'

const loading = ref(true)
const saving = ref(false)
const probing = ref(false)
const error = ref('')
const success = ref('')

const currentServer = computed(() => getCurrentServer() || { name: '', id: '' })

// 表单（英文键，直接映射插件 CrossTransfer.json）
const form = reactive({
  enabled: false,
  selfServerId: '',
  selfSecret: '',
  targets: []
})

// 探测结果
const probeResults = ref([])

const newTarget = () => ({
  name: '',
  enabled: true,
  host: '',
  port: 7777,
  version: 319,
  secret: '',
  password: ''
})

/** 生成 32 位随机 hex 密钥 */
const generateSecret = () => {
  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)
  form.selfSecret = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('')
}

const addTarget = () => {
  form.targets.push(newTarget())
}

const removeTarget = (index) => {
  form.targets.splice(index, 1)
}

/** 从插件端配置（中文键）回填表单 */
const fillFromPlugin = (config) => {
  form.enabled = config['启用'] !== false
  form.selfServerId = config['本服ID'] || ''
  form.selfSecret = config['本服密钥'] || ''
  form.targets = (config['目标服务器列表'] || []).map(t => ({
    name: t['名称'] || '',
    enabled: t['启用'] !== false,
    host: t['地址'] || '',
    port: t['端口'] || 7777,
    version: t['协议版本'] || 319,
    secret: t['共享密钥'] || '',
    password: t['进服密码(可选)'] || ''
  }))
}

/** 构造提交负载（排除展示辅助字段） */
const buildPayload = () => ({
  enabled: form.enabled,
  selfServerId: form.selfServerId,
  selfSecret: form.selfSecret,
  targets: form.targets.map(t => ({
    name: t.name,
    enabled: t.enabled,
    host: t.host,
    port: t.port,
    version: t.version,
    secret: t.secret,
    password: t.password
  }))
})

/** 加载：自动获取插件端现有配置 */
const loadConfig = async () => {
  const data = await getCrossTransferConfig()
  if (data.config) {
    fillFromPlugin(data.config)
  }
}

/** 保存：直接写入插件端 CrossTransfer.json 并热应用 */
const doSave = async () => {
  error.value = ''
  if (!form.selfServerId.trim()) {
    error.value = '请填写本服ID（玩家用 /跨服 <本服ID> 传送）'
    return
  }
  for (const t of form.targets) {
    if (!t.enabled) continue
    if (!t.name.trim()) {
      error.value = '存在未填名称的目标服务器（名称需与对端本服ID一致）'
      return
    }
    if (!t.host.trim()) {
      error.value = `目标服务器 ${t.name} 未填写地址`
      return
    }
  }

  saving.value = true
  try {
    const data = await saveCrossTransferConfig(buildPayload())
    if (data.success) {
      success.value = '已保存到插件端（热应用）'
      setTimeout(() => { success.value = '' }, 2000)
    } else {
      error.value = data.error || '保存失败'
    }
  } catch (err) {
    error.value = '保存失败: ' + err.message
  }
  saving.value = false
}

/** 探测连通性：对启用目标实际 TCP 探测 */
const doProbe = async () => {
  probing.value = true
  error.value = ''
  probeResults.value = []
  try {
    const targets = form.targets.filter(t => t.enabled && t.host.trim()).map(t => ({
      name: t.name,
      host: t.host,
      port: t.port
    }))
    const data = await probeCrossTransfer(targets)
    if (data.success) {
      probeResults.value = data.results || []
    } else {
      error.value = data.error || '探测失败'
    }
  } catch (err) {
    error.value = '探测失败: ' + err.message
  }
  probing.value = false
}

onMounted(async () => {
  try {
    await loadConfig()
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  loading.value = false
})
</script>

<template>
  <div class="cross-transfer-page">
    <div class="page-header">
      <h2>跨服传送</h2>
      <p class="page-desc">
        当前服务器：<strong>{{ currentServer.name }}</strong> —— 配置直接读写该服务器插件端的 CrossTransfer.json
      </p>
    </div>

    <Loading v-if="loading" text="加载中..." />

    <div v-else class="ct-content">
      <!-- 本服设置 -->
      <div class="section-card">
        <div class="section-header">
          <h3>本服设置</h3>
          <label class="switch" title="全局启用/停用跨服传送">
            <input type="checkbox" v-model="form.enabled" />
            <span class="slider"></span>
          </label>
        </div>
        <p class="section-desc">玩家通过 <code>/跨服 <本服ID></code> 传送离开本服；本服ID 需与对端配置里的目标名称一致。</p>

        <div class="form-row">
          <div class="form-group">
            <label class="form-label">本服ID</label>
            <input v-model="form.selfServerId" class="form-input" placeholder="server-a" />
          </div>
          <div class="form-group">
            <label class="form-label">本服密钥</label>
            <div class="secret-input">
              <input v-model="form.selfSecret" type="password" class="form-input" placeholder="对端填写的共享密钥" />
              <button class="btn-icon" title="生成随机密钥" @click="generateSecret">🎲</button>
            </div>
          </div>
        </div>
      </div>

      <!-- 目标服务器列表 -->
      <div class="section-card">
        <div class="section-header">
          <h3>目标服务器</h3>
          <span class="badge">{{ form.targets.length }} 个</span>
        </div>
        <p class="section-desc">共享密钥需填<strong>对端服务器的本服密钥</strong>。</p>

        <div v-if="form.targets.length === 0" class="empty-tip">尚未配置任何目标服务器，点击下方添加</div>

        <div v-for="(t, index) in form.targets" :key="index" class="target-item">
          <div class="target-header">
            <label class="switch switch-sm" title="启用/停用该目标">
              <input type="checkbox" v-model="t.enabled" />
              <span class="slider"></span>
            </label>
            <span class="target-index">#{{ index + 1 }}</span>
            <button class="btn-remove" @click="removeTarget(index)" title="删除此目标">✕</button>
          </div>

          <div class="target-grid">
            <div class="form-group">
              <label class="form-label">名称（对端本服ID）</label>
              <input v-model="t.name" class="form-input" placeholder="server-b" />
            </div>
            <div class="form-group">
              <label class="form-label">地址</label>
              <input v-model="t.host" class="form-input" placeholder="1.2.3.4" />
            </div>
            <div class="form-group">
              <label class="form-label">端口</label>
              <input v-model.number="t.port" type="number" class="form-input" min="1" max="65535" />
            </div>
            <div class="form-group">
              <label class="form-label">协议版本</label>
              <input v-model.number="t.version" type="number" class="form-input" min="0" placeholder="319" />
            </div>
            <div class="form-group">
              <label class="form-label">共享密钥（对端本服密钥）</label>
              <input v-model="t.secret" type="password" class="form-input" placeholder="对端本服密钥" />
            </div>
            <div class="form-group">
              <label class="form-label">进服密码（可选）</label>
              <input v-model="t.password" class="form-input" placeholder="目标服 ServerPassword" />
            </div>
          </div>
        </div>

        <button class="btn-add" @click="addTarget">＋ 添加目标服务器</button>
      </div>

      <!-- 探测结果 -->
      <div class="section-card">
        <div class="section-header">
          <h3>连通性探测</h3>
          <button class="btn-primary btn-sm" :disabled="probing" @click="doProbe">
            {{ probing ? '探测中...' : (probeResults.length ? '重新探测' : '探测连通性') }}
          </button>
        </div>
        <p class="section-desc">由插件端实际 TCP 探测每个启用目标（1.5s 超时）。保存前先确认地址与端口是否正确。</p>

        <div v-if="probing" class="probing-tip">正在探测...</div>
        <div v-else-if="probeResults.length" class="probe-list">
          <div v-for="(r, i) in probeResults" :key="i" class="probe-item">
            <span class="probe-name">{{ r.name }}</span>
            <span class="probe-addr">{{ r.host }}:{{ r.port }}</span>
            <span :class="['probe-badge', r.ok ? 'ok' : 'bad']">{{ r.ok ? '✓ 可达' : '✗ 不可达' }}</span>
          </div>
        </div>
        <div v-else-if="!probing" class="empty-tip">点击"探测连通性"，确认各目标服的地址端口是否可达</div>
      </div>

      <div class="actions">
        <button class="btn-primary" :disabled="saving" @click="doSave">
          {{ saving ? '保存中...' : '保存并应用' }}
        </button>
      </div>
    </div>

    <!-- Toast -->
    <Transition name="toast">
      <div v-if="success" class="toast toast-success"><span>{{ success }}</span></div>
    </Transition>
    <Transition name="toast">
      <div v-if="error" class="toast toast-error"><span>{{ error }}</span></div>
    </Transition>
  </div>
</template>

<style scoped>
.cross-transfer-page {
  padding: 20px;
  width: 100%;
}

.page-header {
  margin-bottom: 20px;
}

.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
}

.page-desc {
  margin: 4px 0 0 0;
  color: var(--text-muted);
  font-size: 0.88rem;
}

.page-desc strong {
  color: var(--text-primary);
}

.ct-content {
  max-width: 900px;
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

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
}

.section-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.1rem;
  font-weight: 600;
}

.section-desc {
  margin: 4px 0 20px 0;
  color: var(--text-muted);
  font-size: 0.85rem;
}

.section-desc strong {
  color: var(--text-primary);
}

.section-desc code {
  background: var(--bg-tertiary);
  padding: 1px 6px;
  border-radius: 4px;
  color: var(--accent-primary);
}

.badge {
  font-size: 0.78rem;
  padding: 2px 10px;
  border-radius: 12px;
  background: var(--accent-primary);
  color: white;
  font-weight: 500;
}

.empty-tip {
  padding: 20px;
  text-align: center;
  color: var(--text-muted);
  font-size: 0.88rem;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  margin-bottom: 16px;
}

.form-row {
  display: flex;
  gap: 16px;
}

.form-group {
  flex: 1;
  margin-bottom: 12px;
  min-width: 0;
}

.form-label {
  display: block;
  margin-bottom: 6px;
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-weight: 500;
}

.form-select,
.form-input {
  width: 100%;
  padding: 10px 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  outline: none;
  transition: border-color 0.2s;
  box-sizing: border-box;
}

.form-select:focus,
.form-input:focus {
  border-color: var(--accent-primary);
}

.secret-input {
  display: flex;
  gap: 6px;
}

.secret-input .form-input {
  flex: 1;
  min-width: 0;
}

.btn-icon {
  width: 38px;
  height: 40px;
  flex-shrink: 0;
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  background: var(--bg-tertiary);
  color: var(--text-muted);
  cursor: pointer;
  font-size: 0.95rem;
  transition: all 0.2s;
}

.btn-icon:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.target-item {
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  padding: 16px;
  border: 1px solid var(--border-color);
  margin-bottom: 12px;
}

.target-header {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
}

.target-index {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--accent-primary);
}

.target-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0 14px;
}

.btn-remove {
  width: 24px;
  height: 24px;
  margin-left: auto;
  border: none;
  border-radius: 50%;
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
  transition: all 0.2s;
}

.btn-remove:hover {
  background: #ef4444;
  color: white;
}

.btn-add {
  width: 100%;
  padding: 10px;
  border: 2px dashed var(--border-color);
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--text-muted);
  font-size: 0.9rem;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-add:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.btn-primary {
  padding: 10px 20px;
  border: none;
  border-radius: var(--radius-md);
  background: var(--accent-primary);
  color: white;
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: opacity 0.2s;
}

.btn-primary:hover:not(:disabled) {
  opacity: 0.85;
}

.btn-primary:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.btn-sm {
  padding: 6px 14px;
  font-size: 0.82rem;
}

.actions {
  display: flex;
  justify-content: flex-end;
}

.switch {
  position: relative;
  display: inline-block;
  width: 50px;
  height: 28px;
  flex-shrink: 0;
}

.switch-sm {
  width: 40px;
  height: 22px;
}

.switch input {
  opacity: 0;
  width: 0;
  height: 0;
}

.slider {
  position: absolute;
  cursor: pointer;
  top: 0; left: 0; right: 0; bottom: 0;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 28px;
  transition: all 0.3s ease;
}

.slider::before {
  content: '';
  position: absolute;
  height: 18px; width: 18px;
  left: 3px; bottom: 3px;
  background: var(--text-muted);
  border-radius: 50%;
  transition: all 0.3s ease;
}

.switch-sm .slider::before {
  height: 12px; width: 12px;
  left: 3px; bottom: 3px;
}

.switch input:checked + .slider {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
}

.switch input:checked + .slider::before {
  transform: translateX(22px);
  background: white;
}

.switch-sm input:checked + .slider::before {
  transform: translateX(18px);
}

.probing-tip {
  padding: 20px;
  text-align: center;
  color: var(--text-muted);
}

.probe-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.probe-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
}

.probe-name {
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--text-primary);
  min-width: 120px;
}

.probe-addr {
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-family: monospace;
}

.probe-badge {
  font-size: 0.82rem;
  padding: 3px 10px;
  border-radius: 10px;
  font-weight: 500;
}

.probe-badge.ok {
  background: rgba(74, 222, 128, 0.15);
  color: #22c55e;
}

.probe-badge.bad {
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
}

.toast {
  position: fixed;
  top: 20px;
  right: 24px;
  z-index: 9999;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 18px;
  border-radius: var(--radius-md, 8px);
  font-size: 0.88rem;
  font-weight: 500;
  box-shadow: 0 4px 16px rgba(0,0,0,0.15);
  pointer-events: none;
  max-width: 360px;
}

.toast-success {
  color: #065f46;
  background: #d1fae5;
  border: 1px solid #6ee7b7;
}

.toast-error {
  color: #991b1b;
  background: #fee2e2;
  border: 1px solid #fca5a5;
}

.toast-enter-active {
  transition: all 0.3s ease-out;
}
.toast-leave-active {
  transition: all 0.25s ease-in;
}
.toast-enter-from {
  opacity: 0;
  transform: translateX(40px);
}
.toast-leave-to {
  opacity: 0;
  transform: translateX(40px);
}

@media (max-width: 767px) {
  .form-row {
    flex-direction: column;
    gap: 0;
  }
  .target-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}
</style>

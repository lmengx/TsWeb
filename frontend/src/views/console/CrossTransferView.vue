<script setup>
import { ref, reactive, onMounted, computed } from 'vue'
import { getCurrentServer, getServers, fetchServers } from '../../utils/serverStore.js'
import {
  getCrossTransferConfig,
  saveCrossTransferConfig,
  revealSelfSecret,
  probeCrossTransfer,
  applyCrossTransfer
} from '../../utils/crossTransferApi.js'

const loading = ref(true)
const saving = ref(false)
const applying = ref(false)
const probing = ref(false)
const error = ref('')
const success = ref('')
const step = ref(1)

// 当前服务器信息
const currentServer = computed(() => getCurrentServer() || { name: '', id: '' })
const serverList = ref([])

// 表单（后端 crossTransfer 结构，英文键）
const form = reactive({
  enabled: false,
  selfServerId: '',
  selfSecret: '',
  targets: []
})

// 密钥显示切换
const showSelfSecret = ref(false)
const showTargetSecret = reactive({})

// 插件端现有配置（自动获取导入源）
const pluginCurrent = ref(null)
const importHint = computed(() =>
  pluginCurrent.value
  && Array.isArray(pluginCurrent.value['目标服务器列表'])
  && pluginCurrent.value['目标服务器列表'].length > 0
)

// 注册表选择
const selectedRegistryServer = ref('')

// 探测结果
const probeResults = ref([])

const newTarget = () => ({
  serverId: '',
  name: '',
  enabled: true,
  host: '',
  port: 7777,
  dedicatedHost: '',
  dedicatedPort: 0,
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

/** 显示本服密钥：后端已有值但表单未载入时先 reveal */
const toggleSelfSecretVisible = async () => {
  showSelfSecret.value = !showSelfSecret.value
  if (showSelfSecret.value && !form.selfSecret) {
    try {
      const data = await revealSelfSecret()
      if (data.selfSecret) form.selfSecret = data.selfSecret
    } catch (err) {
      error.value = '获取密钥失败: ' + err.message
    }
  }
}

const toggleTargetSecretVisible = (index) => {
  showTargetSecret[index] = !showTargetSecret[index]
}

/** 从插件端现有 CrossTransfer.json 导入（中文键 → 表单） */
const importPlugin = () => {
  const p = pluginCurrent.value
  if (!p) return
  form.enabled = p['启用'] !== false
  form.selfServerId = p['本服ID'] || ''
  form.selfSecret = p['本服密钥'] || ''
  form.selfSecretHasValue = !!form.selfSecret
  form.targets = (p['目标服务器列表'] || []).map(t => {
    const target = {
      serverId: '',
      name: t['名称'] || '',
      enabled: t['启用'] !== false,
      host: t['地址'] || '',
      port: t['端口'] || 7777,
      dedicatedHost: t['专线地址'] || '',
      dedicatedPort: t['专线端口'] || 0,
      version: t['协议版本'] || 319,
      secret: t['共享密钥'] || '',
      password: t['进服密码(可选)'] || ''
    }
    target.secretHasValue = !!target.secret
    return target
  })
  success.value = '已从插件端导入现有配置'
  setTimeout(() => { success.value = '' }, 2000)
}

/** 构造提交负载（排除展示用辅助键 selfSecretHasValue / secretHasValue） */
const buildPayload = () => {
  const targets = form.targets.map(t => ({
    serverId: t.serverId,
    name: t.name,
    enabled: t.enabled,
    host: t.host,
    port: t.port,
    dedicatedHost: t.dedicatedHost,
    dedicatedPort: t.dedicatedPort,
    version: t.version,
    secret: t.secret,
    password: t.password
  }))
  return {
    enabled: form.enabled,
    selfServerId: form.selfServerId,
    selfSecret: form.selfSecret,
    targets
  }
}

/** 从服务器注册表添加目标服（自动带出 host/port，密钥由后端 apply 自动配对） */
const addFromRegistry = () => {
  const id = selectedRegistryServer.value
  if (!id) return
  const s = serverList.value.find(x => x.id === id)
  if (!s) return
  form.targets.push({
    serverId: s.id,
    name: '',
    enabled: true,
    host: s.host || '',
    port: 7777,
    dedicatedHost: '',
    dedicatedPort: 0,
    version: 319,
    secret: '',
    password: ''
  })
  selectedRegistryServer.value = ''
}

const addManual = () => {
  form.targets.push(newTarget())
}

const removeTarget = (index) => {
  form.targets.splice(index, 1)
}

/** 保存草稿（仅后端，不下发） */
const saveDraft = async () => {
  saving.value = true
  error.value = ''
  try {
    const data = await saveCrossTransferConfig(buildPayload())
    if (data.success) {
      success.value = '草稿已保存'
      setTimeout(() => { success.value = '' }, 1500)
    } else {
      error.value = data.error || '保存失败'
    }
  } catch (err) {
    error.value = '保存失败: ' + err.message
  }
  saving.value = false
}

/** 进入确认页前的校验 */
const goConfirm = () => {
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
  probeResults.value = []
  step.value = 2
}

/** 连通性探测（插件端实际 TCP 探测，专线优先） */
const doProbe = async () => {
  probing.value = true
  error.value = ''
  probeResults.value = []
  try {
    const targets = form.targets.filter(t => t.enabled).map(t => ({
      name: t.name,
      host: t.host,
      port: t.port,
      dedicatedHost: t.dedicatedHost,
      dedicatedPort: t.dedicatedPort
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

/** 确认并下发启用 */
const doApply = async () => {
  applying.value = true
  error.value = ''
  try {
    const data = await applyCrossTransfer(buildPayload())
    if (data.success) {
      success.value = '已下发到插件端'
      // 展示下发后的插件配置概要
      const pc = data.pluginConfig || {}
      probeResults.value = []
      step.value = 1
      // 刷新回显（后端已写入）
      await loadConfig()
      setTimeout(() => { success.value = '' }, 2500)
    } else {
      error.value = data.error || '下发失败'
    }
  } catch (err) {
    error.value = '下发失败: ' + err.message
  }
  applying.value = false
}

const loadConfig = async () => {
  const data = await getCrossTransferConfig()
  pluginCurrent.value = data.pluginCurrent || null
  const c = data.configured
  if (c) {
    form.enabled = !!c.enabled
    form.selfServerId = c.selfServerId || ''
    // 脱敏回显：后端已有密钥且表单当前为空 → 留空提示“已设置”；表单已有值（刚填/导入）→ 保留
    if (c.hasSelfSecret && !form.selfSecret) form.selfSecret = ''
    form.selfSecretHasValue = !!c.hasSelfSecret || !!form.selfSecret
    form.targets = (c.targets || []).map((t, i) => {
      const old = form.targets[i]
      const secret = (old && old.secret) ? old.secret : ''
      return {
        serverId: t.serverId || '',
        name: t.name || '',
        enabled: t.enabled !== false,
        host: t.host || '',
        port: t.port || 7777,
        dedicatedHost: t.dedicatedHost || '',
        dedicatedPort: t.dedicatedPort || 0,
        version: t.version || 319,
        secret,
        secretHasValue: !!t.hasSecret || !!secret,
        password: t.password || ''
      }
    })
  }
}

onMounted(async () => {
  try {
    await fetchServers()
    serverList.value = getServers().filter(s => s.id !== currentServer.value.id)
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
        当前服务器：<strong>{{ currentServer.name }}</strong> —— 配置保存在后端，确认后下发到插件端 CrossTransfer.json
      </p>
    </div>

    <div v-if="loading" class="loading-state">
      <p>加载中...</p>
    </div>

    <div v-else class="ct-content">
      <!-- 步骤指示器 -->
      <div class="steps">
        <div :class="['step', { active: step === 1 }]">
          <span class="step-num">1</span> 编辑配置
        </div>
        <div :class="['step', { active: step === 2 }]">
          <span class="step-num">2</span> 确认并启用
        </div>
      </div>

      <!-- ════════ Step 1：编辑 ════════ -->
      <template v-if="step === 1">
        <!-- 自动获取提示 -->
        <div v-if="importHint" class="import-bar">
          <span>检测到插件端已有跨服传送配置（{{ pluginCurrent['目标服务器列表'].length }} 个目标服），可一键导入后编辑。</span>
          <button class="btn-primary btn-sm" @click="importPlugin">从插件端导入</button>
        </div>

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
                <input
                  :type="showSelfSecret ? 'text' : 'password'"
                  v-model="form.selfSecret"
                  class="form-input"
                  :placeholder="form.selfSecretHasValue ? '已设置（编辑以覆盖）' : '留空则无法建立跨服通道'"
                />
                <button class="btn-icon" :title="showSelfSecret ? '隐藏' : '显示'" @click="toggleSelfSecretVisible">👁</button>
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
          <p class="section-desc">
            专线地址可选：本服连接该目标服时<strong>优先尝试专线</strong>，失败自动回退公网地址。共享密钥留空时，下发将自动使用对端服务器的本服密钥。
          </p>

          <div v-if="form.targets.length === 0" class="empty-tip">尚未配置任何目标服务器</div>

          <div v-for="(t, index) in form.targets" :key="index" class="target-item">
            <div class="target-header">
              <label class="switch switch-sm" title="启用/停用该目标">
                <input type="checkbox" v-model="t.enabled" />
                <span class="slider"></span>
              </label>
              <span class="target-index">#{{ index + 1 }}</span>
              <span v-if="t.serverId" class="target-bind">已关联注册表服务器</span>
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
                <label class="form-label">专线地址（可选）</label>
                <input v-model="t.dedicatedHost" class="form-input" placeholder="10.0.0.2 或留空" />
              </div>
              <div class="form-group">
                <label class="form-label">专线端口</label>
                <input v-model.number="t.dedicatedPort" type="number" class="form-input" min="0" max="65535" placeholder="0=未配置" />
              </div>
              <div class="form-group">
                <label class="form-label">协议版本</label>
                <input v-model.number="t.version" type="number" class="form-input" min="0" placeholder="319" />
              </div>
              <div class="form-group">
                <label class="form-label">共享密钥</label>
                <div class="secret-input">
                  <input
                    :type="showTargetSecret[index] ? 'text' : 'password'"
                    v-model="t.secret"
                    class="form-input"
                    :placeholder="t.secretHasValue ? '已设置（编辑以覆盖）' : (t.serverId ? '自动使用对端密钥' : '对端本服密钥')"
                  />
                  <button class="btn-icon" :title="showTargetSecret[index] ? '隐藏' : '显示'" @click="toggleTargetSecretVisible(index)">👁</button>
                </div>
              </div>
              <div class="form-group">
                <label class="form-label">进服密码（可选）</label>
                <input v-model="t.password" class="form-input" placeholder="目标服 ServerPassword" />
              </div>
            </div>
          </div>

          <div class="add-row">
            <select v-model="selectedRegistryServer" class="form-select add-select">
              <option value="" disabled>从服务器注册表添加...</option>
              <option v-for="s in serverList" :key="s.id" :value="s.id">{{ s.name }}</option>
            </select>
            <button class="btn-primary btn-sm" :disabled="!selectedRegistryServer" @click="addFromRegistry">添加</button>
            <button class="btn-secondary btn-sm" @click="addManual">手动添加</button>
          </div>
        </div>

        <div class="actions">
          <button class="btn-secondary" :disabled="saving" @click="saveDraft">
            {{ saving ? '保存中...' : '保存草稿' }}
          </button>
          <button class="btn-primary" @click="goConfirm">下一步：确认并启用</button>
        </div>
      </template>

      <!-- ════════ Step 2：确认并启用 ════════ -->
      <template v-else>
        <div class="section-card">
          <div class="section-header">
            <h3>配置摘要</h3>
            <span :class="['state-badge', form.enabled ? 'ok' : 'off']">{{ form.enabled ? '启用' : '停用' }}</span>
          </div>
          <div class="summary-grid">
            <div class="summary-item">
              <span class="summary-label">本服ID</span>
              <span class="summary-value">{{ form.selfServerId || '（未填）' }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">本服密钥</span>
              <span class="summary-value">{{ form.selfSecret ? '••••••••（已设置）' : (form.selfSecretHasValue ? '••••••••（已设置）' : '（未设置）') }}</span>
            </div>
            <div v-for="(t, i) in form.targets.filter(x => x.enabled)" :key="i" class="summary-item target-summary">
              <span class="summary-label">{{ t.name }} <span v-if="!t.enabled" class="muted">（停用）</span></span>
              <span class="summary-value">
                公网 {{ t.host }}:{{ t.port }}
                <span v-if="t.dedicatedHost" class="dedicated"> / 专线 {{ t.dedicatedHost }}:{{ t.dedicatedPort || 0 }}</span>
              </span>
            </div>
          </div>
        </div>

        <div class="section-card">
          <div class="section-header">
            <h3>连通性探测</h3>
            <button class="btn-primary btn-sm" :disabled="probing" @click="doProbe">
              {{ probing ? '探测中...' : (probeResults.length ? '重新探测' : '探测连通性') }}
            </button>
          </div>
          <p class="section-desc">由插件端实际 TCP 探测每个目标服（专线优先，每端点 1.5s 超时）</p>

          <div v-if="probeResults.length === 0 && !probing" class="empty-tip">
            点击"探测连通性"，确认各目标服的公网/专线地址是否可达
          </div>

          <div v-if="probing" class="probing-tip">正在探测...</div>

          <div v-else-if="probeResults.length" class="probe-list">
            <div v-for="(r, i) in probeResults" :key="i" class="probe-item">
              <span class="probe-name">{{ r.name }}</span>
              <span :class="['probe-badge', r.primaryOk ? 'ok' : 'bad']">
                {{ r.primaryOk ? '✓' : '✗' }} 公网 {{ r.host }}:{{ r.port }}
              </span>
              <span v-if="r.dedicatedHost" :class="['probe-badge', r.dedicatedOk ? 'ok' : 'bad']">
                {{ r.dedicatedOk ? '✓' : '✗' }} 专线 {{ r.dedicatedHost }}:{{ r.dedicatedPort }}
              </span>
              <span v-else class="probe-badge muted">未配置专线</span>
            </div>
          </div>
        </div>

        <div class="actions">
          <button class="btn-secondary" @click="step = 1">上一步</button>
          <button class="btn-primary" :disabled="applying" @click="doApply">
            {{ applying ? '下发中...' : '确认并下发启用' }}
          </button>
        </div>
      </template>
    </div>

    <!-- Toast -->
    <Transition name="toast">
      <div v-if="success" class="toast toast-success">
        <span>{{ success }}</span>
      </div>
    </Transition>
    <Transition name="toast">
      <div v-if="error" class="toast toast-error">
        <span>{{ error }}</span>
      </div>
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

.loading-state {
  text-align: center;
  padding: 60px;
  color: var(--text-muted);
}

/* 步骤指示器 */
.steps {
  display: flex;
  gap: 12px;
}

.step {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  border-radius: 20px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  color: var(--text-muted);
  font-size: 0.88rem;
  font-weight: 500;
  transition: all 0.2s;
}

.step.active {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
  color: white;
}

.step-num {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: rgba(127, 127, 127, 0.2);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
}

.step.active .step-num {
  background: rgba(255, 255, 255, 0.3);
}

/* 导入提示条 */
.import-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  background: rgba(74, 222, 128, 0.1);
  border: 1px solid rgba(74, 222, 128, 0.4);
  border-radius: var(--radius-lg);
  color: var(--text-primary);
  font-size: 0.88rem;
}

/* 卡片 */
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

/* 表单 */
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

.form-input[type='number'] {
  min-width: 0;
}

/* 密钥输入（含小按钮） */
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

/* 目标服务器条目 */
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

.target-bind {
  font-size: 0.75rem;
  padding: 2px 8px;
  border-radius: 10px;
  background: rgba(96, 165, 250, 0.15);
  color: #60a5fa;
}

.target-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
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

/* 添加行 */
.add-row {
  display: flex;
  gap: 10px;
  align-items: center;
}

.add-select {
  flex: 1;
  max-width: 320px;
}

/* 按钮 */
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

.btn-secondary {
  padding: 10px 20px;
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--text-secondary);
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-secondary:hover:not(:disabled) {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.btn-secondary:disabled {
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
  gap: 12px;
}

/* Switch */
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

/* 摘要 */
.state-badge {
  font-size: 0.78rem;
  padding: 2px 12px;
  border-radius: 12px;
  font-weight: 500;
}

.state-badge.ok {
  background: rgba(74, 222, 128, 0.15);
  color: #22c55e;
}

.state-badge.off {
  background: rgba(148, 163, 184, 0.15);
  color: var(--text-muted);
}

.summary-grid {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.summary-item {
  display: flex;
  align-items: baseline;
  gap: 12px;
  padding: 8px 0;
  border-bottom: 1px dashed var(--border-color);
}

.summary-item:last-child {
  border-bottom: none;
}

.summary-label {
  min-width: 110px;
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-weight: 500;
}

.summary-value {
  color: var(--text-primary);
  font-size: 0.9rem;
  word-break: break-all;
}

.target-summary .summary-label {
  color: var(--accent-primary);
}

.dedicated {
  color: var(--accent-primary);
}

.muted {
  color: var(--text-muted);
  font-size: 0.85rem;
}

/* 探测 */
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
  gap: 10px;
  flex-wrap: wrap;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
}

.probe-name {
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--text-primary);
  min-width: 100px;
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

.probe-badge.muted {
  background: rgba(148, 163, 184, 0.15);
  color: var(--text-muted);
}

/* Toast */
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

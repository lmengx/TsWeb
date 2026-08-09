<script setup>
import { ref, watch, onBeforeUnmount } from 'vue'
import { post, get } from '../utils/api.js'
import { selectServer, fetchServers } from '../utils/serverStore.js'

const props = defineProps({
  show: { type: Boolean, default: false }
})
const emit = defineEmits(['close', 'added'])

// ═══════════════ 向导状态 ═══════════════
// 步骤：1=方式选择 2=信息配置 3=连接验证 4=完成
const step = ref(1)
const addMode = ref(null)          // null | manual | auto
const autoMachineMode = ref(null)  // null | local | remote
const addedServer = ref(null)

const stepsMeta = [
  { n: 1, label: '方式选择' },
  { n: 2, label: '信息配置' },
  { n: 3, label: '连接验证' },
  { n: 4, label: '完成' }
]

// ═══════════════ 手动模式 ═══════════════
const addForm = ref({ name: '', host: '', port: 7878, apiKey: '', note: '' })
const testing = ref(false)
const testResult = ref(null)       // { ok } | { ok:false, error }
const formError = ref('')

const validateManual = () => {
  const { host, port, apiKey } = addForm.value
  if (!host.trim()) return '地址不能为空'
  if (!Number.isInteger(port) || port < 1 || port > 65535) return '端口需为 1-65535 的整数'
  if (!apiKey.trim()) return 'API Key 不能为空'
  return null
}

// 仅测试连接（不落库）
const runTestOnly = async () => {
  formError.value = validateManual()
  if (formError.value) return
  testing.value = true
  testResult.value = null
  try {
    const res = await post('/api/servers/test', {
      host: addForm.value.host.trim(),
      port: addForm.value.port,
      apiKey: addForm.value.apiKey.trim()
    })
    const data = await res.json()
    if (data.success) testResult.value = { ok: true }
    else testResult.value = { ok: false, error: data.error || '连接失败' }
  } catch (e) {
    testResult.value = { ok: false, error: e.message }
  } finally { testing.value = false }
}

// 测试通过后真正添加
const runAdd = async () => {
  if (!testResult.value?.ok) return
  adding.value = true
  try {
    const res = await post('/api/servers', {
      name: addForm.value.name.trim() || addForm.value.host.trim(),
      host: addForm.value.host.trim(),
      port: addForm.value.port,
      apiKey: addForm.value.apiKey.trim(),
      note: addForm.value.note.trim()
    })
    const data = await res.json()
    if (res.ok && data.success) {
      await finishAdd(data.server)
    } else {
      testResult.value = { ok: false, error: data.error || '添加失败' }
    }
  } catch (e) {
    testResult.value = { ok: false, error: e.message }
  } finally { adding.value = false }
}
const adding = ref(false)

// ═══════════════ 自动-本机（一键流程） ═══════════════
const probePort = ref('7777')
const probeResult = ref(null)       // 探测结果
const autoReadResult = ref(null)    // 修改配置结果
const localPhase = ref('idle')      // idle | probing | reading | verifying | done | error
const localError = ref('')
const verifyAttempt = ref(0)
const verifyMax = 20
const cancelVerify = ref(false)
const isVerifying = ref(false)

const sleep = (ms) => new Promise(r => setTimeout(r, ms))

const startLocalOneClick = async () => {
  cancelVerify.value = false
  localError.value = ''
  // 1. 探测
  localPhase.value = 'probing'
  probeResult.value = null
  try {
    const res = await get(`/api/setup/probe?port=${probePort.value.trim()}`)
    probeResult.value = await res.json()
    if (!probeResult.value.found) {
      localPhase.value = 'error'
      localError.value = probeResult.value.error || `未在端口 ${probePort.value} 找到监听进程`
      return
    }
  } catch (e) {
    localPhase.value = 'error'
    localError.value = e.message
    return
  }
  // 2. 修改配置 + 复制插件
  localPhase.value = 'reading'
  try {
    const res = await post('/api/setup/auto-read', {
      processPath: probeResult.value.processes[0].path
    })
    autoReadResult.value = await res.json()
    if (!autoReadResult.value.success) {
      localPhase.value = 'error'
      localError.value = autoReadResult.value.error || '修改配置失败'
      return
    }
  } catch (e) {
    localPhase.value = 'error'
    localError.value = e.message
    return
  }
  // 3. 等待重启并自动验证（轮询，最多 60 秒）
  await startVerifyLoop()
}

// 统一验证循环（防并发：仅一个循环在跑）
const startVerifyLoop = async () => {
  if (isVerifying.value) return
  isVerifying.value = true
  cancelVerify.value = false
  localPhase.value = 'verifying'
  verifyAttempt.value = 0
  while (verifyAttempt.value < verifyMax && !cancelVerify.value) {
    verifyAttempt.value++
    const ok = await attemptLocalVerify()
    if (ok) break
    if (verifyAttempt.value < verifyMax && !cancelVerify.value) await sleep(3000)
  }
  isVerifying.value = false
  if (!cancelVerify.value && localPhase.value === 'verifying') {
    localPhase.value = 'error'
    localError.value = '等待 TShock 重启超时（60 秒）。请确认服务器已重启后点击「重试验证」'
  }
}

const attemptLocalVerify = async () => {
  try {
    const res = await post('/api/setup/auto-verify', {
      host: '127.0.0.1',
      port: autoReadResult.value.restPort,
      apiKey: autoReadResult.value.tokenKey
    })
    const data = await res.json()
    if (res.ok && data.success) {
      await finishAdd({ id: data.serverId, name: '本机服务器' })
      localPhase.value = 'done'
      return true
    }
    return false
  } catch { return false }
}

const retryLocalVerify = () => { startVerifyLoop() }

// ═══════════════ 自动-远程 ═══════════════
const remoteConfigRaw = ref('')
const remoteLoading = ref(false)
const remoteResult = ref(null)
const remoteHost = ref('')
const remotePort = ref('')
const remoteVerifyLoading = ref(false)
const remoteVerifyError = ref('')
const remotePhase = ref('idle')    // idle | review | verifying | error

const submitRemoteConfig = async () => {
  if (!remoteConfigRaw.value.trim()) {
    remoteVerifyError.value = '请粘贴 tshock/config.json 的内容'
    return
  }
  remoteLoading.value = true
  remotePhase.value = 'idle'
  remoteVerifyError.value = ''
  try {
    const res = await post('/api/setup/auto-remote', { configRaw: remoteConfigRaw.value })
    remoteResult.value = await res.json()
    if (remoteResult.value.success) {
      remotePhase.value = 'review'
      remotePort.value = String(remoteResult.value.restPort)
    } else {
      remotePhase.value = 'error'
      remoteVerifyError.value = remoteResult.value.error || '处理失败'
    }
  } catch (e) {
    remotePhase.value = 'error'
    remoteVerifyError.value = e.message
  } finally { remoteLoading.value = false }
}

const copyRemoteConfig = async () => {
  if (remoteResult.value?.modifiedRaw) {
    await navigator.clipboard.writeText(remoteResult.value.modifiedRaw)
    flash('已复制到剪贴板')
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
  flash('已下载配置文件')
}

const verifyRemoteConnection = async () => {
  if (!remoteResult.value || !remoteHost.value.trim()) return
  remoteVerifyLoading.value = true
  remoteVerifyError.value = ''
  try {
    const port = parseInt(remotePort.value) || remoteResult.value.restPort
    const res = await post('/api/setup/auto-verify', {
      host: remoteHost.value.trim(),
      port,
      apiKey: remoteResult.value.tokenKey
    })
    const data = await res.json()
    if (res.ok && data.success) {
      await finishAdd({ id: data.serverId, name: remoteHost.value.trim() })
      remotePhase.value = 'done'
    } else {
      remoteVerifyError.value = data.error || '验证失败'
    }
  } catch (e) {
    remoteVerifyError.value = e.message
  } finally { remoteVerifyLoading.value = false }
}

// ═══════════════ 公共 ═══════════════
const successMsg = ref('')

const flash = (msg) => {
  successMsg.value = msg
  setTimeout(() => { successMsg.value = '' }, 3000)
}

// 添加成功：自动切换当前服务器 + 同步侧边栏 + 通知父组件
const finishAdd = async (server) => {
  selectServer(server.id)
  await fetchServers()
  addedServer.value = server
  step.value = 4
  emit('added', server)
}

const reset = () => {
  step.value = 1
  addMode.value = null
  autoMachineMode.value = null
  addedServer.value = null
  addForm.value = { name: '', host: '', port: 7878, apiKey: '', note: '' }
  testing.value = false
  adding.value = false
  testResult.value = null
  formError.value = ''
  probeResult.value = null
  autoReadResult.value = null
  localPhase.value = 'idle'
  localError.value = ''
  verifyAttempt.value = 0
  remoteConfigRaw.value = ''
  remoteResult.value = null
  remoteHost.value = ''
  remotePort.value = ''
  remoteVerifyError.value = ''
  remotePhase.value = 'idle'
  successMsg.value = ''
}

watch(() => props.show, (v) => {
  if (v) reset()
  else cancelVerify.value = true
})

onBeforeUnmount(() => { cancelVerify.value = true })

const chooseMode = (mode) => {
  addMode.value = mode
  step.value = 2
  if (mode === 'auto') autoMachineMode.value = null
}
const chooseMachine = (m) => { autoMachineMode.value = m }
const backToMode = () => {
  addMode.value = null
  autoMachineMode.value = null
  step.value = 1
}
const backToMachine = () => { autoMachineMode.value = null }

const close = () => {
  cancelVerify.value = true
  emit('close')
}
const addAgain = () => reset()
</script>

<template>
  <Teleport to="body">
    <div v-if="show" class="modal-mask" @click.self="close">
      <div class="modal wide">

        <!-- 步骤条 -->
        <div class="steps-bar">
          <template v-for="(s, i) in stepsMeta" :key="s.n">
            <div class="step" :class="{ active: step === s.n, done: step > s.n }">
              <span class="step-dot">{{ step > s.n ? '✓' : s.n }}</span>
              <span class="step-label">{{ s.label }}</span>
            </div>
            <div v-if="i < stepsMeta.length - 1" class="step-line" :class="{ done: step > s.n }"></div>
          </template>
        </div>

        <div v-if="successMsg" class="wizard-flash">{{ successMsg }}</div>

        <!-- ════ 步骤 1：方式选择 ════ -->
        <div v-if="step === 1" class="modal-body">
          <div class="modal-head plain">
            <h3>添加服务器</h3>
            <button class="close-btn" @click="close">✕</button>
          </div>
          <p class="wizard-hint">选择配置方式</p>
          <div class="mode-grid">
            <div class="mode-card" @click="chooseMode('auto')">
              <div class="mode-title">自动配置</div>
              <div class="mode-desc">自动扫描本机 TShock 进程并修改配置文件，或解析远程配置文件</div>
              <div class="mode-tag">推荐</div>
            </div>
            <div class="mode-card" @click="chooseMode('manual')">
              <div class="mode-title">手动配置</div>
              <div class="mode-desc">手动输入服务器地址、REST 端口和 API 密钥</div>
            </div>
          </div>
        </div>

        <!-- ════ 步骤 2：手动配置 ════ -->
        <div v-else-if="step === 2 && addMode === 'manual'" class="modal-body">
          <div class="modal-head plain">
            <h3>手动配置</h3>
            <button class="close-btn" @click="close">✕</button>
          </div>
          <div class="form-row">
            <label>服务器名称（留空则使用地址）</label>
            <input v-model="addForm.name" placeholder="如：主服" />
          </div>
          <div class="form-row">
            <label>地址 <span class="req">*</span></label>
            <input v-model="addForm.host" placeholder="127.0.0.1" />
          </div>
          <div class="form-row">
            <label>REST 端口 <span class="req">*</span></label>
            <input v-model.number="addForm.port" type="number" min="1" max="65535" placeholder="7878" />
          </div>
          <div class="form-row">
            <label>API Key <span class="req">*</span></label>
            <input v-model="addForm.apiKey" placeholder="TShock REST API 密钥" />
          </div>
          <div class="form-row">
            <label>备注</label>
            <input v-model="addForm.note" placeholder="可选" />
          </div>

          <div v-if="formError" class="test-result fail">{{ formError }}</div>
          <div v-if="testResult" class="test-result" :class="testResult.ok ? 'ok' : 'fail'">
            {{ testResult.ok ? '连接成功，可添加该服务器' : testResult.error }}
          </div>

          <div class="modal-actions">
            <button class="mini-btn" @click="backToMode">返回</button>
            <button class="mini-btn" @click="close">取消</button>
            <button class="save-btn ghost" :disabled="testing" @click="runTestOnly">
              {{ testing ? '测试中...' : '仅测试连接' }}
            </button>
            <button class="save-btn" :disabled="adding || !testResult?.ok" @click="runAdd">
              {{ adding ? '添加中...' : '添加服务器' }}
            </button>
          </div>
        </div>

        <!-- ════ 步骤 2：自动 → 本机/远程 选择 ════ -->
        <div v-else-if="step === 2 && addMode === 'auto' && !autoMachineMode" class="modal-body">
          <div class="modal-head plain">
            <h3>自动配置</h3>
            <button class="close-btn" @click="close">✕</button>
          </div>
          <p class="wizard-hint">选择服务器所在位置</p>
          <div class="mode-grid">
            <div class="mode-card" @click="chooseMachine('local')">
              <div class="mode-title">本机</div>
              <div class="mode-desc">自动扫描本机 TShock 进程，修改配置文件并复制插件</div>
            </div>
            <div class="mode-card" @click="chooseMachine('remote')">
              <div class="mode-title">远程</div>
              <div class="mode-desc">远程服务器上的 TShock，粘贴配置文件并手动导入</div>
            </div>
          </div>
          <div class="modal-actions">
            <button class="mini-btn" @click="backToMode">返回方式选择</button>
          </div>
        </div>

        <!-- ════ 步骤 2：自动-本机 一键流程 ════ -->
        <div v-else-if="step === 2 && addMode === 'auto' && autoMachineMode === 'local'" class="modal-body">
          <div class="modal-head plain">
            <h3>自动配置 - 本机</h3>
            <button class="close-btn" @click="close">✕</button>
          </div>
          <p class="wizard-hint">一键完成：检测进程 → 修改配置并复制插件 → 等待重启 → 自动验证并添加</p>

          <div class="local-flow">
            <!-- 1. 探测 -->
            <div class="flow-step" :class="{ active: localPhase === 'probing', done: localPhase === 'reading' || localPhase === 'verifying' || localPhase === 'done' }">
              <span class="flow-dot">{{ localPhase === 'reading' || localPhase === 'verifying' || localPhase === 'done' ? '✓' : '1' }}</span>
              <div class="flow-body">
                <span class="flow-title">检测进程</span>
                <div class="probe-bar" v-if="localPhase === 'idle' || localPhase === 'error'">
                  <input v-model="probePort" type="text" class="form-input" placeholder="游戏端口" />
                  <button class="save-btn" @click="startLocalOneClick">开始一键配置</button>
                </div>
                <div v-if="localPhase === 'probing'" class="flow-status">正在扫描端口 {{ probePort }} ...</div>
                <div v-if="probeResult?.found && localPhase !== 'idle'" class="flow-detail">
                  找到 {{ probeResult.processes.length }} 个监听进程
                </div>
              </div>
            </div>

            <!-- 2. 修改配置 -->
            <div class="flow-step" :class="{ active: localPhase === 'reading', done: localPhase === 'verifying' || localPhase === 'done' }">
              <span class="flow-dot">{{ localPhase === 'verifying' || localPhase === 'done' ? '✓' : '2' }}</span>
              <div class="flow-body">
                <span class="flow-title">修改配置并复制插件</span>
                <div v-if="localPhase === 'reading'" class="flow-status">正在修改 tshock/config.json 并复制插件...</div>
                <div v-if="autoReadResult?.success" class="flow-detail">
                  REST 端口 <code>{{ autoReadResult.restPort }}</code> · API Key <code>{{ autoReadResult.tokenKey }}</code>
                </div>
              </div>
            </div>

            <!-- 3. 等待重启 + 验证 -->
            <div class="flow-step" :class="{ active: localPhase === 'verifying', done: localPhase === 'done' }">
              <span class="flow-dot">{{ localPhase === 'done' ? '✓' : '3' }}</span>
              <div class="flow-body">
                <span class="flow-title">等待重启并验证连接</span>
                <div v-if="localPhase === 'verifying'" class="flow-status">
                  请重启 TShock 服务端，正在自动检测（第 {{ verifyAttempt }}/{{ verifyMax }} 次，每 3 秒一次）...
                  <button class="mini-btn retry" @click="retryLocalVerify">立即重试</button>
                </div>
                <div v-if="localPhase === 'done'" class="flow-status ok-text">验证通过，服务器已添加并切换</div>
              </div>
            </div>

            <div v-if="localPhase === 'error'" class="test-result fail">{{ localError }}</div>
          </div>

          <div class="modal-actions">
            <button class="mini-btn" @click="backToMachine">返回选择</button>
            <button class="mini-btn" @click="backToMode">返回方式选择</button>
            <button v-if="localPhase === 'error'" class="save-btn" @click="startLocalOneClick">重新开始</button>
          </div>
        </div>

        <!-- ════ 步骤 2：自动-远程 ════ -->
        <div v-else-if="step === 2 && addMode === 'auto' && autoMachineMode === 'remote'" class="modal-body">
          <div class="modal-head plain">
            <h3>自动配置 - 远程</h3>
            <button class="close-btn" @click="close">✕</button>
          </div>
          <p class="wizard-hint">粘贴远程服务器上的 <code>tshock/config.json</code> 内容，自动修改并导出</p>
          <textarea v-model="remoteConfigRaw" class="form-textarea" rows="6" placeholder="将远程 tshock/config.json 的完整内容粘贴到这里..."></textarea>

          <div v-if="remotePhase === 'error'" class="test-result fail">{{ remoteVerifyError }}</div>

          <div class="modal-actions">
            <button class="mini-btn" @click="backToMachine">返回选择</button>
            <button class="save-btn" @click="submitRemoteConfig" :disabled="remoteLoading">
              {{ remoteLoading ? '处理中...' : '解析并修改配置' }}
            </button>
          </div>

          <div v-if="remotePhase === 'review' && remoteResult" class="remote-review">
            <div class="info-grid mini">
              <div class="info-item">
                <span class="info-label">REST 端口</span>
                <span class="info-value">{{ remoteResult.restPort }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">API Key</span>
                <span class="info-value mono">{{ remoteResult.tokenKey }}</span>
              </div>
            </div>
            <p class="wizard-hint">将修改后的配置文件覆盖到远程服务器，然后重启远程 TShock</p>
            <div class="btn-row">
              <button class="mini-btn" @click="copyRemoteConfig">复制到剪贴板</button>
              <button class="mini-btn" @click="downloadRemoteConfig">下载 config.json</button>
            </div>
            <div class="remote-verify">
              <div class="form-row">
                <label>远程 IP / 域名</label>
                <input v-model="remoteHost" type="text" placeholder="192.168.1.100" />
              </div>
              <div class="form-row">
                <label>REST 端口</label>
                <input v-model="remotePort" type="text" :placeholder="String(remoteResult.restPort)" />
              </div>
              <button class="save-btn" @click="verifyRemoteConnection" :disabled="remoteVerifyLoading || !remoteHost.trim()">
                {{ remoteVerifyLoading ? '验证中...' : '测试远程连接' }}
              </button>
              <div v-if="remoteVerifyError && remotePhase !== 'error'" class="test-result fail">{{ remoteVerifyError }}</div>
            </div>
          </div>
        </div>

        <!-- ════ 步骤 4：完成 ════ -->
        <div v-else-if="step === 4" class="modal-body">
          <div class="modal-head plain">
            <h3>添加服务器</h3>
            <button class="close-btn" @click="close">✕</button>
          </div>
          <div class="done-icon">✓</div>
          <p class="done-text">服务器「{{ addedServer?.name }}」已添加成功</p>
          <p class="done-sub">已自动切换为当前服务器，并生成了独立的 webhook 推送密钥</p>
          <div class="modal-actions">
            <button class="mini-btn" @click="addAgain">继续添加</button>
            <button class="save-btn" @click="close">完成</button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.modal-mask {
  position: fixed; inset: 0; z-index: 200;
  background: rgba(0,0,0,.55);
  display: flex; align-items: center; justify-content: center;
  backdrop-filter: blur(3px);
}
.modal {
  background: var(--bg-card);
  border-radius: 16px;
  width: 520px; max-width: 92vw; max-height: 88vh; overflow: auto;
  box-shadow: var(--shadow-lg);
}
.modal.wide { width: 560px; }
.modal-head { display: flex; justify-content: space-between; align-items: center; padding: 16px 20px; border-bottom: 1px solid var(--border-color); }
.modal-head.plain { border-bottom: none; padding: 0 0 8px; }
.modal-head h3 { margin: 0; color: var(--text-primary); }
.close-btn { background: none; border: none; color: var(--text-muted); font-size: 1.1rem; cursor: pointer; }
.modal-body { padding: 8px 24px 24px; }
.modal-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 18px; flex-wrap: wrap; }

/* 步骤条 */
.steps-bar { display: flex; align-items: center; padding: 18px 24px 14px; }
.step { display: flex; align-items: center; gap: 7px; flex-shrink: 0; }
.step-dot {
  width: 24px; height: 24px; border-radius: 50%;
  display: flex; align-items: center; justify-content: center;
  font-size: .75rem; font-weight: 700;
  background: var(--bg-tertiary); color: var(--text-muted);
  border: 1.5px solid var(--border-color);
  transition: all .25s ease;
}
.step.active .step-dot { background: var(--accent-primary); border-color: var(--accent-primary); color: #fff; box-shadow: 0 0 0 4px rgba(99,102,241,.15); }
.step.done .step-dot { background: #22c55e; border-color: #22c55e; color: #fff; }
.step-label { font-size: .78rem; color: var(--text-muted); white-space: nowrap; }
.step.active .step-label { color: var(--accent-primary); font-weight: 700; }
.step.done .step-label { color: #22c55e; }
.step-line { flex: 1; height: 2px; background: var(--border-color); margin: 0 8px; border-radius: 2px; transition: background .3s ease; }
.step-line.done { background: #22c55e; }

.wizard-flash {
  margin: 0 24px 10px; padding: 8px 12px; border-radius: 8px;
  background: rgba(34,197,94,.12); color: #22c55e; font-size: .85rem;
}

.wizard-hint { margin: 0 0 16px; font-size: .85rem; color: var(--text-muted); line-height: 1.6; }
.wizard-hint code { background: rgba(99,102,241,.15); padding: 1px 6px; border-radius: 5px; color: var(--accent-primary); font-size: .8rem; }

.mode-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 8px; }
.mode-card {
  position: relative;
  background: var(--bg-tertiary); border: 1.5px solid var(--border-color); border-radius: 14px;
  padding: 20px 16px; cursor: pointer; transition: all .25s ease;
}
.mode-card:hover { border-color: var(--accent-primary); background: var(--bg-hover); transform: translateY(-2px); box-shadow: 0 4px 16px rgba(99,102,241,.12); }
.mode-title { font-size: 1rem; font-weight: 700; color: var(--text-primary); margin-bottom: 8px; }
.mode-desc { font-size: .8rem; color: var(--text-muted); line-height: 1.5; }
.mode-tag {
  position: absolute; top: -8px; right: 14px;
  background: linear-gradient(135deg, #6366f1, #4f46e5); color: #fff;
  font-size: .68rem; font-weight: 700; padding: 2px 8px; border-radius: 8px;
}

/* 表单 */
.form-row { display: flex; flex-direction: column; gap: 5px; margin-bottom: 12px; }
.form-row label { font-size: .82rem; color: var(--text-muted); }
.form-row label .req { color: #ef4444; }
.form-row input, .form-row select {
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 8px 10px; border-radius: 8px; font-size: .9rem;
}
.form-row input:focus, .form-row select:focus { outline: none; border-color: var(--accent-primary); }
.form-input {
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 8px 10px; border-radius: 8px; font-size: .9rem; flex: 1;
}
.form-input:focus { outline: none; border-color: var(--accent-primary); }
.form-textarea {
  width: 100%; box-sizing: border-box;
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 10px; border-radius: 8px; font-size: .85rem; font-family: monospace; resize: vertical;
}
.form-textarea:focus { outline: none; border-color: var(--accent-primary); }

/* 按钮 */
.save-btn {
  background: var(--accent-primary); color: #fff; border: none;
  padding: 8px 16px; border-radius: 8px; cursor: pointer; font-size: .88rem; font-weight: 600;
}
.save-btn:hover { opacity: .9; }
.save-btn:disabled { opacity: .45; cursor: not-allowed; }
.save-btn.ghost { background: transparent; border: 1px solid var(--accent-primary); color: var(--accent-primary); }
.save-btn.ghost:hover { background: rgba(99,102,241,.1); }
.mini-btn {
  border: 1px solid var(--border-color); background: var(--bg-tertiary); color: var(--text-primary);
  padding: 5px 10px; border-radius: 7px; cursor: pointer; font-size: .8rem;
}
.mini-btn:hover { border-color: var(--accent-primary); color: var(--accent-primary); }
.mini-btn.retry { margin-left: 8px; }

.test-result { padding: 10px 12px; border-radius: 8px; font-size: .88rem; margin-top: 8px; }
.test-result.ok { background: rgba(34,197,94,.12); color: #22c55e; }
.test-result.fail { background: rgba(239,68,68,.12); color: #ef4444; }

/* 本机一键流程 */
.local-flow { display: flex; flex-direction: column; gap: 0; }
.flow-step { display: flex; gap: 12px; padding: 12px 0; border-bottom: 1px dashed var(--border-light); }
.flow-step:last-child { border-bottom: none; }
.flow-dot {
  width: 26px; height: 26px; border-radius: 50%; flex-shrink: 0;
  display: flex; align-items: center; justify-content: center;
  font-size: .78rem; font-weight: 700;
  background: var(--bg-tertiary); color: var(--text-muted); border: 1.5px solid var(--border-color);
  transition: all .25s ease;
}
.flow-step.active .flow-dot { background: var(--accent-primary); border-color: var(--accent-primary); color: #fff; box-shadow: 0 0 0 4px rgba(99,102,241,.15); }
.flow-step.done .flow-dot { background: #22c55e; border-color: #22c55e; color: #fff; }
.flow-body { flex: 1; display: flex; flex-direction: column; gap: 6px; }
.flow-title { font-size: .88rem; font-weight: 600; color: var(--text-primary); }
.flow-status { font-size: .82rem; color: var(--text-muted); line-height: 1.6; }
.flow-detail { font-size: .8rem; color: var(--text-muted); }
.flow-detail code { background: rgba(99,102,241,.15); padding: 1px 6px; border-radius: 5px; color: var(--accent-primary); font-size: .78rem; }
.probe-bar { display: flex; gap: 8px; margin-top: 2px; }
.ok-text { color: #22c55e; }
.info-grid.mini { display: flex; gap: 20px; margin: 10px 0; flex-wrap: wrap; }
.info-item { display: flex; flex-direction: column; gap: 4px; }
.info-label { font-size: .75rem; color: var(--text-muted); }
.info-value { font-size: .9rem; color: var(--text-primary); font-weight: 600; }
.info-value.mono { font-family: monospace; }
.btn-row { display: flex; gap: 8px; margin: 10px 0; }
.remote-review { margin-top: 14px; padding-top: 14px; border-top: 1px solid var(--border-color); }
.remote-verify { margin-top: 12px; }

.done-icon { font-size: 2.5rem; text-align: center; margin: 12px 0; color: #22c55e; }
.done-text { text-align: center; font-size: 1.05rem; color: var(--text-primary); font-weight: 600; }
.done-sub { text-align: center; font-size: .8rem; color: var(--text-muted); margin-top: 6px; }
</style>

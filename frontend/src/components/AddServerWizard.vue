<script setup>
import { ref, watch, onBeforeUnmount } from 'vue'
import { post, get } from '../utils/api.js'
import { selectServer, fetchServers } from '../utils/serverStore.js'

const props = defineProps({
  show: { type: Boolean, default: false }
})
const emit = defineEmits(['close', 'added'])

// ═══════════════ 视图（两段式：choose → 模式表单 → done） ═══════════════
const view = ref('choose')   // choose | manual | local | remote | done
const addedServer = ref(null)

// ═══════════════ 手动 ═══════════════
const addForm = ref({ name: '', host: '', port: 7878, apiKey: '', note: '' })
const testing = ref(false)
const testOk = ref(false)   // 仅测试通过后可添加（渐进式）
const adding = ref(false)
const msg = ref(null)       // { type: 'ok' | 'fail', text }

const validateManual = () => {
  const { host, port, apiKey } = addForm.value
  if (!host.trim()) return '地址不能为空'
  if (!Number.isInteger(port) || port < 1 || port > 65535) return '端口需为 1-65535 的整数'
  if (!apiKey.trim()) return 'API Key 不能为空'
  return null
}

// 关键字段变化后，先前的测试结果作废
watch(() => [addForm.value.host, addForm.value.port, addForm.value.apiKey], () => {
  testOk.value = false
  msg.value = null
})

const runTestOnly = async () => {
  const err = validateManual()
  if (err) { msg.value = { type: 'fail', text: err }; return }
  testing.value = true
  msg.value = null
  try {
    const res = await post('/api/servers/test', {
      host: addForm.value.host.trim(),
      port: addForm.value.port,
      apiKey: addForm.value.apiKey.trim()
    })
    const data = await res.json()
    if (data.success) {
      testOk.value = true
      msg.value = { type: 'ok', text: '连接成功，可添加该服务器' }
    } else {
      testOk.value = false
      msg.value = { type: 'fail', text: data.error || '连接失败' }
    }
  } catch (e) {
    testOk.value = false
    msg.value = { type: 'fail', text: e.message }
  } finally { testing.value = false }
}

const runAdd = async () => {
  if (!testOk.value) return
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
      msg.value = { type: 'fail', text: data.error || '添加失败' }
    }
  } catch (e) {
    msg.value = { type: 'fail', text: e.message }
  } finally { adding.value = false }
}

// ═══════════════ 自动 · 本机（渐进式：扫描 → 配置 → 验证） ═══════════════
const probePort = ref('7777')
const localName = ref('')            // 服务器名称（留空自动生成）
const scanning = ref(false)
const probeResult = ref(null)      // { found, port, processes:[{pid,path}] }
const editablePath = ref('')       // 进程路径可编辑
const autoReadResult = ref(null)
const localPhase = ref('idle')     // idle | probing | found | reading | verifying | error
const localError = ref('')
const verifyAttempt = ref(0)
const verifyMax = 20
const cancelVerify = ref(false)
const isVerifying = ref(false)

const sleep = (ms) => new Promise(r => setTimeout(r, ms))

const startLocalScan = async () => {
  scanning.value = true
  localError.value = ''
  probeResult.value = null
  editablePath.value = ''
  localPhase.value = 'probing'
  try {
    const res = await get(`/api/setup/probe?port=${probePort.value.trim()}`)
    const data = await res.json()
    if (data.found) {
      probeResult.value = data
      editablePath.value = data.processes[0]?.path || ''
      localPhase.value = 'found'
    } else {
      localPhase.value = 'error'
      localError.value = data.error || `未在端口 ${probePort.value} 找到监听进程`
    }
  } catch (e) {
    localPhase.value = 'error'
    localError.value = e.message
  } finally { scanning.value = false }
}

const startLocalOneClick = async () => {
  if (!editablePath.value) return
  cancelVerify.value = false
  localError.value = ''
  localPhase.value = 'reading'
  try {
    const res = await post('/api/setup/auto-read', { processPath: editablePath.value })
    const data = await res.json()
    if (!data.success) {
      localPhase.value = 'error'
      localError.value = data.error || '修改配置失败'
      return
    }
    autoReadResult.value = data
    await startVerifyLoop()
  } catch (e) {
    localPhase.value = 'error'
    localError.value = e.message
  }
}

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
    localError.value = '等待 TShock 重启超时（60 秒）。请确认服务器已重启后重试'
  }
}

const attemptLocalVerify = async () => {
  try {
    const res = await post('/api/setup/auto-verify', {
      name: localName.value.trim(),
      host: '127.0.0.1',
      port: autoReadResult.value.restPort,
      apiKey: autoReadResult.value.tokenKey
    })
    const data = await res.json()
    if (res.ok && data.success) {
      await finishAdd({ id: data.serverId, name: data.name || localName.value.trim() || '本机服务器' })
      return true
    }
    return false
  } catch { return false }
}

// 失败后重试：回到"已找到进程"（保留端口与路径），可重新一键配置或重扫
const retryLocal = () => {
  localError.value = ''
  if (probeResult.value) {
    localPhase.value = 'found'
  } else {
    localPhase.value = 'idle'
  }
}

// ═══════════════ 自动 · 远程（渐进式：解析 → 复制/下载 → 验证） ═══════════════
const remoteConfigRaw = ref('')
const remoteName = ref('')            // 服务器名称（留空自动生成）
const remoteLoading = ref(false)
const remoteResult = ref(null)
const remoteHost = ref('')
const remotePort = ref('')
const remoteVerifying = ref(false)
const remoteError = ref('')
const remoteCopied = ref(false)

const submitRemoteConfig = async () => {
  if (!remoteConfigRaw.value.trim()) {
    remoteError.value = '请粘贴 tshock/config.json 的内容'
    return
  }
  remoteLoading.value = true
  remoteError.value = ''
  remoteCopied.value = false
  try {
    const res = await post('/api/setup/auto-remote', { configRaw: remoteConfigRaw.value })
    const data = await res.json()
    if (data.success) {
      remoteResult.value = data
      remotePort.value = String(data.restPort)
    } else {
      remoteError.value = data.error || '处理失败'
    }
  } catch (e) {
    remoteError.value = e.message
  } finally { remoteLoading.value = false }
}

const copyRemoteConfig = async () => {
  if (remoteResult.value?.modifiedRaw) {
    await navigator.clipboard.writeText(remoteResult.value.modifiedRaw)
    remoteCopied.value = true
    setTimeout(() => { remoteCopied.value = false }, 2000)
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
}

const verifyRemoteConnection = async () => {
  if (!remoteResult.value || !remoteHost.value.trim()) return
  remoteVerifying.value = true
  remoteError.value = ''
  try {
    const port = parseInt(remotePort.value) || remoteResult.value.restPort
    const res = await post('/api/setup/auto-verify', {
      name: remoteName.value.trim(),
      host: remoteHost.value.trim(),
      port,
      apiKey: remoteResult.value.tokenKey
    })
    const data = await res.json()
    if (res.ok && data.success) {
      await finishAdd({ id: data.serverId, name: data.name || remoteName.value.trim() || remoteHost.value.trim() })
    } else {
      remoteError.value = data.error || '验证失败'
    }
  } catch (e) {
    remoteError.value = e.message
  } finally { remoteVerifying.value = false }
}

// ═══════════════ 公共 ═══════════════
const finishAdd = async (server) => {
  selectServer(server.id)
  await fetchServers()
  addedServer.value = server
  view.value = 'done'
  emit('added', server)
}

const reset = () => {
  cancelVerify.value = true
  isVerifying.value = false
  view.value = 'choose'
  addedServer.value = null
  // 手动
  addForm.value = { name: '', host: '', port: 7878, apiKey: '', note: '' }
  testing.value = false
  testOk.value = false
  adding.value = false
  msg.value = null
  // 本机
  probePort.value = '7777'
  localName.value = ''
  scanning.value = false
  probeResult.value = null
  editablePath.value = ''
  autoReadResult.value = null
  localPhase.value = 'idle'
  localError.value = ''
  verifyAttempt.value = 0
  // 远程
  remoteConfigRaw.value = ''
  remoteName.value = ''
  remoteLoading.value = false
  remoteResult.value = null
  remoteHost.value = ''
  remotePort.value = ''
  remoteVerifying.value = false
  remoteError.value = ''
  remoteCopied.value = false
}

const close = () => {
  cancelVerify.value = true
  emit('close')
}

watch(() => props.show, (v) => {
  if (v) reset()
  else cancelVerify.value = true
})

onBeforeUnmount(() => { cancelVerify.value = true })
</script>

<template>
  <Teleport to="body">
    <div v-if="show" class="modal-mask" @click.self="close">
      <div class="modal">
        <Transition name="fade" mode="out-in">
          <div :key="view" class="view">

            <!-- ════ 视图 1：方式选择 ════ -->
            <div v-if="view === 'choose'" class="view-body">
              <div class="view-head">
                <h3>添加服务器</h3>
                <button class="icon-btn" @click="close">✕</button>
              </div>
              <p class="hint">选择一种方式接入 TShock 服务器</p>
              <div class="mode-list">
                <div class="mode-card" @click="view = 'manual'">
                  <div class="mode-title">手动填写</div>
                  <div class="mode-desc">手动输入服务器地址、REST 端口和 API 密钥</div>
                </div>
                <div class="mode-card" @click="view = 'local'">
                  <div class="mode-title">自动 · 本机</div>
                  <div class="mode-desc">扫描本机 TShock 进程，一键修改配置并接入</div>
                  <span class="mode-tag">推荐</span>
                </div>
                <div class="mode-card" @click="view = 'remote'">
                  <div class="mode-title">自动 · 远程</div>
                  <div class="mode-desc">粘贴远程 config.json，自动生成配置并验证</div>
                </div>
              </div>
              <div class="view-foot">
                <button class="text-btn" @click="close">取消</button>
              </div>
            </div>

            <!-- ════ 视图 2：手动 ════ -->
            <div v-else-if="view === 'manual'" class="view-body">
              <div class="view-head">
                <button class="back-btn" @click="view = 'choose'">← 返回</button>
                <h3>添加服务器 · 手动</h3>
                <button class="icon-btn" @click="close">✕</button>
              </div>
              <div class="form-row">
                <label>名称（留空则使用地址）</label>
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
              <div class="btn-row">
                <button class="btn ghost" :disabled="testing" @click="runTestOnly">
                  {{ testing ? '测试中...' : '仅测试连接' }}
                </button>
                <button class="btn primary" :disabled="!testOk || adding" @click="runAdd">
                  {{ adding ? '添加中...' : '添加服务器' }}
                </button>
              </div>
              <div v-if="msg" class="result" :class="msg.type === 'ok' ? 'ok' : 'fail'">{{ msg.text }}</div>
            </div>

            <!-- ════ 视图 2：自动 · 本机 ════ -->
            <div v-else-if="view === 'local'" class="view-body">
              <div class="view-head">
                <button class="back-btn" @click="view = 'choose'">← 返回</button>
                <h3>添加服务器 · 自动本机</h3>
                <button class="icon-btn" @click="close">✕</button>
              </div>

              <!-- 步骤 1：检测进程 -->
              <div class="step">
                <div class="step-head">
                  <span class="step-dot" :class="{ done: localPhase === 'found' || localPhase === 'reading' || localPhase === 'verifying', active: localPhase === 'probing' }">1</span>
                  <span class="step-title">检测进程</span>
                </div>
                <div v-if="localPhase === 'idle' || localPhase === 'error'" class="probe-bar">
                  <input v-model="probePort" class="form-input" placeholder="游戏端口" />
                  <button class="btn primary" :disabled="scanning" @click="startLocalScan">
                    {{ scanning ? '扫描中...' : '扫描' }}
                  </button>
                </div>
                <div v-if="localPhase === 'probing'" class="step-status">正在扫描端口 {{ probePort }} ...</div>
                <div v-if="probeResult?.found && (localPhase === 'found' || localPhase === 'reading' || localPhase === 'verifying' || localPhase === 'error')" class="proc-card">
                  <div class="proc-row">
                    <span class="proc-label">PID</span>
                    <code class="proc-pid">{{ probeResult.processes[0].pid }}</code>
                    <span class="proc-label">路径</span>
                    <input v-model="editablePath" class="path-input" :disabled="localPhase !== 'found'" />
                  </div>
                  <div class="proc-actions">
                    <span class="proc-note">进程路径可修改</span>
                    <button v-if="localPhase === 'found'" class="btn ghost mini" :disabled="scanning" @click="startLocalScan">重新扫描</button>
                  </div>
                </div>
              </div>

              <!-- 步骤 2：一键配置 -->
              <div v-if="localPhase === 'found'" class="step">
                <div class="step-head">
                  <span class="step-dot">2</span>
                  <span class="step-title">一键配置</span>
                </div>
                <div class="form-row">
                  <label>服务器名称（留空自动生成）</label>
                  <input v-model="localName" placeholder="如：本机主服" />
                </div>
                <button class="btn primary" @click="startLocalOneClick">一键配置并添加</button>
              </div>

              <!-- 步骤 3：等待重启验证 -->
              <div v-if="localPhase === 'reading' || localPhase === 'verifying'" class="step">
                <div class="step-head">
                  <span class="step-dot" :class="{ active: localPhase === 'verifying' }">3</span>
                  <span class="step-title">等待重启验证</span>
                </div>
                <div class="proc-list">
                  <div class="proc-line done">✓ 已修改 tshock/config.json</div>
                  <div class="proc-line" :class="{ active: localPhase === 'verifying' }">
                    {{ localPhase === 'verifying' ? `等待服务器重启验证（第 ${verifyAttempt}/${verifyMax} 次）...` : '正在修改配置并复制插件...' }}
                  </div>
                </div>
              </div>

              <div v-if="localPhase === 'error'" class="result fail">{{ localError }}</div>
              <div v-if="localPhase === 'error'" class="btn-row">
                <button class="btn primary" @click="retryLocal">重试</button>
              </div>
            </div>

            <!-- ════ 视图 2：自动 · 远程 ════ -->
            <div v-else-if="view === 'remote'" class="view-body">
              <div class="view-head">
                <button class="back-btn" @click="view = 'choose'">← 返回</button>
                <h3>添加服务器 · 自动远程</h3>
                <button class="icon-btn" @click="close">✕</button>
              </div>

              <!-- 步骤 1：粘贴配置 -->
              <div class="step">
                <div class="step-head">
                  <span class="step-dot" :class="{ done: remoteResult?.success }">1</span>
                  <span class="step-title">粘贴远程配置文件</span>
                </div>
                <textarea v-model="remoteConfigRaw" class="form-textarea" rows="6" placeholder="将远程 tshock/config.json 的完整内容粘贴到这里..."></textarea>
                <button class="btn primary" :disabled="remoteLoading" @click="submitRemoteConfig">
                  {{ remoteLoading ? '解析中...' : '解析并修改' }}
                </button>
              </div>

              <!-- 步骤 2：配置信息 + 验证（解析成功后展开） -->
              <Transition name="slide">
                <div v-if="remoteResult?.success" class="step">
                  <div class="step-head">
                    <span class="step-dot">2</span>
                    <span class="step-title">验证连接</span>
                  </div>
                  <div class="info-card">
                    <div class="info-item">
                      <span class="info-label">REST 端口</span>
                      <span class="info-value mono">{{ remoteResult.restPort }}</span>
                    </div>
                    <div class="info-item">
                      <span class="info-label">API Key</span>
                      <span class="info-value mono">{{ remoteResult.tokenKey }}</span>
                    </div>
                  </div>
                  <div class="btn-row">
                    <button class="btn ghost" @click="copyRemoteConfig">{{ remoteCopied ? '已复制' : '复制到剪贴板' }}</button>
                    <button class="btn ghost" @click="downloadRemoteConfig">下载 config.json</button>
                  </div>
                  <div class="tip">将修改后的配置文件覆盖到远程服务器，并重启 TShock 后再验证</div>
                  <div class="form-row">
                    <label>服务器名称（留空自动生成）</label>
                    <input v-model="remoteName" type="text" placeholder="如：远程主服" />
                  </div>
                  <div class="form-row">
                    <label>远程 IP / 域名 <span class="req">*</span></label>
                    <input v-model="remoteHost" type="text" placeholder="192.168.1.100" />
                  </div>
                  <div class="form-row">
                    <label>REST 端口</label>
                    <input v-model="remotePort" type="text" :placeholder="String(remoteResult.restPort)" />
                  </div>
                  <button class="btn primary" :disabled="remoteVerifying || !remoteHost.trim()" @click="verifyRemoteConnection">
                    {{ remoteVerifying ? '验证中...' : '验证并添加' }}
                  </button>
                </div>
              </Transition>

              <div v-if="remoteError" class="result fail">{{ remoteError }}</div>
            </div>

            <!-- ════ 视图 3：完成 ════ -->
            <div v-else-if="view === 'done'" class="view-body done-body">
              <div class="done-icon">✓</div>
              <p class="done-text">服务器「{{ addedServer?.name }}」已添加成功</p>
              <p class="done-sub">已自动切换为当前服务器</p>
              <button class="btn primary wide" @click="close">完成</button>
            </div>

          </div>
        </Transition>
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
  width: 500px; max-width: 92vw; max-height: 88vh; overflow: auto;
  box-shadow: var(--shadow-lg);
}
.view { padding: 18px 24px 22px; }

/* 头部 */
.view-head { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
.view-head h3 { margin: 0; flex: 1; text-align: center; color: var(--text-primary); font-size: 1.02rem; }
.back-btn {
  border: none; background: none; color: var(--text-muted);
  font-size: .85rem; cursor: pointer; padding: 4px 2px; flex-shrink: 0;
}
.back-btn:hover { color: var(--accent-primary); }
.icon-btn {
  border: none; background: none; color: var(--text-muted);
  font-size: 1.05rem; cursor: pointer; padding: 4px 6px; flex-shrink: 0;
}
.icon-btn:hover { color: var(--text-primary); }
.text-btn {
  border: none; background: none; color: var(--text-muted);
  font-size: .85rem; cursor: pointer; padding: 6px 14px;
}
.text-btn:hover { color: var(--accent-primary); }

.hint { margin: 2px 0 14px; font-size: .82rem; color: var(--text-muted); }
.req { color: #ef4444; }

/* 方式选择 */
.mode-list { display: flex; flex-direction: column; gap: 10px; }
.mode-card {
  position: relative;
  background: var(--bg-tertiary); border: 1.5px solid var(--border-color); border-radius: 12px;
  padding: 14px 16px; cursor: pointer; transition: all .2s ease;
}
.mode-card:hover {
  border-color: var(--accent-primary); background: var(--bg-hover);
  transform: translateY(-1px); box-shadow: 0 4px 14px rgba(99,102,241,.12);
}
.mode-title { font-size: .95rem; font-weight: 700; color: var(--text-primary); margin-bottom: 4px; }
.mode-desc { font-size: .8rem; color: var(--text-muted); line-height: 1.5; }
.mode-tag {
  position: absolute; top: -8px; right: 14px;
  background: linear-gradient(135deg, #6366f1, #4f46e5); color: #fff;
  font-size: .68rem; font-weight: 700; padding: 2px 8px; border-radius: 8px;
}
.view-foot { display: flex; justify-content: flex-end; margin-top: 14px; }

/* 步骤 */
.step { margin-top: 14px; }
.step-head { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
.step-dot {
  width: 22px; height: 22px; border-radius: 50%; flex-shrink: 0;
  display: flex; align-items: center; justify-content: center;
  font-size: .72rem; font-weight: 700;
  background: var(--bg-tertiary); color: var(--text-muted); border: 1.5px solid var(--border-color);
}
.step-dot.active { background: var(--accent-primary); border-color: var(--accent-primary); color: #fff; box-shadow: 0 0 0 4px rgba(99,102,241,.15); }
.step-dot.done { background: #22c55e; border-color: #22c55e; color: #fff; }
.step-title { font-size: .88rem; font-weight: 600; color: var(--text-primary); }
.step-status { font-size: .82rem; color: var(--text-muted); }

/* 表单 */
.form-row { display: flex; flex-direction: column; gap: 5px; margin-bottom: 11px; }
.form-row label { font-size: .82rem; color: var(--text-muted); }
.form-row input, .form-row select, .form-input {
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 8px 10px; border-radius: 8px; font-size: .9rem;
}
.form-row input:focus, .form-row select:focus, .form-input:focus { outline: none; border-color: var(--accent-primary); }
.form-input { flex: 1; }
.form-textarea {
  width: 100%; box-sizing: border-box;
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 10px; border-radius: 8px; font-size: .84rem; font-family: monospace; resize: vertical;
  margin-bottom: 10px;
}
.form-textarea:focus { outline: none; border-color: var(--accent-primary); }

/* 按钮 */
.btn-row { display: flex; gap: 10px; margin-top: 14px; }
.btn {
  border: none; border-radius: 8px; cursor: pointer;
  padding: 8px 16px; font-size: .88rem; font-weight: 600;
  transition: all .2s ease;
}
.btn.primary {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5); color: #fff;
  box-shadow: 0 2px 8px rgba(99,102,241,.25);
}
.btn.primary:hover { opacity: .92; }
.btn.primary:disabled { opacity: .45; cursor: not-allowed; box-shadow: none; }
.btn.ghost { background: transparent; border: 1px solid var(--accent-primary); color: var(--accent-primary); }
.btn.ghost:hover { background: rgba(99,102,241,.1); }
.btn.ghost:disabled { opacity: .45; cursor: not-allowed; }
.btn.mini { padding: 5px 10px; font-size: .78rem; }
.btn.wide { width: 100%; padding: 10px; }

/* 提示条 */
.result { padding: 10px 12px; border-radius: 8px; font-size: .86rem; margin-top: 12px; }
.result.ok { background: rgba(34,197,94,.12); color: #22c55e; }
.result.fail { background: rgba(239,68,68,.12); color: #ef4444; }
.tip {
  margin: 10px 0; padding: 8px 12px; border-radius: 8px; font-size: .8rem;
  background: rgba(99,102,241,.1); color: var(--accent-primary); line-height: 1.5;
}

/* 本机：进程卡片 */
.probe-bar { display: flex; gap: 8px; }
.proc-card {
  border: 1px solid var(--border-color); border-radius: 10px;
  padding: 10px 12px; background: var(--bg-tertiary);
  margin-top: 8px;
}
.proc-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.proc-label { font-size: .76rem; color: var(--text-muted); }
.proc-pid { font-size: .86rem; color: var(--accent-primary); background: rgba(99,102,241,.12); padding: 1px 7px; border-radius: 6px; }
.path-input {
  flex: 1; min-width: 160px;
  background: var(--bg-card); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 6px 8px; border-radius: 6px; font-size: .82rem; font-family: monospace;
}
.path-input:focus { outline: none; border-color: var(--accent-primary); }
.path-input:disabled { opacity: .75; }
.proc-actions { display: flex; align-items: center; justify-content: space-between; margin-top: 8px; }
.proc-note { font-size: .74rem; color: var(--text-muted); }
.proc-list { display: flex; flex-direction: column; gap: 6px; }
.proc-line { font-size: .82rem; color: var(--text-muted); padding-left: 4px; }
.proc-line.done { color: #22c55e; }
.proc-line.active { color: var(--accent-primary); }

/* 远程：信息卡 */
.info-card {
  display: flex; gap: 24px; flex-wrap: wrap;
  border: 1px solid var(--border-color); border-radius: 10px;
  padding: 10px 14px; background: var(--bg-tertiary); margin-bottom: 10px;
}
.info-item { display: flex; flex-direction: column; gap: 3px; }
.info-label { font-size: .74rem; color: var(--text-muted); }
.info-value { font-size: .9rem; color: var(--text-primary); font-weight: 600; word-break: break-all; }
.info-value.mono { font-family: monospace; }

/* 完成页 */
.done-body { text-align: center; padding: 26px 24px 22px; }
.done-icon { font-size: 2.4rem; color: #22c55e; margin: 6px 0 10px; }
.done-text { font-size: 1.02rem; color: var(--text-primary); font-weight: 600; margin: 0 0 6px; }
.done-sub { font-size: .8rem; color: var(--text-muted); margin: 0 0 20px; }

/* 过渡 */
.fade-enter-active, .fade-leave-active { transition: opacity .15s ease; }
.fade-enter-from, .fade-leave-to { opacity: 0; }
.slide-enter-active { transition: all .25s ease; }
.slide-enter-from { opacity: 0; transform: translateY(-6px); }
</style>

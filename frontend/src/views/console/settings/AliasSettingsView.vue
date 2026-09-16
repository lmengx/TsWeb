<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { get, post } from '../../../utils/api.js'

const loading = ref(true)
const error = ref('')
const success = ref('')
const saving = ref(false)
let loaded = false

// ═══ 配置 ═══
const config = ref({
  bypassPermission: 'tsweb.alias.bypass',
  entries: [],
})

// ═══ 表单（新建/编辑共用）═══
const showForm = ref(false)
const editIdx = ref(-1)
const form = ref(blankForm())

function blankForm() {
  return {
    newCommand: '',
    sourceCommand: '',
    supplement: true,
    notSource: false,
    condition: 0,
    cooldownSeconds: 0,
    shareCooldown: false,
  }
}

// 条件枚举文案
const CONDITIONS = [
  { value: 0, label: '无限制', desc: '任何状态都可使用' },
  { value: 1, label: '死亡时', desc: '仅死亡状态可用' },
  { value: 2, label: '存活时', desc: '仅存活状态可用' },
]

const conditionLabel = (v) => {
  const c = CONDITIONS.find(x => x.value === v)
  return c ? c.label : '无限制'
}

// ═══ 配置读写 ═══
const doSave = async (silent = false) => {
  error.value = ''
  if (!loaded) {
    error.value = '配置尚未加载成功，请刷新页面后重试'
    return
  }
  if (!silent) success.value = ''
  saving.value = true
  try {
    const payload = {
      version: config.value.version ?? 1,
      bypassPermission: config.value.bypassPermission,
      entries: config.value.entries.map(e => ({
        newCommand: e.newCommand,
        sourceCommand: e.sourceCommand,
        enabled: e.enabled,
        supplement: e.supplement,
        notSource: e.notSource,
        condition: e.condition,
        cooldownSeconds: Number(e.cooldownSeconds) || 0,
        shareCooldown: e.shareCooldown,
      })),
    }
    const res = await post('/api/config/aliases', payload)
    const data = await res.json()
    if (data.status === '200' || data.status === 200) {
      if (!silent) {
        success.value = data.message || '已保存'
        setTimeout(() => { success.value = '' }, 2500)
      }
      await fetchConfig()
    } else {
      error.value = data.error || '保存失败'
    }
  } catch (err) {
    error.value = '保存失败: ' + err.message
  } finally {
    saving.value = false
  }
}

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/aliases')
    const data = await res.json()
    if (data.entries) {
      config.value = {
        version: data.version ?? 1,
        bypassPermission: data.bypassPermission || 'tsweb.alias.bypass',
        entries: (data.entries || []).map(e => ({
          newCommand: e.newCommand || '',
          sourceCommand: e.sourceCommand || '',
          enabled: e.enabled !== false,
          supplement: !!e.supplement,
          notSource: !!e.notSource,
          condition: Number(e.condition) || 0,
          cooldownSeconds: Number(e.cooldownSeconds) || 0,
          shareCooldown: !!e.shareCooldown,
        })),
      }
      loaded = true
    } else {
      error.value = data.error || '加载配置失败'
    }
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  loading.value = false
}

// ═══ 表单操作 ═══
const openCreate = () => {
  editIdx.value = -1
  form.value = blankForm()
  showForm.value = true
}

const openEdit = (e) => {
  const idx = config.value.entries.indexOf(e)
  editIdx.value = idx
  form.value = {
    newCommand: e.newCommand,
    sourceCommand: e.sourceCommand,
    supplement: e.supplement,
    notSource: e.notSource,
    condition: e.condition,
    cooldownSeconds: e.cooldownSeconds,
    shareCooldown: e.shareCooldown,
  }
  showForm.value = true
}

const cancelForm = () => {
  showForm.value = false
  editIdx.value = -1
}

const submitForm = async () => {
  error.value = ''
  if (!form.value.newCommand.trim()) {
    error.value = '请输入新命令名'
    return
  }
  if (!form.value.sourceCommand.trim()) {
    error.value = '请输入原始命令'
    return
  }
  const entry = {
    newCommand: form.value.newCommand.trim().replace(/^\//, ''),
    sourceCommand: form.value.sourceCommand.trim(),
    enabled: true,
    supplement: form.value.supplement,
    notSource: form.value.notSource,
    condition: Number(form.value.condition) || 0,
    cooldownSeconds: Number(form.value.cooldownSeconds) || 0,
    shareCooldown: form.value.shareCooldown,
  }
  if (editIdx.value >= 0) {
    config.value.entries[editIdx.value] = { ...config.value.entries[editIdx.value], ...entry }
  } else {
    config.value.entries.push(entry)
  }
  showForm.value = false
  editIdx.value = -1
  await doSave()
}

const toggleEntry = (e) => {
  e.enabled = !e.enabled
  doSave(true)
}

const removeEntry = async (e) => {
  if (!confirm(`确定删除映射「/${e.newCommand}」吗？`)) return
  config.value.entries = config.value.entries.filter(x => x !== e)
  await doSave()
}

const applyBypass = () => {
  doSave(true)
}

// 占位符提示
const PLACEHOLDERS = [
  ['{0}', '第 1 个参数'],
  ['{1}', '第 2 个参数'],
  ['{player}', '玩家名'],
]

onMounted(fetchConfig)
onUnmounted(() => {})
</script>

<template>
  <div class="settings-page">
    <div v-if="loading" class="loading-state"><p>加载中...</p></div>

    <div v-else class="settings-content">
      <!-- ═══ 说明 ═══ -->
      <div class="section-card">
        <h3>命令别名</h3>
        <p class="section-desc">
          把任意命令（TShock 原生或其他插件的命令）映射成一个自定义命令名。玩家输入新命令时自动转调原始命令；
          开启「阻止原始」可禁用原始命令，只允许使用新名。配置保存后即时生效，无需重启服务器。
        </p>
      </div>

      <!-- ═══ 映射列表 ═══ -->
      <div class="section-card">
        <div class="card-head">
          <h3>映射列表</h3>
          <button class="action-btn btn-primary" @click="openCreate">新建映射</button>
        </div>

        <div v-if="!config.entries.length" class="empty">暂无映射，点击「新建映射」添加</div>

        <div v-for="e in config.entries" :key="e.newCommand" class="entry-row">
          <div class="entry-main">
            <div class="entry-title">
              <span class="entry-cmd">/{{ e.newCommand }}</span>
              <span class="entry-arrow">→</span>
              <span class="entry-src">{{ e.sourceCommand }}</span>
              <span v-if="!e.enabled" class="badge st-disabled">已停用</span>
            </div>
            <div class="entry-tags">
              <span v-if="e.notSource" class="tag tag-danger">阻止原始</span>
              <span v-if="e.supplement" class="tag">余段补充</span>
              <span v-if="e.condition !== 0" class="tag">{{ conditionLabel(e.condition) }}</span>
              <span v-if="e.cooldownSeconds > 0" class="tag">
                冷却 {{ e.cooldownSeconds }}s{{ e.shareCooldown ? '（共享）' : '' }}
              </span>
              <span v-if="!e.notSource && !e.supplement && e.condition === 0 && e.cooldownSeconds === 0" class="tag tag-muted">基础映射</span>
            </div>
          </div>
          <div class="entry-actions">
            <label class="switch" :title="e.enabled ? '点击停用' : '点击启用'">
              <input type="checkbox" :checked="e.enabled" @change="toggleEntry(e)" />
              <span class="slider"></span>
            </label>
            <button class="link-btn" @click="openEdit(e)">编辑</button>
            <button class="link-btn link-danger" @click="removeEntry(e)">删除</button>
          </div>
        </div>
      </div>

      <!-- ═══ 新建/编辑表单 ═══ -->
      <div v-if="showForm" class="section-card">
        <h3>{{ editIdx >= 0 ? '编辑映射' : '新建映射' }}</h3>

        <div class="field-row">
          <label class="field-label">新命令名（玩家输入的别名，不含 /）</label>
          <input class="field-input" type="text" v-model="form.newCommand" placeholder="例如：传送 / 被动书店 / gm" />
          <span class="field-hint">玩家输入 /新命令名 即触发；可填中文或英文</span>
        </div>

        <div class="field-row">
          <label class="field-label">原始命令（被映射的命令，可带占位符）</label>
          <input class="field-input" type="text" v-model="form.sourceCommand" placeholder="例如：warp {0} / pskill list / god" />
          <div class="placeholder-hints">
            <span v-for="[ph, desc] in PLACEHOLDERS" :key="ph" class="ph-chip" :title="desc">{{ ph }}</span>
          </div>
        </div>

        <div class="toggle-row">
          <div class="toggle-label-wrap">
            <span class="toggle-label">余段补充</span>
            <span class="toggle-hint">开启后，玩家输入的多余参数自动拼接到命令末尾；关闭则多余参数被拒绝</span>
          </div>
          <label class="switch">
            <input type="checkbox" v-model="form.supplement" />
            <span class="slider"></span>
          </label>
        </div>

        <div class="toggle-row">
          <div class="toggle-label-wrap">
            <span class="toggle-label">阻止原始</span>
            <span class="toggle-hint">开启后原始命令被禁用，玩家只能使用新命令名；拥有免检权限的玩家不受影响</span>
          </div>
          <label class="switch">
            <input type="checkbox" v-model="form.notSource" />
            <span class="slider"></span>
          </label>
        </div>

        <div class="field-row">
          <label class="field-label">限制条件</label>
          <select class="field-input" v-model.number="form.condition">
            <option v-for="c in CONDITIONS" :key="c.value" :value="c.value">{{ c.label }}（{{ c.desc }}）</option>
          </select>
        </div>

        <div class="time-row">
          <div class="field-row time-field">
            <label class="field-label">冷却秒数（0 = 无冷却）</label>
            <input class="field-input" type="number" min="0" v-model.number="form.cooldownSeconds" />
          </div>
          <div class="field-row time-field" v-if="form.cooldownSeconds > 0">
            <label class="field-label">冷却共享</label>
            <label class="switch" style="margin-top: 8px">
              <input type="checkbox" v-model="form.shareCooldown" />
              <span class="slider"></span>
            </label>
            <span class="field-hint">开启：全服共享冷却；关闭：每人独立冷却</span>
          </div>
        </div>

        <div class="form-actions">
          <button class="action-btn btn-primary" :disabled="saving" @click="submitForm">{{ saving ? '保存中...' : '保存' }}</button>
          <button class="action-btn btn-plain" @click="cancelForm">取消</button>
        </div>
      </div>

      <!-- ═══ 全局设置 ═══ -->
      <div class="section-card">
        <h3>全局设置</h3>

        <div class="field-row">
          <label class="field-label">免检权限（拥有者可继续使用被阻止的原始命令）</label>
          <input class="field-input" type="text" v-model="config.bypassPermission" @change="applyBypass" />
          <span class="field-hint">默认：tsweb.alias.bypass，可在 TShock 组管理中授予</span>
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
  </div>
</template>

<style scoped>
.settings-page {
  padding: 20px;
  width: 100%;
}

.settings-content {
  max-width: 860px;
}

.loading-state {
  text-align: center;
  padding: 60px;
  color: var(--text-muted);
}

.section-card {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  margin-bottom: 20px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.section-card h3 {
  margin: 0 0 4px 0;
  color: var(--text-primary);
  font-size: 1.1rem;
  font-weight: 600;
}

.section-desc {
  margin: 8px 0 0 0;
  color: var(--text-muted);
  font-size: 0.85rem;
  line-height: 1.6;
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

/* ── 条目行 ── */
.entry-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 14px 0;
  border-bottom: 1px solid var(--border-light);
}

.entry-row:last-child {
  border-bottom: none;
}

.entry-main {
  flex: 1;
  min-width: 0;
}

.entry-title {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.entry-cmd {
  color: var(--accent-primary);
  font-weight: 700;
  font-size: 0.95rem;
  font-family: monospace;
}

.entry-arrow {
  color: var(--text-muted);
  font-size: 0.85rem;
}

.entry-src {
  color: var(--text-primary);
  font-size: 0.9rem;
  font-family: monospace;
  word-break: break-all;
}

.entry-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 6px;
}

.tag {
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.72rem;
  font-weight: 500;
  background: rgba(59, 130, 246, 0.12);
  color: #3b82f6;
  border: 1px solid rgba(59, 130, 246, 0.25);
}

.tag-danger {
  background: rgba(239, 68, 68, 0.12);
  color: #ef4444;
  border-color: rgba(239, 68, 68, 0.25);
}

.tag-muted {
  background: var(--bg-hover);
  color: var(--text-muted);
  border-color: var(--border-color);
}

.entry-actions {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
}

.badge {
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
  white-space: nowrap;
}

.st-disabled {
  background: rgba(107, 114, 128, 0.15);
  color: #9ca3af;
}

.empty {
  color: var(--text-muted);
  font-size: 0.85rem;
  padding: 16px 0;
  text-align: center;
}

.link-btn {
  background: none;
  border: none;
  color: var(--accent-primary);
  font-size: 0.8rem;
  cursor: pointer;
  padding: 2px 6px;
}

.link-btn:hover { text-decoration: underline; }
.link-danger { color: var(--accent-error); }

/* ── 表单 ── */
.toggle-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px 0;
  border-bottom: 1px solid var(--border-light);
}

.toggle-label-wrap {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
  min-width: 0;
}

.toggle-label {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.95rem;
}

.toggle-hint {
  color: var(--text-muted);
  font-size: 0.8rem;
  line-height: 1.4;
}

.time-row {
  display: flex;
  gap: 16px;
}

.time-field {
  flex: 1;
}

.field-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 0;
}

.field-label {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.9rem;
}

.field-input {
  background: var(--bg-hover);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  padding: 8px 12px;
  font-size: 0.9rem;
  width: 100%;
  box-sizing: border-box;
}

.field-hint {
  color: var(--text-muted);
  font-size: 0.78rem;
}

.placeholder-hints {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 6px;
}

.ph-chip {
  font-family: monospace;
  font-size: 0.72rem;
  background: var(--bg-hover);
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 2px 8px;
  cursor: help;
}

.form-actions {
  display: flex;
  gap: 10px;
  margin-top: 10px;
}

/* ── 按钮 ── */
.action-btn {
  padding: 8px 20px;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  border: none;
  transition: all 0.2s ease;
}

.action-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-primary {
  background: var(--accent-primary);
  color: white;
}

.btn-primary:hover:not(:disabled) {
  opacity: 0.9;
}

.btn-plain {
  background: var(--bg-hover);
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
}

/* ── 开关 ── */
.switch {
  position: relative;
  display: inline-block;
  width: 44px;
  height: 24px;
  flex-shrink: 0;
}

.switch input {
  opacity: 0;
  width: 0;
  height: 0;
}

.slider {
  position: absolute;
  cursor: pointer;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: var(--bg-hover);
  border: 2px solid var(--border-color);
  border-radius: 24px;
  transition: all 0.3s ease;
}

.slider::before {
  content: '';
  position: absolute;
  height: 16px;
  width: 16px;
  left: 2px;
  bottom: 2px;
  background: var(--text-muted);
  border-radius: 50%;
  transition: all 0.3s ease;
}

.switch input:checked + .slider {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
}

.switch input:checked + .slider::before {
  transform: translateX(20px);
  background: white;
}

/* ── Toast ── */
.toast {
  position: fixed;
  top: 20px;
  right: 20px;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 18px;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  z-index: 2000;
  box-shadow: var(--shadow-lg);
}

.toast-success {
  background: rgba(34, 197, 94, 0.15);
  color: var(--accent-secondary);
  border: 1px solid rgba(34, 197, 94, 0.3);
}

.toast-error {
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.toast-enter-active,
.toast-leave-active {
  transition: all 0.3s ease;
}

.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(-10px);
}
</style>

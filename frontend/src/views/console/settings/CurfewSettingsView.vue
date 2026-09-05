<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { get, post } from '../../../utils/api.js'

const loading = ref(true)
const error = ref('')
const success = ref('')
const saving = ref(false)
let saveTimer = null

// ═══ 配置 ═══
const config = ref({
  defaultMessage: '',
  exemptGroups: ['owner', 'superadmin'],
  entries: [],
})
const now = ref('')
const nextOpen = ref('')
const allowedGroups = ref([])

// ═══ 表单（新建/编辑共用）═══
const showForm = ref(false)
const editId = ref('')
const form = ref(blankForm())

function blankForm() {
  return {
    name: '',
    repeatDaily: true,
    startTime: '22:00',
    endTime: '06:00',
    startDate: '',
    endDate: '',
    message: '',
    exemptGroupsText: '',
  }
}

const globalExemptText = ref('')

// ═══ 计算状态 ═══
const activeCount = computed(() => config.value.entries.filter(e => e.active).length)
const curfewActive = computed(() => activeCount.value > 0)

const scheduleText = (e) => {
  if (e.repeatDaily) return `每天 ${e.startTime || '--:--'} ~ ${e.endTime || '--:--'}`
  return `${e.startDate || '????-??-??'} ${e.startTime || '--:--'} ~ ${e.endDate || '????-??-??'} ${e.endTime || '--:--'}`
}

const statusOf = (e) => {
  if (e.active) return { text: '🔴 生效中', cls: 'st-active' }
  if (e.expired) return { text: '已到期', cls: 'st-expired' }
  if (e.enabled) return { text: '待生效', cls: 'st-pending' }
  return { text: '已停用', cls: 'st-disabled' }
}

const groupsText = (e) => (e.resolvedGroups && e.resolvedGroups.length ? e.resolvedGroups.join('、') : '（全局）')

// ═══ 配置读写 ═══
const doSave = async (silent = false) => {
  error.value = ''
  if (!silent) success.value = ''
  saving.value = true
  try {
    const payload = {
      defaultMessage: config.value.defaultMessage,
      exemptGroups: config.value.exemptGroups,
      entries: config.value.entries.map(e => ({
        id: e.id,
        name: e.name,
        enabled: e.enabled,
        repeatDaily: e.repeatDaily,
        startTime: e.startTime,
        endTime: e.endTime,
        startDate: e.startDate,
        endDate: e.endDate,
        message: e.message,
        exemptGroups: e.exemptGroups && e.exemptGroups.length ? e.exemptGroups : null,
      })),
    }
    const res = await post('/api/config/curfew', payload)
    const data = await res.json()
    if (data.status === '200') {
      if (!silent) {
        success.value = data.message || '已保存'
        setTimeout(() => { success.value = '' }, 2000)
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

const saveDebounced = () => {
  clearTimeout(saveTimer)
  saveTimer = setTimeout(() => doSave(true), 500)
}

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/curfew')
    const data = await res.json()
    if (data.entries) {
      config.value.defaultMessage = data.defaultMessage || ''
      config.value.exemptGroups = data.exemptGroups || []
      config.value.entries = (data.entries || []).map(e => ({
        ...e,
        exemptGroups: e.exemptGroups || [],
      }))
      now.value = data.now || ''
      nextOpen.value = data.nextOpen || ''
      allowedGroups.value = data.allowedGroups || []
      globalExemptText.value = (config.value.exemptGroups || []).join(', ')
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
  editId.value = ''
  form.value = blankForm()
  showForm.value = true
}

const openEdit = (e) => {
  editId.value = e.id
  form.value = {
    name: e.name,
    repeatDaily: e.repeatDaily,
    startTime: e.startTime,
    endTime: e.endTime,
    startDate: e.startDate,
    endDate: e.endDate,
    message: e.message || '',
    exemptGroupsText: (e.exemptGroups || []).join(', '),
  }
  showForm.value = true
}

const cancelForm = () => {
  showForm.value = false
  editId.value = ''
}

const submitForm = async () => {
  error.value = ''
  if (!form.value.name.trim()) {
    error.value = '请输入条目名称'
    return
  }
  if (!form.value.startTime || !form.value.endTime) {
    error.value = '请填写开始与结束时间'
    return
  }
  if (!form.value.repeatDaily && (!form.value.startDate || !form.value.endDate)) {
    error.value = '一次性条目需要填写开始与结束日期'
    return
  }

  const entry = {
    id: editId.value || undefined,
    name: form.value.name.trim(),
    enabled: true,
    repeatDaily: form.value.repeatDaily,
    startTime: form.value.startTime,
    endTime: form.value.endTime,
    startDate: form.value.repeatDaily ? '' : form.value.startDate,
    endDate: form.value.repeatDaily ? '' : form.value.endDate,
    message: form.value.message,
    exemptGroups: form.value.exemptGroupsText.split(',').map(s => s.trim()).filter(Boolean),
  }

  if (editId.value) {
    const idx = config.value.entries.findIndex(e => e.id === editId.value)
    if (idx >= 0) config.value.entries[idx] = { ...config.value.entries[idx], ...entry, id: editId.value }
  } else {
    config.value.entries.push({ ...entry, id: 'new-' + Date.now(), active: false, expired: false })
  }
  showForm.value = false
  editId.value = ''
  await doSave()
}

const toggleEntry = (e) => {
  e.enabled = !e.enabled
  saveDebounced()
}

const removeEntry = async (e) => {
  if (!confirm(`确定删除宵禁条目「${e.name}」吗？`)) return
  config.value.entries = config.value.entries.filter(x => x.id !== e.id)
  await doSave()
}

const applyGlobalGroups = () => {
  config.value.exemptGroups = globalExemptText.value.split(',').map(s => s.trim()).filter(Boolean)
  saveDebounced()
}

const applyDefaultMessage = () => {
  saveDebounced()
}

// 占位符提示
const PLACEHOLDERS = [
  ['{now}', '当前时间（HH:mm）'],
  ['{date}', '当前日期（yyyy-MM-dd）'],
  ['{weekday}', '星期几'],
  ['{startTime}', '宵禁开始时间'],
  ['{endTime}', '宵禁结束时间'],
  ['{timeLeft}', '距离结束剩余时长'],
  ['{allowedGroups}', '可进服组列表'],
  ['{curfewName}', '条目名称'],
  ['{serverName}', '服务器名称'],
]

onMounted(fetchConfig)
onUnmounted(() => clearTimeout(saveTimer))
</script>

<template>
  <div class="settings-page">
    <div v-if="loading" class="loading-state"><p>加载中...</p></div>

    <div v-else class="settings-content">
      <!-- ═══ 当前状态 ═══ -->
      <div class="section-card">
        <h3>🚦 当前宵禁状态</h3>
        <div class="status-row">
          <span :class="['status-pill', curfewActive ? 'pill-active' : 'pill-idle']">
            {{ curfewActive ? `🔴 生效中（${activeCount} 个条目）` : '⚪ 未生效' }}
          </span>
          <span v-if="nextOpen" class="status-meta">下一次生效：{{ nextOpen }}</span>
          <span v-else class="status-meta">暂无排期的宵禁条目</span>
        </div>
        <p class="section-desc">
          生效规则：任一激活条目即拦截所有非豁免组玩家进服（并集）。可进服组：{{ allowedGroups.length ? allowedGroups.join('、') : '（无）' }} · 服务器时间：{{ now || '-' }}
        </p>
      </div>

      <!-- ═══ 条目列表 ═══ -->
      <div class="section-card">
        <div class="card-head">
          <h3>📋 宵禁条目</h3>
          <button class="action-btn btn-primary" @click="openCreate">＋ 新建条目</button>
        </div>
        <p class="section-desc">每日循环条目按钟点每天自动生效/结束；一次性条目到期后自动关闭，需再次开启请重新启用或新建。</p>

        <div v-if="!config.entries.length" class="empty">暂无条目，点击"新建条目"添加</div>

        <div v-for="e in config.entries" :key="e.id" class="entry-row">
          <div class="entry-main">
            <div class="entry-title">
              <span class="entry-name">{{ e.name }}</span>
              <span :class="['badge', statusOf(e).cls]">{{ statusOf(e).text }}</span>
            </div>
            <div class="entry-schedule">{{ scheduleText(e) }}</div>
            <div class="entry-groups">豁免组：{{ groupsText(e) }}</div>
            <div v-if="e.message" class="entry-msg">消息：{{ e.message.replace(/\n/g, ' ⏎ ') }}</div>
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
        <h3>{{ editId ? '✏️ 编辑条目' : '＋ 新建条目' }}</h3>

        <div class="field-row">
          <label class="field-label">名称</label>
          <input class="field-input" type="text" v-model="form.name" placeholder="例如：深夜维护" />
        </div>

        <div class="toggle-row">
          <div class="toggle-label-wrap">
            <span class="toggle-label">每天循环生效</span>
            <span class="toggle-hint">开启后按每天固定时段自动生效/结束；关闭为一次性（需指定日期）</span>
          </div>
          <label class="switch">
            <input type="checkbox" v-model="form.repeatDaily" />
            <span class="slider"></span>
          </label>
        </div>

        <div class="time-row">
          <div class="field-row time-field">
            <label class="field-label">开始时间</label>
            <input class="field-input" type="time" v-model="form.startTime" />
          </div>
          <div class="field-row time-field">
            <label class="field-label">结束时间</label>
            <input class="field-input" type="time" v-model="form.endTime" />
          </div>
        </div>

        <div v-if="!form.repeatDaily" class="time-row">
          <div class="field-row time-field">
            <label class="field-label">开始日期</label>
            <input class="field-input" type="date" v-model="form.startDate" />
          </div>
          <div class="field-row time-field">
            <label class="field-label">结束日期</label>
            <input class="field-input" type="date" v-model="form.endDate" />
          </div>
        </div>

        <div class="field-row">
          <label class="field-label">踢出消息模板（留空用全局默认）</label>
          <textarea class="field-input field-area" v-model="form.message" rows="4"
            placeholder="支持 \n 换行与占位符，例如：{now} / {endTime} / {timeLeft} / {allowedGroups} / {curfewName}" />
          <div class="placeholder-hints">
            <span v-for="[ph, desc] in PLACEHOLDERS" :key="ph" class="ph-chip" :title="desc">{{ ph }}</span>
          </div>
        </div>

        <div class="field-row">
          <label class="field-label">条目专属豁免组（逗号分隔，留空用全局）</label>
          <input class="field-input" type="text" v-model="form.exemptGroupsText" placeholder="owner, superadmin" />
        </div>

        <div class="form-actions">
          <button class="action-btn btn-primary" :disabled="saving" @click="submitForm">{{ saving ? '保存中...' : '保存' }}</button>
          <button class="action-btn btn-plain" @click="cancelForm">取消</button>
        </div>
      </div>

      <!-- ═══ 全局设置 ═══ -->
      <div class="section-card">
        <h3>⚙️ 全局设置</h3>

        <div class="field-row">
          <label class="field-label">全局豁免组（逗号分隔）</label>
          <input class="field-input" type="text" v-model="globalExemptText" @change="applyGlobalGroups" />
          <span class="field-hint">未单独配置豁免组的条目使用此列表；命中即放行进服</span>
        </div>

        <div class="field-row">
          <label class="field-label">全局默认踢出消息模板</label>
          <textarea class="field-input field-area" v-model="config.defaultMessage" rows="5" @change="applyDefaultMessage" />
          <div class="placeholder-hints">
            <span v-for="[ph, desc] in PLACEHOLDERS" :key="ph" class="ph-chip" :title="desc">{{ ph }}</span>
          </div>
          <span class="field-hint">支持占位符与 \n 换行；条目未单独配置消息时使用</span>
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
  margin: 8px 0 16px 0;
  color: var(--text-muted);
  font-size: 0.85rem;
  line-height: 1.5;
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

/* ── 状态 ── */
.status-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 8px;
}

.status-pill {
  padding: 6px 14px;
  border-radius: 999px;
  font-size: 0.9rem;
  font-weight: 600;
}

.pill-active {
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.pill-idle {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
  border: 1px solid rgba(34, 197, 94, 0.3);
}

.status-meta {
  color: var(--text-muted);
  font-size: 0.85rem;
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
  gap: 10px;
}

.entry-name {
  color: var(--text-primary);
  font-weight: 600;
  font-size: 0.95rem;
}

.entry-schedule {
  color: var(--text-muted);
  font-size: 0.85rem;
  margin-top: 3px;
}

.entry-groups {
  color: var(--text-muted);
  font-size: 0.8rem;
  margin-top: 2px;
}

.entry-msg {
  color: var(--text-muted);
  font-size: 0.78rem;
  margin-top: 2px;
  opacity: 0.8;
  word-break: break-all;
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

.st-active { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
.st-pending { background: rgba(59, 130, 246, 0.15); color: #3b82f6; }
.st-expired { background: rgba(107, 114, 128, 0.15); color: #9ca3af; }
.st-disabled { background: rgba(107, 114, 128, 0.15); color: #9ca3af; }

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

.field-area {
  font-family: inherit;
  resize: vertical;
  line-height: 1.5;
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

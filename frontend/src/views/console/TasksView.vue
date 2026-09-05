<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import AppSelect from '../../components/AppSelect.vue'
import Loading from '../../components/Loading.vue'
import {
  listTasks, getTask, saveTask, deleteTask, runTask,
  listTaskLogs, getTaskLogDetail, BOSS_NAMES
} from '../../api/tasksApi.js'

// ===== 状态 =====
const loading = ref(true)
const tasks = ref([])
const error = ref('')
const success = ref('')

const showEditor = ref(false)
const editing = ref(false)
const saving = ref(false)
const form = reactive(emptyTask())

const showLogs = ref(false)
const logsLoading = ref(false)
const logTask = ref(null)
const logs = ref([])
const logDetail = ref(null)

// 命令拖拽状态
const dragIndex = ref(-1)

const CONDITION_TYPES = [
  { value: 'always', label: '无条件' },
  { value: 'online_count', label: '在线人数' },
  { value: 'boss_defeated', label: 'BOSS已击败' },
  { value: 'player_online', label: '指定玩家在线' }
]

const TRIGGER_MODES = [
  { value: 'manual', label: '手动执行' },
  { value: 'interval', label: '间隔执行' },
  { value: 'daily', label: '每天定时' }
]

const EXEC_MODES = [
  { value: 'sequential', label: '顺序执行' },
  { value: 'concurrent', label: '并发执行' }
]

// 时间选择器选项（00-23 / 00-59）
const HOURS = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, '0'))
const MINUTES = Array.from({ length: 60 }, (_, i) => String(i).padStart(2, '0'))

// ===== 任务模板（新建时点击填充） =====
const TASK_TEMPLATES = [
  {
    name: '凌晨关服重启',
    desc: '每天 03:00，玩家数为 0 时执行关服（重启需自行配置）',
    build: () => ({
      name: '凌晨关服重启',
      enabled: true,
      triggerMode: 'daily',
      dailyTime: '03:00',
      condition: { type: 'online_count', not: false, params: { min: 0, max: 0, bossNames: [], playerNames: [] } },
      execMode: 'sequential',
      commands: [
        '/bc 服务器即将关闭',
        '/off confirm'
      ]
    })
  },
  {
    name: '自动清理地面物品',
    desc: '每 2 小时广播提示，1 分钟后清理地面掉落物',
    build: () => ({
      name: '自动清理地面物品',
      enabled: true,
      triggerMode: 'interval',
      intervalSeconds: 7200,
      condition: { type: 'always', not: false, params: { min: 0, max: 9999, bossNames: [], playerNames: [] } },
      execMode: 'sequential',
      commands: [
        '/bc 1分钟后将清理地面物品',
        '/wait 60000',
        '/clear item 9999'
      ]
    })
  },
  {
    name: '定时自动保存',
    desc: '每 30 分钟自动保存世界',
    build: () => ({
      name: '定时自动保存',
      enabled: true,
      triggerMode: 'interval',
      intervalSeconds: 1800,
      condition: { type: 'always', not: false, params: { min: 0, max: 9999, bossNames: [], playerNames: [] } },
      execMode: 'sequential',
      commands: [
        '/bc 正在保存世界',
        '/save'
      ]
    })
  }
]

const applyTemplate = (tpl) => {
  Object.assign(form, tpl.build())
  editing.value = false
}

// 每日时间的 时/分 双联选择（组合成 HH:mm）
const dailyHour = computed({
  get: () => form.dailyTime?.split(':')[0] ?? '00',
  set: (v) => { form.dailyTime = `${v}:${dailyMinute.value}` }
})
const dailyMinute = computed({
  get: () => form.dailyTime?.split(':')[1] ?? '00',
  set: (v) => { form.dailyTime = `${dailyHour.value}:${v}` }
})

function emptyTask() {
  return {
    id: '',
    name: '',
    enabled: true,
    triggerMode: 'manual',
    intervalSeconds: 600,
    dailyTime: '04:00',
    condition: { type: 'always', not: false, params: { min: 0, max: 9999, bossNames: [], playerNames: [] } },
    execMode: 'sequential',
    commands: []
  }
}

// ===== 加载 =====
const loadTasks = async () => {
  loading.value = true
  error.value = ''
  try {
    const data = await listTasks()
    if (data.status === 200 || data.status === '200') {
      tasks.value = data.tasks || []
    } else {
      error.value = data.error || '加载任务失败'
    }
  } catch (e) {
    error.value = '加载失败: ' + e.message
  }
  loading.value = false
}

// ===== 摘要 =====
const conditionSummary = (cond) => {
  if (!cond) return '—'
  let text = ''
  switch (cond.type) {
    case 'always': text = '无条件'; break
    case 'online_count': {
      const p = cond.params || {}
      const parts = []
      if (p.min !== undefined && p.min !== null && p.min !== 0) parts.push(`≥${p.min}人`)
      if (p.max !== undefined && p.max !== null && p.max !== 9999) parts.push(`≤${p.max}人`)
      text = parts.length ? parts.join(' 且 ') : '无条件'
      break
    }
    case 'boss_defeated': text = `击败 ${(cond.params?.bossNames || []).join('/') || '—'}`; break
    case 'player_online': text = `在线: ${(cond.params?.playerNames || []).join('/') || '—'}`; break
    default: text = cond.type
  }
  return cond.not ? `非(${text})` : text
}

const triggerSummary = (t) => {
  switch (t.triggerMode) {
    case 'interval': return `每 ${t.intervalSeconds} 秒`
    case 'daily': return `每天 ${t.dailyTime}`
    default: return '手动'
  }
}

const statusLabel = (status) => {
  const map = { success: '成功', failed: '失败', skipped: '跳过', running: '执行中' }
  return map[status] || status || '未执行'
}

const statusClass = (status) => {
  const map = { success: 'ok', failed: 'bad', skipped: 'warn', running: 'run' }
  return map[status] || ''
}

// ===== 卡片启用开关 =====
const toggleEnabled = async (task) => {
  error.value = ''
  try {
    const data = await getTask(task.id)
    if (data.status !== 200 && data.status !== '200') {
      error.value = data.error || '切换失败'
      return
    }
    const full = JSON.parse(JSON.stringify(data.task))
    full.enabled = !task.enabled
    const res = await saveTask(full)
    if (res.status === 200 || res.status === '200') {
      task.enabled = full.enabled
    } else {
      error.value = res.error || '切换失败'
    }
  } catch (e) {
    error.value = '切换失败: ' + e.message
  }
}

// ===== 编辑器 =====
const openCreate = () => {
  Object.assign(form, emptyTask())
  editing.value = false
  showEditor.value = true
}

const openEdit = async (task) => {
  try {
    const data = await getTask(task.id)
    if (data.status === 200 || data.status === '200') {
      Object.assign(form, JSON.parse(JSON.stringify(data.task)))
      if (!form.condition) form.condition = emptyTask().condition
      if (!form.condition.params) form.condition.params = { min: 0, max: 9999, bossNames: [], playerNames: [] }
      if (!form.commands) form.commands = []
      editing.value = true
      showEditor.value = true
    } else {
      error.value = data.error || '加载任务失败'
    }
  } catch (e) {
    error.value = '加载失败: ' + e.message
  }
}

const closeEditor = () => { showEditor.value = false }

const save = async () => {
  if (!form.name.trim()) { error.value = '请输入任务名称'; return }
  saving.value = true
  error.value = ''
  success.value = ''
  try {
    const data = await saveTask(form)
    if (data.status === 200 || data.status === '200') {
      success.value = '任务已保存'
      showEditor.value = false
      setTimeout(() => { success.value = '' }, 2000)
      await loadTasks()
    } else {
      error.value = data.error || '保存失败'
    }
  } catch (e) {
    error.value = '保存失败: ' + e.message
  }
  saving.value = false
}

const removeTask = async (task) => {
  if (!confirm(`确定删除任务「${task.name}」？`)) return
  error.value = ''
  try {
    const data = await deleteTask(task.id)
    if (data.status === 200 || data.status === '200') {
      await loadTasks()
    } else {
      error.value = data.error || '删除失败'
    }
  } catch (e) {
    error.value = '删除失败: ' + e.message
  }
}

const execTask = async (task, force) => {
  error.value = ''
  try {
    const data = await runTask(task.id, force)
    if (data.status === 200 || data.status === '200') {
      success.value = `任务「${task.name}」已开始执行`
      setTimeout(() => { success.value = '' }, 2000)
      setTimeout(loadTasks, 1500)
    } else {
      error.value = data.error || '执行失败'
    }
  } catch (e) {
    error.value = '执行失败: ' + e.message
  }
}

// ===== 命令操作（拖拽排序）=====
const addCommand = () => { form.commands.push('') }
const removeCommand = (i) => { form.commands.splice(i, 1) }

const onDragStart = (i, e) => {
  dragIndex.value = i
  e.dataTransfer.effectAllowed = 'move'
  e.dataTransfer.setData('text/plain', String(i))
}

const onDragOver = (i, e) => {
  e.preventDefault()
  const from = dragIndex.value
  if (from === -1 || from === i) return
  const arr = form.commands
  const [item] = arr.splice(from, 1)
  arr.splice(i, 0, item)
  dragIndex.value = i
}

const onDragEnd = () => {
  dragIndex.value = -1
}

// ===== 条件操作 =====
const toggleBoss = (name) => {
  const arr = form.condition.params.bossNames
  const idx = arr.indexOf(name)
  if (idx >= 0) arr.splice(idx, 1)
  else arr.push(name)
}
const newPlayerName = ref('')
const addPlayer = () => {
  const name = newPlayerName.value.trim()
  if (!name) return
  if (!form.condition.params.playerNames) form.condition.params.playerNames = []
  if (!form.condition.params.playerNames.includes(name)) {
    form.condition.params.playerNames.push(name)
  }
  newPlayerName.value = ''
}
const removePlayer = (i) => { form.condition.params.playerNames.splice(i, 1) }

// ===== 执行记录 =====
const openLogs = async (task) => {
  logTask.value = task
  showLogs.value = true
  logDetail.value = null
  logsLoading.value = true
  try {
    const data = await listTaskLogs(task.id, 1, 20)
    if (data.status === 200 || data.status === '200') {
      logs.value = data.logs || []
    } else {
      logs.value = []
    }
  } catch (e) {
    logs.value = []
  }
  logsLoading.value = false
}

const closeLogs = () => { showLogs.value = false; logDetail.value = null }

const closeLogDetail = () => { logDetail.value = null }

const viewLogDetail = async (log) => {
  try {
    const data = await getTaskLogDetail(log.id)
    if (data.status === 200 || data.status === '200') {
      logDetail.value = data.log
    }
  } catch (e) {
    error.value = '加载记录失败: ' + e.message
  }
}

onMounted(() => { loadTasks() })
</script>

<template>
  <div class="tasks-page">
    <div class="page-header">
      <h2>自动任务</h2>
      <p class="page-desc">定时执行 TShock 命令，支持条件触发与执行记录追溯</p>
    </div>

    <div v-if="error" class="alert alert-error">{{ error }}</div>
    <div v-if="success" class="alert alert-success">{{ success }}</div>

    <Transition name="fade-slide" mode="out-in">
      <Loading v-if="loading" key="loading" text="加载中..." />

      <!-- ===== 任务卡片列表 ===== -->
      <div v-else key="content" class="tasks-content">
      <div class="tasks-toolbar">
        <button class="btn-add" @click="openCreate">＋ 新建任务</button>
      </div>

      <div v-if="tasks.length === 0" class="empty-state">
        <p>暂无任务，点击「＋ 新建任务」开始创建</p>
      </div>

      <div v-else class="task-grid">
        <div v-for="task in tasks" :key="task.id" class="task-card" :class="{ disabled: task.triggerMode !== 'manual' && !task.enabled }">
          <!-- 头部：名称 + 启用开关（手动任务无自动触发，不显示开关） -->
          <div class="card-head">
            <div class="card-title">
              <span class="task-name">{{ task.name }}</span>
              <span v-if="task.running" class="running-tag">执行中...</span>
            </div>
            <label v-if="task.triggerMode !== 'manual'" class="switch" title="启用/禁用" @click.stop>
              <input type="checkbox" :checked="task.enabled" @change="toggleEnabled(task)" />
              <span class="slider"></span>
            </label>
          </div>

          <!-- 触发信息 -->
          <div class="card-trigger">
            <span class="tag">{{ triggerSummary(task) }}</span>
            <span class="tag">{{ task.execMode === 'concurrent' ? '并发' : '顺序' }}</span>
            <span class="tag">{{ task.commandCount }} 条命令</span>
          </div>

          <!-- 条件 -->
          <div class="card-cond">
            <span class="cond-label">前提</span>
            <span class="cond-text">{{ conditionSummary(task.condition) }}</span>
          </div>

          <!-- 统计信息 -->
          <div class="card-stats">
            <div class="stat-item">
              <span class="stat-value">{{ task.runCount ?? 0 }}</span>
              <span class="stat-label">执行次数</span>
            </div>
            <div class="stat-divider"></div>
            <div class="stat-item">
              <span class="stat-value stat-time">{{ task.lastRunAt || '—' }}</span>
              <span class="stat-label">上次执行</span>
            </div>
            <div class="stat-divider"></div>
            <div class="stat-item">
              <span class="status-badge" :class="statusClass(task.lastRunStatus)">{{ statusLabel(task.lastRunStatus) }}</span>
              <span class="stat-label">上次状态</span>
            </div>
          </div>

          <!-- 操作按钮 -->
          <div class="card-actions">
            <button class="btn-mini btn-run" @click="execTask(task, true)" title="立即强制执行（跳过条件）">▶ 执行</button>
            <button class="btn-mini" @click="openEdit(task)">编辑</button>
            <button class="btn-mini" @click="openLogs(task)">记录</button>
            <button class="btn-mini btn-danger" @click="removeTask(task)">删除</button>
          </div>
        </div>
      </div>
    </div>
    </Transition>

    <!-- ===== 新建/编辑弹窗 ===== -->
    <div v-if="showEditor" class="modal-overlay" @click.self="closeEditor">
      <div class="modal-panel editor-panel">
        <div class="modal-header">
          <h3>{{ editing ? '编辑任务' : '新建任务' }}</h3>
          <button class="modal-close" @click="closeEditor">✕</button>
        </div>

        <div class="editor-body">
          <!-- 模板选择（仅新建时显示） -->
          <div v-if="!editing" class="template-section">
            <h4>从模板创建</h4>
            <div class="template-grid">
              <button
                v-for="tpl in TASK_TEMPLATES"
                :key="tpl.name"
                class="template-card"
                @click="applyTemplate(tpl)"
              >
                <span class="template-name">{{ tpl.name }}</span>
                <span class="template-desc">{{ tpl.desc }}</span>
              </button>
            </div>
          </div>

          <!-- 基础设置 -->
          <div class="form-row">
            <div class="form-group grow">
              <label class="form-label">任务名称</label>
              <input v-model="form.name" class="form-input" placeholder="例如：凌晨自动重启" />
            </div>
          </div>

          <!-- 触发模式 -->
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">触发模式</label>
              <AppSelect v-model="form.triggerMode" :options="TRIGGER_MODES" />
            </div>
            <div v-if="form.triggerMode === 'interval'" class="form-group">
              <label class="form-label">间隔（秒）</label>
              <input v-model.number="form.intervalSeconds" type="number" min="1" class="form-input" />
            </div>
            <div v-if="form.triggerMode === 'daily'" class="form-group">
              <label class="form-label">每日时间</label>
              <div class="time-picker">
                <AppSelect v-model="dailyHour" :options="HOURS" placeholder="时" />
                <span class="time-colon">:</span>
                <AppSelect v-model="dailyMinute" :options="MINUTES" placeholder="分" />
              </div>
            </div>
            <div class="form-group">
              <label class="form-label">执行方式</label>
              <AppSelect v-model="form.execMode" :options="EXEC_MODES" />
            </div>
          </div>

          <!-- 前提条件 -->
          <div class="sub-section">
            <h4>执行前提</h4>
            <div class="form-row">
              <div class="form-group">
                <label class="form-label">条件类型</label>
                <AppSelect v-model="form.condition.type" :options="CONDITION_TYPES" />
              </div>
              <div class="form-group">
                <label class="form-label">取否（结果反转）</label>
                <label class="switch">
                  <input type="checkbox" v-model="form.condition.not" />
                  <span class="slider"></span>
                </label>
              </div>
            </div>

            <!-- 在线人数 -->
            <div v-if="form.condition.type === 'online_count'" class="form-row">
              <div class="form-group">
                <label class="form-label">最少人数</label>
                <input v-model.number="form.condition.params.min" type="number" min="0" class="form-input" />
              </div>
              <div class="form-group">
                <label class="form-label">最多人数</label>
                <input v-model.number="form.condition.params.max" type="number" min="0" class="form-input" />
              </div>
            </div>

            <!-- BOSS 已击败 -->
            <div v-if="form.condition.type === 'boss_defeated'" class="boss-selector">
              <label class="form-label">已击败的 BOSS（可多选，全部满足）</label>
              <div class="boss-grid">
                <button
                  v-for="name in BOSS_NAMES"
                  :key="name"
                  class="boss-chip"
                  :class="{ active: form.condition.params.bossNames.includes(name) }"
                  @click="toggleBoss(name)"
                >{{ name }}</button>
              </div>
            </div>

            <!-- 指定玩家在线 -->
            <div v-if="form.condition.type === 'player_online'" class="player-input-row">
              <label class="form-label">需要同时在线的玩家</label>
              <div class="player-tags">
                <span v-for="(p, i) in form.condition.params.playerNames" :key="i" class="player-tag">
                  {{ p }}
                  <button class="tag-remove" @click="removePlayer(i)">✕</button>
                </span>
              </div>
              <div class="add-row">
                <input v-model="newPlayerName" class="form-input" placeholder="输入玩家名..." @keyup.enter="addPlayer" />
                <button class="btn-mini" @click="addPlayer">添加</button>
              </div>
            </div>
          </div>

          <!-- 命令列表（拖拽排序） -->
          <div class="sub-section">
            <h4>命令列表 <span class="hint">拖动排序 · 顺序模式支持 /wait <毫秒></span></h4>
            <div
              v-for="(cmd, i) in form.commands"
              :key="i"
              class="cmd-row"
              :class="{ dragging: dragIndex === i }"
              @dragover="onDragOver(i, $event)"
              @dragend="onDragEnd"
            >
              <span
                class="drag-handle"
                title="按住拖动排序"
                draggable="true"
                @dragstart="onDragStart(i, $event)"
              >⠿</span>
              <span class="cmd-index">{{ i + 1 }}</span>
              <input v-model="form.commands[i]" class="form-input cmd-input" placeholder="/broadcast 示例" />
              <button class="btn-mini btn-danger" @click="removeCommand(i)" title="删除">✕</button>
            </div>
            <button class="btn-add" @click="addCommand">＋ 添加命令</button>
          </div>
        </div>

        <div class="modal-footer">
          <button class="btn-cancel" @click="closeEditor">取消</button>
          <button class="btn-save" :disabled="saving" @click="save">{{ saving ? '保存中...' : '保存' }}</button>
        </div>
      </div>
    </div>

    <!-- ===== 执行记录弹窗 ===== -->
    <div v-if="showLogs" class="modal-overlay" @click.self="closeLogs">
      <div class="modal-panel logs-panel">
        <div class="modal-header">
          <h3>执行记录 — {{ logTask?.name }}</h3>
          <button class="modal-close" @click="closeLogs">✕</button>
        </div>

        <Loading v-if="logsLoading" text="加载中..." />

        <div v-else-if="logs.length === 0" class="empty-state">
          <p>暂无执行记录</p>
        </div>

        <div v-else class="logs-content">
          <div class="logs-list">
            <div v-for="log in logs" :key="log.id" class="log-item" @click="viewLogDetail(log)">
              <span class="log-time">{{ log.triggeredAt }}</span>
              <span class="status-badge" :class="statusClass(log.status)">{{ statusLabel(log.status) }}</span>
              <span class="log-dur">{{ log.durationMs ? (log.durationMs / 1000).toFixed(1) + 's' : '—' }}</span>
              <span v-if="log.skipped" class="log-cond">条件未通过</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ===== 二级模态框：执行详情 ===== -->
    <div v-if="logDetail" class="modal-overlay modal-level2" @click.self="closeLogDetail">
      <div class="modal-panel detail-panel">
        <div class="modal-header">
          <h3>执行详情</h3>
          <button class="modal-close" @click="closeLogDetail">✕</button>
        </div>
        <div class="detail-body">
          <div class="detail-meta">
            <span>任务: {{ logDetail.taskName }}</span>
            <span>触发: {{ logDetail.triggeredAt }} ({{ logDetail.triggerMode }})</span>
            <span>状态: {{ statusLabel(logDetail.status) }} · {{ (logDetail.durationMs / 1000).toFixed(2) }}s</span>
          </div>
          <div v-if="logDetail.errorSummary" class="detail-error">{{ logDetail.errorSummary }}</div>
          <div class="detail-commands">
            <div v-for="c in logDetail.commands" :key="c.index" class="detail-cmd">
              <div class="detail-cmd-head">
                <span class="cmd-index">{{ c.index + 1 }}</span>
                <code class="cmd-text">{{ c.command }}</code>
                <span class="status-badge" :class="c.success ? 'ok' : 'bad'">{{ c.success ? '成功' : '失败' }}</span>
              </div>
              <div v-if="c.output" class="cmd-output">{{ c.output }}</div>
              <div v-if="c.error" class="cmd-error">{{ c.error }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tasks-page { padding: 0 4px; }
.page-header { margin-bottom: 16px; }
.page-header h2 { margin: 0 0 4px; font-size: 1.4rem; color: var(--text-primary); }
.page-desc { margin: 0; color: var(--text-secondary); font-size: 0.9rem; }

.alert { padding: 10px 14px; border-radius: 10px; margin-bottom: 12px; font-size: 0.9rem; }
.alert-error { background: rgba(239, 68, 68, 0.12); color: #ef4444; border: 1px solid rgba(239, 68, 68, 0.3); }
.alert-success { background: rgba(34, 197, 94, 0.12); color: #22c55e; border: 1px solid rgba(34, 197, 94, 0.3); }

.empty-state { padding: 40px; text-align: center; color: var(--text-secondary); }

.tasks-toolbar { display: flex; justify-content: flex-end; margin-bottom: 16px; }
.btn-add { padding: 8px 16px; border-radius: 10px; border: 1px solid rgba(99, 102, 241, 0.3); background: rgba(99, 102, 241, 0.12); color: #818cf8; font-size: 0.88rem; cursor: pointer; transition: all 0.2s; }
.btn-add:hover { background: rgba(99, 102, 241, 0.22); }

/* ── 任务卡片 ── */
.task-grid { display: flex; flex-direction: column; gap: 14px; }
.task-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 14px;
  padding: 16px 18px;
  transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
}
.task-card:hover { border-color: rgba(99, 102, 241, 0.35); box-shadow: 0 4px 20px rgba(0, 0, 0, 0.12); }
.task-card.disabled { opacity: 0.55; }

.card-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; }
.card-title { display: flex; align-items: center; gap: 10px; }
.task-name { font-size: 1.05rem; font-weight: 700; color: var(--text-primary); }
.running-tag { padding: 2px 10px; border-radius: 6px; background: rgba(99, 102, 241, 0.15); color: #818cf8; font-size: 0.75rem; font-weight: 600; animation: pulse 1.5s infinite; }
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }

.card-trigger { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 10px; }
.tag { padding: 2px 10px; border-radius: 6px; background: var(--bg-tertiary); border: 1px solid var(--border-light); color: var(--text-secondary); font-size: 0.78rem; }

.card-cond { display: flex; align-items: center; gap: 8px; margin-bottom: 14px; }
.cond-label { font-size: 0.72rem; color: var(--text-muted); font-weight: 600; flex-shrink: 0; }
.cond-text { font-size: 0.85rem; color: var(--text-secondary); }

.card-stats {
  display: flex; align-items: center; gap: 20px;
  padding: 12px 14px;
  background: var(--bg-tertiary);
  border-radius: 10px;
  margin-bottom: 14px;
}
.stat-item { display: flex; flex-direction: column; gap: 3px; flex: 1; align-items: flex-start; }
.stat-value { font-size: 1rem; font-weight: 700; color: var(--text-primary); }
.stat-value.stat-time { font-size: 0.85rem; font-weight: 500; }
.stat-label { font-size: 0.7rem; color: var(--text-muted); }
.stat-divider { width: 1px; height: 28px; background: var(--border-light); }

.card-actions { display: flex; gap: 8px; flex-wrap: wrap; }
.btn-mini { padding: 5px 12px; border-radius: 8px; border: 1px solid var(--border-light); background: var(--bg-tertiary); color: var(--text-secondary); font-size: 0.8rem; cursor: pointer; transition: all 0.2s; }
.btn-mini:hover { background: var(--bg-hover); color: var(--text-primary); }
.btn-run { background: rgba(99, 102, 241, 0.15); color: #818cf8; border-color: rgba(99, 102, 241, 0.3); }
.btn-run:hover { background: rgba(99, 102, 241, 0.25); }
.btn-danger { color: #ef4444; }
.btn-danger:hover { background: rgba(239, 68, 68, 0.12); }

.status-badge { display: inline-block; padding: 2px 10px; border-radius: 6px; font-size: 0.78rem; font-weight: 600; }
.status-badge.ok { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
.status-badge.bad { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
.status-badge.warn { background: rgba(245, 158, 11, 0.15); color: #f59e0b; }
.status-badge.run { background: rgba(99, 102, 241, 0.15); color: #818cf8; }

/* 每日时间双联选择器 */
.time-picker { display: flex; align-items: center; gap: 4px; }
.time-colon { color: var(--text-muted); font-weight: 700; font-size: 1rem; margin: 0 2px; }

/* 开关 */
.switch { position: relative; display: inline-block; width: 42px; height: 24px; flex-shrink: 0; }
.switch input { opacity: 0; width: 0; height: 0; }
.slider { position: absolute; cursor: pointer; inset: 0; background: var(--bg-tertiary); border: 1px solid var(--border-light); transition: 0.3s; border-radius: 24px; }
.slider:before { content: ''; position: absolute; height: 16px; width: 16px; left: 3px; top: 3px; background: var(--text-muted); border-radius: 50%; transition: 0.3s; }
.switch input:checked + .slider { background: var(--accent-primary); border-color: var(--accent-primary); }
.switch input:checked + .slider:before { transform: translateX(18px); background: white; }

/* ── 弹窗 ── */
.modal-overlay {
  position: fixed; inset: 0; z-index: 10000;
  background: rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(4px);
  display: flex; align-items: center; justify-content: center;
  padding: 20px;
}
.modal-panel {
  background: var(--bg-primary);
  border: 1px solid var(--border-light);
  border-radius: 16px;
  max-width: 720px;
  width: 100%;
  max-height: 85vh;
  display: flex; flex-direction: column;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
  animation: modalIn 0.2s ease;
}
@keyframes modalIn { from { opacity: 0; transform: translateY(10px) scale(0.98); } to { opacity: 1; transform: none; } }
.logs-panel { max-width: 860px; }
/* 二级模态框：叠加在一级之上 */
.modal-level2 { z-index: 10001; }
.detail-panel { max-width: 640px; }
.detail-body { padding: 16px 20px; overflow-y: auto; flex: 1; }
.modal-header { display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid var(--border-light); }
.modal-header h3 { margin: 0; font-size: 1.05rem; color: var(--text-primary); }
.modal-close { width: 32px; height: 32px; border-radius: 10px; border: 1px solid var(--border-light); background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; transition: all 0.15s; }
.modal-close:hover { color: var(--text-primary); background: var(--bg-hover); }
.modal-footer { display: flex; justify-content: flex-end; gap: 10px; padding: 14px 20px; border-top: 1px solid var(--border-light); }
.btn-cancel { padding: 8px 18px; border-radius: 10px; border: 1px solid var(--border-light); background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; transition: all 0.2s; }
.btn-cancel:hover { background: var(--bg-hover); color: var(--text-primary); }
.btn-save { padding: 8px 18px; border-radius: 10px; border: none; background: linear-gradient(135deg, var(--accent-primary), #4f46e5); color: white; cursor: pointer; font-weight: 600; transition: all 0.2s; }
.btn-save:disabled { opacity: 0.5; cursor: not-allowed; }

.editor-body { padding: 18px 20px; overflow-y: auto; flex: 1; }
.form-row { display: flex; gap: 14px; margin-bottom: 14px; flex-wrap: wrap; }
.form-row .form-group { flex: 0 0 auto; }
.form-row .form-group.grow { flex: 1; min-width: 160px; }
.form-group { display: flex; flex-direction: column; gap: 6px; }
.form-label { font-size: 0.8rem; color: var(--text-secondary); font-weight: 600; }
.form-input {
  padding: 8px 12px; border-radius: 10px; border: 1px solid var(--border-light);
  background: var(--bg-tertiary); color: var(--text-primary); font-size: 0.9rem; outline: none;
  transition: border-color 0.2s, box-shadow 0.2s; min-width: 100px;
}
.form-input:focus { border-color: var(--accent-primary); box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.12); }
.form-input::placeholder { color: var(--text-muted); }

/* ── 下拉框（自定义外观）── */
.form-select {
  appearance: none;
  -webkit-appearance: none;
  -moz-appearance: none;
  padding: 8px 34px 8px 12px;
  border-radius: 10px;
  border: 1px solid var(--border-light);
  background-color: var(--bg-tertiary);
  color: var(--text-primary);
  font-size: 0.9rem;
  font-family: inherit;
  outline: none;
  cursor: pointer;
  transition: border-color 0.2s, box-shadow 0.2s;
  /* 自定义下拉箭头 */
  background-image: url("data:image/svg+xml;charset=UTF-8,%3csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='%23999' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3e%3cpolyline points='6 9 12 15 18 9'%3e%3c/polyline%3e%3c/svg%3e");
  background-repeat: no-repeat;
  background-position: right 10px center;
  background-size: 14px;
  padding-right: 34px;
}
.form-select:hover { border-color: rgba(99, 102, 241, 0.4); }
.form-select:focus { border-color: var(--accent-primary); box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.12); }
.form-select option { background: var(--bg-primary); color: var(--text-primary); }

.sub-section { margin-top: 18px; padding-top: 16px; border-top: 1px solid var(--border-light); }
.sub-section h4 { margin: 0 0 12px; font-size: 0.95rem; color: var(--text-primary); }
.hint { font-size: 0.75rem; color: var(--text-muted); font-weight: 400; margin-left: 8px; }

/* ── 任务模板 ── */
.template-section { margin-bottom: 18px; }
.template-section h4 { margin: 0 0 10px; font-size: 0.95rem; color: var(--text-primary); }
.template-grid { display: flex; flex-wrap: wrap; gap: 8px; }
.template-card {
  flex: 1 1 200px;
  min-width: 200px;
  display: flex; flex-direction: column; gap: 4px;
  padding: 12px 14px;
  border-radius: 10px;
  border: 1px dashed var(--border-light);
  background: var(--bg-tertiary);
  color: var(--text-primary);
  cursor: pointer;
  text-align: left;
  transition: all 0.2s;
}
.template-card:hover { border-color: var(--accent-primary); background: rgba(99, 102, 241, 0.08); transform: translateY(-1px); }
.template-name { font-size: 0.88rem; font-weight: 700; color: var(--accent-primary); }
.template-desc { font-size: 0.75rem; color: var(--text-muted); line-height: 1.4; }

/* BOSS 选择器 */
.boss-selector { margin-bottom: 14px; }
.boss-grid { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; }
.boss-chip {
  padding: 6px 12px; border-radius: 8px; border: 1px solid var(--border-light);
  background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; font-size: 0.82rem;
  transition: all 0.2s;
}
.boss-chip:hover { border-color: rgba(99, 102, 241, 0.4); color: var(--text-primary); }
.boss-chip.active { background: rgba(99, 102, 241, 0.2); color: #818cf8; border-color: rgba(99, 102, 241, 0.4); }

/* 玩家标签 */
.player-input-row { margin-bottom: 14px; }
.player-tags { display: flex; flex-wrap: wrap; gap: 6px; margin: 8px 0; }
.player-tag { display: inline-flex; align-items: center; gap: 4px; padding: 3px 10px; border-radius: 8px; background: rgba(99, 102, 241, 0.12); color: #818cf8; font-size: 0.8rem; }
.tag-remove { border: none; background: transparent; color: inherit; cursor: pointer; font-size: 0.7rem; }
.add-row { display: flex; gap: 8px; }

/* ── 命令列表（拖拽排序）── */
.cmd-row {
  display: flex; align-items: center; gap: 8px; margin-bottom: 8px;
  padding: 6px 8px;
  border-radius: 10px;
  border: 1px solid transparent;
  transition: all 0.15s;
  user-select: none;
}
.cmd-row:hover { background: var(--bg-hover); border-color: var(--border-light); }
.cmd-row.dragging { opacity: 0.5; border-color: var(--accent-primary); background: rgba(99, 102, 241, 0.08); }
.drag-handle {
  cursor: grab; color: var(--text-muted); font-size: 1.1rem; flex-shrink: 0;
  padding: 4px 6px; margin: -4px 0; border-radius: 6px; line-height: 1;
  user-select: none; -webkit-user-select: none;
  transition: color 0.15s, background 0.15s;
}
.drag-handle:hover { color: var(--accent-primary); background: rgba(99, 102, 241, 0.1); }
.drag-handle:active { cursor: grabbing; }
.cmd-index { width: 20px; text-align: center; color: var(--text-muted); font-size: 0.8rem; flex-shrink: 0; }
.cmd-input { flex: 1; font-family: monospace; font-size: 0.85rem; }

/* 执行记录 */
.logs-content { display: flex; flex-direction: column; padding: 14px 20px; overflow-y: auto; flex: 1; gap: 12px; }
.logs-list { display: flex; flex-direction: column; gap: 6px; }
.log-item {
  display: flex; align-items: center; gap: 12px; padding: 8px 12px;
  border-radius: 10px; border: 1px solid var(--border-light); cursor: pointer; transition: all 0.15s;
}
.log-item:hover { background: var(--bg-hover); }
.log-item.selected { border-color: var(--accent-primary); background: rgba(99, 102, 241, 0.08); }
.log-time { font-size: 0.82rem; color: var(--text-secondary); flex: 1; }
.log-dur { font-size: 0.8rem; color: var(--text-muted); }
.log-cond { font-size: 0.75rem; color: var(--text-muted); }

.detail-meta { display: flex; justify-content: space-between; font-size: 0.8rem; color: var(--text-secondary); margin-bottom: 10px; flex-wrap: wrap; gap: 8px; }
.detail-error { padding: 8px 12px; border-radius: 8px; background: rgba(239, 68, 68, 0.1); color: #ef4444; font-size: 0.8rem; margin-bottom: 10px; }
.detail-commands { display: flex; flex-direction: column; gap: 8px; }
.detail-cmd { border: 1px solid var(--border-light); border-radius: 10px; padding: 8px 12px; }
.detail-cmd-head { display: flex; align-items: center; gap: 10px; }
.cmd-text { font-family: monospace; font-size: 0.82rem; color: var(--text-primary); flex: 1; word-break: break-all; }
.cmd-output { margin-top: 6px; padding: 6px 10px; border-radius: 6px; background: var(--bg-tertiary); font-family: monospace; font-size: 0.78rem; color: var(--text-secondary); white-space: pre-wrap; word-break: break-all; }
.cmd-error { margin-top: 6px; color: #ef4444; font-size: 0.78rem; }
</style>

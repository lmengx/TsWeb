<template>
  <div class="file-manager">
    <div class="page-header">
      <h2>📁 文件管理</h2>
      <span class="path-badge">根目录: TShock 程序目录</span>
    </div>

    <!-- ═══ 工具栏 + 视图切换 + 面包屑 ═══ -->
    <div class="toolbar glass">
      <div class="view-tabs">
        <button class="view-tab" :class="{ active: viewMode === 'browse' }" @click="switchView('browse')">浏览文件</button>
        <button class="view-tab" :class="{ active: viewMode === 'saved' }" @click="switchView('saved')">已保存 💾</button>
      </div>

      <nav v-if="viewMode === 'browse'" class="breadcrumb">
        <span class="crumb-link" :class="{ active: currentPath === '' }" @click="goTo('')">/</span>
        <template v-for="(seg, i) in pathSegments" :key="i">
          <span class="crumb-sep">/</span>
          <span class="crumb-link" :class="{ active: i === pathSegments.length - 1 }"
            @click="goTo(pathSegments.slice(0, i + 1).join('/'))">{{ seg }}</span>
        </template>
      </nav>
      <span v-else class="breadcrumb saved-hint">已保存到后端服务器的文件（data/transfer）</span>

      <div class="toolbar-actions">
        <template v-if="viewMode === 'browse'">
          <button class="btn btn-upload" @click="triggerUpload" :disabled="uploadingCount > 0">⬆ 上传</button>
        </template>
        <button class="btn btn-ghost" @click="viewMode === 'browse' ? loadDir() : loadSaved()" :disabled="loading">
          <span class="spin" :class="{ spinning: loading }">🔄</span> 刷新
        </button>
      </div>
    </div>

    <!-- ═══ 浏览视图：文件列表 ═══ -->
    <div v-if="viewMode === 'browse'" class="file-list glass">
      <div class="file-row file-row-header">
        <span class="col-name">名称</span>
        <span class="col-size">大小</span>
        <span class="col-actions">操作</span>
      </div>

      <Loading v-if="loading" size="sm" text="加载中..." />
      <div v-else-if="entries.length === 0" class="list-status">空目录</div>

      <div v-for="e in entries" :key="e.type + e.name" class="file-row"
        :class="{ isdir: e.type === 'dir' }"
        @click="e.type === 'dir' ? enterDir(e.name) : previewFile(e)">
        <span class="col-name">
          <span class="file-icon">{{ e.type === 'dir' ? '📁' : fileIcon(e.name) }}</span>
          <span class="file-name" :title="e.name">{{ e.name }}</span>
        </span>
        <span class="col-size">{{ e.type === 'dir' ? '—' : formatSize(e.size) }}</span>
        <span class="col-actions">
          <template v-if="e.type === 'file'">
            <button class="act-btn" title="预览/编辑" :disabled="!isTextFile(e.name)"
              @click.stop="previewFile(e)">👁</button>
            <button class="act-btn" title="直接下载到本地" @click.stop="startDownload(e)">⬇</button>
            <button class="act-btn save" title="保存到后端服务器" @click.stop="startSave(e)">💾</button>
            <button class="act-btn danger" title="删除" @click.stop="confirmDelete(e)">🗑</button>
          </template>
          <span v-else class="act-hint">进入</span>
        </span>
      </div>
    </div>

    <!-- ═══ 已保存视图 ═══ -->
    <div v-else class="file-list glass">
      <div class="file-row file-row-header">
        <span class="col-name">名称</span>
        <span class="col-size">大小</span>
        <span class="col-size">保存时间</span>
        <span class="col-actions">操作</span>
      </div>

      <Loading v-if="loading" size="sm" text="加载中..." />
      <div v-else-if="savedFiles.length === 0" class="list-status">暂无已保存的文件</div>

      <div v-for="f in savedFiles" :key="f.name" class="file-row">
        <span class="col-name">
          <span class="file-icon">📄</span>
          <span class="file-name" :title="f.name">{{ f.name }}</span>
        </span>
        <span class="col-size">{{ formatSize(f.size) }}</span>
        <span class="col-size">{{ formatTime(f.mtime) }}</span>
        <span class="col-actions">
          <button class="act-btn" title="下载" @click.stop="downloadSaved(f)">⬇</button>
          <button class="act-btn danger" title="删除" @click.stop="confirmDeleteSaved(f)">🗑</button>
        </span>
      </div>
    </div>

    <!-- ═══ 传输任务（上传/下载/保存进度） ═══ -->
    <div v-if="activeTransfers.length > 0" class="transfer-panel glass">
      <div class="transfer-title">传输任务</div>
      <div v-for="t in activeTransfers" :key="t.id" class="transfer-item">
        <div class="transfer-info">
          <span class="transfer-icon">{{ t.dir === 'up' ? '⬆' : t.dir === 'save' ? '💾' : '⬇' }}</span>
          <span class="transfer-name" :title="t.name">{{ t.name }}</span>
          <span class="transfer-status" :class="t.status">{{ statusText(t) }}</span>
        </div>
        <div class="progress-track">
          <div class="progress-fill" :class="{ indeterminate: t.dir === 'save' && t.status === 'running' }"
            :style="{ width: t.percent + '%' }"></div>
        </div>
        <div class="transfer-sub" v-if="t.status === 'running'">
          <template v-if="t.dir === 'save'">保存到后端中（SSE 拉取 → 落盘）...</template>
          <template v-else>{{ formatSize(t.received) }} / {{ formatSize(t.total) }} ({{ t.percent }}%)</template>
        </div>
        <div class="transfer-sub done-sub" v-else-if="t.status === 'done'">
          ✓ {{ t.dir === 'save' ? '已保存到后端' : '完成' }}（{{ formatSize(t.size) }}）
        </div>
        <div class="transfer-sub" v-else-if="t.status === 'error'">{{ t.error }}</div>
      </div>
    </div>

    <!-- ═══ 预览弹层 ═══ -->
    <Teleport to="body">
      <div v-if="preview.visible" class="preview-overlay" @click.self="closePreview">
        <div class="preview-dialog">
          <div class="preview-header">
            <span class="preview-name" :title="preview.path">{{ preview.name }}</span>
            <span class="preview-path">{{ preview.path }}</span>
            <button class="preview-close" @click="closePreview">✕</button>
          </div>
          <div class="preview-body">
            <Loading v-if="preview.loading" size="sm" text="加载中..." />
            <div v-else-if="preview.error" class="preview-status error">{{ preview.error }}</div>
            <textarea v-else v-model="preview.content" spellcheck="false" class="preview-editor"></textarea>
          </div>
          <div class="preview-footer" v-if="!preview.loading && !preview.error">
            <span class="preview-save-msg" :class="preview.saveStatus">{{ preview.saveMsg }}</span>
            <button class="btn btn-primary" :disabled="preview.saving" @click="savePreview">
              {{ preview.saving ? '保存中...' : '保存' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <input ref="fileInput" type="file" multiple hidden @change="onFilesSelected" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import {
  listDir, readFile, writeFile, deleteFile,
  uploadFile, downloadFile, saveBlob, saveToBackend,
  listSavedFiles, downloadSavedFile, deleteSavedFile,
  isTextFile, formatSize
} from '../../utils/fileApi.js'
import Loading from '../../components/Loading.vue'

// ═══ 视图切换 ═══
const viewMode = ref('browse')
const switchView = (mode) => {
  viewMode.value = mode
  loading.value = false
  if (mode === 'saved') loadSaved()
  else loadDir()
}

// ═══ 目录导航 ═══
const entries = ref([])
const loading = ref(false)
const currentPath = ref('')

const pathSegments = computed(() => currentPath.value ? currentPath.value.split('/') : [])

const loadDir = async () => {
  loading.value = true
  try {
    const result = await listDir(currentPath.value)
    const list = (result.entries || []).map(e => ({ ...e }))
    list.sort((a, b) => {
      if (a.type !== b.type) return a.type === 'dir' ? -1 : 1
      return a.name.localeCompare(b.name)
    })
    entries.value = list
  } catch (e) {
    alert('加载目录失败: ' + e.message)
  } finally {
    loading.value = false
  }
}

const enterDir = (name) => {
  currentPath.value = currentPath.value ? `${currentPath.value}/${name}` : name
  loadDir()
}

const goTo = (path) => {
  currentPath.value = path
  loadDir()
}

const join = (name) => currentPath.value ? `${currentPath.value}/${name}` : name

// ═══ 上传 ═══
const fileInput = ref(null)
const uploads = ref([])
const uploadingCount = computed(() => uploads.value.filter(t => t.status === 'running').length)
const activeTransfers = computed(() => {
  const up = uploads.value.map(t => ({ ...t, dir: 'up' }))
  const down = downloads.value.map(t => ({ ...t, dir: 'down' }))
  const saves = saveTasks.value.map(t => ({ ...t, dir: 'save' }))
  return [...up, ...down, ...saves].filter(t => t.status !== 'done' || t.justDone)
})

const triggerUpload = () => fileInput.value?.click()

const onFilesSelected = (e) => {
  const files = [...(e.target.files || [])]
  e.target.value = ''
  if (!files.length) return
  files.forEach(f => {
    const task = {
      id: `up-${Date.now()}-${Math.random().toString(36).slice(2)}`,
      name: f.name,
      path: join(f.name),
      size: f.size,
      received: 0,
      percent: 0,
      status: 'running',
      justDone: true
    }
    uploads.value.push(task)
    runUpload(task, f)
  })
}

const runUpload = async (task, file) => {
  try {
    await uploadFile(currentPath.value, file, ({ sent, total }) => {
      task.received = sent
      task.total = total
      task.percent = total > 0 ? Math.round((sent / total) * 100) : 0
    })
    task.status = 'done'
    task.percent = 100
    setTimeout(() => {
      const i = uploads.value.indexOf(task)
      if (i >= 0) uploads.value.splice(i, 1)
    }, 3000)
    loadDir()
  } catch (err) {
    task.status = 'error'
    task.error = err.message
  }
}

// ═══ 直接下载（SSE 实时） ═══
const downloads = ref([])

const startDownload = async (e) => {
  const task = {
    id: `dn-${Date.now()}-${Math.random().toString(36).slice(2)}`,
    name: e.name,
    path: join(e.name),
    size: e.size || 0,
    received: 0,
    percent: 0,
    status: 'running',
    justDone: true
  }
  downloads.value.push(task)
  try {
    const { blob, name } = await downloadFile(task.path, ({ received, size, percent }) => {
      task.received = received
      task.size = size || task.size
      task.total = size || task.total
      task.percent = percent
    })
    saveBlob(blob, name)
    task.status = 'done'
    task.percent = 100
    setTimeout(() => {
      const i = downloads.value.indexOf(task)
      if (i >= 0) downloads.value.splice(i, 1)
    }, 3000)
  } catch (err) {
    task.status = 'error'
    task.error = err.message
  }
}

// ═══ 保存到后端 ═══
const saveTasks = ref([])

const startSave = async (e) => {
  const task = {
    id: `sv-${Date.now()}-${Math.random().toString(36).slice(2)}`,
    name: e.name,
    path: join(e.name),
    size: e.size || 0,
    received: 0,
    percent: 30,
    status: 'running',
    justDone: true
  }
  saveTasks.value.push(task)
  try {
    const result = await saveToBackend(task.path)
    task.status = 'done'
    task.percent = 100
    task.size = result.size || task.size
    setTimeout(() => {
      const i = saveTasks.value.indexOf(task)
      if (i >= 0) saveTasks.value.splice(i, 1)
    }, 3000)
  } catch (err) {
    task.status = 'error'
    task.error = err.message
  }
}

// ═══ 已保存文件管理 ═══
const savedFiles = ref([])

const loadSaved = async () => {
  loading.value = true
  try {
    savedFiles.value = await listSavedFiles()
  } catch (e) {
    alert('加载已保存文件失败: ' + e.message)
  } finally {
    loading.value = false
  }
}

const downloadSaved = async (f) => {
  try {
    await downloadSavedFile(f.name)
  } catch (err) {
    alert('下载失败: ' + err.message)
  }
}

const confirmDeleteSaved = async (f) => {
  if (!confirm(`确定要删除已保存的文件「${f.name}」吗？`)) return
  try {
    await deleteSavedFile(f.name)
    loadSaved()
  } catch (err) {
    alert('删除失败: ' + err.message)
  }
}

// ═══ 删除（浏览视图） ═══
const confirmDelete = async (e) => {
  const msg = `确定要删除文件「${e.name}」吗？\n此操作不可恢复！`
  if (!confirm(msg)) return
  try {
    await deleteFile(join(e.name))
    alert('删除成功')
    loadDir()
  } catch (err) {
    alert('删除失败: ' + err.message)
  }
}

// ═══ 预览 / 编辑 ═══
const preview = ref({
  visible: false,
  name: '',
  path: '',
  content: '',
  loading: false,
  saving: false,
  error: '',
  saveMsg: '',
  saveStatus: ''
})

const fileIcon = (name) => {
  const ext = name.split('.').pop()?.toLowerCase()
  if (['png', 'jpg', 'jpeg', 'gif', 'webp', 'ico', 'bmp'].includes(ext)) return '🖼'
  if (['zip', 'rar', '7z', 'tar', 'gz'].includes(ext)) return '🗜'
  if (['wld', 'twld', 'bak'].includes(ext)) return '🗺'
  if (['sqlite', 'db'].includes(ext)) return '🗄'
  if (['dll', 'exe'].includes(ext)) return '⚙'
  return '📄'
}

const previewFile = async (e) => {
  if (!isTextFile(e.name)) {
    alert('仅支持文本文件预览（txt/log/json/yml 等）')
    return
  }
  const path = join(e.name)
  preview.value.visible = true
  preview.value.name = e.name
  preview.value.path = path
  preview.value.content = ''
  preview.value.loading = true
  preview.value.error = ''
  preview.value.saveMsg = ''
  try {
    const result = await readFile(path)
    if (result.error && result.content === undefined) throw new Error(result.error)
    preview.value.content = result.content ?? ''
  } catch (err) {
    preview.value.error = err.message
  } finally {
    preview.value.loading = false
  }
}

const savePreview = async () => {
  preview.value.saving = true
  preview.value.saveMsg = ''
  try {
    await writeFile(preview.value.path, preview.value.content)
    preview.value.saveMsg = '✓ 保存成功'
    preview.value.saveStatus = 'ok'
  } catch (err) {
    preview.value.saveMsg = '✗ 保存失败: ' + err.message
    preview.value.saveStatus = 'err'
  } finally {
    preview.value.saving = false
  }
}

const closePreview = () => {
  if (preview.value.saving) return
  preview.value.visible = false
}

// ═══ 辅助 ═══
const statusText = (t) => {
  if (t.status === 'done') return '完成'
  if (t.status === 'error') return '失败'
  return '传输中'
}

const formatTime = (ms) => {
  if (!ms) return '-'
  const d = new Date(ms)
  const p = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}

onMounted(loadDir)
</script>

<style scoped>
.file-manager {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 0 20px 20px;
  gap: 14px;
  min-height: 0;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-top: 4px;
  flex-shrink: 0;
}

.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.4rem;
  font-weight: 600;
}

.path-badge {
  font-size: 0.78rem;
  color: var(--text-muted);
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
  padding: 4px 10px;
  border-radius: 20px;
}

/* ── 工具栏 ── */
.toolbar {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 10px 14px;
  border-radius: var(--radius-lg, 12px);
  border: 1px solid var(--border-light);
  background: var(--bg-card);
  flex-shrink: 0;
  flex-wrap: wrap;
}

.view-tabs {
  display: flex;
  gap: 4px;
  padding: 3px;
  background: var(--bg-tertiary);
  border-radius: 10px;
  flex-shrink: 0;
}

.view-tab {
  border: none;
  background: transparent;
  color: var(--text-secondary);
  padding: 6px 14px;
  border-radius: 8px;
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.view-tab.active {
  background: var(--bg-card);
  color: var(--accent-primary);
  box-shadow: 0 1px 6px rgba(0, 0, 0, 0.15);
}

.breadcrumb {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  font-size: 0.9rem;
  min-width: 0;
  flex: 1;
}

.crumb-link {
  color: var(--accent-primary);
  cursor: pointer;
  padding: 2px 6px;
  border-radius: 6px;
  transition: background 0.15s;
  white-space: nowrap;
}

.crumb-link:hover { background: var(--bg-hover); }
.crumb-link.active { color: var(--text-primary); font-weight: 600; cursor: default; }
.crumb-sep { color: var(--text-muted); }

.saved-hint { color: var(--text-muted); font-size: 0.85rem; }

.toolbar-actions { display: flex; gap: 8px; flex-shrink: 0; }

.btn {
  border: none;
  border-radius: 10px;
  padding: 8px 14px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  color: var(--text-primary);
}

.btn:disabled { opacity: 0.5; cursor: not-allowed; }

.btn-upload {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: #fff;
  box-shadow: 0 2px 10px rgba(99, 102, 241, 0.25);
}

.btn-upload:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 14px rgba(99, 102, 241, 0.35); }

.btn-ghost {
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
}

.btn-ghost:hover:not(:disabled) { background: var(--bg-hover); }

.spin { display: inline-block; }
.spin.spinning { animation: spin 0.8s linear infinite; }

/* ── 文件列表 ── */
.file-list {
  flex: 1;
  overflow-y: auto;
  border-radius: var(--radius-lg, 12px);
  border: 1px solid var(--border-light);
  background: var(--bg-card);
  min-height: 0;
}

.file-row {
  display: flex;
  align-items: center;
  padding: 9px 14px;
  gap: 12px;
  border-bottom: 1px solid var(--border-light);
  cursor: pointer;
  transition: background 0.15s;
}

.file-row:hover { background: var(--bg-hover); }
.file-row.isdir .file-name { font-weight: 600; }

.file-row-header {
  cursor: default;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  position: sticky;
  top: 0;
  background: var(--bg-card);
  z-index: 2;
}

.col-name { flex: 1; display: flex; align-items: center; gap: 8px; min-width: 0; }
.col-size { width: 110px; flex-shrink: 0; font-size: 0.8rem; color: var(--text-muted); }
.col-actions { width: 150px; flex-shrink: 0; display: flex; gap: 4px; justify-content: flex-end; }

.file-icon { font-size: 1rem; flex-shrink: 0; }
.file-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.act-btn {
  width: 30px;
  height: 30px;
  border-radius: 8px;
  border: 1px solid var(--border-light);
  background: var(--bg-tertiary);
  cursor: pointer;
  font-size: 0.85rem;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s;
}

.act-btn:hover:not(:disabled) { background: var(--bg-hover); border-color: var(--accent-primary); }
.act-btn.save:hover:not(:disabled) { border-color: #10b981; background: rgba(16, 185, 129, 0.12); }
.act-btn.danger:hover:not(:disabled) { border-color: #ef4444; background: rgba(239, 68, 68, 0.12); }
.act-btn:disabled { opacity: 0.35; cursor: not-allowed; }

.act-hint { font-size: 0.72rem; color: var(--text-muted); padding-right: 8px; }

.list-status { padding: 32px; text-align: center; color: var(--text-muted); font-size: 0.9rem; }

/* ── 传输任务 ── */
.transfer-panel {
  border-radius: var(--radius-lg, 12px);
  border: 1px solid var(--border-light);
  background: var(--bg-card);
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  flex-shrink: 0;
  max-height: 180px;
  overflow-y: auto;
}

.transfer-title {
  font-size: 0.78rem;
  font-weight: 700;
  color: var(--text-muted);
  letter-spacing: 0.5px;
}

.transfer-item { display: flex; flex-direction: column; gap: 4px; }

.transfer-info { display: flex; align-items: center; gap: 8px; font-size: 0.82rem; }
.transfer-name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.transfer-status { font-size: 0.72rem; }
.transfer-status.running { color: var(--accent-primary); }
.transfer-status.done { color: #22c55e; }
.transfer-status.error { color: #ef4444; }

.progress-track {
  height: 6px;
  border-radius: 4px;
  background: var(--bg-tertiary);
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  border-radius: 4px;
  background: linear-gradient(90deg, var(--accent-primary), #4f46e5);
  transition: width 0.2s;
}

.progress-fill.indeterminate {
  width: 40% !important;
  animation: slide 1.2s ease-in-out infinite;
}

@keyframes slide {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(250%); }
}

.transfer-sub { font-size: 0.72rem; color: var(--text-muted); }
.transfer-sub.done-sub { color: #22c55e; }

/* ── 预览弹层 ── */
.preview-overlay {
  position: fixed;
  inset: 0;
  z-index: 10000;
  background: rgba(0, 0, 0, 0.55);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
}

.preview-dialog {
  width: min(900px, 100%);
  height: min(72vh, 640px);
  background: var(--bg-primary);
  border-radius: 14px;
  border: 1px solid var(--border-light);
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.4);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.preview-header {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border-light);
}

.preview-name { font-weight: 700; color: var(--text-primary); font-size: 0.95rem; }
.preview-path { flex: 1; font-size: 0.75rem; color: var(--text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.preview-close {
  width: 30px;
  height: 30px;
  border-radius: 8px;
  border: 1px solid var(--border-light);
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  cursor: pointer;
}

.preview-close:hover { background: var(--bg-hover); }

.preview-body { flex: 1; display: flex; min-height: 0; }

.preview-editor {
  flex: 1;
  width: 100%;
  border: none;
  outline: none;
  resize: none;
  padding: 14px 16px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  font-family: 'Cascadia Code', Consolas, 'Courier New', monospace;
  font-size: 0.83rem;
  line-height: 1.55;
  tab-size: 4;
}

.preview-status {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-muted);
  font-size: 0.9rem;
}
.preview-status.error { color: #ef4444; }

.preview-footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  padding: 10px 16px;
  border-top: 1px solid var(--border-light);
}

.btn-primary {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: #fff;
}

.btn-primary:hover:not(:disabled) { transform: translateY(-1px); }

.preview-save-msg { font-size: 0.78rem; margin-right: auto; }
.preview-save-msg.ok { color: #22c55e; }
.preview-save-msg.err { color: #ef4444; }

@keyframes spin { to { transform: rotate(360deg); } }
</style>

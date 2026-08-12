<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { apiRequest, post, put, del } from '../../utils/api.js'
import { getCurrentServerId, selectServer, fetchServers } from '../../utils/serverStore.js'
import ServerCard from '../../components/ServerCard.vue'
import AddServerWizard from '../../components/AddServerWizard.vue'

// ═══════════════ 状态 ═══════════════
const servers = ref([])
const loading = ref(false)
const error = ref('')
const success = ref('')

const showAddModal = ref(false)

// 编辑弹窗
const showEditModal = ref(false)
const editForm = ref({ id: '', name: '', host: '', port: 7878, apiKey: '', note: '', enabled: true })
const editSaving = ref(false)

const flash = (msg, type = 'success') => {
  if (type === 'success') { success.value = msg; error.value = '' }
  else { error.value = msg; success.value = '' }
  setTimeout(() => { success.value = ''; error.value = '' }, 3000)
}

// ═══════════════ 服务器列表 ═══════════════
const loadServers = async (silent = false) => {
  // 仅首次加载（列表为空）显示骨架屏；定时/事件刷新静默进行，避免切换时生硬闪烁
  if (!silent && servers.value.length === 0) loading.value = true
  try {
    const res = await apiRequest('/api/servers', { method: 'GET' })
    if (res.ok) {
      const data = await res.json()
      servers.value = data.servers || []
    }
  } catch (e) { flash('加载服务器失败: ' + e.message, 'error') }
  finally { loading.value = false }
}

const currentServerId = computed(() => getCurrentServerId())
const currentServerName = computed(() => {
  const s = servers.value.find(x => x.id === currentServerId.value)
  return s ? (s.name || s.host) : '未选择'
})
const onlineCount = computed(() => servers.value.filter(s => s.connected).length)

const switchCurrent = (id) => {
  if (id === getCurrentServerId()) return
  // 纯本地切换：selectServer 已更新响应式状态（徽标/统计联动），无需重拉服务器列表。
  // 不弹顶部 flash 提示——v-if 插入/移除元素会推动整个页面上下跳动，造成顿挫；
  // 徽标/统计条/侧边栏的即时联动本身就是足够的切换反馈。
  selectServer(id)
  // 通知侧边栏等全局组件同步「当前服务器」徽标
  window.dispatchEvent(new CustomEvent('server-changed', { detail: { serverId: id } }))
}

const testServer = async (id) => {
  try {
    const res = await post(`/api/servers/${id}/test`, {})
    const data = await res.json()
    if (data.success) flash(`连接成功（${servers.value.find(s => s.id === id)?.name || ''}）`)
    else flash(`连接失败: ${data.error || '未知错误'}`, 'error')
    await loadServers()
  } catch (e) { flash('测试失败: ' + e.message, 'error') }
}

const removeServer = async (s) => {
  if (!confirm(`确定删除服务器「${s.name || s.host}」？此操作不可撤销。`)) return
  try {
    const res = await del(`/api/servers/${s.id}`)
    if (res.ok) {
      flash('服务器已删除')
      await loadServers()
      await fetchServers()
    } else {
      const data = await res.json().catch(() => ({}))
      flash(data.error || '删除失败', 'error')
    }
  } catch (e) { flash('删除失败: ' + e.message, 'error') }
}

// ═══════════════ 同步开关（卡片上直接切换保存） ═══════════════
const toggleSync = async (s, field, value) => {
  // 先乐观更新本地回显，接口失败再回滚
  const prev = { ...s }
  s[field] = value
  try {
    const res = await put(`/api/servers/${s.id}`, { [field]: value })
    if (res.ok) {
      flash(`已${value ? '开启' : '关闭'}「${field === 'syncQQAccounts' ? '同步QQ注册' : '上传与接收uuid'}」`)
      await loadServers(true)
      await fetchServers()
    } else {
      Object.assign(s, prev)
      const data = await res.json().catch(() => ({}))
      flash(data.error || '保存失败', 'error')
    }
  } catch (e) {
    Object.assign(s, prev)
    flash('保存失败: ' + e.message, 'error')
  }
}

// ═══════════════ 编辑 ═══════════════
const openEdit = (s) => {
  editForm.value = {
    id: s.id, name: s.name, host: s.host, port: s.port,
    apiKey: '', note: s.note || '', enabled: s.enabled !== false
  }
  showEditModal.value = true
}

const saveEdit = async () => {
  if (!editForm.value.host || !editForm.value.port) {
    return flash('host 和 port 为必填', 'error')
  }
  editSaving.value = true
  try {
    const body = {
      name: editForm.value.name,
      host: editForm.value.host,
      port: editForm.value.port,
      note: editForm.value.note,
      enabled: editForm.value.enabled
    }
    if (editForm.value.apiKey) body.apiKey = editForm.value.apiKey
    const res = await put(`/api/servers/${editForm.value.id}`, body)
    if (res.ok) {
      flash('服务器已更新')
      showEditModal.value = false
      await loadServers()
      await fetchServers()
    } else {
      const data = await res.json().catch(() => ({}))
      flash(data.error || '保存失败', 'error')
    }
  } catch (e) { flash('保存失败: ' + e.message, 'error') }
  finally { editSaving.value = false }
}

// ═══════════════ 添加向导回调 ═══════════════
const handleAdded = async () => {
  await loadServers()
}

// 定时刷新（静默）：保持卡片在线状态实时同步；切换服务器为纯本地操作，无需重拉列表
let statusTimer = null
const refreshServers = () => { loadServers(true) }

onMounted(() => {
  loadServers()
  statusTimer = setInterval(refreshServers, 15000)
})

onUnmounted(() => {
  if (statusTimer) clearInterval(statusTimer)
})
</script>

<template>
  <div class="servers-content">
    <div class="section-header">
      <h2>服务器管理</h2>
    </div>

    <div v-if="success" class="flash success">{{ success }}</div>
    <div v-if="error" class="flash error">{{ error }}</div>

    <!-- 统计条 -->
      <div class="stats-bar">
        <div class="stat-item">
          <span class="stat-num">{{ servers.length }}</span>
          <span class="stat-label">服务器总数</span>
        </div>
        <div class="stat-item">
          <span class="stat-num online">{{ onlineCount }}</span>
          <span class="stat-label">在线中</span>
        </div>
        <div class="stat-item">
          <span class="stat-num current">{{ currentServerName }}</span>
          <span class="stat-label">当前服务器</span>
        </div>
      </div>

      <div class="server-toolbar">
        <button class="add-btn" @click="showAddModal = true">＋ 添加服务器</button>
      </div>

      <!-- 加载骨架 -->
      <div v-if="loading" class="server-grid">
        <div v-for="i in 3" :key="i" class="skeleton-card">
          <div class="sk-line w60"></div>
          <div class="sk-line w80"></div>
          <div class="sk-line w50"></div>
        </div>
      </div>

      <!-- 空状态 -->
      <div v-else-if="servers.length === 0" class="empty-state">
        <div class="empty-icon">
          <svg width="56" height="56" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="2" y="4" width="20" height="14" rx="2" ry="2"/>
            <path d="M8 21h8"/><path d="M12 17v4"/>
            <path d="M7 9h10" stroke-width="2"/>
          </svg>
        </div>
        <h3>尚未添加任何服务器</h3>
        <p>点击「添加服务器」，通过自动扫描或手动输入，将你的 TShock 服务器接入管理面板</p>
        <button class="add-btn" @click="showAddModal = true">＋ 添加第一个服务器</button>
      </div>

      <!-- 卡片列表 -->
      <div v-else class="server-grid">
        <ServerCard
          v-for="s in servers" :key="s.id"
          :server="s"
          :is-current="s.id === currentServerId"
          @switch-current="switchCurrent"
          @test="testServer"
          @edit="openEdit"
          @remove="removeServer"
          @toggle-sync="toggleSync"
        />
      </div>
  </div>

  <!-- ══════════ 添加服务器向导 ══════════ -->
  <AddServerWizard :show="showAddModal" @close="showAddModal = false" @added="handleAdded" />

  <!-- ══════════ 编辑服务器弹窗 ══════════ -->
  <div v-if="showEditModal" class="modal-mask" @click.self="showEditModal = false">
    <div class="modal">
      <div class="modal-head">
        <h3>编辑服务器</h3>
        <button class="close-btn" @click="showEditModal = false">✕</button>
      </div>
      <div class="modal-body">
        <div class="form-row">
          <label>服务器名称</label>
          <input v-model="editForm.name" />
        </div>
        <div class="form-row">
          <label>地址</label>
          <input v-model="editForm.host" />
        </div>
        <div class="form-row">
          <label>REST 端口</label>
          <input v-model.number="editForm.port" type="number" min="1" max="65535" />
        </div>
        <div class="form-row">
          <label>API Key（留空保持不变）</label>
          <input v-model="editForm.apiKey" placeholder="留空则保持不变" />
        </div>
        <div class="form-row">
          <label>备注</label>
          <input v-model="editForm.note" />
        </div>
        <div class="form-row">
          <label>启用</label>
          <select v-model="editForm.enabled">
            <option :value="true">启用</option>
            <option :value="false">停用</option>
          </select>
        </div>
        <div class="modal-actions">
          <button class="mini-btn" @click="showEditModal = false">取消</button>
          <button class="save-btn" :disabled="editSaving" @click="saveEdit">{{ editSaving ? '保存中...' : '保存' }}</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.servers-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: auto;
  padding: 0 20px 20px;
}
.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  padding-top: 16px;
}
.section-header h2 { margin: 0; color: var(--text-primary); font-size: 1.4rem; }
.tab-switch { display: flex; gap: 6px; background: var(--bg-tertiary); border-radius: 9px; padding: 4px; }
.tab-switch button {
  border: none; background: transparent; color: var(--text-muted);
  padding: 6px 16px; border-radius: 7px; cursor: pointer; font-size: 0.9rem;
}
.tab-switch button.active { background: var(--accent-primary); color: #fff; }

.flash { padding: 10px 14px; border-radius: 8px; margin-bottom: 12px; font-size: 0.9rem; }
.flash.success { background: rgba(34,197,94,.12); color: #22c55e; }
.flash.error { background: rgba(239,68,68,.12); color: #ef4444; }

/* 统计条 */
.stats-bar {
  display: flex; gap: 12px; flex-wrap: wrap;
  margin-bottom: 16px;
}
.stat-item {
  flex: 1; min-width: 140px;
  display: flex; flex-direction: column; gap: 4px;
  background: linear-gradient(135deg, var(--bg-card), var(--bg-tertiary));
  border: 1px solid var(--border-color); border-radius: 14px;
  padding: 14px 18px;
  transition: border-color .2s ease;
}
.stat-item:hover { border-color: var(--border-light); }
.stat-num { font-size: 1.4rem; font-weight: 800; color: var(--text-primary); line-height: 1.2; }
.stat-num.online { color: #22c55e; }
.stat-num.current {
  font-size: 1.1rem; color: var(--accent-primary);
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.stat-label { font-size: .78rem; color: var(--text-muted); }

.server-toolbar { margin-bottom: 16px; }
.add-btn {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: #fff; border: none;
  padding: 10px 18px; border-radius: 9px; cursor: pointer; font-size: .92rem; font-weight: 600;
  box-shadow: 0 2px 10px rgba(99,102,241,.25);
  transition: all .2s ease;
}
.add-btn:hover { opacity: .9; transform: translateY(-1px); box-shadow: 0 4px 14px rgba(99,102,241,.3); }

.server-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 14px; }

/* 骨架 */
.skeleton-card {
  background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 14px;
  padding: 18px; display: flex; flex-direction: column; gap: 12px;
}
.sk-line { height: 14px; border-radius: 6px; background: var(--bg-tertiary); animation: pulse 1.4s ease-in-out infinite; }
.sk-line.w60 { width: 60%; } .sk-line.w80 { width: 80%; } .sk-line.w50 { width: 50%; }
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: .45; } }

/* 空状态 */
.empty-state {
  text-align: center; padding: 56px 20px;
  background: var(--bg-card); border: 1px dashed var(--border-color); border-radius: 16px;
  color: var(--text-muted);
}
.empty-icon { color: var(--text-muted); opacity: .5; margin-bottom: 16px; }
.empty-state h3 { color: var(--text-primary); margin: 0 0 8px; font-size: 1.1rem; }
.empty-state p { margin: 0 0 20px; font-size: .88rem; max-width: 420px; margin-inline: auto; line-height: 1.6; }

/* 编辑弹窗 */
.modal-mask { position: fixed; inset: 0; background: rgba(0,0,0,.55); display: flex; align-items: center; justify-content: center; z-index: 200; backdrop-filter: blur(3px); }
.modal { background: var(--bg-card); border-radius: 14px; width: 440px; max-width: 92vw; max-height: 88vh; overflow: auto; box-shadow: var(--shadow-lg); }
.modal-head { display: flex; justify-content: space-between; align-items: center; padding: 16px 20px; border-bottom: 1px solid var(--border-color); }
.modal-head h3 { margin: 0; color: var(--text-primary); }
.close-btn { background: none; border: none; color: var(--text-muted); font-size: 1.1rem; cursor: pointer; }
.modal-body { padding: 20px; }
.modal-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 18px; }
.form-row { display: flex; flex-direction: column; gap: 5px; margin-bottom: 12px; }
.form-row label { font-size: .82rem; color: var(--text-muted); }
.form-row input, .form-row select {
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 8px 10px; border-radius: 8px; font-size: .9rem;
}
.form-row input:focus, .form-row select:focus { outline: none; border-color: var(--accent-primary); }
.checkbox-row {
  display: flex; align-items: flex-start; gap: 8px;
  font-size: .85rem; color: var(--text-primary); cursor: pointer;
  padding: 4px 0; line-height: 1.5;
}
.checkbox-row input { width: 15px; height: 15px; margin-top: 2px; accent-color: var(--accent-primary); }
.save-btn {
  background: var(--accent-primary); color: #fff; border: none;
  padding: 8px 16px; border-radius: 8px; cursor: pointer; font-size: .88rem; font-weight: 600;
}
.save-btn:hover { opacity: .9; }
.save-btn:disabled { opacity: .5; cursor: not-allowed; }
.mini-btn {
  border: 1px solid var(--border-color); background: var(--bg-tertiary); color: var(--text-primary);
  padding: 5px 10px; border-radius: 7px; cursor: pointer; font-size: .8rem;
}
.mini-btn:hover { border-color: var(--accent-primary); color: var(--accent-primary); }
</style>

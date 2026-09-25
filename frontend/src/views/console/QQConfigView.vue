<script setup>
import { ref, onMounted, computed } from 'vue'
import { apiRequest } from '../../utils/api.js'
import Loading from '../../components/Loading.vue'

// ═══════════════════════════════════════════════════════════
// 状态（整个页面仅 admin 可用，路由 + 后端双重校验）
// ═══════════════════════════════════════════════════════════
const activeTab = ref('list') // 'list' | 'settings'
const loading = ref(false)
const error = ref('')
const okMsg = ref('')

const flashMsg = (msg, isErr = false) => {
  if (isErr) { okMsg.value = ''; error.value = msg }
  else { error.value = ''; okMsg.value = msg }
  setTimeout(() => { error.value = ''; okMsg.value = '' }, 3000)
}

// ── 分页1：QQ 绑定列表 ──
const list = ref([])
const total = ref(0)
const searchQ = ref('')

const filtered = computed(() => {
  const q = searchQ.value.trim().toLowerCase()
  if (!q) return list.value
  return list.value.filter(row =>
    row.username?.toLowerCase().includes(q) ||
    String(row.qq || '').includes(q)
  )
})

/** 分钟 → 小时文本 */
const fmtHours = (minutes) => {
  const m = Number(minutes) || 0
  if (m <= 0) return '0 小时'
  const h = Math.floor(m / 60)
  const rest = m % 60
  return h > 0 ? (rest > 0 ? `${h} 小时 ${rest} 分` : `${h} 小时`) : `${rest} 分`
}

const fmtTime = (ts) => {
  if (!ts) return ''
  return new Date(ts).toLocaleString('zh-CN', { hour12: false })
}

const loadList = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await apiRequest('/api/bot/qq-list', { method: 'GET' })
    if (res.ok) {
      const data = await res.json()
      list.value = data.list || []
      total.value = data.total || 0
    } else {
      const d = await res.json().catch(() => ({}))
      error.value = d.error || '加载失败'
    }
  } catch (e) { error.value = e.message } finally { loading.value = false }
}

// ── 重新获取并计算时长 ──
const refreshing = ref(false)

const refreshPlaytime = async () => {
  if (refreshing.value) return
  refreshing.value = true
  okMsg.value = ''
  error.value = ''
  try {
    const res = await apiRequest('/api/bot/playtime-refresh', { method: 'POST' })
    const d = await res.json().catch(() => ({}))
    if (res.ok) {
      flashMsg(`已从服务器重新获取并计算（成功 ${d.ok}/${d.total}）`)
      loadList()
    } else {
      flashMsg(d.error || '刷新失败', true)
    }
  } catch (e) { flashMsg(e.message, true) } finally { refreshing.value = false }
}

// ── 解绑 ──
const unbindTarget = ref(null)
const showUnbind = (row) => { unbindTarget.value = row }

const doUnbind = async () => {
  const row = unbindTarget.value
  if (!row) return
  try {
    const res = await apiRequest('/api/bot/qq-unbind', {
      method: 'POST',
      body: JSON.stringify({ username: row.username })
    })
    const d = await res.json().catch(() => ({}))
    if (res.ok) {
      flashMsg(`已解绑 ${row.username}`)
      unbindTarget.value = null
      loadList()
    } else {
      flashMsg(d.error || '解绑失败', true)
    }
  } catch (e) { flashMsg(e.message, true) }
}

// ── 改绑 QQ ──
const rebindTarget = ref(null)
const newQq = ref('')

const showRebind = (row) => {
  rebindTarget.value = row
  newQq.value = ''
}

const doRebind = async () => {
  const row = rebindTarget.value
  const qq = newQq.value.trim()
  if (!row || !qq) return
  if (!/^\d{5,15}$/.test(qq)) { flashMsg('QQ 号格式不正确', true); return }
  try {
    const res = await apiRequest('/api/bot/qq-rebind', {
      method: 'POST',
      body: JSON.stringify({ username: row.username, qq })
    })
    const d = await res.json().catch(() => ({}))
    if (res.ok) {
      flashMsg(`${row.username} 已改绑为 ${qq}`)
      rebindTarget.value = null
      loadList()
    } else {
      flashMsg(d.error || '改绑失败', true)
    }
  } catch (e) { flashMsg(e.message, true) }
}

// ── 改名（QQ 联动：改台账 + 广播各服）──
const renameTarget = ref(null)
const renameNewName = ref('')
const renameSubmitting = ref(false)

const showRename = (row) => {
  renameTarget.value = row
  renameNewName.value = ''
}

const doRename = async () => {
  const row = renameTarget.value
  const newName = renameNewName.value.trim()
  if (!row || !newName) return
  renameSubmitting.value = true
  try {
    const res = await apiRequest('/api/useradmin/rename', {
      method: 'POST',
      body: JSON.stringify({ username: row.username, newName, mode: 'qq' })
    })
    const d = await res.json().catch(() => ({}))
    if (res.ok) {
      flashMsg(`${row.username} 已改名为 ${newName}（服务器 ${d.ok}/${d.total}${d.ledger ? '，台账已联动' : ''}）`)
      renameTarget.value = null
      loadList()
    } else {
      flashMsg(d.error || '改名失败', true)
    }
  } catch (e) { flashMsg(e.message, true) }
  finally { renameSubmitting.value = false }
}
const bindModalOpen = ref(false)
const bindPlayer = ref('')
const bindQq = ref('')
const bindServerId = ref('')          // '' = 全部启用服广播查询
const onlinePlayers = ref([])         // 当前选中服务器的在线玩家（辅助选取）
const onlineLoading = ref(false)
const bindSubmitting = ref(false)

const openBindModal = () => {
  bindModalOpen.value = true
  bindPlayer.value = ''
  bindQq.value = ''
  bindServerId.value = ''
  onlinePlayers.value = []
  loadOnlinePlayers()
}

const closeBindModal = () => {
  if (bindSubmitting.value) return
  bindModalOpen.value = false
}

// 拉取当前服务器的在线玩家（用于「选取玩家」辅助；apiRequest 自动带 x-server-id）
const loadOnlinePlayers = async () => {
  if (onlineLoading.value) return
  onlineLoading.value = true
  try {
    const res = await apiRequest('/api/tshock/users?onlineOnly=true', { method: 'GET' })
    if (res.ok) {
      const data = await res.json()
      onlinePlayers.value = (data.users || []).filter(u => u && u.name)
    } else {
      onlinePlayers.value = []
    }
  } catch (e) {
    onlinePlayers.value = []
  } finally { onlineLoading.value = false }
}

const pickOnlinePlayer = (name) => {
  bindPlayer.value = name
}

const doBind = async () => {
  const player = bindPlayer.value.trim()
  const qq = bindQq.value.trim()
  if (!player) { flashMsg('请选择或输入玩家名', true); return }
  if (!qq) { flashMsg('请输入 QQ 号', true); return }
  if (!/^\d{5,15}$/.test(qq)) { flashMsg('QQ 号格式不正确', true); return }

  bindSubmitting.value = true
  try {
    const res = await apiRequest('/api/bot/qq-bind', {
      method: 'POST',
      body: JSON.stringify({
        qq,
        player,
        serverId: bindServerId.value || undefined
      })
    })
    const d = await res.json().catch(() => ({}))
    if (res.ok) {
      flashMsg(`已绑定 ${player}（QQ ${qq}）`)
      bindModalOpen.value = false
      loadList()
    } else {
      // 多服同名冲突：提示可指定服务器
      const hint = d.conflict && Array.isArray(d.servers) && d.servers.length > 0
        ? ` 冲突服务器：${d.servers.map(s => s.name).join('、')}`
        : ''
      flashMsg((d.error || '绑定失败') + hint, true)
    }
  } catch (e) { flashMsg(e.message, true) } finally { bindSubmitting.value = false }
}

// ── 分页2：QQ 机器人设置 ──
const settings = ref({ mainServerId: '', onlineMode: 'all', pollIntervalMinutes: 10 })
const serverOptions = ref([])
const saving = ref(false)

const loadSettings = async () => {
  try {
    const res = await apiRequest('/api/bot/settings', { method: 'GET' })
    if (res.ok) {
      const data = await res.json()
      settings.value = data.bot || settings.value
      serverOptions.value = (data.servers || []).filter(s => s.enabled !== false)
    }
  } catch (e) { flashMsg(e.message, true) }
}

const saveSettings = async () => {
  saving.value = true
  try {
    const res = await apiRequest('/api/bot/settings', {
      method: 'POST',
      body: JSON.stringify({
        mainServerId: settings.value.mainServerId,
        onlineMode: settings.value.onlineMode,
        pollIntervalMinutes: Number(settings.value.pollIntervalMinutes) || 10
      })
    })
    const d = await res.json().catch(() => ({}))
    if (res.ok) {
      flashMsg('设置已保存')
      settings.value = d.bot || settings.value
    } else {
      flashMsg(d.error || '保存失败', true)
    }
  } catch (e) { flashMsg(e.message, true) } finally { saving.value = false }
}

onMounted(() => {
  loadList()
  loadSettings()
})
</script>

<template>
  <div class="qq-config">
    <!-- ═══ 顶部 Tab ═══ -->
    <div class="tab-bar">
      <button class="tab-btn" :class="{ active: activeTab === 'list' }" @click="activeTab = 'list'">
        QQ 绑定列表
      </button>
      <button class="tab-btn" :class="{ active: activeTab === 'settings' }" @click="activeTab = 'settings'">
        QQ 机器人设置
      </button>
    </div>

    <div v-if="error" class="msg-box error">{{ error }}</div>
    <div v-if="okMsg" class="msg-box success">{{ okMsg }}</div>

    <!-- ═══ 分页1：QQ 绑定列表 ═══ -->
    <div v-show="activeTab === 'list'" class="panel">
      <div class="panel-head">
        <h3>QQ 绑定列表（{{ total }}）</h3>
        <div class="head-actions">
          <button class="btn primary" @click="openBindModal">手动绑定</button>
          <button class="btn" :disabled="refreshing" @click="refreshPlaytime">
            {{ refreshing ? '聚合中...' : '重新获取并计算' }}
          </button>
          <div class="search-box">
            <input v-model="searchQ" placeholder="搜索玩家名 / QQ" />
          </div>
        </div>
      </div>

      <Loading v-if="loading" text="加载中..." />

      <table v-else class="data-table">
        <thead>
          <tr>
            <th>玩家名称</th>
            <th>QQ</th>
            <th>多服游玩时长</th>
            <th>更新时间</th>
            <th class="col-op">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in filtered" :key="row.username">
            <td>{{ row.username }}</td>
            <td>{{ row.qq }}</td>
            <td>{{ fmtHours(row.playtime?.total) }}</td>
            <td class="muted">{{ fmtTime(row.updatedAt) }}</td>
            <td class="col-op">
              <button class="btn small" @click="showRename(row)">改名</button>
              <button class="btn small" @click="showRebind(row)">改绑QQ</button>
              <button class="btn small danger" @click="showUnbind(row)">解绑</button>
            </td>
          </tr>
          <tr v-if="!loading && filtered.length === 0">
            <td colspan="5" class="empty">暂无绑定记录</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- ═══ 分页2：QQ 机器人设置 ═══ -->
    <div v-show="activeTab === 'settings'" class="panel">
      <div class="settings-form">
        <h3>QQ 机器人设置</h3>

        <div class="form-row">
          <label>主服务器</label>
          <select v-model="settings.mainServerId">
            <option value="">（未设置，使用第一个启用服）</option>
            <option v-for="s in serverOptions" :key="s.id" :value="s.id">{{ s.name }}</option>
          </select>
          <p class="hint">机器人「进度」默认查询该服；「在线」方式2 显示该服完整玩家列表。</p>
        </div>

        <div class="form-row">
          <label>在线查询方式</label>
          <div class="radio-group">
            <label class="radio-item">
              <input type="radio" value="all" v-model="settings.onlineMode" />
              方式1：同时显示所有服务器的在线状态
            </label>
            <label class="radio-item">
              <input type="radio" value="main" v-model="settings.onlineMode" />
              方式2：主服显示完整列表，其它服用服务器名指代
            </label>
          </div>
        </div>

        <div class="form-row">
          <label>时长聚合间隔（分钟）</label>
          <input type="number" min="1" max="1440" v-model.number="settings.pollIntervalMinutes" />
          <p class="hint">后端定时向所有服务器拉取累计游玩时长并落盘，用于绑定列表与「我的信息」。</p>
        </div>

        <div class="form-actions">
          <button class="btn primary" :disabled="saving" @click="saveSettings">
            {{ saving ? '保存中...' : '保存设置' }}
          </button>
        </div>
      </div>
    </div>

    <!-- ═══ 解绑确认模态 ═══ -->
    <div v-if="unbindTarget" class="modal-overlay" @click.self="unbindTarget = null">
      <div class="modal">
        <h3>确认解绑</h3>
        <p>将解除「{{ unbindTarget.username }}」（QQ {{ unbindTarget.qq }}）的绑定关系。各服务器本地账号保留、密码不变，仅移除 QQ 关联。</p>
        <div class="modal-actions">
          <button class="btn" @click="unbindTarget = null">取消</button>
          <button class="btn danger" @click="doUnbind">确认解绑</button>
        </div>
      </div>
    </div>

    <!-- ═══ 改绑 QQ 模态 ═══ -->
    <div v-if="rebindTarget" class="modal-overlay" @click.self="rebindTarget = null">
      <div class="modal">
        <h3>改绑 QQ</h3>
        <p>玩家「{{ rebindTarget.username }}」当前绑定 QQ：{{ rebindTarget.qq }}</p>
        <input v-model="newQq" placeholder="输入新的 QQ 号" class="modal-input" />
        <div class="modal-actions">
          <button class="btn" @click="rebindTarget = null">取消</button>
          <button class="btn primary" @click="doRebind">确认改绑</button>
        </div>
      </div>
    </div>

    <!-- ═══ 改名模态（QQ 联动：改台账 + 广播各服）═══ -->
    <div v-if="renameTarget" class="modal-overlay" @click.self="renameTarget = null">
      <div class="modal">
        <h3>账号改名（QQ 联动）</h3>
        <p>玩家「{{ renameTarget.username }}」（QQ：{{ renameTarget.qq }}）将改名为：</p>
        <input v-model="renameNewName" placeholder="输入新用户名（1-32 字符）" class="modal-input" />
        <p class="modal-hint">改名将同步后端台账、游玩时长、各服务器账号名及 QQ 绑定关系。</p>
        <div class="modal-actions">
          <button class="btn" @click="renameTarget = null">取消</button>
          <button class="btn primary" :disabled="renameSubmitting" @click="doRename">
            {{ renameSubmitting ? '改名中...' : '确认改名' }}
          </button>
        </div>
      </div>
    </div>

    <!-- ═══ 管理员手动绑定模态 ═══ -->
    <div v-if="bindModalOpen" class="modal-overlay" @click.self="closeBindModal">
      <div class="modal modal-bind">
        <h3>手动绑定 QQ</h3>
        <p class="modal-desc">选取一名玩家并输入 QQ 号，将其与角色绑定（写入台账并同步到各启用服，角色在线时绑定即全服免密）。</p>

        <div class="form-row">
          <label>玩家名</label>
          <input v-model="bindPlayer" placeholder="输入或从下方在线玩家中选择" class="modal-input" />
        </div>

        <div class="online-pick">
          <div class="online-pick-head">
            <span>在线玩家（{{ onlinePlayers.length }}）</span>
            <button class="btn small" :disabled="onlineLoading" @click="loadOnlinePlayers">
              {{ onlineLoading ? '加载中...' : '刷新' }}
            </button>
          </div>
          <div v-if="onlineLoading" class="online-pick-empty">加载中...</div>
          <div v-else-if="onlinePlayers.length === 0" class="online-pick-empty">
            当前服务器无在线玩家（或未选中服务器）
          </div>
          <div v-else class="online-pick-list">
            <button
              v-for="p in onlinePlayers"
              :key="p.name"
              class="online-pick-item"
              :class="{ active: bindPlayer.trim() === p.name }"
              @click="pickOnlinePlayer(p.name)"
            >{{ p.name }}</button>
          </div>
        </div>

        <div class="form-row">
          <label>QQ 号</label>
          <input v-model="bindQq" placeholder="输入要绑定的 QQ 号" class="modal-input" />
        </div>

        <div class="form-row">
          <label>指定服务器（可选）</label>
          <select v-model="bindServerId" class="modal-input">
            <option value="">（全部启用服务器，自动查找角色）</option>
            <option v-for="s in serverOptions" :key="s.id" :value="s.id">{{ s.name }}</option>
          </select>
          <p class="hint">角色在多个服务器存在同名账号时需指定服务器；单服环境留空即可。</p>
        </div>

        <div class="modal-actions">
          <button class="btn" :disabled="bindSubmitting" @click="closeBindModal">取消</button>
          <button class="btn primary" :disabled="bindSubmitting" @click="doBind">
            {{ bindSubmitting ? '绑定中...' : '确认绑定' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.qq-config { max-width: 900px; }

.tab-bar {
  display: flex; gap: 8px;
  margin-bottom: 16px;
  border-bottom: 1px solid var(--border-light);
  padding-bottom: 8px;
}
.tab-btn {
  padding: 8px 18px;
  border: 1px solid var(--border-light);
  border-radius: 10px;
  background: var(--bg-card);
  color: var(--text-secondary);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 600;
  transition: all .2s;
}
.tab-btn:hover { border-color: var(--accent-primary); }
.tab-btn.active {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: #fff;
  border-color: transparent;
}

.msg-box {
  padding: 10px 14px;
  border-radius: 10px;
  margin-bottom: 14px;
  font-size: .88rem;
}
.msg-box.error { background: rgba(239,68,68,.12); color: #ef4444; border: 1px solid rgba(239,68,68,.3); }
.msg-box.success { background: rgba(34,197,94,.12); color: #22c55e; border: 1px solid rgba(34,197,94,.3); }

.panel {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 14px;
  padding: 20px;
}
.panel-head {
  display: flex; align-items: center; justify-content: space-between;
  margin-bottom: 14px;
}
.panel-head h3 { margin: 0; font-size: 1rem; color: var(--text-primary); }
.head-actions { display: flex; align-items: center; gap: 10px; }
.head-actions .btn:disabled { opacity: .6; cursor: not-allowed; }
.search-box input {
  padding: 7px 12px;
  border: 1px solid var(--border-light);
  border-radius: 10px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  width: 220px;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: .88rem;
}
.data-table th, .data-table td {
  text-align: left;
  padding: 10px 12px;
  border-bottom: 1px solid var(--border-light);
}
.data-table th { color: var(--text-muted); font-weight: 600; font-size: .8rem; }
.data-table td { color: var(--text-primary); }
.data-table .muted { color: var(--text-muted); font-size: .8rem; }
.data-table .empty { text-align: center; color: var(--text-muted); padding: 28px; }
.col-op { white-space: nowrap; text-align: right; }
.col-op .btn { margin-left: 6px; }

.btn {
  padding: 7px 14px;
  border: 1px solid var(--border-light);
  border-radius: 10px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  cursor: pointer;
  font-size: .85rem;
  transition: all .2s;
}
.btn:hover { border-color: var(--accent-primary); }
.btn.primary {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: #fff; border-color: transparent;
}
.btn.primary:disabled { opacity: .6; cursor: not-allowed; }
.btn.danger { color: #ef4444; border-color: rgba(239,68,68,.4); }
.btn.danger:hover { background: rgba(239,68,68,.12); }
.btn.small { padding: 5px 10px; font-size: .78rem; }

.settings-form { max-width: 560px; }
.settings-form h3 { margin: 0 0 18px; font-size: 1rem; color: var(--text-primary); }
.form-row { margin-bottom: 18px; }
.form-row label {
  display: block;
  font-size: .85rem;
  font-weight: 600;
  color: var(--text-secondary);
  margin-bottom: 6px;
}
.form-row select, .form-row input[type="number"] {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border-light);
  border-radius: 10px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
}
.hint { font-size: .76rem; color: var(--text-muted); margin-top: 6px; }

.radio-group { display: flex; flex-direction: column; gap: 8px; }
.radio-item {
  display: flex; align-items: center; gap: 8px;
  font-size: .86rem; color: var(--text-primary);
  cursor: pointer;
}
.form-actions { margin-top: 20px; }

.modal-overlay {
  position: fixed; inset: 0;
  background: rgba(0,0,0,.45);
  display: flex; align-items: center; justify-content: center;
  z-index: 10000;
}
.modal {
  width: 380px; max-width: 90vw;
  background: var(--bg-primary);
  border: 1px solid var(--border-light);
  border-radius: 14px;
  padding: 22px;
  box-shadow: var(--shadow-lg);
}
.modal h3 { margin: 0 0 12px; font-size: 1rem; color: var(--text-primary); }
.modal p { font-size: .86rem; color: var(--text-secondary); margin: 0 0 14px; line-height: 1.6; }
.modal-input {
  width: 100%; padding: 8px 12px;
  border: 1px solid var(--border-light);
  border-radius: 10px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  margin-bottom: 14px;
  box-sizing: border-box;
}
.modal-actions { display: flex; justify-content: flex-end; gap: 10px; }
.modal-hint {
  font-size: .8rem; color: var(--text-muted);
  margin: 10px 0 0; line-height: 1.5;
}

/* ── 手动绑定模态 ── */
.modal-bind { width: 460px; }
.modal-desc {
  font-size: .82rem; color: var(--text-muted);
  margin: 0 0 16px; line-height: 1.6;
}
.modal-bind .form-row { margin-bottom: 14px; }
.modal-bind .form-row label {
  display: block;
  font-size: .82rem; font-weight: 600;
  color: var(--text-secondary);
  margin-bottom: 6px;
}
.modal-bind select.modal-input { width: 100%; }
.online-pick {
  margin-bottom: 14px;
  border: 1px solid var(--border-light);
  border-radius: 10px;
  padding: 10px;
  background: var(--bg-tertiary);
}
.online-pick-head {
  display: flex; align-items: center; justify-content: space-between;
  font-size: .8rem; color: var(--text-secondary);
  margin-bottom: 8px;
}
.online-pick-list {
  display: flex; flex-wrap: wrap; gap: 6px;
  max-height: 130px; overflow-y: auto;
}
.online-pick-item {
  padding: 4px 10px;
  border: 1px solid var(--border-light);
  border-radius: 8px;
  background: var(--bg-card);
  color: var(--text-primary);
  font-size: .8rem;
  cursor: pointer;
  transition: all .15s;
}
.online-pick-item:hover { border-color: var(--accent-primary); }
.online-pick-item.active {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: #fff;
  border-color: transparent;
}
.online-pick-empty {
  font-size: .8rem; color: var(--text-muted);
  padding: 8px 2px;
}
.modal-actions .btn:disabled { opacity: .6; cursor: not-allowed; }
</style>

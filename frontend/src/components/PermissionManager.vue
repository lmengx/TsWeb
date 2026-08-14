<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { get, post } from '../utils/api.js'
import { getPermissionName, searchPermissions, permissionMap } from '../utils/permissionMap.js'

// ═══════════════ 数据 ═══════════════
const loading = ref(false)
const error = ref('')
const activeTab = ref('aggregate')
const summary = ref({ players: [], permissions: [] })
const records = ref([])
const allUsers = ref([])

// ═══════════════ 聚合视图状态 ═══════════════
const selectedPlayer = ref('')        // 选中的玩家（'' = 全部）
const selectedPermission = ref('')    // 选中的权限（'' = 全部）
const playerSortDesc = ref(true)      // true = 多→少
const permSortDesc = ref(true)

// ═══════════════ 明细视图状态 ═══════════════
const sortKey = ref('createdAt')
const sortDesc = ref(true)            // 默认降序 = 签发时间从后往前
const filterPlayer = ref('')
const filterPermission = ref('')
const filterGrantedBy = ref('')
const filterStatus = ref('all')
const filterDateFrom = ref('')
const filterDateTo = ref('')
const selectedIds = ref(new Set())

// ═══════════════ 快速签发弹窗 ═══════════════
const quickModal = ref(false)
const quickData = ref({ player: '', permission: '', expireMode: 'permanent', expireDate: '', durationDays: 0, durationHours: 0, durationMinutes: 0, note: '' })
const quickSuggestions = ref([])
const quickUserSuggestions = ref([])
const quickSugStyle = ref({})

// ═══════════════ 批量签发弹窗 ═══════════════
const batchModal = ref(false)
const batchData = ref({ players: [], permissions: [], expireMode: 'permanent', expireDate: '', durationDays: 0, durationHours: 0, durationMinutes: 0, note: '' })
const batchPlayerQuery = ref('')
const batchPermQuery = ref('')
const batchPermTab = ref('normal')
const customPermInput = ref('')
const batchResult = ref(null)

// ═══════════════ 续期/改期弹窗 ═══════════════
const renewModal = ref(false)
const renewTarget = ref(null)
const renewData = ref({ expireMode: 'permanent', expireDate: '', durationDays: 0, durationHours: 0, durationMinutes: 0 })

const submitting = ref(false)

// 常用权限分组（与组管理快速添加保持一致）
const reasonablePermissionKeys = [
  'tshock.npc.hurttown', 'tshock.npc.spawnpets', 'tshock.npc.startdd2', 'tshock.npc.startinvasion',
  'tshock.npc.summonboss', 'tshock.tp.demonconch', 'tshock.tp.magicconch', 'tshock.tp.pylon',
  'tshock.tp.rod', 'tshock.tp.tppotion', 'tshock.tp.wormhole', 'tshock.world.movenpc',
  'tshock.world.time.usemoondial', 'tshock.world.time.usesundial', 'tshock.world.worldupgrades'
]
const tpPermissionKeys = ['tshock.tp.self', 'tshock.tp.block', 'tshock.tp.spawn', 'tshock.tp.home']
const allPermissionKeys = Object.keys(permissionMap)

// ═══════════════ 工具函数 ═══════════════
const isExpired = (r) => r.expireAt && new Date(r.expireAt.replace(' ', 'T')) <= new Date()

// ═══════════════ 数据加载 ═══════════════
const fetchSummary = async () => {
  const res = await get('/api/permissions/summary')
  const data = await res.json()
  if (data.error) throw new Error(data.error)
  summary.value = data
}

const fetchRecords = async () => {
  const res = await get('/api/permissions/list')
  const data = await res.json()
  if (data.error) throw new Error(data.error)
  records.value = data.items || []
}

const fetchUsers = async () => {
  try {
    const res = await get('/api/tshock/users')
    const data = await res.json()
    allUsers.value = data.users || []
  } catch { allUsers.value = [] }
}

const refreshAll = async (silent = false) => {
  if (!silent) { loading.value = true; error.value = '' }
  try {
    await Promise.all([fetchSummary(), fetchRecords()])
  } catch (e) {
    error.value = '加载失败: ' + e.message
  } finally {
    loading.value = false
  }
}

// ═══════════════ 聚合按钮云 ═══════════════
const playerButtons = computed(() => {
  const list = [...summary.value.players]
  list.sort((a, b) => playerSortDesc.value ? b.count - a.count : a.count - b.count)
  return list
})
const permButtons = computed(() => {
  const list = [...summary.value.permissions]
  list.sort((a, b) => permSortDesc.value ? b.count - a.count : a.count - b.count)
  return list
})
const togglePlayerFilter = (p) => { selectedPlayer.value = selectedPlayer.value === p ? '' : p }
const togglePermissionFilter = (p) => { selectedPermission.value = selectedPermission.value === p ? '' : p }
const clearFilters = () => { selectedPlayer.value = ''; selectedPermission.value = '' }

// ═══════════════ 明细计算（排序 + 筛选） ═══════════════
const playerCountMap = computed(() => {
  const m = {}
  for (const p of summary.value.players) m[p.player] = p.count
  return m
})
const permCountMap = computed(() => {
  const m = {}
  for (const p of summary.value.permissions) m[p.permission] = p.count
  return m
})

const filteredRecords = computed(() => {
  let list = [...records.value]

  // 聚合视图下：按钮选中即筛选
  if (activeTab.value === 'aggregate') {
    if (selectedPlayer.value) list = list.filter(r => r.player === selectedPlayer.value)
    if (selectedPermission.value) list = list.filter(r => r.permission === selectedPermission.value)
  }

  // 明细筛选区
  if (filterPlayer.value) {
    const q = filterPlayer.value.toLowerCase()
    list = list.filter(r => r.player.toLowerCase().includes(q))
  }
  if (filterPermission.value) {
    const q = filterPermission.value.toLowerCase()
    list = list.filter(r => r.permission.toLowerCase().includes(q))
  }
  if (filterGrantedBy.value) {
    const q = filterGrantedBy.value.toLowerCase()
    list = list.filter(r => (r.grantedBy || '').toLowerCase().includes(q))
  }
  if (filterStatus.value === 'active') list = list.filter(r => !isExpired(r))
  if (filterStatus.value === 'expired') list = list.filter(r => isExpired(r))
  if (filterDateFrom.value) list = list.filter(r => r.createdAt.slice(0, 10) >= filterDateFrom.value)
  if (filterDateTo.value) list = list.filter(r => r.createdAt.slice(0, 10) <= filterDateTo.value)

  // 排序（desc = 从多到少 / 时间从后往前）
  const dir = sortDesc.value ? -1 : 1
  list.sort((a, b) => {
    switch (sortKey.value) {
      case 'createdAt': return dir * a.createdAt.localeCompare(b.createdAt)
      case 'permission': return dir * a.permission.localeCompare(b.permission)
      case 'player': return dir * a.player.localeCompare(b.player)
      case 'expireAt': {
        const ea = a.expireAt || '9999-99-99 99:99:99'
        const eb = b.expireAt || '9999-99-99 99:99:99'
        return dir * ea.localeCompare(eb)
      }
      case 'grantedBy': return dir * (a.grantedBy || '').localeCompare(b.grantedBy || '')
      case 'count': {
        const ca = permCountMap.value[a.permission] || 0
        const cb = permCountMap.value[b.permission] || 0
        return dir * (ca - cb)
      }
      default: return 0
    }
  })
  return list
})

const toggleSort = (key) => {
  if (sortKey.value === key) {
    sortDesc.value = !sortDesc.value
  } else {
    sortKey.value = key
    sortDesc.value = key === 'createdAt'
  }
}
const sortArrow = (key) => {
  if (sortKey.value !== key) return ''
  return sortDesc.value ? '▼' : '▲'
}
const sortLabel = (key) => {
  const map = { createdAt: '签发时间', permission: '权限名称', player: '目标玩家', count: '数量', expireAt: '到期时间', grantedBy: '签发人' }
  return map[key] || key
}

// 多选回收
const toggleRow = (id) => {
  const s = new Set(selectedIds.value)
  if (s.has(id)) s.delete(id); else s.add(id)
  selectedIds.value = s
}
const isRowSelected = (id) => selectedIds.value.has(id)
const clearSelection = () => { selectedIds.value = new Set() }
const toggleSelectAll = () => {
  const all = filteredRecords.value
  const allSelected = all.length > 0 && all.every(r => selectedIds.value.has(r.id))
  const s = new Set(selectedIds.value)
  if (allSelected) all.forEach(r => s.delete(r.id))
  else all.forEach(r => s.add(r.id))
  selectedIds.value = s
}

// ═══════════════ 快速签发 ═══════════════
const openQuickModal = () => {
  error.value = ''
  quickData.value = { player: '', permission: '', expireMode: 'permanent', expireDate: '', durationDays: 0, durationHours: 0, durationMinutes: 0, note: '' }
  quickSuggestions.value = []
  quickUserSuggestions.value = []
  quickModal.value = true
}

const onPlayerInput = () => {
  const q = quickData.value.player.trim().toLowerCase()
  if (!q) { quickUserSuggestions.value = []; return }
  quickUserSuggestions.value = allUsers.value.map(u => u.name).filter(n => n && n.toLowerCase().includes(q)).slice(0, 10)
  if (quickUserSuggestions.value.length > 0) updateQuickPos('.quick-player-wrapper input')
}
const selectUser = (name) => {
  quickData.value.player = name
  quickUserSuggestions.value = []
}

const onPermissionInput = () => {
  quickSuggestions.value = searchPermissions(quickData.value.permission)
  if (quickSuggestions.value.length > 0) updateQuickPos('.quick-perm-wrapper input')
}
const selectSuggestion = (key) => {
  quickData.value.permission = key
  quickSuggestions.value = []
}

const updateQuickPos = (selector) => {
  const input = document.querySelector(selector)
  if (input) {
    const rect = input.getBoundingClientRect()
    quickSugStyle.value = {
      position: 'fixed',
      top: `${rect.bottom + 4}px`,
      left: `${rect.left}px`,
      width: `${rect.width}px`,
      zIndex: 3000
    }
  }
}

const buildExpireParams = (d) => {
  if (d.expireMode === 'absolute' && d.expireDate) {
    return { expireAt: d.expireDate.replace('T', ' ') }
  }
  if (d.expireMode === 'duration') {
    const secs = (Number(d.durationDays) || 0) * 86400 + (Number(d.durationHours) || 0) * 3600 + (Number(d.durationMinutes) || 0) * 60
    if (secs > 0) return { expiresIn: String(secs) }
  }
  return {}
}

// Date → datetime-local 输入框格式（yyyy-MM-ddTHH:mm）
const toLocalInput = (d) => {
  const pad = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
// 切到「指定时间」时的默认值：当前时间 +1 天
const defaultExpireDate = () => toLocalInput(new Date(Date.now() + 24 * 3600 * 1000))

// 三个弹窗：切到“指定时间”且无值时自动填入默认时间
watch(() => quickData.value.expireMode, (m) => { if (m === 'absolute' && !quickData.value.expireDate) quickData.value.expireDate = defaultExpireDate() })
watch(() => batchData.value.expireMode, (m) => { if (m === 'absolute' && !batchData.value.expireDate) batchData.value.expireDate = defaultExpireDate() })
watch(() => renewData.value.expireMode, (m) => { if (m === 'absolute' && !renewData.value.expireDate) renewData.value.expireDate = defaultExpireDate() })

const submitQuickGrant = async () => {
  const d = quickData.value
  if (!d.player.trim()) { error.value = '请选择目标玩家'; return }
  if (!d.permission.trim()) { error.value = '请输入权限名称'; return }
  submitting.value = true
  error.value = ''
  try {
    const body = { player: d.player.trim(), permission: d.permission.trim(), note: d.note, ...buildExpireParams(d) }
    const res = await post('/api/permissions/grant', body)
    const data = await res.json()
    if (data.error) { error.value = data.error; return }
    quickModal.value = false
    await refreshAll(true)
  } catch (e) {
    error.value = '签发失败: ' + e.message
  } finally {
    submitting.value = false
  }
}

// ═══════════════ 批量签发 ═══════════════
const openBatchModal = () => {
  error.value = ''
  batchResult.value = null
  batchData.value = { players: [], permissions: [], expireMode: 'permanent', expireDate: '', durationDays: 0, durationHours: 0, durationMinutes: 0, note: '' }
  batchPlayerQuery.value = ''
  batchPermQuery.value = ''
  batchPermTab.value = 'normal'
  customPermInput.value = ''
  batchModal.value = true
}

const batchPlayerOptions = computed(() => {
  // 注意：必须保留已选中的玩家（不排除），否则选中后无法再次点击取消
  const q = batchPlayerQuery.value.trim().toLowerCase()
  const names = allUsers.value.map(u => u.name).filter(Boolean)
  if (!q) return names
  return names.filter(n => n.toLowerCase().includes(q))
})
const batchPermissionOptions = computed(() => {
  let keys
  if (batchPermTab.value === 'normal') keys = reasonablePermissionKeys
  else if (batchPermTab.value === 'tp') keys = tpPermissionKeys
  else keys = allPermissionKeys
  const q = batchPermQuery.value.trim().toLowerCase()
  const list = keys.map(k => ({ key: k, name: permissionMap[k] || k }))
  if (!q) return list
  return list.filter(p => p.key.toLowerCase().includes(q) || p.name.toLowerCase().includes(q))
})
const toggleBatchPlayer = (name) => {
  const arr = [...batchData.value.players]
  const i = arr.indexOf(name)
  if (i >= 0) arr.splice(i, 1); else arr.push(name)
  batchData.value.players = arr
}
const toggleBatchPermission = (key) => {
  const arr = [...batchData.value.permissions]
  const i = arr.indexOf(key)
  if (i >= 0) arr.splice(i, 1); else arr.push(key)
  batchData.value.permissions = arr
}
const addCustomPermission = () => {
  // 支持逗号分隔一次添加多个自定义权限
  const val = customPermInput.value.trim()
  if (!val) return
  const added = []
  val.split(',').map(s => s.trim()).filter(s => s.length > 0).forEach(s => {
    if (!batchData.value.permissions.includes(s)) {
      batchData.value.permissions = [...batchData.value.permissions, s]
      added.push(s)
    }
  })
  if (added.length === 0) error.value = '权限已存在或为空'
  else error.value = ''
  customPermInput.value = ''
}
const batchTotal = computed(() => batchData.value.players.length * batchData.value.permissions.length)

const submitBatchGrant = async () => {
  const d = batchData.value
  if (d.players.length === 0) { error.value = '请选择至少一个目标玩家'; return }
  if (d.permissions.length === 0) { error.value = '请选择至少一个权限'; return }
  submitting.value = true
  error.value = ''
  batchResult.value = null
  try {
    const body = { players: d.players, permissions: d.permissions, note: d.note, ...buildExpireParams(d) }
    const res = await post('/api/permissions/grant-batch', body)
    const data = await res.json()
    if (data.error) { error.value = data.error; return }
    batchResult.value = data
    await refreshAll(true)
  } catch (e) {
    error.value = '批量签发失败: ' + e.message
  } finally {
    submitting.value = false
  }
}
const closeBatchModal = () => {
  batchModal.value = false
  batchResult.value = null
}

// ═══════════════ 回收 ═══════════════
const revokeOne = async (r) => {
  if (!confirm(`确定回收 ${r.player} 的个人权限 ${r.permission} 吗？`)) return
  error.value = ''
  try {
    const res = await post('/api/permissions/revoke', { player: r.player, permission: r.permission })
    const data = await res.json()
    if (data.error) { error.value = data.error; return }
    await refreshAll(true)
  } catch (e) {
    error.value = '回收失败: ' + e.message
  }
}

const revokeSelected = async () => {
  const rows = filteredRecords.value.filter(r => selectedIds.value.has(r.id))
  if (rows.length === 0) { error.value = '请先勾选要回收的记录'; return }
  if (!confirm(`确定批量回收选中的 ${rows.length} 条个人权限吗？`)) return
  error.value = ''
  submitting.value = true
  try {
    const players = [...new Set(rows.map(r => r.player))]
    const permissions = [...new Set(rows.map(r => r.permission))]
    const res = await post('/api/permissions/revoke-batch', { players, permissions })
    const data = await res.json()
    if (data.error) { error.value = data.error; return }
    clearSelection()
    await refreshAll(true)
  } catch (e) {
    error.value = '批量回收失败: ' + e.message
  } finally {
    submitting.value = false
  }
}

// ═══════════════ 续期 / 改期 ═══════════════
const openRenew = (r) => {
  renewTarget.value = r
  renewData.value = { expireMode: 'permanent', expireDate: '', durationDays: 0, durationHours: 0, durationMinutes: 0 }
  error.value = ''
  renewModal.value = true
}
const submitRenew = async () => {
  const t = renewTarget.value
  submitting.value = true
  error.value = ''
  try {
    // 重新签发（upsert 语义：更新到期时间与签发人）
    const body = { player: t.player, permission: t.permission, note: t.note, ...buildExpireParams(renewData.value) }
    const res = await post('/api/permissions/grant', body)
    const data = await res.json()
    if (data.error) { error.value = data.error; return }
    renewModal.value = false
    await refreshAll(true)
  } catch (e) {
    error.value = '更新到期失败: ' + e.message
  } finally {
    submitting.value = false
  }
}

// ═══════════════ 清理过期 ═══════════════
const cleanupExpired = async () => {
  if (!confirm('确定清理所有已过期的个人权限记录吗？')) return
  error.value = ''
  try {
    const res = await post('/api/permissions/cleanup', {})
    const data = await res.json()
    if (data.error) { error.value = data.error; return }
    alert(data.response || '清理完成')
    await refreshAll(true)
  } catch (e) {
    error.value = '清理失败: ' + e.message
  }
}

const handleClickOutside = (event) => {
  if (quickSuggestions.value.length > 0 && !event.target.closest('.quick-perm-wrapper')) quickSuggestions.value = []
  if (quickUserSuggestions.value.length > 0 && !event.target.closest('.quick-player-wrapper')) quickUserSuggestions.value = []
}

onMounted(() => {
  refreshAll()
  fetchUsers()
  document.addEventListener('click', handleClickOutside)
})
onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
})
</script>

<template>
  <div class="perm-manager">
    <!-- ═══ 头部 ═══ -->
    <div class="header">
      <h2>个人权限</h2>
      <div class="header-actions">
        <button class="btn-quick" @click="openQuickModal">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>
          快速签发
        </button>
        <button class="btn-batch" @click="openBatchModal">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
          批量签发
        </button>
        <button class="btn-icon" title="清理过期权限" @click="cleanupExpired">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
        </button>
        <button class="btn-secondary" @click="refreshAll()" :disabled="loading">{{ loading ? '加载中...' : '刷新' }}</button>
      </div>
    </div>

    <div v-if="error" class="error-message">{{ error }}</div>
    <div v-if="loading" class="loading"><div class="spinner"></div><span>加载中...</span></div>

    <template v-else>
      <!-- ═══ Tab 切换 ═══ -->
      <div class="tabs">
        <button class="tab-item" :class="{ active: activeTab === 'aggregate' }" @click="activeTab = 'aggregate'">聚合统计</button>
        <button class="tab-item" :class="{ active: activeTab === 'detail' }" @click="activeTab = 'detail'">明细记录</button>
      </div>

      <!-- ═══ 聚合视图 ═══ -->
      <div v-if="activeTab === 'aggregate'" class="aggregate-view">
        <div class="filter-bar" v-if="selectedPlayer || selectedPermission">
          <span class="filter-label">当前筛选:</span>
          <span v-if="selectedPlayer" class="filter-chip" @click="togglePlayerFilter(selectedPlayer)">玩家 {{ selectedPlayer }} ✕</span>
          <span v-if="selectedPermission" class="filter-chip" @click="togglePermissionFilter(selectedPermission)">权限 {{ selectedPermission }} ✕</span>
          <button class="btn-link" @click="clearFilters">反选（清除全部）</button>
        </div>

        <!-- 玩家按钮云 -->
        <div class="cloud-section">
          <div class="cloud-header">
            <span class="cloud-title">玩家（按权限数）</span>
            <button class="btn-direction" :title="playerSortDesc ? '当前从多到少，点击反向' : '当前从少到多，点击反向'" @click="playerSortDesc = !playerSortDesc">
              {{ playerSortDesc ? '多→少' : '少→多' }} ⇅
            </button>
          </div>
          <div v-if="playerButtons.length" class="btn-cloud">
            <button
              v-for="p in playerButtons" :key="p.player"
              class="cloud-btn" :class="{ active: selectedPlayer === p.player }"
              @click="togglePlayerFilter(p.player)"
              :title="`${p.player} · ${p.count} 个权限 · 最近签发 ${p.lastGrantedAt || '-'}`"
            >
              <span class="cb-name">{{ p.player }}</span>
              <span class="cb-count">{{ p.count }}</span>
            </button>
          </div>
          <div v-else class="cloud-empty">暂无个人权限，点击右上角「快速签发」开始</div>
        </div>

        <!-- 权限按钮云 -->
        <div class="cloud-section">
          <div class="cloud-header">
            <span class="cloud-title">权限（按持有者数）</span>
            <button class="btn-direction" :title="permSortDesc ? '当前从多到少，点击反向' : '当前从少到多，点击反向'" @click="permSortDesc = !permSortDesc">
              {{ permSortDesc ? '多→少' : '少→多' }} ⇅
            </button>
          </div>
          <div v-if="permButtons.length" class="btn-cloud">
            <button
              v-for="p in permButtons" :key="p.permission"
              class="cloud-btn" :class="{ active: selectedPermission === p.permission }"
              @click="togglePermissionFilter(p.permission)"
              :title="`${p.permission} (${getPermissionName(p.permission)}) · 被签发给 ${p.count} 位玩家`"
            >
              <span class="cb-name">{{ p.permission }}</span>
              <span class="cb-count">{{ p.count }}</span>
            </button>
          </div>
          <div v-else class="cloud-empty">暂无已签发权限</div>
        </div>

        <!-- 聚合筛选下的明细预览（签发时间从后往前） -->
        <div class="preview-section">
          <div class="preview-header">
            <span class="cloud-title">签发记录（按签发时间从后往前）</span>
            <span v-if="filteredRecords.length" class="preview-count">共 {{ filteredRecords.length }} 条</span>
          </div>
          <div class="table-wrap">
            <table class="perm-table">
              <thead>
                <tr>
                  <th>签发时间</th>
                  <th>权限</th>
                  <th>目标玩家</th>
                  <th>到期时间</th>
                  <th>签发人</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="r in filteredRecords.slice(0, 30)" :key="r.id">
                  <td>{{ r.createdAt }}</td>
                  <td class="perm-cell">
                    <span class="perm-key">{{ r.permission }}</span>
                    <span class="perm-cn">{{ getPermissionName(r.permission) }}</span>
                  </td>
                  <td>{{ r.player }}</td>
                  <td :class="{ expired: isExpired(r) }">{{ r.expireAt || '永久' }}</td>
                  <td>{{ r.grantedBy || '-' }}</td>
                </tr>
                <tr v-if="filteredRecords.length === 0"><td colspan="5" class="td-empty">无记录</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- ═══ 明细视图 ═══ -->
      <div v-else class="detail-view">
        <div class="detail-toolbar">
          <div class="filters">
            <input v-model="filterPlayer" class="filter-input" placeholder="按玩家筛选" />
            <input v-model="filterPermission" class="filter-input" placeholder="按权限筛选" />
            <input v-model="filterGrantedBy" class="filter-input" placeholder="按签发人筛选" />
            <select v-model="filterStatus" class="filter-input select">
              <option value="all">全部状态</option>
              <option value="active">生效中</option>
              <option value="expired">已过期</option>
            </select>
            <input v-model="filterDateFrom" type="date" class="filter-input" title="签发时间从" />
            <span class="date-sep">至</span>
            <input v-model="filterDateTo" type="date" class="filter-input" title="签发时间至" />
          </div>
          <button class="btn-batch" :disabled="selectedIds.size === 0" @click="revokeSelected">回收选中 ({{ selectedIds.size }})</button>
        </div>

        <div class="table-wrap">
          <table class="perm-table">
            <thead>
              <tr>
                <th class="th-check"><input type="checkbox" :checked="filteredRecords.length > 0 && filteredRecords.every(r => selectedIds.has(r.id))" @change="toggleSelectAll" /></th>
                <th @click="toggleSort('createdAt')">签发时间 {{ sortArrow('createdAt') }}</th>
                <th @click="toggleSort('permission')">权限名称 {{ sortArrow('permission') }}</th>
                <th @click="toggleSort('player')">目标玩家 {{ sortArrow('player') }}</th>
                <th @click="toggleSort('count')">数量 {{ sortArrow('count') }}</th>
                <th @click="toggleSort('expireAt')">到期时间 {{ sortArrow('expireAt') }}</th>
                <th @click="toggleSort('grantedBy')">签发人 {{ sortArrow('grantedBy') }}</th>
                <th>备注</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="r in filteredRecords" :key="r.id" :class="{ 'row-expired': isExpired(r) }">
                <td class="td-check"><input type="checkbox" :checked="isRowSelected(r.id)" @change="toggleRow(r.id)" /></td>
                <td>{{ r.createdAt }}</td>
                <td class="perm-cell">
                  <span class="perm-key">{{ r.permission }}</span>
                  <span class="perm-cn">{{ getPermissionName(r.permission) }}</span>
                </td>
                <td>{{ r.player }}</td>
                <td>{{ permCountMap[r.permission] || 0 }}</td>
                <td :class="{ expired: isExpired(r) }">{{ r.expireAt || '永久' }}</td>
                <td>{{ r.grantedBy || '-' }}</td>
                <td class="td-note">{{ r.note || '-' }}</td>
                <td class="td-actions">
                  <button class="btn-small" @click="openRenew(r)">续期</button>
                  <button class="btn-small danger" @click="revokeOne(r)">回收</button>
                </td>
              </tr>
              <tr v-if="filteredRecords.length === 0"><td colspan="9" class="td-empty">无匹配记录</td></tr>
            </tbody>
          </table>
        </div>
        <div class="detail-footer">
          <span>共 {{ filteredRecords.length }} 条 / 排序：{{ sortLabel(sortKey) }}（{{ sortDesc ? '从多到少' : '从少到多' }}）</span>
          <span class="footer-tip">点击列头排序；已过期记录以红色标记，点击「续期」可延长或修改到期时间</span>
        </div>
      </div>
    </template>

    <!-- ═══ 快速签发弹窗 ═══ -->
    <div v-if="quickModal" class="modal-overlay" @click.self="quickModal = false">
      <div class="modal">
        <div class="modal-header">
          <h3>快速签发个人权限</h3>
          <button class="close-btn" @click="quickModal = false">✕</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label>目标玩家</label>
            <div class="quick-player-wrapper">
              <input v-model="quickData.player" type="text" placeholder="输入玩家名搜索..." @input="onPlayerInput" />
            </div>
            <Teleport to="body">
              <div v-if="quickUserSuggestions.length > 0" class="suggestions-dropdown-teleport" :style="quickSugStyle">
                <div v-for="n in quickUserSuggestions" :key="n" class="suggestion-item" @click="selectUser(n)">
                  <span class="suggestion-key">{{ n }}</span>
                </div>
              </div>
            </Teleport>
          </div>
          <div class="form-group">
            <label>权限名称</label>
            <div class="quick-perm-wrapper">
              <input v-model="quickData.permission" type="text" placeholder="输入权限名或中文描述..." @input="onPermissionInput" />
            </div>
            <Teleport to="body">
              <div v-if="quickSuggestions.length > 0" class="suggestions-dropdown-teleport" :style="quickSugStyle">
                <div v-for="item in quickSuggestions" :key="item.key" class="suggestion-item" @click="selectSuggestion(item.key)">
                  <span class="suggestion-key">{{ item.key }}</span>
                  <span class="suggestion-value">{{ item.value }}</span>
                </div>
              </div>
            </Teleport>
          </div>
          <div class="form-group">
            <label>到期方式</label>
            <div class="expire-options">
              <label class="radio"><input type="radio" value="permanent" v-model="quickData.expireMode" /> 永久生效</label>
              <label class="radio"><input type="radio" value="absolute" v-model="quickData.expireMode" /> 指定时间</label>
              <label class="radio"><input type="radio" value="duration" v-model="quickData.expireMode" /> 有效时长</label>
            </div>
            <div v-if="quickData.expireMode === 'absolute'" class="expire-sub">
              <input v-model="quickData.expireDate" type="datetime-local" class="filter-input" />
            </div>
            <div v-else-if="quickData.expireMode === 'duration'" class="expire-sub duration">
              <input v-model.number="quickData.durationDays" type="number" min="0" class="dur-input" /> 天
              <input v-model.number="quickData.durationHours" type="number" min="0" max="23" class="dur-input" /> 时
              <input v-model.number="quickData.durationMinutes" type="number" min="0" max="59" class="dur-input" /> 分
            </div>
          </div>
          <div class="form-group">
            <label>备注（可选）</label>
            <input v-model="quickData.note" type="text" placeholder="签发备注，用于审计追溯" />
          </div>
          <div v-if="error" class="error-message">{{ error }}</div>
        </div>
        <div class="modal-footer">
          <button class="btn-secondary" @click="quickModal = false">取消</button>
          <button class="btn-primary" :disabled="submitting" @click="submitQuickGrant">{{ submitting ? '签发中...' : '签发' }}</button>
        </div>
      </div>
    </div>

    <!-- ═══ 批量签发弹窗 ═══ -->
    <div v-if="batchModal" class="modal-overlay" @click.self="closeBatchModal">
      <div class="modal batch-modal">
        <div class="modal-header">
          <h3>批量签发（多玩家 × 多权限）</h3>
          <button class="close-btn" @click="closeBatchModal">✕</button>
        </div>
        <div class="modal-body">
          <div class="batch-cols">
            <!-- 玩家选择 -->
            <div class="batch-col">
              <div class="batch-col-header">
                <span>目标玩家</span>
                <button class="btn-link" @click="batchData.players = allUsers.map(u => u.name).filter(Boolean)">全选</button>
              </div>
              <input v-model="batchPlayerQuery" class="filter-input" placeholder="搜索玩家..." />
              <div class="batch-list">
                <label v-for="n in batchPlayerOptions" :key="n" class="batch-item" :class="{ picked: batchData.players.includes(n) }">
                  <input type="checkbox" :checked="batchData.players.includes(n)" @change="toggleBatchPlayer(n)" />
                  <span>{{ n }}</span>
                </label>
                <div v-if="batchPlayerOptions.length === 0" class="cloud-empty">无更多玩家</div>
              </div>
            </div>
            <!-- 权限选择 -->
            <div class="batch-col">
              <div class="batch-col-header">
                <span>权限</span>
                <button class="btn-link" @click="batchData.permissions = [...new Set([...batchData.permissions, ...batchPermissionOptions.map(p => p.key)])]">全选本页</button>
              </div>
              <input v-model="batchPermQuery" class="filter-input" placeholder="搜索权限..." />
              <div class="batch-tabs">
                <button class="mini-tab" :class="{ active: batchPermTab === 'normal' }" @click="batchPermTab = 'normal'">常规</button>
                <button class="mini-tab" :class="{ active: batchPermTab === 'tp' }" @click="batchPermTab = 'tp'">TP</button>
                <button class="mini-tab" :class="{ active: batchPermTab === 'all' }" @click="batchPermTab = 'all'">全部</button>
              </div>
              <div class="batch-list">
                <label v-for="p in batchPermissionOptions" :key="p.key" class="batch-item" :class="{ picked: batchData.permissions.includes(p.key) }">
                  <input type="checkbox" :checked="batchData.permissions.includes(p.key)" @change="toggleBatchPermission(p.key)" />
                  <span class="perm-cn">{{ p.name }}</span>
                  <span class="suggestion-value">{{ p.key }}</span>
                </label>
                <div v-if="batchPermissionOptions.length === 0" class="cloud-empty">无匹配权限</div>
              </div>

              <!-- 已选权限标签（含自定义），点击 ✕ 移除 -->
              <div v-if="batchData.permissions.length" class="selected-chips">
                <span v-for="p in batchData.permissions" :key="p" class="selected-chip" :title="p">
                  {{ getPermissionName(p) }}
                  <span class="chip-x" @click="toggleBatchPermission(p)">✕</span>
                </span>
              </div>
              <!-- 自定义权限：任意权限名（含插件自定义），逗号分隔，回车添加（置于底部） -->
              <div class="custom-perm-row">
                <input v-model="customPermInput" class="filter-input custom-perm-input" placeholder="自定义权限名，逗号分隔，回车添加" @keyup.enter="addCustomPermission" />
                <button class="btn-small" @click="addCustomPermission">添加</button>
              </div>
            </div>
          </div>

          <div class="batch-meta">
            <span class="batch-total">已选 {{ batchData.players.length }} 位玩家 × {{ batchData.permissions.length }} 个权限 = <b>{{ batchTotal }}</b> 条</span>
          </div>

          <div class="form-group">
            <label>到期方式（统一应用）</label>
            <div class="expire-options">
              <label class="radio"><input type="radio" value="permanent" v-model="batchData.expireMode" /> 永久生效</label>
              <label class="radio"><input type="radio" value="absolute" v-model="batchData.expireMode" /> 指定时间</label>
              <label class="radio"><input type="radio" value="duration" v-model="batchData.expireMode" /> 有效时长</label>
            </div>
            <div v-if="batchData.expireMode === 'absolute'" class="expire-sub">
              <input v-model="batchData.expireDate" type="datetime-local" class="filter-input" />
            </div>
            <div v-else-if="batchData.expireMode === 'duration'" class="expire-sub duration">
              <input v-model.number="batchData.durationDays" type="number" min="0" class="dur-input" /> 天
              <input v-model.number="batchData.durationHours" type="number" min="0" max="23" class="dur-input" /> 时
              <input v-model.number="batchData.durationMinutes" type="number" min="0" max="59" class="dur-input" /> 分
            </div>
          </div>
          <div class="form-group">
            <label>备注（可选，统一应用）</label>
            <input v-model="batchData.note" type="text" placeholder="批量签发备注" />
          </div>

          <!-- 批量结果 -->
          <div v-if="batchResult" class="batch-result" :class="{ ok: batchResult.failed === 0, warn: batchResult.failed > 0 }">
            <div class="batch-result-title">{{ batchResult.response }}</div>
            <div v-if="batchResult.failures && batchResult.failures.length" class="batch-failures">
              <div v-for="(f, i) in batchResult.failures" :key="i" class="failure-line">{{ f.player }} · {{ f.permission || '' }}：{{ f.reason }}</div>
            </div>
          </div>

          <div v-if="error" class="error-message">{{ error }}</div>
        </div>
        <div class="modal-footer">
          <button class="btn-secondary" @click="closeBatchModal">关闭</button>
          <button class="btn-primary" :disabled="submitting || batchTotal === 0" @click="submitBatchGrant">
            {{ submitting ? '签发中...' : `批量签发 ${batchTotal} 条` }}
          </button>
        </div>
      </div>
    </div>

    <!-- ═══ 续期 / 改期弹窗 ═══ -->
    <div v-if="renewModal" class="modal-overlay" @click.self="renewModal = false">
      <div class="modal">
        <div class="modal-header">
          <h3>修改到期时间 - {{ renewTarget.player }} / {{ renewTarget.permission }}</h3>
          <button class="close-btn" @click="renewModal = false">✕</button>
        </div>
        <div class="modal-body">
          <div class="info-row">当前到期：{{ renewTarget.expireAt || '永久' }}</div>
          <div class="form-group">
            <label>到期方式</label>
            <div class="expire-options">
              <label class="radio"><input type="radio" value="permanent" v-model="renewData.expireMode" /> 永久生效</label>
              <label class="radio"><input type="radio" value="absolute" v-model="renewData.expireMode" /> 指定时间</label>
              <label class="radio"><input type="radio" value="duration" v-model="renewData.expireMode" /> 有效时长</label>
            </div>
            <div v-if="renewData.expireMode === 'absolute'" class="expire-sub">
              <input v-model="renewData.expireDate" type="datetime-local" class="filter-input" />
            </div>
            <div v-else-if="renewData.expireMode === 'duration'" class="expire-sub duration">
              <input v-model.number="renewData.durationDays" type="number" min="0" class="dur-input" /> 天
              <input v-model.number="renewData.durationHours" type="number" min="0" max="23" class="dur-input" /> 时
              <input v-model.number="renewData.durationMinutes" type="number" min="0" max="59" class="dur-input" /> 分
            </div>
          </div>
          <div v-if="error" class="error-message">{{ error }}</div>
        </div>
        <div class="modal-footer">
          <button class="btn-secondary" @click="renewModal = false">取消</button>
          <button class="btn-primary" :disabled="submitting" @click="submitRenew">{{ submitting ? '保存中...' : '保存' }}</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.perm-manager { padding: 24px; height: 100%; overflow-y: auto; }
.header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; flex-wrap: wrap; gap: 12px; }
.header h2 { margin: 0; color: var(--text-primary); font-size: 1.5rem; font-weight: 600; }
.header-actions { display: flex; gap: 10px; align-items: center; }

.btn-primary {
  display: flex; align-items: center; gap: 8px; padding: 10px 18px;
  background: var(--accent-primary); color: white; border: none; border-radius: 8px;
  cursor: pointer; font-weight: 500; transition: all 0.2s ease;
}
.btn-primary:hover { background: var(--accent-primary-hover); transform: translateY(-1px); }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; transform: none; }
.btn-secondary {
  padding: 10px 18px; background: transparent; color: var(--text-secondary);
  border: 1px solid var(--border-color); border-radius: 8px; cursor: pointer;
  font-weight: 500; transition: all 0.2s ease;
}
.btn-secondary:hover { background: var(--bg-tertiary); border-color: var(--text-secondary); }
.btn-quick, .btn-batch {
  display: flex; align-items: center; gap: 8px; padding: 10px 18px; color: white;
  border: none; border-radius: 8px; cursor: pointer; font-weight: 500; transition: all 0.2s ease;
}
.btn-quick { background: #10b981; }
.btn-quick:hover { background: #059669; transform: translateY(-1px); }
.btn-batch { background: var(--accent-primary); }
.btn-batch:hover { background: var(--accent-primary-hover); transform: translateY(-1px); }
.btn-batch:disabled { opacity: 0.5; cursor: not-allowed; transform: none; }
.btn-icon {
  display: flex; align-items: center; justify-content: center; width: 40px; height: 40px;
  background: transparent; color: var(--text-secondary); border: 1px solid var(--border-color);
  border-radius: 8px; cursor: pointer; transition: all 0.2s ease;
}
.btn-icon:hover { background: rgba(239, 68, 68, 0.1); color: var(--accent-error); border-color: var(--accent-error); }
.btn-link { background: none; border: none; color: var(--accent-primary); cursor: pointer; font-size: 0.85rem; padding: 4px 8px; }
.btn-link:hover { text-decoration: underline; }
.btn-small {
  padding: 4px 10px; font-size: 0.8rem; background: transparent; color: var(--text-secondary);
  border: 1px solid var(--border-color); border-radius: 6px; cursor: pointer; transition: all 0.15s;
}
.btn-small:hover { background: var(--bg-tertiary); color: var(--accent-primary); border-color: var(--accent-primary); }
.btn-small.danger:hover { background: rgba(239, 68, 68, 0.1); color: var(--accent-error); border-color: var(--accent-error); }

.error-message {
  padding: 12px 16px; background: rgba(239, 68, 68, 0.1); color: var(--accent-error);
  border-radius: 8px; margin-bottom: 16px; border: 1px solid rgba(239, 68, 68, 0.2);
}
.loading { display: flex; align-items: center; justify-content: center; gap: 12px; padding: 48px; color: var(--text-muted); }
.spinner { width: 24px; height: 24px; border: 2px solid var(--border-color); border-top-color: var(--accent-primary); border-radius: 50%; animation: spin 0.8s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }

.tabs { display: flex; gap: 4px; border-bottom: 1px solid var(--border-light); margin-bottom: 18px; }
.tab-item {
  padding: 10px 18px; background: none; border: none; border-bottom: 2px solid transparent;
  color: var(--text-secondary); cursor: pointer; font-size: 0.95rem; font-weight: 500; transition: all 0.2s;
}
.tab-item.active { color: var(--accent-primary); border-bottom-color: var(--accent-primary); }

/* ── 聚合视图 ── */
.filter-bar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-bottom: 14px; }
.filter-label { color: var(--text-muted); font-size: 0.85rem; }
.filter-chip {
  padding: 4px 10px; background: rgba(99, 102, 241, 0.12); color: var(--accent-primary);
  border: 1px solid rgba(99, 102, 241, 0.3); border-radius: 999px; cursor: pointer; font-size: 0.85rem;
}
.filter-chip:hover { background: rgba(99, 102, 241, 0.25); }

.cloud-section { margin-bottom: 20px; }
.cloud-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; }
.cloud-title { color: var(--text-muted); font-size: 0.9rem; font-weight: 600; }
.btn-direction {
  padding: 5px 12px; background: var(--bg-tertiary); color: var(--text-secondary);
  border: 1px solid var(--border-color); border-radius: 6px; cursor: pointer; font-size: 0.8rem; transition: all 0.2s;
}
.btn-direction:hover { color: var(--accent-primary); border-color: var(--accent-primary); }
.btn-cloud { display: flex; flex-wrap: wrap; gap: 8px; }
.cloud-btn {
  display: inline-flex; align-items: center; gap: 6px; padding: 7px 12px;
  background: var(--bg-card); color: var(--text-primary); border: 1px solid var(--border-light);
  border-radius: 999px; cursor: pointer; font-size: 0.85rem; transition: all 0.2s;
}
.cloud-btn:hover { border-color: var(--accent-primary); box-shadow: 0 2px 8px rgba(99, 102, 241, 0.15); }
.cloud-btn.active { background: var(--accent-primary); color: white; border-color: var(--accent-primary); }
.cb-name { max-width: 220px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.cb-count {
  min-width: 22px; text-align: center; padding: 1px 7px; border-radius: 999px;
  background: rgba(99, 102, 241, 0.15); color: var(--accent-primary); font-weight: 700; font-size: 0.78rem;
}
.cloud-btn.active .cb-count { background: rgba(255, 255, 255, 0.25); color: white; }
.cloud-empty { color: var(--text-muted); font-size: 0.85rem; padding: 10px 0; }

/* ── 表格 ── */
.preview-section { margin-top: 8px; }
.preview-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; }
.preview-count { color: var(--text-muted); font-size: 0.85rem; }
.detail-toolbar { display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-bottom: 14px; flex-wrap: wrap; }
.filters { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
.filter-input {
  padding: 8px 12px; background: var(--bg-tertiary); color: var(--text-primary);
  border: 1px solid var(--border-light); border-radius: 8px; font-size: 0.85rem; min-width: 140px;
}
.filter-input:focus { outline: none; border-color: var(--accent-primary); }
.filter-input.select { min-width: 120px; cursor: pointer; }
.date-sep { color: var(--text-muted); font-size: 0.85rem; }
.table-wrap { overflow-x: auto; border: 1px solid var(--border-light); border-radius: 10px; }
.perm-table { width: 100%; border-collapse: collapse; background: var(--bg-card); font-size: 0.88rem; }
.perm-table th, .perm-table td { padding: 10px 12px; text-align: left; border-bottom: 1px solid var(--border-light); white-space: nowrap; }
.perm-table thead th {
  background: var(--bg-tertiary); color: var(--text-muted); font-weight: 600; font-size: 0.82rem;
  user-select: none; cursor: pointer; position: sticky; top: 0; z-index: 1;
}
.perm-table thead th:hover { color: var(--accent-primary); }
.perm-table tbody tr:hover { background: var(--bg-hover); }
.perm-table tbody tr.row-expired { opacity: 0.55; }
.perm-table tbody tr.row-expired .expired { color: var(--accent-error); font-weight: 600; }
.th-check, .td-check { width: 32px; text-align: center; cursor: pointer; }
.perm-cell { display: flex; flex-direction: column; gap: 2px; }
.perm-key { font-weight: 600; color: var(--text-primary); }
.perm-cn { color: var(--text-muted); font-size: 0.78rem; }
.expired { color: var(--accent-error); font-weight: 600; }
.td-note { max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text-secondary); }
.td-actions { display: flex; gap: 6px; }
.td-empty { text-align: center; color: var(--text-muted); padding: 24px !important; }
.detail-footer { display: flex; justify-content: space-between; align-items: center; margin-top: 10px; color: var(--text-muted); font-size: 0.82rem; flex-wrap: wrap; gap: 8px; }

/* ── 弹窗 ── */
.modal-overlay {
  position: fixed; top: 0; left: 0; right: 0; bottom: 0; z-index: 2000;
  background: rgba(0, 0, 0, 0.6); backdrop-filter: blur(4px);
  display: flex; align-items: center; justify-content: center;
}
.modal {
  background: var(--bg-card); border-radius: 16px; width: 92%; max-width: 520px;
  max-height: 88vh; overflow: hidden; display: flex; flex-direction: column;
  box-shadow: 0 20px 40px rgba(0, 0, 0, 0.2);
}
.modal.batch-modal { max-width: 860px; width: 95%; }
.modal-header { display: flex; justify-content: space-between; align-items: center; padding: 18px 22px; border-bottom: 1px solid var(--border-light); }
.modal-header h3 { margin: 0; color: var(--text-primary); font-size: 1.05rem; }
.close-btn {
  width: 32px; height: 32px; border-radius: 8px; border: 1px solid var(--border-light);
  background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; font-size: 0.9rem;
}
.close-btn:hover { color: var(--accent-error); border-color: var(--accent-error); }
.modal-body { padding: 20px 22px; overflow-y: auto; }
.modal-footer { display: flex; justify-content: flex-end; gap: 10px; padding: 14px 22px; border-top: 1px solid var(--border-light); }
.form-group { margin-bottom: 16px; }
.form-group label { display: block; color: var(--text-muted); font-size: 0.85rem; margin-bottom: 6px; }
.form-group input[type="text"] {
  width: 100%; padding: 9px 12px; background: var(--bg-tertiary); color: var(--text-primary);
  border: 1px solid var(--border-light); border-radius: 8px; font-size: 0.9rem;
}
.form-group input[type="text"]:focus { outline: none; border-color: var(--accent-primary); }
.info-row { color: var(--text-secondary); font-size: 0.88rem; margin-bottom: 12px; }

.expire-options { display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 8px; }
.radio { display: flex; align-items: center; gap: 6px; color: var(--text-primary); font-size: 0.88rem; cursor: pointer; }
.expire-sub { margin-top: 4px; }
.expire-sub.duration { display: flex; align-items: center; gap: 6px; color: var(--text-secondary); font-size: 0.85rem; flex-wrap: wrap; }
.dur-input {
  width: 70px; padding: 7px 10px; background: var(--bg-tertiary); color: var(--text-primary);
  border: 1px solid var(--border-light); border-radius: 8px; text-align: center;
}
/* 去掉数字输入框的上下调整箭头 */
.dur-input::-webkit-outer-spin-button,
.dur-input::-webkit-inner-spin-button {
  -webkit-appearance: none;
  margin: 0;
}
.dur-input {
  -moz-appearance: textfield;
  appearance: textfield;
}

/* 建议下拉（Teleport） */
.suggestions-dropdown-teleport {
  position: fixed; background: var(--bg-card); border: 1px solid var(--border-color);
  border-radius: 10px; box-shadow: 0 12px 32px rgba(0, 0, 0, 0.2); z-index: 3000;
  max-height: 280px; overflow-y: auto; padding: 4px;
}
.suggestion-item {
  display: flex; flex-direction: column; gap: 2px; padding: 8px 12px; cursor: pointer;
  border-radius: 6px; transition: background 0.15s;
}
.suggestion-item:hover { background: var(--bg-hover); }
.suggestion-key { color: var(--text-primary); font-size: 0.85rem; font-weight: 600; }
.suggestion-value { color: var(--text-muted); font-size: 0.78rem; }

/* ── 批量弹窗 ── */
.batch-cols { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; height: 340px; }
.batch-col { border: 1px solid var(--border-light); border-radius: 10px; padding: 12px; display: flex; flex-direction: column; gap: 8px; min-height: 0; }
.batch-col-header { display: flex; justify-content: space-between; align-items: center; color: var(--text-muted); font-size: 0.85rem; font-weight: 600; }
.batch-list { flex: 1; min-height: 0; overflow-y: auto; display: flex; flex-direction: column; gap: 2px; }
.batch-item {
  display: flex; align-items: center; gap: 8px; padding: 7px 10px; border-radius: 8px;
  cursor: pointer; color: var(--text-primary); font-size: 0.85rem; transition: background 0.15s;
}
.batch-item:hover { background: var(--bg-hover); }
.batch-item.picked { background: rgba(99, 102, 241, 0.1); }
.batch-item .perm-cn { flex: 1; }
.batch-tabs { display: flex; gap: 4px; }
.mini-tab {
  padding: 4px 12px; background: var(--bg-tertiary); color: var(--text-secondary);
  border: 1px solid var(--border-light); border-radius: 6px; cursor: pointer; font-size: 0.8rem;
}
.mini-tab.active { background: var(--accent-primary); color: white; border-color: var(--accent-primary); }
.custom-perm-row { display: flex; gap: 6px; }
.custom-perm-input { flex: 1; min-width: 0; }
.selected-chips {
  display: flex; flex-wrap: wrap; gap: 6px; max-height: 76px; overflow-y: auto;
  padding: 4px 0;
}
.selected-chip {
  display: inline-flex; align-items: center; gap: 4px; padding: 3px 8px;
  background: rgba(99, 102, 241, 0.12); color: var(--text-primary);
  border: 1px solid rgba(99, 102, 241, 0.3); border-radius: 999px; font-size: 0.78rem;
}
.chip-x { cursor: pointer; color: var(--text-muted); font-weight: 700; padding: 0 2px; }
.chip-x:hover { color: var(--accent-error); }
.batch-meta { margin: 14px 0 4px; }
.batch-total { color: var(--text-secondary); font-size: 0.9rem; }
.batch-total b { color: var(--accent-primary); }
.batch-result { margin-top: 12px; padding: 12px 14px; border-radius: 10px; font-size: 0.88rem; }
.batch-result.ok { background: rgba(16, 185, 129, 0.1); color: #10b981; border: 1px solid rgba(16, 185, 129, 0.3); }
.batch-result.warn { background: rgba(245, 158, 11, 0.1); color: #f59e0b; border: 1px solid rgba(245, 158, 11, 0.3); }
.batch-result-title { font-weight: 600; margin-bottom: 6px; }
.batch-failures { max-height: 120px; overflow-y: auto; }
.failure-line { color: var(--text-secondary); font-size: 0.82rem; padding: 2px 0; }

@media (max-width: 767px) {
  .batch-cols { grid-template-columns: 1fr; }
  .header-actions { flex-wrap: wrap; }
}
</style>

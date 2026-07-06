<script setup>
import { ref, computed, onMounted } from 'vue'
import { get, post } from '../utils/api.js'

const props = defineProps({
  users: {
    type: Array,
    default: () => []
  },
  activeUsers: {
    type: Array,
    default: () => []
  },
  unverifiedPlayers: {
    type: Array,
    default: () => []
  },
  unverifiedLoading: {
    type: Boolean,
    default: false
  },
  loading: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['refresh', 'goToUserDetail', 'goToUnverified'])

const searchQuery = ref('')

// 创建用户模态框
const showCreateModal = ref(false)
const newUsername = ref('')
const newPassword = ref('')
const newGroup = ref('')
const groups = ref([])
const createLoading = ref(false)
const createError = ref('')
const createSuccess = ref('')
const showGroupDropdown = ref(false)

const toggleGroupDropdown = () => {
  showGroupDropdown.value = !showGroupDropdown.value
}

const selectGroup = (group) => {
  newGroup.value = group
  showGroupDropdown.value = false
}

const closeGroupDropdown = () => {
  showGroupDropdown.value = false
}

const fetchGroups = async () => {
  try {
    const response = await get('/api/tshock/groups')
    const result = await response.json()
    if (result.groups) {
      groups.value = result.groups.map(g => ({ name: g.GroupName }))
    }
  } catch (err) {
    console.error('Failed to fetch groups:', err)
  }
}

const openCreateModal = () => {
  newUsername.value = ''
  newPassword.value = ''
  newGroup.value = ''
  createError.value = ''
  createSuccess.value = ''
  showCreateModal.value = true
  fetchGroups()
}

const closeCreateModal = () => {
  showCreateModal.value = false
  showGroupDropdown.value = false
  newUsername.value = ''
  newPassword.value = ''
  newGroup.value = ''
  createError.value = ''
  createSuccess.value = ''
}

const executeCreateUser = async () => {
  if (!newUsername.value.trim() || !newPassword.value.trim()) {
    createError.value = '用户名和密码不能为空'
    return
  }

  createLoading.value = true
  createError.value = ''
  createSuccess.value = ''

  try {
    const response = await post('/api/tshock/user/create', {
      username: newUsername.value.trim(),
      password: newPassword.value,
      group: newGroup.value
    })
    const result = await response.json()

    if (result.error) {
      createError.value = result.error
    } else {
      createSuccess.value = result.response || '创建成功'
      emit('refresh')
      setTimeout(() => closeCreateModal(), 1500)
    }
  } catch (err) {
    createError.value = err.message || '创建失败'
  }

  createLoading.value = false
}

// 清空全部角色
const showClearAllDataModal = ref(false)
const clearAllDataUsername = ref('')
const clearAllDataPassword = ref('')
const clearAllDataLoading = ref(false)
const clearAllDataError = ref('')
const clearAllDataSuccess = ref('')

const openClearAllDataModal = () => {
  clearAllDataUsername.value = ''
  clearAllDataPassword.value = ''
  clearAllDataError.value = ''
  clearAllDataSuccess.value = ''
  showClearAllDataModal.value = true
}

const closeClearAllDataModal = () => {
  showClearAllDataModal.value = false
  clearAllDataUsername.value = ''
  clearAllDataPassword.value = ''
}

const executeClearAllData = async () => {
  if (!clearAllDataUsername.value.trim() || !clearAllDataPassword.value.trim()) {
    clearAllDataError.value = '用户名和密码不能为空'
    return
  }

  clearAllDataLoading.value = true
  clearAllDataError.value = ''
  clearAllDataSuccess.value = ''

  try {
    const response = await post('/api/tshock/clearallcharacter', {
      username: clearAllDataUsername.value.trim(),
      password: clearAllDataPassword.value
    })
    const result = await response.json()

    if (result.error) {
      clearAllDataError.value = result.error
    } else {
      clearAllDataSuccess.value = result.response || '角色数据已全部清空'
      emit('refresh')
      setTimeout(() => closeClearAllDataModal(), 1500)
    }
  } catch (err) {
    clearAllDataError.value = err.message || '清空失败'
  }

  clearAllDataLoading.value = false
}

const isUserOnline = (username) => {
  return props.activeUsers.some(name => name.toLowerCase() === username.toLowerCase())
}

const sortedUsers = computed(() => {
  const online = []
  const offline = []
  
  props.users.forEach(user => {
    if (isUserOnline(user.name)) {
      online.push(user)
    } else {
      offline.push(user)
    }
  })
  
  return [...online, ...offline]
})

const filteredUsers = computed(() => {
  if (!searchQuery.value.trim()) {
    return sortedUsers.value
  }
  const query = searchQuery.value.toLowerCase()
  return sortedUsers.value.filter(user => 
    user.name.toLowerCase().includes(query)
  )
})

const handleRowClick = (user) => {
  emit('goToUserDetail', user.name)
}
</script>

<template>
  <div class="players-content">
    <div class="section-header">
      <div class="header-title">
        <h2>玩家列表</h2>
        <span class="online-count-badge" :class="{ 'has-online': activeUsers.length > 0 }">
          ● {{ activeUsers.length }} / {{ users.length }} 在线
        </span>
      </div>
      <div class="header-actions">
        <button @click="emit('refresh')" :disabled="loading" class="refresh-btn">
          {{ loading ? '刷新中...' : '刷新' }}
        </button>
      </div>
    </div>

    <div class="search-bar">
      <input
        v-model="searchQuery"
        type="text"
        placeholder="搜索用户名..."
        class="search-input"
      />
      <button @click="openCreateModal" class="create-user-btn">+ 创建用户</button>
      <button @click="openClearAllDataModal" class="clear-all-data-btn">清空全部角色</button>
    </div>

    <!-- 未登录玩家置顶区块 -->
    <div v-if="unverifiedPlayers.length > 0" class="unverified-section">
      <div class="unverified-header">
        <span class="unverified-icon">⚠</span>
        <span class="unverified-title">未登录玩家 ({{ unverifiedPlayers.length }})</span>
      </div>
      <div class="unverified-list">
        <div
          v-for="p in unverifiedPlayers"
          :key="p.nickname"
          class="unverified-item"
          @click="emit('goToUnverified', p.nickname)"
        >
          <span :class="['uv-status', p.hasAccount ? 'uv-unverified' : 'uv-unregistered']">
            {{ p.hasAccount ? '未验证' : '未注册' }}
          </span>
          <span class="uv-nickname">{{ p.nickname }}</span>
          <span class="uv-ip">{{ p.ip }}</span>
          <span class="uv-arrow">→</span>
        </div>
      </div>
    </div>

    <div v-if="loading" class="loading-state">
      <p>加载中...</p>
    </div>

    <div v-else-if="filteredUsers.length === 0" class="empty-state">
      <p>{{ searchQuery ? '未找到匹配的用户' : '暂无用户' }}</p>
    </div>

    <div v-else class="users-table-container">
      <table class="users-table">
        <thead>
          <tr>
            <th>状态</th>
            <th>ID</th>
            <th>用户名</th>
            <th>用户组</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="user in filteredUsers"
            :key="user.id"
            @click="handleRowClick(user)"
            class="clickable-row"
          >
            <td>
              <span v-if="isUserOnline(user.name)" class="online-indicator" title="在线"></span>
              <span v-else class="offline-indicator" title="离线"></span>
            </td>
            <td>{{ user.id }}</td>
            <td>{{ user.name }}</td>
            <td>{{ user.group }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>

    <!-- 创建用户模态框 -->
    <div v-if="showCreateModal" class="modal-overlay" @click.self="closeCreateModal">
      <div class="modal-dialog">
        <div class="modal-header">
          <h3>创建用户</h3>
          <button class="modal-close" @click="closeCreateModal">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label">用户名 <span class="required">*</span></label>
            <input
              v-model="newUsername"
              type="text"
              placeholder="输入用户名"
              class="form-input"
              @keyup.enter="executeCreateUser"
            />
          </div>
          <div class="form-group">
            <label class="form-label">密码 <span class="required">*</span></label>
            <input
              v-model="newPassword"
              type="password"
              placeholder="输入密码"
              class="form-input"
              @keyup.enter="executeCreateUser"
            />
          </div>
          <div class="form-group">
            <label class="form-label">权限组</label>
            <div class="custom-select-wrapper">
              <div class="custom-select-trigger" @click="toggleGroupDropdown">
                <span class="custom-select-value" :class="{ placeholder: !newGroup }">{{ newGroup || '请选择用户组' }}</span>
                <span class="custom-select-arrow" :class="{ rotated: showGroupDropdown }">▼</span>
              </div>
              <div v-if="showGroupDropdown" class="custom-select-dropdown">
                <div class="custom-select-option" :class="{ selected: !newGroup }" @click="selectGroup('')">默认权限组</div>
                <div v-for="g in groups" :key="g.name" class="custom-select-option" :class="{ selected: newGroup === g.name }" @click="selectGroup(g.name)">{{ g.name }}</div>
              </div>
            </div>
          </div>

          <div v-if="createError" class="error-msg">{{ createError }}</div>
          <div v-if="createSuccess" class="success-msg">{{ createSuccess }}</div>
        </div>
        <div class="modal-footer">
          <button class="modal-btn cancel" @click="closeCreateModal" :disabled="createLoading">取消</button>
          <button class="modal-btn confirm" @click="executeCreateUser" :disabled="createLoading">
            {{ createLoading ? '创建中...' : '确认创建' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 清空全部角色模态框 -->
    <div v-if="showClearAllDataModal" class="modal-overlay" @click.self="closeClearAllDataModal">
      <div class="modal-dialog modal-danger-border">
        <div class="modal-header">
          <h3>⚠️ 清空全部角色数据</h3>
          <button class="modal-close" @click="closeClearAllDataModal">×</button>
        </div>
        <div class="modal-body">
          <div class="danger-warning">
            <p class="warning-title">🚨 <strong>危险操作 — 不可撤销！</strong></p>
            <p>此操作将 <strong>永久删除所有玩家的角色数据</strong>（背包、装备、生命魔力、永久增益等）。</p>
            <p>每个玩家下次登录时将以全新角色进入服务器。</p>
          </div>
          <div class="form-group">
            <label class="form-label">你的用户名</label>
            <input
              v-model="clearAllDataUsername"
              type="text"
              placeholder="输入你的用户名以确认"
              class="form-input"
              autocomplete="off"
            />
          </div>
          <div class="form-group">
            <label class="form-label">你的密码</label>
            <input
              v-model="clearAllDataPassword"
              type="password"
              placeholder="输入你的密码以确认"
              class="form-input"
              autocomplete="new-password"
              @keyup.enter="executeClearAllData"
            />
          </div>

          <div v-if="clearAllDataError" class="error-msg">{{ clearAllDataError }}</div>
          <div v-if="clearAllDataSuccess" class="success-msg">{{ clearAllDataSuccess }}</div>
        </div>
        <div class="modal-footer">
          <button class="modal-btn cancel" @click="closeClearAllDataModal" :disabled="clearAllDataLoading">取消</button>
          <button class="modal-btn confirm danger-confirm" @click="executeClearAllData" :disabled="clearAllDataLoading">
            {{ clearAllDataLoading ? '执行中...' : '确认清空全部角色' }}
          </button>
        </div>
      </div>
    </div>
</template>

<style scoped>
.players-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 0;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
  padding: 0 20px;
}

.header-title {
  display: flex;
  align-items: center;
  gap: 14px;
}

.online-count-badge {
  padding: 4px 12px;
  background: var(--bg-tertiary);
  border-radius: 20px;
  font-size: 0.8rem;
  color: var(--text-muted);
  font-weight: 600;
  white-space: nowrap;
  border: 1px solid var(--border-light);
  transition: all 0.3s ease;
}

.online-count-badge.has-online {
  background: rgba(34, 197, 94, 0.12);
  color: #22c55e;
  border-color: rgba(34, 197, 94, 0.3);
}

.section-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.4rem;
  font-weight: 600;
}

.refresh-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
  display: flex;
  align-items: center;
  gap: 8px;
}

.refresh-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}

.refresh-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  background: var(--bg-hover);
}

.search-bar {
  padding: 0 20px;
  margin-bottom: 16px;
  display: flex;
  align-items: center;
  gap: 12px;
}

.search-input {
  width: 100%;
  max-width: 300px;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-lg);
  color: var(--text-primary);
  font-size: 0.95rem;
  transition: all 0.25s ease;
}

.search-input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.search-input::placeholder {
  color: var(--text-muted);
}

.loading-state,
.empty-state {
  text-align: center;
  padding: 60px 20px;
  color: var(--text-muted);
}

.users-table-container {
  flex: 1;
  overflow-y: auto;
  padding: 0 20px;
}

.users-table {
  width: 100%;
  border-collapse: collapse;
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  overflow: hidden;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
}

.users-table th {
  background: var(--bg-tertiary);
  padding: 14px 20px;
  text-align: left;
  color: var(--text-secondary);
  font-weight: 600;
  font-size: 0.9rem;
  border-bottom: 2px solid var(--border-color);
}

.users-table td {
  padding: 14px 20px;
  border-bottom: 1px solid var(--border-light);
  color: var(--text-primary);
  font-size: 0.95rem;
}

.clickable-row {
  cursor: pointer;
  transition: all 0.25s ease;
}

.clickable-row:hover {
  background: var(--bg-hover);
  transform: scale(1.002);
}

.clickable-row:active {
  background: var(--accent-primary);
}

.invsee-btn {
  padding: 8px 16px;
  background: linear-gradient(135deg, var(--accent-secondary), #16a34a);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.invsee-btn:hover {
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}

.online-indicator {
  display: inline-block;
  width: 10px;
  height: 10px;
  background: #22c55e;
  border-radius: 50%;
  box-shadow: 0 0 6px #22c55e;
}

.offline-indicator {
  display: inline-block;
  width: 10px;
  height: 10px;
  background: #6b7280;
  border-radius: 50%;
}

.create-user-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, #16a34a, #15803d);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 600;
  transition: all 0.25s ease;
  white-space: nowrap;
  box-shadow: var(--shadow-sm);
  flex-shrink: 0;
}

.create-user-btn:hover {
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}

.clear-all-data-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, #7c3aed, #6d28d9);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 600;
  transition: all 0.25s ease;
  white-space: nowrap;
  box-shadow: var(--shadow-sm);
  flex-shrink: 0;
}

.clear-all-data-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(124, 58, 237, 0.4);
}

.modal-danger-border {
  border: 2px solid #7c3aed;
}

.danger-warning {
  background: rgba(124, 58, 237, 0.1);
  border: 1px solid rgba(124, 58, 237, 0.3);
  border-radius: var(--radius-md);
  padding: 16px;
  margin-bottom: 20px;
}

.danger-warning p {
  margin: 6px 0;
  color: var(--text-primary);
  font-size: 0.9rem;
}

.danger-warning .warning-title {
  color: #7c3aed;
  font-size: 1rem;
  margin-bottom: 8px;
}

.modal-btn.confirm.danger-confirm {
  background: linear-gradient(135deg, #7c3aed, #6d28d9);
}

.modal-btn.confirm.danger-confirm:hover:not(:disabled) {
  opacity: 0.85;
}

/* 未登录玩家置顶区块 */
.unverified-section {
  margin: 0 20px 16px;
  background: rgba(239, 68, 68, 0.08);
  border: 1px solid rgba(239, 68, 68, 0.25);
  border-radius: var(--radius-xl);
  overflow: hidden;
}

.unverified-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.1);
  border-bottom: 1px solid rgba(239, 68, 68, 0.15);
}

.unverified-icon {
  font-size: 1rem;
}

.unverified-title {
  color: #ef4444;
  font-weight: 700;
  font-size: 0.9rem;
}

.unverified-list {
  display: flex;
  flex-direction: column;
}

.unverified-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 16px;
  cursor: pointer;
  transition: all 0.2s;
  border-bottom: 1px solid rgba(239, 68, 68, 0.08);
}

.unverified-item:last-child {
  border-bottom: none;
}

.unverified-item:hover {
  background: rgba(239, 68, 68, 0.1);
}

.uv-status {
  padding: 2px 10px;
  border-radius: 12px;
  font-size: 0.75rem;
  font-weight: 600;
  white-space: nowrap;
}

.uv-unregistered {
  background: rgba(239, 68, 68, 0.2);
  color: #ef4444;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.uv-unverified {
  background: rgba(245, 158, 11, 0.2);
  color: #f59e0b;
  border: 1px solid rgba(245, 158, 11, 0.3);
}

.uv-nickname {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.9rem;
  flex: 1;
}

.uv-ip {
  color: var(--text-muted);
  font-size: 0.8rem;
  font-family: monospace;
}

.uv-arrow {
  color: var(--text-muted);
  font-size: 0.9rem;
}

.unverified-item:hover .uv-arrow {
  color: var(--accent-primary);
}

/* 模态框通用样式 */
.modal-overlay {
  position: fixed;
  z-index: 1000;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
}

.modal-dialog {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 0;
  width: 90%;
  max-width: 440px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.4);
  border: 1px solid var(--border-light);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 18px 24px;
  border-bottom: 1px solid var(--border-light);
}

.modal-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.1rem;
}

.modal-close {
  background: none;
  border: none;
  color: var(--text-muted);
  font-size: 1.5rem;
  cursor: pointer;
  padding: 0;
  line-height: 1;
}

.modal-close:hover {
  color: var(--text-primary);
}

.modal-body {
  padding: 24px;
}

.form-group {
  margin-bottom: 18px;
  position: relative;
  width: 100%;
}

.form-label {
  display: block;
  color: var(--text-primary);
  font-size: 0.9rem;
  font-weight: 600;
  margin-bottom: 6px;
}

.required {
  color: var(--accent-error);
}

.form-input {
  width: 100%;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  transition: all 0.25s ease;
  box-sizing: border-box;
}

.form-input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.form-input::placeholder {
  color: var(--text-muted);
}

.custom-select-wrapper {
  position: relative;
  width: 100%;
}

.custom-select-trigger {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  cursor: pointer;
  transition: all 0.25s ease;
  box-sizing: border-box;
}

.custom-select-trigger:hover {
  border-color: var(--accent-primary);
}

.custom-select-value {
  color: var(--text-primary);
}

.custom-select-value.placeholder {
  color: var(--text-muted);
}

.custom-select-arrow {
  color: var(--text-muted);
  font-size: 0.75rem;
  transition: transform 0.2s ease;
}

.custom-select-arrow.rotated {
  transform: rotate(180deg);
}

.custom-select-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  z-index: 100;
  background: var(--bg-card);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  max-height: 200px;
  overflow-y: auto;
  box-shadow: var(--shadow-lg);
}

.custom-select-option {
  padding: 12px 16px;
  color: var(--text-primary);
  font-size: 0.95rem;
  cursor: pointer;
  transition: all 0.2s ease;
  border-bottom: 1px solid var(--border-light);
}

.custom-select-option:last-child {
  border-bottom: none;
}

.custom-select-option:hover {
  background: rgba(99, 102, 241, 0.15);
  color: var(--accent-primary);
}

.custom-select-option.selected {
  background: rgba(99, 102, 241, 0.2);
  color: var(--accent-primary);
  font-weight: 600;
}

.error-msg {
  padding: 10px 14px;
  background: rgba(239, 68, 68, 0.1);
  color: var(--accent-error);
  border-radius: var(--radius-md);
  border: 1px solid rgba(239, 68, 68, 0.3);
  font-size: 0.85rem;
  margin-top: 4px;
}

.success-msg {
  padding: 10px 14px;
  background: rgba(22, 163, 74, 0.1);
  color: #16a34a;
  border-radius: var(--radius-md);
  border: 1px solid rgba(22, 163, 74, 0.3);
  font-size: 0.85rem;
  margin-top: 4px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 24px;
  border-top: 1px solid var(--border-light);
}

.modal-btn {
  padding: 10px 24px;
  border: none;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
}

.modal-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.modal-btn.cancel {
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
}

.modal-btn.cancel:hover:not(:disabled) {
  background: var(--border-color);
}

.modal-btn.confirm {
  background: var(--accent-primary);
  color: #fff;
}

.modal-btn.confirm:hover:not(:disabled) {
  opacity: 0.85;
}
</style>

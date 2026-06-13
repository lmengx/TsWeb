<script setup>
import { ref, onMounted } from 'vue'
import { get, post } from '../utils/api.js'
import { getPermissionName, searchPermissions } from '../utils/permissionMap.js'

const groups = ref([])
const loading = ref(false)
const error = ref('')
const selectedGroup = ref(null)
const showModal = ref(false)
const modalMode = ref('create')

const formData = ref({
  groupName: '',
  parent: '',
  commands: '',
  chatColor: '',
  prefix: '',
  suffix: ''
})

const permissionInput = ref('')
const permissionSuggestions = ref([])
const focusedGroup = ref(null)

const handlePermissionInput = (groupName) => {
  focusedGroup.value = groupName
  permissionSuggestions.value = searchPermissions(permissionInput.value)
}

const selectPermission = (permissionKey) => {
  permissionInput.value = permissionKey
  permissionSuggestions.value = []
}

const clearFocus = () => {
  setTimeout(() => {
    focusedGroup.value = null
    permissionSuggestions.value = []
  }, 200)
}

const fetchGroups = async () => {
  loading.value = true
  error.value = ''
  try {
    const response = await get('/api/tshock/groups')
    const result = await response.json()
    if (result.error) {
      error.value = result.error
    } else {
      groups.value = result.groups || []
    }
  } catch (err) {
    error.value = '获取组列表失败: ' + err.message
  }
  loading.value = false
}

const openCreateModal = () => {
  modalMode.value = 'create'
  formData.value = {
    groupName: '',
    parent: '',
    commands: '',
    chatColor: '',
    prefix: '',
    suffix: ''
  }
  showModal.value = true
}

const openEditModal = (group) => {
  modalMode.value = 'edit'
  selectedGroup.value = group
  formData.value = {
    groupName: group.GroupName,
    parent: group.Parent || '',
    commands: group.Commands || '',
    chatColor: group.ChatColor || '',
    prefix: group.Prefix || '',
    suffix: group.Suffix || ''
  }
  showModal.value = true
}

const closeModal = () => {
  showModal.value = false
  selectedGroup.value = null
}

const handleSave = async () => {
  error.value = ''
  try {
    if (modalMode.value === 'create') {
      const response = await post('/api/tshock/groups/create', formData.value)
      const result = await response.json()
      if (result.error) {
        error.value = result.error
        return
      }
    } else {
      const updateData = {
        groupName: formData.value.groupName,
        parent: formData.value.parent,
        chatColor: formData.value.chatColor,
        prefix: formData.value.prefix,
        suffix: formData.value.suffix
      }
      const response = await post('/api/tshock/groups/update', updateData)
      const result = await response.json()
      if (result.error) {
        error.value = result.error
        return
      }
    }
    closeModal()
    await fetchGroups()
  } catch (err) {
    error.value = '保存失败: ' + err.message
  }
}

const handleDelete = async (groupName) => {
  if (!confirm(`确定要删除组 "${groupName}" 吗？`)) return

  error.value = ''
  try {
    const response = await post('/api/tshock/groups/delete', { groupName })
    const result = await response.json()
    if (result.error) {
      error.value = result.error
      return
    }
    await fetchGroups()
  } catch (err) {
    error.value = '删除失败: ' + err.message
  }
}

const handleAddPermission = async (groupName) => {
  if (!permissionInput.value.trim()) return

  error.value = ''
  try {
    const response = await post('/api/tshock/groups/permission/add', {
      groupName,
      permission: permissionInput.value.trim()
    })
    const result = await response.json()
    if (result.error) {
      error.value = result.error
      return
    }
    permissionInput.value = ''
    await fetchGroups()
  } catch (err) {
    error.value = '添加权限失败: ' + err.message
  }
}

const handleRemovePermission = async (groupName, permission) => {
  error.value = ''
  try {
    const response = await post('/api/tshock/groups/permission/remove', {
      groupName,
      permission
    })
    const result = await response.json()
    if (result.error) {
      error.value = result.error
      return
    }
    await fetchGroups()
  } catch (err) {
    error.value = '删除权限失败: ' + err.message
  }
}

const getPermissionsList = (commandsStr) => {
  if (!commandsStr) return []
  return commandsStr.split(',').filter(p => p.trim())
}

onMounted(() => {
  fetchGroups()
})
</script>

<template>
  <div class="group-manager">
    <div class="header">
      <h2>组管理</h2>
      <button @click="openCreateModal" class="btn-primary">创建组</button>
    </div>

    <div v-if="error" class="error-message">{{ error }}</div>

    <div v-if="loading" class="loading">加载中...</div>

    <div v-else class="groups-grid">
      <div
        v-for="group in groups"
        :key="group.GroupName"
        class="group-card"
      >
        <div class="group-header">
          <h3>{{ group.GroupName }}</h3>
          <div class="group-actions">
            <button @click="openEditModal(group)" class="btn-small">编辑</button>
            <button @click="handleDelete(group.GroupName)" class="btn-small btn-danger">删除</button>
          </div>
        </div>

        <div class="group-info">
          <div v-if="group.Parent" class="info-row">
            <span class="label">父组:</span>
            <span class="value">{{ group.Parent }}</span>
          </div>
          <div v-if="group.ChatColor" class="info-row">
            <span class="label">颜色:</span>
            <span class="value">{{ group.ChatColor }}</span>
          </div>
          <div v-if="group.Prefix" class="info-row">
            <span class="label">前缀:</span>
            <span class="value">{{ group.Prefix }}</span>
          </div>
          <div v-if="group.Suffix" class="info-row">
            <span class="label">后缀:</span>
            <span class="value">{{ group.Suffix }}</span>
          </div>
        </div>

        <div class="group-permissions">
          <span class="label">权限:</span>
          <div class="permissions-list">
            <span
              v-for="perm in getPermissionsList(group.Commands)"
              :key="perm"
              class="permission-tag"
              @click="handleRemovePermission(group.GroupName, perm)"
              title="点击删除"
            >
              {{ perm }} ({{ getPermissionName(perm) }}) ×
            </span>
            <span v-if="!group.Commands" class="no-permissions">无</span>
          </div>
        </div>

        <div class="add-permission">
          <div class="permission-input-wrapper">
            <input
              v-model="permissionInput"
              type="text"
              placeholder="添加权限..."
              @keyup.enter="handleAddPermission(group.GroupName)"
              @input="handlePermissionInput(group.GroupName)"
              @focus="handlePermissionInput(group.GroupName)"
              @blur="clearFocus"
            />
            <div v-if="permissionSuggestions.length > 0 && focusedGroup === group.GroupName" class="suggestions-dropdown">
              <div
                v-for="item in permissionSuggestions"
                :key="item.key"
                class="suggestion-item"
                @click="selectPermission(item.key)"
              >
                <span class="suggestion-key">{{ item.key }}</span>
                <span class="suggestion-value">{{ item.value }}</span>
              </div>
            </div>
          </div>
          <button @click="handleAddPermission(group.GroupName)" class="btn-small">添加</button>
        </div>
      </div>

      <div v-if="groups.length === 0 && !loading" class="empty-state">
        暂无组数据
      </div>
    </div>

    <div v-if="showModal" class="modal-overlay" @click.self="closeModal">
      <div class="modal">
        <div class="modal-header">
          <h3>{{ modalMode === 'create' ? '创建组' : '编辑组' }}</h3>
          <button @click="closeModal" class="close-btn">×</button>
        </div>

        <div class="modal-body">
          <div class="form-group">
            <label>组名</label>
            <input
              v-model="formData.groupName"
              type="text"
              :disabled="modalMode === 'edit'"
              placeholder="输入组名"
            />
          </div>

          <div class="form-group">
            <label>父组</label>
            <input
              v-model="formData.parent"
              type="text"
              placeholder="输入父组名称"
            />
          </div>

          <div class="form-group">
            <label>聊天颜色</label>
            <input
              v-model="formData.chatColor"
              type="text"
              placeholder="例如: 255 255 255"
            />
          </div>

          <div class="form-group">
            <label>前缀</label>
            <input
              v-model="formData.prefix"
              type="text"
              placeholder="输入前缀"
            />
          </div>

          <div class="form-group">
            <label>后缀</label>
            <input
              v-model="formData.suffix"
              type="text"
              placeholder="输入后缀"
            />
          </div>

          <div v-if="modalMode === 'edit'" class="form-group">
            <label>权限</label>
            <input
              v-model="formData.commands"
              type="text"
              placeholder="用逗号分隔的权限列表"
            />
          </div>

          <div v-if="error" class="error-message">{{ error }}</div>
        </div>

        <div class="modal-footer">
          <button @click="closeModal" class="btn-secondary">取消</button>
          <button @click="handleSave" class="btn-primary">
            {{ modalMode === 'create' ? '创建' : '保存' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.group-manager {
  padding: 20px;
  height: 100%;
  overflow-y: auto;
}

.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
}

.header h2 {
  margin: 0;
  color: var(--text-primary);
}

.btn-primary {
  padding: 10px 20px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-weight: 500;
}

.btn-primary:hover {
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}

.btn-secondary {
  padding: 10px 20px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
}

.btn-small {
  padding: 6px 12px;
  font-size: 0.85rem;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  cursor: pointer;
}

.btn-danger {
  color: var(--accent-error);
  border-color: var(--accent-error);
}

.btn-danger:hover {
  background: rgba(239, 68, 68, 0.1);
}

.error-message {
  padding: 12px;
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border-radius: var(--radius-md);
  margin-bottom: 16px;
}

.loading {
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.groups-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

.group-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-lg);
  padding: 16px;
}

.group-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--border-light);
}

.group-header h3 {
  margin: 0;
  color: var(--text-primary);
}

.group-actions {
  display: flex;
  gap: 8px;
}

.group-info {
  margin-bottom: 12px;
}

.info-row {
  display: flex;
  gap: 8px;
  margin-bottom: 4px;
  font-size: 0.9rem;
}

.info-row .label {
  color: var(--text-muted);
}

.info-row .value {
  color: var(--text-primary);
}

.group-permissions {
  margin-bottom: 12px;
}

.group-permissions .label {
  display: block;
  color: var(--text-muted);
  font-size: 0.85rem;
  margin-bottom: 6px;
}

.permissions-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.permission-tag {
  padding: 4px 8px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border-radius: var(--radius-sm);
  font-size: 0.85rem;
  cursor: pointer;
}

.permission-tag:hover {
  background: rgba(239, 68, 68, 0.2);
  color: var(--accent-error);
}

.no-permissions {
  color: var(--text-muted);
  font-size: 0.85rem;
}

.add-permission {
  display: flex;
  gap: 8px;
}

.add-permission input {
  flex: 1;
  padding: 8px 12px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 0.85rem;
}

.permission-input-wrapper {
  position: relative;
  flex: 1;
}

.permission-input-wrapper input {
  width: 100%;
}

.suggestions-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg);
  max-height: 200px;
  overflow-y: auto;
  z-index: 100;
}

.suggestion-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  cursor: pointer;
  border-bottom: 1px solid var(--border-light);
}

.suggestion-item:last-child {
  border-bottom: none;
}

.suggestion-item:hover {
  background: var(--bg-tertiary);
}

.suggestion-key {
  font-size: 0.85rem;
  color: var(--text-primary);
  font-family: monospace;
}

.suggestion-value {
  font-size: 0.8rem;
  color: var(--text-muted);
  margin-left: 12px;
}

.empty-state {
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.7);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  width: 90%;
  max-width: 450px;
  max-height: 85vh;
  overflow-y: auto;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border-light);
}

.modal-header h3 {
  margin: 0;
  color: var(--text-primary);
}

.close-btn {
  background: none;
  border: none;
  font-size: 1.5rem;
  color: var(--text-muted);
  cursor: pointer;
}

.modal-body {
  padding: 20px;
}

.form-group {
  margin-bottom: 16px;
}

.form-group label {
  display: block;
  margin-bottom: 6px;
  color: var(--text-secondary);
  font-weight: 500;
  font-size: 0.9rem;
}

.form-group input {
  width: 100%;
  padding: 10px 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  box-sizing: border-box;
}

.form-group input:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.form-group input:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 20px;
  border-top: 1px solid var(--border-light);
}
</style>
<script setup>
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { get, post } from '../utils/api.js'
import { getPermissionName, searchPermissions, permissionMap } from '../utils/permissionMap.js'

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

const permissionModal = ref(false)
const permissionTargetGroup = ref('')
const permissionInput = ref('')
const permissionSuggestions = ref([])
const permissionSuggestionsStyle = ref({})
const permissionContextMenu = ref({
  show: false,
  x: 0,
  y: 0,
  permission: '',
  groupName: ''
})
const quickAddModal = ref(false)
const quickAddTargetGroup = ref('')
const quickAddLoading = ref(false)
const quickAddSelected = ref([])
const groupSelectOpen = ref(false)
const groupSelectStyle = ref({})
const quickAddActiveTab = ref('normal')
const reasonablePermissionKeys = [
  'tshock.npc.hurttown',
  'tshock.npc.spawnpets',
  'tshock.npc.startdd2',
  'tshock.npc.startinvasion',
  'tshock.npc.summonboss',
  'tshock.tp.demonconch',
  'tshock.tp.magicconch',
  'tshock.tp.pylon',
  'tshock.tp.rod',
  'tshock.tp.tppotion',
  'tshock.tp.wormhole',
  'tshock.world.movenpc',
  'tshock.world.time.usemoondial',
  'tshock.world.time.usesundial',
  'tshock.world.worldupgrades'
]
const tpPermissionKeys = [
  'tshock.tp.self',
  'tshock.tp.block',
  'tshock.tp.spawn',
  'tshock.tp.home'
]

const availableGroups = computed(() => {
  return groups.value.filter(g => g.GroupName.toLowerCase() !== 'guest')
})

const handlePermissionInput = () => {
  permissionSuggestions.value = searchPermissions(permissionInput.value)
  if (permissionSuggestions.value.length > 0) {
    updatePermissionSuggestionsPosition()
  }
}

const updatePermissionSuggestionsPosition = () => {
  const input = document.querySelector('.permission-input-wrapper input')
  if (input) {
    const rect = input.getBoundingClientRect()
    permissionSuggestionsStyle.value = {
      position: 'fixed',
      top: `${rect.bottom + 4}px`,
      left: `${rect.left}px`,
      width: `${rect.width}px`,
      zIndex: 2000
    }
  }
}
const selectPermission = (permissionKey) => {
  permissionInput.value = permissionKey
  permissionSuggestions.value = []
}
const openPermissionModal = (groupName) => {
  permissionTargetGroup.value = groupName
  permissionInput.value = ''
  permissionSuggestions.value = []
  permissionModal.value = true
}
const closePermissionModal = () => {
  permissionModal.value = false
  permissionTargetGroup.value = ''
  permissionInput.value = ''
  permissionSuggestions.value = []
}
const openQuickAddModal = () => {
  quickAddTargetGroup.value = ''
  quickAddActiveTab.value = 'normal'
  loadQuickAddPermissions('normal')
  quickAddModal.value = true
}
const switchQuickAddTab = (tab) => {
  quickAddActiveTab.value = tab
  loadQuickAddPermissions(tab)
}
const loadQuickAddPermissions = (tab) => {
  const keys = tab === 'normal' ? reasonablePermissionKeys : tpPermissionKeys
  quickAddSelected.value = keys.map(key => ({
    key,
    name: permissionMap[key] || key,
    checked: true
  }))
}
const closeQuickAddModal = () => {
  quickAddModal.value = false
  quickAddTargetGroup.value = ''
  quickAddSelected.value = []
  groupSelectOpen.value = false
}
const toggleGroupSelect = () => {
  groupSelectOpen.value = !groupSelectOpen.value
  if (groupSelectOpen.value) {
    updateGroupSelectPosition()
  }
}

const updateGroupSelectPosition = () => {
  const trigger = document.querySelector('.group-select-trigger')
  if (trigger) {
    const rect = trigger.getBoundingClientRect()
    groupSelectStyle.value = {
      position: 'fixed',
      top: `${rect.bottom + 4}px`,
      left: `${rect.left}px`,
      width: `${rect.width}px`,
      zIndex: 2000
    }
  }
}

const selectGroup = (groupName) => {
  quickAddTargetGroup.value = groupName
  groupSelectOpen.value = false
}
const toggleQuickPermission = (index) => {
  quickAddSelected.value[index].checked = !quickAddSelected.value[index].checked
}
const handleQuickAddPermissions = async () => {
  if (!quickAddTargetGroup.value) {
    error.value = '请选择目标组'
    return
  }
  const selectedPermissions = quickAddSelected.value.filter(p => p.checked).map(p => p.key)
  if (selectedPermissions.length === 0) {
    error.value = '请至少选择一个权限'
    return
  }
  quickAddLoading.value = true
  error.value = ''
  let successCount = 0
  let failCount = 0
  for (const permission of selectedPermissions) {
    try {
      const response = await post('/api/tshock/groups/permission/add', {
        groupName: quickAddTargetGroup.value,
        permission
      })
      const result = await response.json()
      if (result.error) {
        failCount++
      } else {
        successCount++
      }
    } catch (err) {
      failCount++
    }
  }
  quickAddLoading.value = false
  if (failCount === 0) {
    closeQuickAddModal()
    await fetchGroups()
  } else if (successCount > 0) {
    error.value = `成功添加 ${successCount} 个权限，失败 ${failCount} 个`
  } else {
    error.value = '添加权限失败'
  }
}
const openPermissionMenu = (event, groupName, permission) => {
  event.preventDefault()
  event.stopPropagation()
  permissionContextMenu.value = {
    show: true,
    x: event.clientX,
    y: event.clientY,
    permission,
    groupName
  }
}
const closePermissionMenu = () => {
  permissionContextMenu.value.show = false
}
const copyPermission = async (permission) => {
  try {
    await navigator.clipboard.writeText(permission)
    error.value = ''
  } catch (err) {
    error.value = '复制失败'
  }
  closePermissionMenu()
}
const deletePermission = async () => {
  const { groupName, permission } = permissionContextMenu.value
  closePermissionMenu()
  await handleRemovePermission(groupName, permission)
}
const handleClickOutside = (event) => {
  if (permissionContextMenu.value.show && !event.target.closest('.context-menu')) {
    closePermissionMenu()
  }
  if (groupSelectOpen.value && !event.target.closest('.group-select-wrapper')) {
    groupSelectOpen.value = false
  }
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
const handleAddPermission = async () => {
  if (!permissionInput.value.trim()) return
  error.value = ''
  try {
    const response = await post('/api/tshock/groups/permission/add', {
      groupName: permissionTargetGroup.value,
      permission: permissionInput.value.trim()
    })
    const result = await response.json()
    if (result.error) {
      error.value = result.error
      return
    }
    closePermissionModal()
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
  document.addEventListener('click', handleClickOutside)
})
onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
})
</script>

<template>
  <div class="group-manager">
    <div class="header">
      <h2>组管理</h2>
      <div class="header-actions">
        <button @click="openQuickAddModal" class="btn-quick">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>
          快速添加权限
        </button>
        <button @click="openCreateModal" class="btn-primary">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>
          创建组
        </button>
      </div>
    </div>
    <div v-if="error" class="error-message">{{ error }}</div>
    <div v-if="loading" class="loading">
      <div class="spinner"></div>
      <span>加载中...</span>
    </div>
    <div v-else class="groups-grid">
      <div
        v-for="group in groups"
        :key="group.GroupName"
        class="group-card"
      >
        <div class="group-header">
          <h3>{{ group.GroupName }}</h3>
          <div class="group-actions">
            <button @click="openEditModal(group)" class="btn-icon" title="编辑">
              <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
            </button>
            <button @click="handleDelete(group.GroupName)" class="btn-icon btn-danger-icon" title="删除">
              <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
            </button>
          </div>
        </div>
        <div class="group-info">
          <div v-if="group.Parent" class="info-row">
            <span class="label">父组</span>
            <span class="value">{{ group.Parent }}</span>
          </div>
          <div v-if="group.ChatColor" class="info-row">
            <span class="label">颜色</span>
            <span class="value">{{ group.ChatColor }}</span>
          </div>
          <div v-if="group.Prefix" class="info-row">
            <span class="label">前缀</span>
            <span class="value">{{ group.Prefix }}</span>
          </div>
          <div v-if="group.Suffix" class="info-row">
            <span class="label">后缀</span>
            <span class="value">{{ group.Suffix }}</span>
          </div>
        </div>
        <div class="group-permissions">
          <div class="permissions-header">
            <span class="label">权限</span>
            <button @click="openPermissionModal(group.GroupName)" class="btn-add-small">
              <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>
              添加
            </button>
          </div>
          <div class="permissions-list">
            <span
              v-for="perm in getPermissionsList(group.Commands)"
              :key="perm"
              class="permission-tag"
              @click="openPermissionMenu($event, group.GroupName, perm)"
            >
              {{ perm }} ({{ getPermissionName(perm) }})
            </span>
            <span v-if="!group.Commands" class="no-permissions">无权限</span>
          </div>
        </div>
      </div>
      <div v-if="groups.length === 0 && !loading" class="empty-state">
        <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
        <p>暂无组数据</p>
      </div>
    </div>
    <div
      v-if="permissionContextMenu.show"
      class="context-menu"
      :style="{ left: permissionContextMenu.x + 'px', top: permissionContextMenu.y + 'px' }"
    >
      <div class="context-menu-item" @click="copyPermission(permissionContextMenu.permission)">
        <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>
        复制权限名称
      </div>
      <div class="context-menu-item danger" @click="deletePermission">
        <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
        删除权限
      </div>
    </div>
    <div v-if="showModal" class="modal-overlay" @click.self="closeModal">
      <div class="modal">
        <div class="modal-header">
          <h3>{{ modalMode === 'create' ? '创建组' : '编辑组' }}</h3>
          <button @click="closeModal" class="close-btn">
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
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
    <div v-if="permissionModal" class="modal-overlay" @click.self="closePermissionModal">
      <div class="modal permission-modal">
        <div class="modal-header">
          <h3>添加权限 - {{ permissionTargetGroup }}</h3>
          <button @click="closePermissionModal" class="close-btn">
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label>权限名称</label>
            <div class="permission-input-wrapper">
              <input
                v-model="permissionInput"
                type="text"
                placeholder="输入权限名称或中文描述..."
                @input="handlePermissionInput"
                @keyup.enter="handleAddPermission"
              />
            </div>
          </div>
          
          <Teleport to="body">
            <div 
              v-if="permissionSuggestions.length > 0 && permissionModal" 
              class="suggestions-dropdown-teleport"
              :style="permissionSuggestionsStyle"
            >
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
          </Teleport>
          
          <div v-if="error" class="error-message">{{ error }}</div>
        </div>
        <div class="modal-footer">
          <button @click="closePermissionModal" class="btn-secondary">取消</button>
          <button @click="handleAddPermission" class="btn-primary" :disabled="!permissionInput.trim()">
            添加
          </button>
        </div>
      </div>
    </div>
    <div v-if="quickAddModal" class="modal-overlay" @click.self="closeQuickAddModal">
      <div class="modal quick-add-modal">
        <div class="modal-header">
          <h3>快速添加合理权限</h3>
          <button @click="closeQuickAddModal" class="close-btn">
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label>选择目标组</label>
            <div class="group-select-wrapper">
              <div class="group-select-trigger" @click="toggleGroupSelect">
                <span :class="{ placeholder: !quickAddTargetGroup }">
                  {{ quickAddTargetGroup || '请选择组' }}
                </span>
                <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" :class="{ rotated: groupSelectOpen }"><polyline points="6 9 12 15 18 9"/></svg>
              </div>
            </div>
          </div>
          
          <Teleport to="body">
            <div 
              v-if="groupSelectOpen && quickAddModal" 
              class="group-select-dropdown-teleport"
              :style="groupSelectStyle"
            >
              <div
                v-for="group in availableGroups"
                :key="group.GroupName"
                class="group-option"
                :class="{ selected: quickAddTargetGroup === group.GroupName }"
                @click="selectGroup(group.GroupName)"
              >
                <span>{{ group.GroupName }}</span>
                <svg v-if="quickAddTargetGroup === group.GroupName" xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
              </div>
            </div>
          </Teleport>
          
          <div class="quick-add-tabs">
            <button
              class="tab-item"
              :class="{ active: quickAddActiveTab === 'normal' }"
              @click="switchQuickAddTab('normal')"
            >
              常规权限
            </button>
            <button
              class="tab-item"
              :class="{ active: quickAddActiveTab === 'tp' }"
              @click="switchQuickAddTab('tp')"
            >
              TP系列权限
            </button>
          </div>
          <p class="quick-add-desc">
            {{ quickAddActiveTab === 'normal' ? '选择需要添加的合理权限（默认全选）:' : '选择需要添加的TP权限（默认全选）:' }}
          </p>
          <div class="permission-grid">
            <div
              v-for="(perm, index) in quickAddSelected"
              :key="perm.key"
              class="permission-card"
              :class="{ selected: perm.checked }"
              @click="toggleQuickPermission(index)"
            >
              <div class="permission-checkbox-wrapper">
                <input
                  type="checkbox"
                  :checked="perm.checked"
                  @click.stop
                  class="permission-checkbox"
                />
              </div>
              <div class="permission-info">
                <span class="permission-name">{{ perm.name }}</span>
                <span class="permission-key">{{ perm.key }}</span>
              </div>
            </div>
          </div>
          <div v-if="error" class="error-message">{{ error }}</div>
        </div>
        <div class="modal-footer">
          <button @click="closeQuickAddModal" class="btn-secondary">取消</button>
          <button
            @click="handleQuickAddPermissions"
            class="btn-primary"
            :disabled="quickAddLoading || !quickAddTargetGroup || quickAddSelected.filter(p => p.checked).length === 0"
          >
            {{ quickAddLoading ? '添加中...' : '添加所选权限' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
<style scoped>
.group-manager {
  padding: 24px;
  height: 100%;
  overflow-y: auto;
}
.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 24px;
}
.header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
  font-weight: 600;
}
.header-actions {
  display: flex;
  gap: 12px;
}
.btn-primary {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 18px;
  background: var(--accent-primary);
  color: white;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-weight: 500;
  transition: all 0.2s ease;
}
.btn-primary:hover {
  background: var(--accent-primary-hover);
  transform: translateY(-1px);
}
.btn-primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  transform: none;
}
.btn-secondary {
  padding: 10px 18px;
  background: transparent;
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  cursor: pointer;
  font-weight: 500;
  transition: all 0.2s ease;
}
.btn-secondary:hover {
  background: var(--bg-tertiary);
  border-color: var(--text-secondary);
}
.btn-quick {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 18px;
  background: #10b981;
  color: white;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-weight: 500;
  transition: all 0.2s ease;
}
.btn-quick:hover {
  background: #059669;
  transform: translateY(-1px);
}
.btn-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  background: transparent;
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s ease;
}
.btn-icon:hover {
  background: var(--bg-tertiary);
  color: var(--accent-primary);
  border-color: var(--accent-primary);
}
.btn-danger-icon:hover {
  background: rgba(239, 68, 68, 0.1);
  color: var(--accent-error);
  border-color: var(--accent-error);
}
.btn-add-small {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  background: var(--accent-primary);
  color: white;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 500;
  transition: all 0.2s ease;
}
.btn-add-small:hover {
  background: var(--accent-primary-hover);
}
.error-message {
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.1);
  color: var(--accent-error);
  border-radius: 8px;
  margin-bottom: 16px;
  border: 1px solid rgba(239, 68, 68, 0.2);
}
.loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 48px;
  color: var(--text-muted);
}
.spinner {
  width: 24px;
  height: 24px;
  border: 2px solid var(--border-color);
  border-top-color: var(--accent-primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
@keyframes spin {
  to { transform: rotate(360deg); }
}
.groups-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
  gap: 20px;
}
.group-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 12px;
  padding: 20px;
  transition: all 0.2s ease;
}
.group-card:hover {
  border-color: var(--border-color);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
}
.group-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}
.group-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.1rem;
  font-weight: 600;
}
.group-actions {
  display: flex;
  gap: 8px;
}
.group-info {
  margin-bottom: 16px;
}
.info-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  font-size: 0.9rem;
}
.info-row .label {
  color: var(--text-muted);
  font-weight: 500;
}
.info-row .value {
  color: var(--text-primary);
}
.group-permissions {
  margin-bottom: 8px;
}
.permissions-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.permissions-header .label {
  color: var(--text-muted);
  font-size: 0.9rem;
  font-weight: 500;
}
.permissions-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.permission-tag {
  padding: 6px 12px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border-radius: 6px;
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.2s ease;
  border: 1px solid transparent;
}
.permission-tag:hover {
  background: var(--accent-primary);
  color: white;
  border-color: var(--accent-primary);
}
.no-permissions {
  color: var(--text-muted);
  font-size: 0.85rem;
  padding: 6px 0;
}
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 48px;
  color: var(--text-muted);
  gap: 12px;
}
.empty-state svg {
  opacity: 0.5;
}
.empty-state p {
  margin: 0;
  font-size: 0.95rem;
}
.context-menu {
  position: fixed;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  z-index: 2000;
  min-width: 180px;
  overflow: hidden;
  padding: 4px;
}
.context-menu-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  cursor: pointer;
  color: var(--text-primary);
  font-size: 0.9rem;
  border-radius: 6px;
  transition: background 0.15s ease;
}
.context-menu-item:hover {
  background: var(--bg-tertiary);
}
.context-menu-item.danger {
  color: var(--accent-error);
}
.context-menu-item.danger:hover {
  background: rgba(239, 68, 68, 0.1);
}
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}
.modal {
  background: var(--bg-card);
  border-radius: 16px;
  width: 90%;
  max-width: 450px;
  max-height: 85vh;
  overflow: hidden;
  box-shadow: 0 20px 40px rgba(0, 0, 0, 0.2);
}
.permission-modal {
  max-width: 500px;
}
.quick-add-modal {
  max-width: 650px;
}
.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px 24px;
  border-bottom: 1px solid var(--border-light);
}
.modal-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.2rem;
  font-weight: 600;
}
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: none;
  color: var(--text-muted);
  cursor: pointer;
  padding: 4px;
  border-radius: 6px;
  transition: all 0.2s ease;
}
.close-btn:hover {
  background: var(--bg-tertiary);
  color: var(--text-primary);
}
.modal-body {
  padding: 24px;
}
.form-group {
  margin-bottom: 20px;
}
.form-group label {
  display: block;
  margin-bottom: 8px;
  color: var(--text-secondary);
  font-weight: 500;
  font-size: 0.9rem;
}
.form-group input {
  width: 100%;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  color: var(--text-primary);
  font-size: 0.95rem;
  box-sizing: border-box;
  transition: all 0.2s ease;
}
.form-group input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(var(--accent-primary-rgb), 0.1);
}
.form-group input:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.group-select-wrapper {
  position: relative;
}
.group-select-trigger {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s ease;
}
.group-select-trigger:hover {
  border-color: var(--text-muted);
}
.group-select-trigger .placeholder {
  color: var(--text-muted);
}
.group-select-trigger svg {
  transition: transform 0.2s ease;
}
.group-select-trigger svg.rotated {
  transform: rotate(180deg);
}
.group-select-dropdown,
.group-select-dropdown-teleport {
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  max-height: 240px;
  overflow-y: auto;
}
.group-option {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  cursor: pointer;
  transition: background 0.15s ease;
  border-bottom: 1px solid var(--border-light);
}
.group-option:last-child {
  border-bottom: none;
}
.group-option:hover {
  background: var(--bg-tertiary);
}
.group-option.selected {
  background: rgba(var(--accent-primary-rgb), 0.1);
  color: var(--accent-primary);
}
.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 20px 24px;
  border-top: 1px solid var(--border-light);
}
.permission-input-wrapper {
  position: relative;
}
.permission-input-wrapper input {
  width: 100%;
}
.suggestions-dropdown,
.suggestions-dropdown-teleport {
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  max-height: 240px;
  overflow-y: auto;
}
.suggestion-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  cursor: pointer;
  border-bottom: 1px solid var(--border-light);
  transition: background 0.15s ease;
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
.quick-add-tabs {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
  background: var(--bg-tertiary);
  padding: 4px;
  border-radius: 8px;
}
.tab-item {
  flex: 1;
  padding: 10px 16px;
  background: transparent;
  border: none;
  border-radius: 6px;
  color: var(--text-secondary);
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
}
.tab-item:hover {
  background: rgba(0, 0, 0, 0.05);
}
.tab-item.active {
  background: var(--bg-card);
  color: var(--accent-primary);
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.05);
}
.quick-add-desc {
  color: var(--text-muted);
  font-size: 0.9rem;
  margin-bottom: 16px;
}
.permission-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 12px;
}
.permission-card {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 14px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s ease;
}
.permission-card:hover {
  background: var(--bg-secondary);
  border-color: var(--text-muted);
}
.permission-card.selected {
  background: rgba(var(--accent-primary-rgb), 0.1);
  border-color: var(--accent-primary);
}
.permission-checkbox-wrapper {
  display: flex;
  align-items: center;
  padding-top: 2px;
}
.permission-checkbox {
  width: 18px;
  height: 18px;
  cursor: pointer;
  accent-color: var(--accent-primary);
}
.permission-info {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.permission-name {
  font-size: 0.9rem;
  color: var(--text-primary);
  font-weight: 500;
}
.permission-key {
  font-size: 0.75rem;
  color: var(--text-muted);
  font-family: monospace;
}
</style>
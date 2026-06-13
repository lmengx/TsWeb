<script setup>
import { ref, computed } from 'vue'

const props = defineProps({
  users: {
    type: Array,
    default: () => []
  },
  activeUsers: {
    type: Array,
    default: () => []
  },
  loading: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['refresh', 'goToUserDetail'])

const searchQuery = ref('')

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
      <h2>玩家列表</h2>
      <button @click="emit('refresh')" :disabled="loading" class="refresh-btn">
        {{ loading ? '刷新中...' : '刷新' }}
      </button>
    </div>

    <div class="search-bar">
      <input
        v-model="searchQuery"
        type="text"
        placeholder="搜索用户名..."
        class="search-input"
      />
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
</style>

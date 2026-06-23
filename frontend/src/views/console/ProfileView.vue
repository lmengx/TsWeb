<script setup>
import { ref, onMounted } from 'vue'
import { get } from '../../utils/api.js'
import InventoryViewer from '../../components/InventoryViewer.vue'

const userDetails = ref(null)
const inventory = ref([])
const loading = ref(true)
const error = ref('')
const inventoryLoading = ref(false)
const inventoryError = ref('')

const isOnline = ref(false)

const fetchSelfInfo = async () => {
  loading.value = true
  error.value = ''
  
  try {
    const response = await get('/api/tshock/self')
    const result = await response.json()
    
    if (result.success && result.userInfo) {
      userDetails.value = result.userInfo
      
      if (result.inventory && result.inventory.status === '200' && result.inventory.inventory) {
        inventory.value = result.inventory.inventory.inventory || result.inventory.inventory
      } else if (result.inventory && result.inventory.error) {
        inventoryError.value = result.inventory.error
      }
      
      isOnline.value = result.isOnline || false
    } else {
      error.value = result.error || '获取信息失败'
    }
  } catch (err) {
    error.value = '加载失败: ' + err.message
  }
  
  loading.value = false
  inventoryLoading.value = false
}

onMounted(() => {
  fetchSelfInfo()
})
</script>

<template>
  <div class="profile-page">
    <div class="page-header">
      <div class="title-section">
        <h2>个人资料</h2>
        <template v-if="userDetails">
          <span class="username">{{ userDetails.Username || userDetails.name }}</span>
          <div class="online-status" :class="{ online: isOnline }">
            <span class="status-dot"></span>
            <span>{{ isOnline ? '在线' : '离线' }}</span>
          </div>
        </template>
      </div>
    </div>

    <div v-if="loading" class="loading-state">
      <p>加载中...</p>
    </div>

    <div v-else-if="error" class="error-state">
      <p>{{ error }}</p>
    </div>

    <template v-else-if="userDetails">
      <div class="user-info-section">
        <dl class="info-list">
          <div class="info-item">
            <dt>用户组</dt>
            <dd>{{ userDetails.Usergroup || userDetails.group || '默认' }}</dd>
          </div>
          <div class="info-item">
            <dt>注册时间</dt>
            <dd>{{ userDetails.Registered ? new Date(userDetails.Registered).toLocaleString() : '未知' }}</dd>
          </div>
          <div class="info-item">
            <dt>最后访问</dt>
            <dd>{{ userDetails.LastAccessed ? new Date(userDetails.LastAccessed).toLocaleString() : '从未访问' }}</dd>
          </div>
        </dl>
      </div>

      <div class="inventory-section">
        <InventoryViewer
          :inventory="inventory"
          :loading="inventoryLoading"
          :error="inventoryError"
          :readonly="true"
          :show-header="false"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.profile-page {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  padding: 0;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 24px;
  padding: 0 20px;
}

.title-section {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 12px;
}

.title-section h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.4rem;
  font-weight: 600;
}

.username {
  color: var(--accent-primary);
  font-weight: 600;
}

.online-status {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  background: var(--bg-tertiary);
  border-radius: 20px;
  font-size: 0.8rem;
  color: var(--text-muted);
}

.online-status.online {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-muted);
  backdrop-filter: blur(4px);
}

.online-status.online .status-dot {
  background: #22c55e;
  box-shadow: 0 0 8px rgba(34, 197, 94, 0.6);
}

.loading-state,
.error-state {
  text-align: center;
  padding: 60px 20px;
  color: var(--text-muted);
}

.error-state {
  color: var(--accent-error);
}

.user-info-section {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  margin: 0 20px 24px;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
}

.info-list {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 16px;
  margin: 0;
}

.info-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  border: 1px solid var(--border-light);
}

.info-item dt {
  color: var(--text-secondary);
  font-weight: 600;
  font-size: 0.95rem;
}

.info-item dd {
  color: var(--text-primary);
  font-size: 0.95rem;
  margin: 0;
}

.inventory-section {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 0 20px;
}

.inventory-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.inventory-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.2rem;
  font-weight: 600;
}
</style>
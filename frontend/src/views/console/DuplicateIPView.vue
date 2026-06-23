<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { get } from '../../utils/api.js'

const router = useRouter()

const loading = ref(false)
const error = ref('')
const duplicateIPData = ref([])

const fetchDuplicateIPs = async () => {
  loading.value = true
  error.value = ''

  try {
    const response = await get('/api/tshock/allduplicateips')
    const result = await response.json()

    if (result.duplicateips) {
      duplicateIPData.value = result.duplicateips
    } else if (result.error) {
      error.value = result.error
    }
  } catch (err) {
    error.value = err.message
  }

  loading.value = false
}

const goToUser = (username) => {
  router.push(`/console/users/${username}`)
}
</script>

<template>
  <div class="duplicate-ip-view">
    <div class="page-header">
      <h2>共享IP检测</h2>
    </div>

    <div class="section">
      <div class="content-header">
        <button @click="fetchDuplicateIPs" :disabled="loading" class="detect-btn">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="10"></circle>
            <polygon points="16.24 7.76 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76"></polygon>
          </svg>
          {{ loading ? '检测中...' : '检测' }}
        </button>
      </div>

      <div v-if="error" class="error-message">{{ error }}</div>

      <div v-if="loading" class="loading">
        加载中...
      </div>

      <div v-else-if="duplicateIPData.length > 0" class="ip-list">
        <div v-for="group in duplicateIPData" :key="group.index" class="ip-group-block">
          <div class="group-title">关联组 #{{ group.index }}</div>
          <div class="group-content">
            <div class="group-section">
              <div class="section-label">关联账号</div>
              <div class="section-content">
                <span
                  v-for="account in group.accounts"
                  :key="account.id"
                  class="user-chip"
                  @click="goToUser(account.username)"
                >
                  {{ account.username }}
                </span>
              </div>
            </div>
            <div class="group-section">
              <div class="section-label">登录IP</div>
              <div class="section-content">
                <span
                  v-for="(ip, idx) in group.ips"
                  :key="idx"
                  class="ip-chip"
                >
                  {{ ip }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
      
      <div v-else class="empty-state">
        暂无共享IP数据
      </div>
    </div>
  </div>
</template>

<style scoped>
.duplicate-ip-view {
  padding: 20px;
  width: 100%;
}

.page-header {
  margin-bottom: 16px;
}

.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
}

.section {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 20px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.content-header {
  display: flex;
  justify-content: flex-start;
  margin-bottom: 16px;
}

.detect-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  transition: all 0.25s ease;
}

.detect-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.4);
}

.detect-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.error-message {
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.1);
  color: var(--accent-error);
  border-radius: var(--radius-md);
  margin-bottom: 16px;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.loading {
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.empty-state {
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.ip-list {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.ip-group-block {
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.1), rgba(79, 70, 229, 0.05));
  border: 2px solid rgba(99, 102, 241, 0.3);
  border-radius: var(--radius-xl);
  padding: 16px;
}

.group-title {
  color: var(--accent-primary);
  font-weight: 700;
  font-size: 1rem;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid rgba(99, 102, 241, 0.2);
}

.group-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.group-section {
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  padding: 12px;
  border: 1px solid var(--border-color);
}

.section-label {
  color: var(--text-secondary);
  font-weight: 600;
  font-size: 0.85rem;
  margin-bottom: 8px;
  padding: 4px 8px;
  background: rgba(99, 102, 241, 0.1);
  border-radius: var(--radius-sm);
  display: inline-block;
}

.section-content {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.ip-chip {
  padding: 6px 12px;
  background: rgba(239, 68, 68, 0.1);
  color: #ef4444;
  border-radius: var(--radius-md);
  font-size: 0.85rem;
}

.user-chip {
  padding: 6px 12px;
  background: linear-gradient(135deg, var(--accent-secondary), #16a34a);
  color: white;
  border-radius: var(--radius-md);
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.25s ease;
}

.user-chip:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(22, 163, 74, 0.4);
}
</style>
<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { get } from '../../utils/api.js'

const router = useRouter()

const loading = ref(false)
const error = ref('')
const banList = ref([])
const searchQuery = ref('')
const currentPage = ref(1)
const pageSize = ref(10)

const ticksToDate = (ticks) => {
  try {
    const date = new Date((ticks - 621355968000000000) / 10000)
    return date.toLocaleString('zh-CN')
  } catch {
    return '-'
  }
}

const parseIdentifier = (identifier) => {
  if (!identifier) return { type: '未知', value: '-' }
  
  if (identifier.startsWith('ip:')) {
    return { type: 'IP', value: identifier.substring(3) }
  } else if (identifier.startsWith('uuid:')) {
    const uuid = identifier.substring(5)
    return { type: 'UUID', value: uuid.length > 12 ? uuid.substring(0, 12) + '...' : uuid }
  } else if (identifier.startsWith('acc:')) {
    return { type: '账户', value: identifier.substring(4) }
  } else if (identifier.startsWith('acc：')) {
    return { type: '账户', value: identifier.substring(4) }
  }
  
  return { type: '未知', value: identifier }
}

const filteredBanList = computed(() => {
  if (!searchQuery.value.trim()) {
    return banList.value
  }
  const query = searchQuery.value.toLowerCase()
  return banList.value.filter(ban => {
    const parsed = parseIdentifier(ban.identifier)
    return parsed.value.toLowerCase().includes(query) ||
           ban.reason?.toLowerCase().includes(query) ||
           ban.banning_user?.toLowerCase().includes(query)
  })
})

const totalItems = computed(() => filteredBanList.value.length)
const totalPages = computed(() => Math.ceil(totalItems.value / pageSize.value))

const paginatedBanList = computed(() => {
  const start = (currentPage.value - 1) * pageSize.value
  const end = start + pageSize.value
  return filteredBanList.value.slice(start, end)
})

const displayedPages = computed(() => {
  const pages = []
  const total = totalPages.value
  const current = currentPage.value
  
  if (total <= 5) {
    for (let i = 1; i <= total; i++) {
      pages.push(i)
    }
  } else {
    if (current <= 3) {
      for (let i = 1; i <= 4; i++) {
        pages.push(i)
      }
      pages.push('...')
      pages.push(total)
    } else if (current >= total - 2) {
      pages.push(1)
      pages.push('...')
      for (let i = total - 3; i <= total; i++) {
        pages.push(i)
      }
    } else {
      pages.push(1)
      pages.push('...')
      for (let i = current - 1; i <= current + 1; i++) {
        pages.push(i)
      }
      pages.push('...')
      pages.push(total)
    }
  }
  
  return pages
})

const fetchBanList = async () => {
  loading.value = true
  error.value = ''

  try {
    const response = await get('/api/tshock/banlist')
    const result = await response.json()

    if (result.status === '200' && result.bans) {
      banList.value = result.bans
    } else if (result.error) {
      error.value = result.error
    } else {
      error.value = '获取封禁列表失败'
    }
  } catch (err) {
    error.value = err.message
  }

  loading.value = false
}

const goToPage = (page) => {
  if (page === '...' || page < 1 || page > totalPages.value) return
  currentPage.value = page
}

const goToUser = (username) => {
  router.push(`/console/users/${username}`)
}

const resetPage = () => {
  currentPage.value = 1
}

onMounted(() => {
  fetchBanList()
})
</script>

<template>
  <div class="ban-list-view">
    <div class="page-header">
      <h2>封禁列表</h2>
    </div>

    <div class="section">
      <div class="content-header">
        <div class="search-box">
          <input
            v-model="searchQuery"
            type="text"
            placeholder="搜索玩家、IP或原因..."
            class="search-input"
            @input="resetPage"
          />
        </div>
        <button @click="fetchBanList" :disabled="loading" class="refresh-btn">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 2v6h-6"></path>
            <path d="M3 12a9 9 0 0 1 15-6.7L21 8"></path>
            <path d="M3 22v-6h6"></path>
            <path d="M21 12a9 9 0 0 1-15 6.7L3 16"></path>
          </svg>
          {{ loading ? '刷新中...' : '刷新' }}
        </button>
      </div>

      <div v-if="error" class="error-message">{{ error }}</div>

      <div v-if="loading" class="loading">
        加载中...
      </div>

      <div v-else-if="paginatedBanList.length > 0" class="ban-table-wrapper">
        <table class="ban-table">
          <thead>
            <tr>
              <th>编号</th>
              <th>类型</th>
              <th>标识符</th>
              <th>封禁原因</th>
              <th>封禁者</th>
              <th>封禁时间</th>
              <th>到期时间</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="ban in paginatedBanList" :key="ban.ticket_number">
              <td class="ticket-cell">#{{ ban.ticket_number }}</td>
              <td class="type-cell">
                <span class="type-badge" :class="parseIdentifier(ban.identifier).type.toLowerCase()">
                  {{ parseIdentifier(ban.identifier).type }}
                </span>
              </td>
              <td class="identifier-cell">
                <span 
                  v-if="parseIdentifier(ban.identifier).type === '账户'"
                  class="player-name"
                  @click="goToUser(parseIdentifier(ban.identifier).value)"
                >
                  {{ parseIdentifier(ban.identifier).value }}
                </span>
                <span v-else>
                  {{ parseIdentifier(ban.identifier).value }}
                </span>
              </td>
              <td class="reason-cell">{{ ban.reason || '-' }}</td>
              <td class="user-cell">{{ ban.banning_user || '-' }}</td>
              <td class="date-cell">{{ ticksToDate(ban.start_date_ticks) }}</td>
              <td class="date-cell">
                <span :class="{ permanent: ban.end_date_ticks === 3155378976000000000 }">
                  {{ ban.end_date_ticks === 3155378976000000000 ? '永久' : ticksToDate(ban.end_date_ticks) }}
                </span>
              </td>
            </tr>
          </tbody>
        </table>

        <div v-if="totalPages > 1" class="pagination">
          <button
            class="page-btn prev"
            :disabled="currentPage === 1"
            @click="goToPage(currentPage - 1)"
          >
            ‹
          </button>

          <button
            v-for="page in displayedPages"
            :key="page"
            class="page-btn"
            :class="{ active: page === currentPage, ellipsis: page === '...' }"
            :disabled="page === '...'"
            @click="goToPage(page)"
          >
            {{ page }}
          </button>

          <button
            class="page-btn next"
            :disabled="currentPage === totalPages"
            @click="goToPage(currentPage + 1)"
          >
            ›
          </button>
        </div>

        <div class="pagination-info">
          显示 {{ (currentPage - 1) * pageSize + 1 }} - {{ Math.min(currentPage * pageSize, totalItems) }} 条，共 {{ totalItems }} 条
        </div>
      </div>

      <div v-else class="empty-state">
        <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="10"></circle>
          <line x1="15" y1="9" x2="9" y2="15"></line>
          <line x1="9" y1="9" x2="15" y2="15"></line>
        </svg>
        <p>暂无封禁记录</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ban-list-view {
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
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  gap: 16px;
}

.search-box {
  flex: 1;
  max-width: 400px;
}

.search-input {
  width: 100%;
  padding: 10px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
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

.refresh-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 10px 20px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  transition: all 0.25s ease;
}

.refresh-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.4);
}

.refresh-btn:disabled {
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
  padding: 60px 40px;
  color: var(--text-muted);
}

.empty-state svg {
  margin-bottom: 16px;
  opacity: 0.5;
}

.empty-state p {
  margin: 0;
  font-size: 1rem;
}

.ban-table-wrapper {
  overflow-x: auto;
}

.ban-table {
  width: 100%;
  border-collapse: collapse;
}

.ban-table th,
.ban-table td {
  padding: 12px 16px;
  text-align: left;
  border-bottom: 1px solid var(--border-light);
}

.ban-table th {
  color: var(--text-secondary);
  font-weight: 600;
  font-size: 0.85rem;
  background: var(--bg-tertiary);
}

.ban-table tbody tr {
  transition: background 0.2s ease;
}

.ban-table tbody tr:hover {
  background: var(--bg-hover);
}

.ban-table td {
  color: var(--text-primary);
  font-size: 0.95rem;
}

.ticket-cell {
  font-weight: 600;
  color: var(--accent-primary);
  min-width: 60px;
}

.type-cell {
  min-width: 60px;
}

.type-badge {
  padding: 4px 10px;
  border-radius: var(--radius-sm);
  font-size: 0.75rem;
  font-weight: 600;
}

.type-badge.ip {
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
}

.type-badge.uuid {
  background: rgba(139, 92, 246, 0.15);
  color: #8b5cf6;
}

.type-badge.账户 {
  background: rgba(22, 163, 74, 0.15);
  color: #16a34a;
}

.type-badge.未知 {
  background: rgba(156, 163, 175, 0.15);
  color: #6b7280;
}

.identifier-cell {
  min-width: 150px;
}

.player-name {
  color: var(--accent-primary);
  cursor: pointer;
  font-weight: 500;
  transition: color 0.2s ease;
}

.player-name:hover {
  color: #818cf8;
  text-decoration: underline;
}

.reason-cell {
  max-width: 250px;
  color: var(--text-primary);
}

.user-cell {
  min-width: 120px;
  color: var(--text-secondary);
}

.date-cell {
  min-width: 140px;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.date-cell .permanent {
  color: var(--accent-error);
  font-weight: 600;
}

.pagination {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 8px;
  margin-top: 20px;
}

.page-btn {
  min-width: 36px;
  height: 36px;
  padding: 0 10px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  cursor: pointer;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  justify-content: center;
}

.page-btn:hover:not(:disabled):not(.ellipsis) {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.page-btn.active {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  border-color: var(--accent-primary);
  color: white;
}

.page-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.page-btn.ellipsis {
  border: none;
  background: transparent;
  cursor: default;
}

.pagination-info {
  text-align: center;
  margin-top: 12px;
  font-size: 0.85rem;
  color: var(--text-muted);
}
</style>
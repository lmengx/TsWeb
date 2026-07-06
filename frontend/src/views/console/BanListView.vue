<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { get, post } from '../../utils/api.js'

const router = useRouter()

const loading = ref(false)
const error = ref('')
const banList = ref([])
const searchQuery = ref('')
const showActiveOnly = ref(true) // 默认只显示生效中的封禁
const currentPage = ref(1)
const pageSize = ref(10)

// 排序
const sortField = ref('start_date_ticks')
const sortOrder = ref('desc') // 'asc' 或 'desc'

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

const isBanExpired = (ban) => {
  // 3155378976000000000 = DateTime.MaxValue.Ticks = 永久封禁，永不过期
  if (ban.end_date_ticks === 3155378976000000000) return false
  // 当前UTC时间的.NET刻度
  const nowTicks = (Date.now() * 10000) + 621355968000000000
  return ban.end_date_ticks <= nowTicks
}

const filteredBanList = computed(() => {
  let list = banList.value

  // 筛选：仅生效中
  if (showActiveOnly.value) {
    list = list.filter(ban => !isBanExpired(ban))
  }

  // 筛选：搜索关键词
  if (searchQuery.value.trim()) {
    const query = searchQuery.value.toLowerCase()
    list = list.filter(ban => {
      const parsed = parseIdentifier(ban.identifier)
      const ticketStr = String(ban.ticket_number)
      return parsed.value.toLowerCase().includes(query) ||
             ban.reason?.toLowerCase().includes(query) ||
             ban.banning_user?.toLowerCase().includes(query) ||
             ticketStr.includes(query)
    })
  }

  // 排序
  list = [...list].sort((a, b) => {
    let cmp = 0
    switch (sortField.value) {
      case 'ticket_number':
        cmp = a.ticket_number - b.ticket_number
        break
      case 'start_date_ticks':
        cmp = a.start_date_ticks - b.start_date_ticks
        break
      case 'end_date_ticks':
        cmp = a.end_date_ticks - b.end_date_ticks
        break
      case 'reason':
        cmp = (a.reason || '').localeCompare(b.reason || '')
        break
      case 'banning_user':
        cmp = (a.banning_user || '').localeCompare(b.banning_user || '')
        break
      default:
        cmp = a.start_date_ticks - b.start_date_ticks
    }
    return sortOrder.value === 'asc' ? cmp : -cmp
  })

  return list
})

const toggleSort = (field) => {
  if (sortField.value === field) {
    sortOrder.value = sortOrder.value === 'asc' ? 'desc' : 'asc'
  } else {
    sortField.value = field
    sortOrder.value = 'desc'
  }
  currentPage.value = 1
}

const sortIndicator = (field) => {
  if (sortField.value !== field) return ''
  return sortOrder.value === 'asc' ? ' ▲' : ' ▼'
}

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

// 多选
const selectedTickets = ref(new Set())

const toggleSelect = (ticket) => {
  const set = new Set(selectedTickets.value)
  if (set.has(ticket)) {
    set.delete(ticket)
  } else {
    set.add(ticket)
  }
  selectedTickets.value = set
}

const isAllSelected = computed(() => {
  const page = paginatedBanList.value
  return page.length > 0 && page.every(b => selectedTickets.value.has(b.ticket_number))
})

const toggleSelectAll = () => {
  if (isAllSelected.value) {
    const set = new Set(selectedTickets.value)
    paginatedBanList.value.forEach(b => set.delete(b.ticket_number))
    selectedTickets.value = set
  } else {
    const set = new Set(selectedTickets.value)
    paginatedBanList.value.forEach(b => set.add(b.ticket_number))
    selectedTickets.value = set
  }
}

const clearSelection = () => {
  selectedTickets.value = new Set()
}

// 批量解封
const showBatchUnbanModal = ref(false)
const batchUnbanLoading = ref(false)
const batchUnbanError = ref('')
const batchUnbanSuccess = ref('')
const batchUnbanProgress = ref(0)
const batchUnbanTotal = ref(0)

const openBatchUnbanModal = () => {
  batchUnbanError.value = ''
  batchUnbanSuccess.value = ''
  batchUnbanProgress.value = 0
  batchUnbanTotal.value = selectedTickets.value.size
  showBatchUnbanModal.value = true
}

const closeBatchUnbanModal = () => {
  showBatchUnbanModal.value = false
  batchUnbanLoading.value = false
  batchUnbanError.value = ''
  batchUnbanSuccess.value = ''
  batchUnbanProgress.value = 0
}

const executeBatchUnban = async () => {
  batchUnbanLoading.value = true
  batchUnbanError.value = ''
  batchUnbanSuccess.value = ''

  const tickets = [...selectedTickets.value]
  let successCount = 0
  let failCount = 0

  for (let i = 0; i < tickets.length; i++) {
    batchUnbanProgress.value = i + 1
    try {
      const response = await post('/api/tshock/unban', { ticket: tickets[i] })
      const result = await response.json()
      if (result.error) {
        failCount++
      } else {
        successCount++
      }
    } catch {
      failCount++
    }
  }

  batchUnbanLoading.value = false
  batchUnbanProgress.value = tickets.length

  if (failCount === 0) {
    batchUnbanSuccess.value = `全部解封成功，共 ${successCount} 条`
  } else {
    batchUnbanSuccess.value = `解封完成：成功 ${successCount} 条，失败 ${failCount} 条`
  }

  clearSelection()
  setTimeout(() => {
    fetchBanList()
    closeBatchUnbanModal()
  }, 1500)
}

// 解封相关（单条）
const showUnbanModal = ref(false)
const unbanTicket = ref('')
const unbanLoading = ref(false)
const unbanError = ref('')
const unbanSuccess = ref('')

const openUnbanModal = (ticket) => {
  unbanTicket.value = ticket
  unbanError.value = ''
  unbanSuccess.value = ''
  showUnbanModal.value = true
}

const closeUnbanModal = () => {
  showUnbanModal.value = false
  unbanTicket.value = ''
  unbanError.value = ''
  unbanSuccess.value = ''
}

const executeUnban = async () => {
  unbanLoading.value = true
  unbanError.value = ''
  unbanSuccess.value = ''

  try {
    const response = await post('/api/tshock/unban', {
      ticket: unbanTicket.value
    })
    const result = await response.json()

    if (result.error) {
      unbanError.value = result.error
    } else {
      unbanSuccess.value = result.response || '解封成功'
      // 刷新封禁列表
      setTimeout(() => {
        fetchBanList()
        closeUnbanModal()
      }, 1000)
    }
  } catch (err) {
    unbanError.value = err.message || '解封失败'
  }

  unbanLoading.value = false
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
        <label class="active-filter">
          <input type="checkbox" v-model="showActiveOnly" @change="resetPage" />
          <span class="checkbox-label">仅生效中</span>
        </label>
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

      <div v-if="selectedTickets.size > 0" class="batch-bar">
        <span class="batch-info">已选中 {{ selectedTickets.size }} 条</span>
        <button class="batch-unban-btn" @click="openBatchUnbanModal">批量解封</button>
        <button class="batch-clear-btn" @click="clearSelection">取消选择</button>
      </div>

      <div v-if="error" class="error-message">{{ error }}</div>

      <div v-if="loading" class="loading">
        加载中...
      </div>

      <div v-else-if="paginatedBanList.length > 0" class="ban-table-wrapper">
        <table class="ban-table">
          <thead>
            <tr>
              <th class="checkbox-th">
                <input type="checkbox" :checked="isAllSelected" @change="toggleSelectAll" class="select-checkbox" />
              </th>
              <th class="sortable-th" @click="toggleSort('ticket_number')">
              编号<span class="sort-indicator">{{ sortIndicator('ticket_number') }}</span>
            </th>
            <th>类型</th>
            <th>标识符</th>
            <th class="sortable-th" @click="toggleSort('reason')">
              封禁原因<span class="sort-indicator">{{ sortIndicator('reason') }}</span>
            </th>
            <th class="sortable-th" @click="toggleSort('banning_user')">
              封禁者<span class="sort-indicator">{{ sortIndicator('banning_user') }}</span>
            </th>
            <th class="sortable-th" @click="toggleSort('start_date_ticks')">
              封禁时间<span class="sort-indicator">{{ sortIndicator('start_date_ticks') }}</span>
            </th>
            <th class="sortable-th" @click="toggleSort('end_date_ticks')">
              到期时间<span class="sort-indicator">{{ sortIndicator('end_date_ticks') }}</span>
            </th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="ban in paginatedBanList" :key="ban.ticket_number" :class="{ 'row-selected': selectedTickets.has(ban.ticket_number) }">
              <td class="checkbox-cell">
                <input
                  type="checkbox"
                  :checked="selectedTickets.has(ban.ticket_number)"
                  @change="toggleSelect(ban.ticket_number)"
                  class="select-checkbox"
                />
              </td>
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
              <td class="action-cell">
                <button @click="openUnbanModal(ban.ticket_number)" class="unban-btn">解封</button>
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

    <!-- 解封确认弹窗（单条） -->
    <div v-if="showUnbanModal" class="modal-overlay" @click.self="closeUnbanModal">
      <div class="modal-dialog">
        <div class="modal-header">
          <h3>确认解封</h3>
          <button class="modal-close" @click="closeUnbanModal">×</button>
        </div>
        <div class="modal-body">
          <p>确定要解封票号为 <strong>#{{ unbanTicket }}</strong> 的封禁记录吗？</p>
          <p class="modal-warning">此操作将永久删除该封禁记录，被封玩家将可以重新进入服务器。</p>

          <div v-if="unbanError" class="error-message" style="margin-top: 12px;">{{ unbanError }}</div>
          <div v-if="unbanSuccess" class="success-message" style="margin-top: 12px;">{{ unbanSuccess }}</div>
        </div>
        <div class="modal-footer">
          <button class="modal-btn cancel" @click="closeUnbanModal" :disabled="unbanLoading">取消</button>
          <button class="modal-btn confirm" @click="executeUnban" :disabled="unbanLoading">
            {{ unbanLoading ? '处理中...' : '确认解封' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 批量解封弹窗 -->
    <div v-if="showBatchUnbanModal" class="modal-overlay" @click.self="closeBatchUnbanModal">
      <div class="modal-dialog">
        <div class="modal-header">
          <h3>批量解封</h3>
          <button class="modal-close" @click="closeBatchUnbanModal">×</button>
        </div>
        <div class="modal-body">
          <p>确定要解封选中的 <strong>{{ batchUnbanTotal }}</strong> 条封禁记录吗？</p>
          <p class="modal-warning">此操作将永久删除所有选中的封禁记录。</p>

          <div v-if="batchUnbanLoading" class="progress-bar-wrapper">
            <div class="progress-bar">
              <div class="progress-fill" :style="{ width: (batchUnbanProgress / batchUnbanTotal * 100) + '%' }"></div>
            </div>
            <span class="progress-text">{{ batchUnbanProgress }} / {{ batchUnbanTotal }}</span>
          </div>

          <div v-if="batchUnbanError" class="error-message" style="margin-top: 12px;">{{ batchUnbanError }}</div>
          <div v-if="batchUnbanSuccess" class="success-message" style="margin-top: 12px;">{{ batchUnbanSuccess }}</div>
        </div>
        <div class="modal-footer">
          <button class="modal-btn cancel" @click="closeBatchUnbanModal" :disabled="batchUnbanLoading">取消</button>
          <button class="modal-btn confirm" @click="executeBatchUnban" :disabled="batchUnbanLoading">
            {{ batchUnbanLoading ? '解封中...' : '确认批量解封' }}
          </button>
        </div>
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

.active-filter {
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  user-select: none;
  padding: 8px 14px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  transition: all 0.2s ease;
  white-space: nowrap;
}

.active-filter:hover {
  border-color: var(--accent-primary);
}

.active-filter input[type="checkbox"] {
  width: 16px;
  height: 16px;
  accent-color: var(--accent-primary);
  cursor: pointer;
}

.checkbox-label {
  color: var(--text-primary);
  font-size: 0.9rem;
  font-weight: 500;
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

/* 批量操作栏 */
.batch-bar {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 16px;
  background: rgba(99, 102, 241, 0.1);
  border: 1px solid rgba(99, 102, 241, 0.3);
  border-radius: var(--radius-md);
  margin-bottom: 16px;
}

.batch-info {
  color: var(--accent-primary);
  font-weight: 600;
  font-size: 0.9rem;
}

.batch-unban-btn {
  padding: 8px 18px;
  background: linear-gradient(135deg, #7c3aed, #6d28d9);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 600;
  transition: all 0.2s ease;
}

.batch-unban-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(124, 58, 237, 0.4);
}

.batch-clear-btn {
  padding: 8px 18px;
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.85rem;
  transition: all 0.2s ease;
}

.batch-clear-btn:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

/* 复选框 */
.checkbox-th {
  width: 40px;
  text-align: center;
  padding: 12px 8px !important;
}

.checkbox-cell {
  width: 40px;
  text-align: center;
  padding: 12px 8px !important;
}

.select-checkbox {
  width: 16px;
  height: 16px;
  accent-color: var(--accent-primary);
  cursor: pointer;
}

.row-selected {
  background: rgba(99, 102, 241, 0.08) !important;
}

/* 进度条 */
.progress-bar-wrapper {
  margin-top: 12px;
  display: flex;
  align-items: center;
  gap: 12px;
}

.progress-bar {
  flex: 1;
  height: 8px;
  background: var(--bg-tertiary);
  border-radius: 4px;
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  border-radius: 4px;
  transition: width 0.3s ease;
}

.progress-text {
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-weight: 600;
  white-space: nowrap;
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

.ban-table th.sortable-th {
  cursor: pointer;
  user-select: none;
  transition: background 0.2s ease;
}

.ban-table th.sortable-th:hover {
  background: var(--bg-hover);
}

.sort-indicator {
  color: var(--accent-primary);
  font-size: 0.75rem;
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

.action-cell {
  min-width: 80px;
  text-align: center;
}

.unban-btn {
  padding: 6px 16px;
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border: 1px solid rgba(239, 68, 68, 0.3);
  border-radius: var(--radius-sm);
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 600;
  transition: all 0.2s ease;
}

.unban-btn:hover {
  background: rgba(239, 68, 68, 0.25);
  border-color: var(--accent-error);
}

/* 弹窗样式 */
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
  overflow: hidden;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 24px;
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
  padding: 20px 24px;
}

.modal-body p {
  margin: 0 0 8px 0;
  color: var(--text-primary);
  font-size: 0.95rem;
  line-height: 1.5;
}

.modal-warning {
  color: var(--accent-error) !important;
  font-size: 0.85rem !important;
  background: rgba(239, 68, 68, 0.08);
  padding: 10px 12px;
  border-radius: var(--radius-sm);
  margin-top: 12px !important;
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
  background: var(--accent-error);
  color: #fff;
}

.modal-btn.confirm:hover:not(:disabled) {
  opacity: 0.85;
}

.success-message {
  padding: 10px 14px;
  background: rgba(22, 163, 74, 0.1);
  color: #16a34a;
  border-radius: var(--radius-md);
  border: 1px solid rgba(22, 163, 74, 0.3);
  font-size: 0.85rem;
}
</style>
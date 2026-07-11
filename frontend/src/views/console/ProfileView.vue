<script setup>
import { ref, onMounted, computed } from 'vue'
import { get } from '../../utils/api.js'
import InventoryViewer from '../../components/InventoryViewer.vue'

const userDetails = ref(null)
const inventory = ref([])
const loading = ref(true)
const error = ref('')
const inventoryLoading = ref(false)
const inventoryError = ref('')

const isOnline = ref(false)

const formatLocalDate = (dateStr) => {
  if (!dateStr) return '未知'
  const match = dateStr.match(/(\d{4})-(\d{2})-(\d{2})[T\s](\d{2}):(\d{2}):(\d{2})/)
  if (match) {
    const date = new Date(
      parseInt(match[1]),
      parseInt(match[2]) - 1,
      parseInt(match[3]),
      parseInt(match[4]),
      parseInt(match[5]),
      parseInt(match[6])
    )
    return date.toLocaleString('zh-CN')
  }
  return new Date(dateStr).toLocaleString('zh-CN')
}

const avatarLetter = computed(() => {
  const name = userDetails.value?.Username || '?'
  return name.charAt(0).toUpperCase()
})

const fetchSelfInfo = async () => {
  loading.value = true
  error.value = ''

  try {
    const [infoRes, invRes] = await Promise.all([
      get('/api/user/selfinfo'),
      get('/api/user/selfinventory')
    ])
    const info = await infoRes.json()
    const inv = await invRes.json()

    if (info.username) {
      userDetails.value = {
        Username: info.username,
        Usergroup: info.group,
        QQ: info.qq,
        Registered: info.registered,
        LastAccessed: info.lastAccessed
      }
      isOnline.value = info.isOnline || false

      if (!inv.error) {
        inventory.value = inv.items || []
      } else {
        inventoryError.value = inv.error
      }
    } else {
      error.value = info.error || '获取信息失败'
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
    <div v-if="loading" class="loading-state">
      <div class="loading-spinner"></div>
      <p>加载中...</p>
    </div>

    <div v-else-if="error" class="error-state">
      <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
        <circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line>
      </svg>
      <p>{{ error }}</p>
    </div>

    <template v-else-if="userDetails">
      <!-- ── 玻璃态卡片 ── -->
      <div class="glass-card">
        <!-- 顶部：头像 + 名称 -->
        <div class="gc-top">
          <div class="gc-avatar">
            <span class="gc-avatar-text">{{ avatarLetter }}</span>
            <span class="gc-status-dot" :class="{ online: isOnline }"></span>
          </div>
          <div class="gc-name-area">
            <h1 class="gc-name">{{ userDetails.Username }}</h1>
            <div class="gc-tags">
              <span class="gc-tag">{{ userDetails.Usergroup }}</span>
              <span class="gc-online" :class="{ online: isOnline }">
                <span class="gc-dot"></span>
                {{ isOnline ? '在线' : '离线' }}
              </span>
            </div>
          </div>
        </div>

        <!-- 分隔信息条 -->
        <div class="gc-info">
          <div class="gc-row">
            <span class="gc-label">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M23 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path>
              </svg>
              QQ
            </span>
            <span class="gc-value" :class="{ bound: userDetails.QQ }">{{ userDetails.QQ || '未绑定' }}</span>
          </div>
          <div class="gc-divider"></div>
          <div class="gc-row">
            <span class="gc-label">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line>
              </svg>
              注册
            </span>
            <span class="gc-value">{{ userDetails.Registered ? formatLocalDate(userDetails.Registered) : '未知' }}</span>
          </div>
          <div class="gc-divider"></div>
          <div class="gc-row">
            <span class="gc-label">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline>
              </svg>
              最近
            </span>
            <span class="gc-value">{{ userDetails.LastAccessed ? formatLocalDate(userDetails.LastAccessed) : '从未访问' }}</span>
          </div>
        </div>
      </div>

      <!-- ── 背包 ── -->
      <div class="glass-card inv-card">
        <div class="inv-header">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="2" y="7" width="20" height="14" rx="2" ry="2"></rect><path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"></path>
          </svg>
          <span>背包</span>
          <span class="inv-count">{{ inventory.length }} 格</span>
        </div>
        <div class="inv-scroll">
          <InventoryViewer
            :inventory="inventory"
            :loading="inventoryLoading"
            :error="inventoryError"
            :readonly="true"
            :show-header="false"
          />
        </div>
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
  padding: 28px 32px;
  max-width: 860px;
  margin: 0 auto;
  width: 100%;
  box-sizing: border-box;
  gap: 20px;
}

/* ── 加载 / 错误 ── */
.loading-state,
.error-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 14px;
  padding: 80px 20px;
  color: var(--text-muted);
}
.error-state { color: var(--accent-error); }
.loading-spinner {
  width: 28px; height: 28px;
  border: 2.5px solid var(--border-light);
  border-top-color: var(--accent-primary);
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }

/* ── 玻璃态卡片 ── */
.glass-card {
  background: var(--bg-card);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border: 1px solid var(--border-light);
  border-radius: 18px;
  padding: 28px 30px;
  box-shadow: 0 4px 24px rgba(0, 0, 0, 0.04);
  transition: box-shadow 0.25s ease;
}
.glass-card:hover {
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.06);
}

/* ── 顶部头像区域 ── */
.gc-top {
  display: flex;
  align-items: center;
  gap: 18px;
  margin-bottom: 24px;
}
.gc-avatar {
  position: relative;
  flex-shrink: 0;
  width: 60px;
  height: 60px;
  border-radius: 50%;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.15), rgba(99, 102, 241, 0.05));
  border: 2px solid var(--border-light);
  display: flex;
  align-items: center;
  justify-content: center;
}
.gc-avatar-text {
  font-size: 1.5rem;
  font-weight: 800;
  color: var(--accent-primary);
  user-select: none;
}
.gc-status-dot {
  position: absolute;
  bottom: 1px;
  right: 1px;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  border: 2.5px solid var(--bg-card);
  background: var(--text-muted);
}
.gc-status-dot.online {
  background: #22c55e;
  box-shadow: 0 0 8px rgba(34, 197, 94, 0.5);
}

.gc-name-area { flex: 1; min-width: 0; }
.gc-name {
  margin: 0 0 6px;
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
}
.gc-tags {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.gc-tag {
  padding: 2px 10px;
  border-radius: 12px;
  font-size: 0.75rem;
  font-weight: 600;
  background: rgba(99, 102, 241, 0.12);
  color: var(--accent-primary);
}
.gc-online {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--text-muted);
}
.gc-online.online { color: #22c55e; }
.gc-dot {
  width: 5px; height: 5px;
  border-radius: 50%;
  background: currentColor;
}

/* ── 分隔信息条 ── */
.gc-info { display: flex; flex-direction: column; }
.gc-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 4px;
  gap: 12px;
}
.gc-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.88rem;
  font-weight: 500;
  color: var(--text-secondary);
  white-space: nowrap;
}
.gc-label svg { flex-shrink: 0; }
.gc-value {
  font-size: 0.92rem;
  font-weight: 600;
  color: var(--text-primary);
  text-align: right;
  font-variant-numeric: tabular-nums;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.gc-value.bound { color: #22c55e; }

/* ── 分隔线（渐变细线，非全宽） ── */
.gc-divider {
  height: 1px;
  margin: 0 4px;
  background: linear-gradient(90deg, transparent, var(--border-light) 15%, var(--border-light) 85%, transparent);
}

/* ── 背包卡片 ── */
.inv-card { padding: 20px 24px; }
.inv-header {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-secondary);
  margin-bottom: 14px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--border-light);
}
.inv-header svg { color: var(--accent-primary); }
.inv-count {
  margin-left: auto;
  font-size: 0.75rem;
  color: var(--text-muted);
  background: var(--bg-tertiary);
  padding: 1px 8px;
  border-radius: 8px;
}

/* ── 移动端 ── */
@media (max-width: 767px) {
  .profile-page { padding: 16px; gap: 14px; }
  .glass-card { padding: 20px; border-radius: 14px; }
  .gc-avatar { width: 48px; height: 48px; }
  .gc-avatar-text { font-size: 1.2rem; }
  .gc-name { font-size: 1.2rem; }
  .gc-row { padding: 10px 2px; }
  .inv-card { padding: 16px; }
}
</style>

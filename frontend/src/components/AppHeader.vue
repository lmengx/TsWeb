<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useTheme } from '../composables/useTheme'
import { get } from '../utils/api.js'

const router = useRouter()
const { isDark, toggleTheme } = useTheme()

const showUserMenu = ref(false)
const userMenuRef = ref(null)
let closeTimer = null

const user = ref(null)
const serverConnected = ref(false)
const userOnline = ref(false)
const qqBound = ref(false)
const qqNumber = ref('')
let statusTimer = null

const isLoggedIn = computed(() => user.value !== null)
const currentUser = computed(() => user.value?.username || '未登录')
const userGroup = computed(() => user.value?.usergroup || '')
const displayGroup = computed(() => {
  const g = userGroup.value.toLowerCase()
  if (g.includes('owner')) return 'Owner'
  if (g.includes('superadmin')) return 'SuperAdmin'
  return userGroup.value || '未知'
})
const isAdmin = computed(() => {
  const g = userGroup.value.toLowerCase()
  return g.includes('owner') || g.includes('superadmin')
})

const loadUser = () => {
  const saved = localStorage.getItem('user')
  if (saved) {
    try { user.value = JSON.parse(saved) } catch { user.value = null }
  }
}

const fetchStatus = async () => {
  try {
    const res = await get('/api/status')
    const data = await res.json()
    serverConnected.value = data.connected
  } catch { serverConnected.value = false }
  if (user.value?.username) {
    try {
      const res = await get('/api/tshock/activeusers')
      const data = await res.json()
      if (data.activeusers) {
        const names = data.activeusers.split('\t').filter(n => n.trim())
        userOnline.value = names.some(n => n.toLowerCase() === user.value.username.toLowerCase())
      } else { userOnline.value = false }
    } catch { userOnline.value = false }
  }
  if (user.value?.username) {
    try {
      const res = await get('/api/tshock/userlist?username=' + encodeURIComponent(user.value.username))
      const data = await res.json()
      if (data.status === '200' && data.users?.[0]) {
        qqNumber.value = data.users[0].QQ || ''
        qqBound.value = !!qqNumber.value
      }
    } catch { qqBound.value = false }
  }
}

const goHome = () => router.push('/')
const goLogin = () => router.push('/login')
const toggleUserMenu = () => { showUserMenu.value = !showUserMenu.value }

const handleMouseEnter = () => {
  if (closeTimer) { clearTimeout(closeTimer); closeTimer = null }
  showUserMenu.value = true
}
const handleMouseLeave = () => {
  closeTimer = setTimeout(() => { showUserMenu.value = false; closeTimer = null }, 150)
}

const logout = () => {
  localStorage.removeItem('user')
  user.value = null
  showUserMenu.value = false
  router.push('/')
}

const handleClickOutside = (event) => {
  if (userMenuRef.value && !userMenuRef.value.contains(event.target)) {
    showUserMenu.value = false
  }
}

onMounted(() => {
  loadUser()
  fetchStatus()
  statusTimer = setInterval(fetchStatus, 10000)
  document.addEventListener('click', handleClickOutside)
})

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
  if (statusTimer) clearInterval(statusTimer)
})
</script>

<template>
  <nav class="console-nav">
    <div class="nav-brand" @click="goHome">
      <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
        <line x1="3" y1="9" x2="21" y2="9"></line>
        <line x1="9" y1="21" x2="9" y2="9"></line>
      </svg>
      <span class="nav-title">TSWeb</span>
    </div>

    <div class="nav-actions">
      <button class="theme-btn" @click="toggleTheme" :title="isDark ? '切换白天' : '切换黑夜'">
        <svg v-if="isDark" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="5"></circle>
          <line x1="12" y1="1" x2="12" y2="3"></line>
          <line x1="12" y1="21" x2="12" y2="23"></line>
          <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"></line>
          <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"></line>
          <line x1="1" y1="12" x2="3" y2="12"></line>
          <line x1="21" y1="12" x2="23" y2="12"></line>
          <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"></line>
          <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"></line>
        </svg>
        <svg v-else width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>
        </svg>
      </button>

      <div class="user-menu-wrapper" ref="userMenuRef" @mouseenter="handleMouseEnter" @mouseleave="handleMouseLeave">
        <div class="user-status" @click="isLoggedIn ? toggleUserMenu() : goLogin()" :class="{ active: showUserMenu }">
          <span class="status-dot" :class="{ online: serverConnected }"></span>
          <span class="username">{{ currentUser }}</span>
          <span class="expand-icon">{{ isLoggedIn ? (showUserMenu ? '▲' : '▼') : '' }}</span>
        </div>

        <transition name="dropdown">
          <div v-if="showUserMenu && isLoggedIn" class="user-dropdown">
            <div class="dropdown-header">
              <div class="user-info">
                <div class="user-name">{{ currentUser }}</div>
                <div class="user-meta">
                  <span class="meta-item">
                    <span class="meta-label">权限组</span>
                    <span class="meta-value tag">{{ displayGroup }}</span>
                  </span>
                  <span class="meta-item">
                    <span class="meta-label">游戏状态</span>
                    <span class="meta-value" :class="userOnline ? 'online' : 'offline'">{{ userOnline ? '游戏中' : '离线' }}</span>
                  </span>
                  <span class="meta-item">
                    <span class="meta-label">QQ 绑定</span>
                    <span class="meta-value qq-glow" :class="qqBound ? 'bound' : 'unbound'">{{ qqBound ? qqNumber : '未绑定' }}</span>
                  </span>
                </div>
              </div>
            </div>
            <div class="dropdown-divider"></div>
            <div class="dropdown-actions">
              <button class="logout-btn" @click="logout">退出登录</button>
            </div>
          </div>
        </transition>
      </div>
    </div>
  </nav>
</template>

<style scoped>
.console-nav {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 24px;
  margin: 12px 16px;
  background: var(--bg-card);
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(16px);
  border-radius: 14px;
  border: 1px solid var(--border-light);
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.06);
}

.nav-brand {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #6366f1;
  cursor: pointer;
}

.nav-title {
  font-size: 1rem;
  font-weight: 800;
  color: var(--text-primary);
}

.nav-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

/* 主题按钮 */
.theme-btn {
  width: 34px;
  height: 34px;
  border-radius: 9px;
  border: 1.5px solid var(--border-color);
  background: var(--bg-tertiary);
  color: var(--text-muted);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;
}

.theme-btn:hover {
  border-color: #6366f1;
  color: #6366f1;
}

/* 用户状态 */
.user-menu-wrapper {
  position: relative;
}

.user-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 12px;
  background: var(--bg-tertiary);
  border-radius: 9px;
  border: 1px solid var(--border-color);
  cursor: pointer;
  transition: all 0.2s ease;
}

.user-status:hover { background: var(--bg-hover); border-color: var(--accent-primary); }
.user-status.active { background: var(--bg-hover); border-color: var(--accent-primary); }

.status-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #6b7280;
}

.status-dot.online {
  background: #22c55e;
  box-shadow: 0 0 8px rgba(34, 197, 94, 0.6);
}

.username { font-size: 0.85rem; color: var(--text-primary); font-weight: 500; }

.expand-icon { font-size: 0.65rem; color: var(--text-secondary); margin-left: 4px; }

.user-dropdown {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  width: 260px;
  background: var(--bg-card);
  border-radius: 14px;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
  overflow: hidden;
  z-index: 1001;
}

.dropdown-enter-active, .dropdown-leave-active { transition: all 0.25s ease; }
.dropdown-enter-from, .dropdown-leave-to { opacity: 0; transform: translateY(-10px); }

.dropdown-header { padding: 20px; }
.user-info { flex: 1; }
.user-name { font-size: 1.1rem; font-weight: 600; color: var(--text-primary); margin-bottom: 16px; }
.user-meta { display: flex; flex-direction: column; gap: 10px; }
.meta-item { display: flex; justify-content: space-between; align-items: center; }
.meta-label { font-size: 0.82rem; color: var(--text-muted); }
.meta-value { font-size: 0.85rem; font-weight: 600; color: var(--text-primary); }
.meta-value.tag {
  color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.15);
  padding: 2px 10px;
  border-radius: 10px;
  font-size: 0.78rem;
}
.meta-value.online { color: #22c55e; }
.meta-value.offline { color: #6b7280; }
.meta-value.bound { color: #22c55e; }
.meta-value.unbound { color: #6b7280; }

.dropdown-divider { height: 1px; background: var(--border-light); margin: 0 20px; }
.dropdown-actions { padding: 16px 20px; }

.logout-btn {
  width: 100%;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
}

.logout-btn:hover { background: rgba(239, 68, 68, 0.1); border-color: var(--accent-error); color: var(--accent-error); }

.meta-value.qq-glow.bound {
  font-weight: 800;
  color: #22c55e;
  background: linear-gradient(90deg, #22c55e, #4ade80, #22c55e);
  background-size: 200% 100%;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  animation: qq-shimmer 2.5s ease-in-out infinite;
}

@keyframes qq-shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}
</style>

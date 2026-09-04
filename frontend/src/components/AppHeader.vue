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
let statusTimer = null

const isLoggedIn = computed(() => user.value !== null)
const currentUser = computed(() => user.value?.username || '未登录')
const userGroup = computed(() => user.value?.usergroup || '')
const displayGroup = computed(() => {
  const g = userGroup.value.toLowerCase()
  if (g.includes('admin')) return '管理员'
  if (g.includes('subadmin')) return '子管理员'
  return userGroup.value || '未知'
})
const isAdmin = computed(() => {
  const g = userGroup.value.toLowerCase()
  return g.includes('admin') && !g.includes('subadmin')
})

const loadUser = () => {
  const saved = localStorage.getItem('user')
  if (saved) {
    try { user.value = JSON.parse(saved) } catch { user.value = null }
  }
}

const fetchStatus = async () => {
  if (user.value?.username) {
    try {
      await get('/api/user/selfinfo')
      serverConnected.value = true
    } catch { serverConnected.value = false }
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
  statusTimer = setInterval(() => { fetchStatus() }, 10000)
  document.addEventListener('click', handleClickOutside)
})

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
  if (statusTimer) clearInterval(statusTimer)
})
</script>

<template>
  <nav class="console-nav glass">
    <div class="nav-brand" @click="goHome">
      <div class="brand-logo">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
          <line x1="3" y1="9" x2="21" y2="9"></line>
          <line x1="9" y1="21" x2="9" y2="9"></line>
        </svg>
      </div>
      <span class="nav-title text-gradient">TSWeb</span>
      <span class="nav-sub">管理面板</span>
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
          <svg v-if="isLoggedIn" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="expand-icon" :class="{ rotated: showUserMenu }">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
        </div>

        <transition name="dropdown">
          <div v-if="showUserMenu && isLoggedIn" class="user-dropdown">
            <div class="dropdown-header">
              <div class="user-avatar">{{ currentUser.charAt(0).toUpperCase() }}</div>
              <div class="user-info">
                <div class="user-name">{{ currentUser }}</div>
                <div class="user-meta">
                  <span class="meta-item">
                    <span class="meta-label">权限组</span>
                    <span class="meta-value tag tag-primary">{{ displayGroup }}</span>
                  </span>
                </div>
              </div>
            </div>
            <div class="dropdown-divider"></div>
            <div class="dropdown-actions">
              <button class="logout-btn" @click="logout">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"></path>
                  <polyline points="16 17 21 12 16 7"></polyline>
                  <line x1="21" y1="12" x2="9" y2="12"></line>
                </svg>
                退出登录
              </button>
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
  padding: 10px 20px;
  margin: 12px 16px;
  border-radius: var(--radius-lg);
  border: 1px solid var(--border-light);
  box-shadow: var(--shadow-md);
}

.nav-brand {
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
  user-select: none;
}

.brand-logo {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border-radius: 10px;
  background: var(--gradient-primary);
  color: #fff;
  box-shadow: var(--glow-primary);
}

.nav-title {
  font-size: 1.05rem;
  font-weight: 800;
  letter-spacing: 0.5px;
}

.nav-sub {
  font-size: 0.72rem;
  color: var(--text-muted);
  border-left: 1px solid var(--border-color);
  padding-left: 10px;
  margin-left: 2px;
}

.nav-actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

/* 主题按钮 */
.theme-btn {
  width: 36px;
  height: 36px;
  border-radius: 10px;
  border: 1px solid var(--border-color);
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all var(--dur-fast) var(--ease-out);
}

.theme-btn:hover {
  border-color: var(--border-glow);
  color: var(--accent-primary);
  box-shadow: var(--glow-primary);
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
  border-radius: 10px;
  border: 1px solid var(--border-color);
  cursor: pointer;
  transition: all var(--dur-fast) var(--ease-out);
}

.user-status:hover { background: var(--bg-hover); border-color: var(--border-glow); }
.user-status.active { background: var(--bg-hover); border-color: var(--accent-primary); }

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-muted);
}

.status-dot.online {
  background: var(--accent-secondary);
  box-shadow: 0 0 8px rgba(16, 185, 129, 0.7);
}

.username { font-size: 0.85rem; color: var(--text-primary); font-weight: 600; }

.expand-icon {
  color: var(--text-secondary);
  transition: transform var(--dur-fast) var(--ease-out);
}
.expand-icon.rotated { transform: rotate(180deg); }

.user-dropdown {
  position: absolute;
  top: calc(100% + 10px);
  right: 0;
  width: 280px;
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
  overflow: hidden;
  z-index: 1001;
}

.dropdown-enter-active, .dropdown-leave-active { transition: all 0.22s var(--ease-out); }
.dropdown-enter-from, .dropdown-leave-to { opacity: 0; transform: translateY(-8px) scale(0.98); }

.dropdown-header { padding: 20px; display: flex; gap: 14px; align-items: center; }

.user-avatar {
  width: 44px;
  height: 44px;
  border-radius: 12px;
  background: var(--gradient-primary);
  color: #fff;
  font-size: 1.2rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  box-shadow: var(--glow-primary);
}

.user-info { flex: 1; min-width: 0; }
.user-name { font-size: 1.05rem; font-weight: 700; color: var(--text-primary); margin-bottom: 10px; }
.user-meta { display: flex; flex-direction: column; gap: 8px; }
.meta-item { display: flex; justify-content: space-between; align-items: center; }
.meta-label { font-size: 0.78rem; color: var(--text-muted); }

.dropdown-divider { height: 1px; background: var(--border-light); margin: 0 20px; }
.dropdown-actions { padding: 16px 20px; }

.logout-btn {
  width: 100%;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 11px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all var(--dur-fast) var(--ease-out);
}

.logout-btn:hover {
  background: rgba(244, 63, 94, 0.12);
  border-color: var(--accent-error);
  color: var(--accent-error);
}
</style>

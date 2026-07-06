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
    try {
      user.value = JSON.parse(saved)
    } catch {
      user.value = null
    }
  }
}

const fetchStatus = async () => {
  try {
    const res = await get('/api/status')
    const data = await res.json()
    serverConnected.value = data.connected
  } catch {
    serverConnected.value = false
  }

  if (user.value?.username) {
    try {
      const res = await get('/api/tshock/activeusers')
      const data = await res.json()
      if (data.activeusers) {
        const names = data.activeusers.split('\t').filter(n => n.trim())
        userOnline.value = names.some(n => n.toLowerCase() === user.value.username.toLowerCase())
      } else {
        userOnline.value = false
      }
    } catch {
      userOnline.value = false
    }
  }

  if (user.value?.username) {
    try {
      const res = await get('/api/tshock/userlist?username=' + encodeURIComponent(user.value.username))
      const data = await res.json()
      if (data.status === '200' && data.users?.[0]) {
        qqNumber.value = data.users[0].QQ || ''
        qqBound.value = !!qqNumber.value
      }
    } catch {
      qqBound.value = false
    }
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
  <header class="app-header glass">
    <div class="header-content">
      <div class="header-left">
        <h1 class="app-title" @click="goHome">TsWeb</h1>
      </div>

      <div class="header-right">
        <button
          @click="toggleTheme"
          class="theme-toggle btn btn-secondary"
          :title="isDark ? '切换到浅色模式' : '切换到深色模式'"
        >
          <span class="theme-text">{{ isDark ? '白天' : '黑夜' }}</span>
        </button>

        <div
          class="user-menu-wrapper"
          ref="userMenuRef"
          @mouseenter="handleMouseEnter"
          @mouseleave="handleMouseLeave"
        >
          <div
            class="user-status"
            @click="isLoggedIn ? toggleUserMenu() : goLogin()"
            :class="{ active: showUserMenu }"
          >
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
                      <span class="meta-value" :class="userOnline ? 'online' : 'offline'">
                        {{ userOnline ? '游戏中' : '离线' }}
                      </span>
                    </span>
                    <span class="meta-item">
                      <span class="meta-label">QQ 绑定</span>
                      <span class="meta-value" :class="qqBound ? 'bound' : 'unbound'">
                        {{ qqBound ? qqNumber : '未绑定' }}
                      </span>
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
    </div>
  </header>
</template>

<style scoped>
.app-header {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 1000;
  padding: 12px 24px;
  border-bottom: 1px solid var(--border-light);
}

.header-content {
  max-width: 1920px;
  margin: 0 auto;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.header-left { display: flex; align-items: center; }

.app-title {
  font-size: 1.5rem;
  font-weight: 700;
  background: linear-gradient(135deg, var(--accent-primary), #a5b4fc);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  margin: 0;
  cursor: pointer;
  transition: opacity 0.2s ease;
}

.app-title:hover { opacity: 0.8; }

.header-right { display: flex; align-items: center; gap: 16px; }

.theme-toggle { padding: 8px 14px; font-size: 0.85rem; transition: all 0.2s ease; }

.theme-text { font-weight: 500; }

.user-menu-wrapper { position: relative; }

.user-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-color);
  cursor: pointer;
  transition: all 0.2s ease;
}

.user-status:hover { background: var(--bg-hover); border-color: var(--accent-primary); }

.user-status.active { background: var(--bg-hover); border-color: var(--accent-primary); }

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #6b7280;
}

.status-dot.online {
  background: #22c55e;
  box-shadow: 0 0 8px rgba(34, 197, 94, 0.6);
}

.username { font-size: 0.9rem; color: var(--text-primary); font-weight: 500; }

.expand-icon { font-size: 0.7rem; color: var(--text-secondary); margin-left: 4px; }

.user-dropdown {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  width: 260px;
  background: var(--bg-card);
  border-radius: var(--radius-xl);
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

.settings-btn {
  width: 100%;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
  margin-bottom: 8px;
}

.settings-btn:hover { background: var(--bg-hover); border-color: var(--accent-primary); }

.logout-btn {
  width: 100%;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
}

.logout-btn:hover { background: rgba(239, 68, 68, 0.1); border-color: var(--accent-error); color: var(--accent-error); }
</style>

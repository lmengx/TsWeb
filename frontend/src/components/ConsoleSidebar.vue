<script setup>
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const user = (() => {
  const saved = localStorage.getItem('user')
  if (saved) {
    try {
      return JSON.parse(saved)
    } catch {
      return null
    }
  }
  return null
})()

const isAdmin = computed(() => {
  if (!user?.usergroup) return false
  const usergroup = user.usergroup.toLowerCase()
  return usergroup.includes('owner') || usergroup.includes('superadmin')
})

const sidebarItems = [
  { id: 'terminal', name: '控制台', path: '/console/terminal' },
  { id: 'players', name: '玩家管理', path: '/console/players' },
  { id: 'groups', name: '组管理', path: '/console/groups', adminOnly: true },
  { id: 'server', name: '服务器设置', path: '/console/server' },
  { id: 'logs', name: '日志查看', path: '/console/logs' },
  { id: 'plugins', name: '插件管理', path: '/console/plugins' }
]

const adminSidebarItems = computed(() => {
  return sidebarItems.filter(item => !item.adminOnly || isAdmin)
})

const isActive = (path) => {
  return route.path === path
}

const goToSettings = () => {
  router.push('/settings')
}
</script>

<script>
import { computed } from 'vue'
</script>

<template>
  <aside class="sidebar glass">
    <nav class="sidebar-nav">
      <router-link
        v-for="item in adminSidebarItems"
        :key="item.id"
        :to="item.path"
        class="sidebar-item"
        :class="{ active: isActive(item.path) }"
      >
        <span class="sidebar-name">{{ item.name }}</span>
        <div v-if="isActive(item.path)" class="active-indicator"></div>
      </router-link>

      <div
        v-if="isAdmin"
        class="sidebar-item clickable"
        @click="goToSettings"
      >
        <span class="sidebar-name">⚙️ 应用配置</span>
      </div>
    </nav>
  </aside>
</template>

<style scoped>
.sidebar {
  width: 192px;
  border-right: 1px solid var(--border-light);
  padding: 20px 0;
  flex-shrink: 0;
  height: 100%;
}

.sidebar-nav {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 0 8px;
}

.sidebar-item {
  display: flex;
  align-items: center;
  padding: 12px 16px;
  cursor: pointer;
  border-radius: var(--radius-md);
  transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
  position: relative;
  color: var(--text-secondary);
  text-decoration: none;
}

.sidebar-item:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
  transform: translateX(4px);
}

.sidebar-item.active {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  box-shadow: var(--shadow-md);
}

.sidebar-item.active:hover {
  transform: translateX(4px);
  box-shadow: var(--shadow-lg);
}

.sidebar-item.clickable {
  cursor: pointer;
}

.sidebar-name {
  font-size: 0.95rem;
  font-weight: 500;
}

.active-indicator {
  position: absolute;
  right: 0;
  top: 50%;
  transform: translateY(-50%);
  width: 3px;
  height: 24px;
  background: rgba(255, 255, 255, 0.8);
  border-radius: 2px 0 0 2px;
}
</style>

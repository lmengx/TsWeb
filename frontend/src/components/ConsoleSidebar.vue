<script setup>
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isAdmin } from '../utils/authHelper.js'

const route = useRoute()
const router = useRouter()

const sidebarItems = [
  { id: 'profile', name: '个人资料', path: '/console/profile' },
  { id: 'progress', name: '世界进度', path: '/console/progress' },
  { 
    id: 'tools', 
    name: '工具', 
    path: '/console/tools',
    children: [
      { id: 'item-search', name: '物品查询', path: '/console/tools/item-search' },
      { id: 'gradient-text', name: '彩色文字', path: '/console/tools/gradient-text' },
      { id: 'resources', name: '资源下载', path: '/console/tools/resources' }
    ]
  },
  { id: 'terminal', name: '控制台', path: '/console/terminal', adminOnly: true },
  { id: 'players', name: '玩家管理', path: '/console/players', adminOnly: true },
  { id: 'online', name: '在线统计', path: '/console/online', adminOnly: true },
  { id: 'groups', name: '组管理', path: '/console/groups', adminOnly: true },
  { id: 'banlist', name: '封禁列表', path: '/console/banlist', adminOnly: true },
  {
    id: 'anticheat',
    name: '反作弊',
    path: '/console/anticheat',
    adminOnly: true,
    children: [
        { id: 'item-restrict', name: '物品限制配置', path: '/console/anticheat/item-restrict' },
        { id: 'proj-restrict', name: '弹幕限制配置', path: '/console/anticheat/proj-restrict' },
        { id: 'duplicate-ip', name: '共享IP检测', path: '/console/anticheat/duplicate-ip' }
      ]
  },
  { id: 'settings', name: '设置', path: '/console/settings', adminOnly: true }
]

const adminSidebarItems = computed(() => {
  return sidebarItems.filter(item => !item.adminOnly || isAdmin())
})

const isActive = (path) => {
  return route.path === path
}

const isActiveParent = (parentPath) => {
  return route.path.startsWith(parentPath)
}

const isExpanded = (parentPath) => {
  return route.path.startsWith(parentPath)
}

const handleParentClick = (item) => {
  if (item.children && item.children.length > 0) {
    router.push(item.children[0].path)
  } else {
    router.push(item.path)
  }
}
</script>

<script>
import { computed } from 'vue'
</script>

<template>
  <aside class="sidebar glass">
    <nav class="sidebar-nav">
      <template v-for="item in adminSidebarItems" :key="item.id">
        <div v-if="item.children && item.children.length > 0" class="sidebar-item-group">
          <div
            class="sidebar-item parent-item"
            :class="{ active: isActiveParent(item.path) }"
            @click="handleParentClick(item)"
          >
            <span class="sidebar-name">{{ item.name }}</span>
            <span class="expand-icon" :class="{ rotated: isExpanded(item.path) }">▼</span>
            <div v-if="isActiveParent(item.path)" class="active-indicator"></div>
          </div>
          
          <div v-if="isExpanded(item.path)" class="sidebar-submenu">
            <router-link
              v-for="child in item.children"
              :key="child.id"
              :to="child.path"
              class="sidebar-item child-item"
              :class="{ active: isActive(child.path) }"
            >
              <span class="sidebar-name">{{ child.name }}</span>
              <div v-if="isActive(child.path)" class="active-indicator"></div>
            </router-link>
          </div>
        </div>
        
        <router-link
          v-else
          :to="item.path"
          class="sidebar-item"
          :class="{ active: isActive(item.path) }"
        >
          <span class="sidebar-name">{{ item.name }}</span>
          <div v-if="isActive(item.path)" class="active-indicator"></div>
        </router-link>
      </template>
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

.parent-item {
  justify-content: space-between;
}

.expand-icon {
  font-size: 0.6rem;
  transition: transform 0.25s ease;
  color: inherit;
}

.expand-icon.rotated {
  transform: rotate(180deg);
}

.sidebar-submenu {
  padding-left: 16px;
  overflow: hidden;
  transition: all 0.25s ease;
}

.child-item {
  padding-left: 24px;
  font-size: 0.85rem;
  opacity: 0.9;
}

.child-item:hover {
  padding-left: 28px;
}

.child-item.active {
  background: rgba(99, 102, 241, 0.6);
}
</style>
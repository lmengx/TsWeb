<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isAdmin } from '../utils/authHelper.js'

const route = useRoute()
const router = useRouter()

const aboutVisible = ref(false)
const isMobile = ref(false)
let mql = null

// ── 移动端检测 ──
onMounted(() => {
  // 检查许可
  fetch('/api/config/license-check')
    .then(r => r.json())
    .then(d => { if (!d.hidden) aboutVisible.value = true })
    .catch(() => {})

  mql = window.matchMedia('(max-width: 767px)')
  isMobile.value = mql.matches
  mql.addEventListener('change', onMediaChange)
})

onUnmounted(() => {
  if (mql) mql.removeEventListener('change', onMediaChange)
})

const onMediaChange = (e) => { isMobile.value = e.matches }

// ── 侧边栏配置 ──
const sidebarItems = [
  // ═══ 管理 ═══
  { id: 'online', name: '总览', path: '/console/online', adminOnly: true, icon: '📊' },
  { id: 'terminal', name: '控制台', path: '/console/terminal', adminOnly: true, icon: '💻' },
  { id: 'players', name: '玩家管理', path: '/console/players', adminOnly: true, icon: '👥' },
  { id: 'groups', name: '组管理', path: '/console/groups', adminOnly: true },
  {
    id: 'anticheat', name: '反作弊', path: '/console/anticheat', adminOnly: true,
    children: [
      { id: 'item-restrict', name: '物品限制配置', path: '/console/anticheat/item-restrict' },
      { id: 'proj-restrict', name: '弹幕限制配置', path: '/console/anticheat/proj-restrict' },
      { id: 'duplicate-ip', name: '共享IP检测', path: '/console/anticheat/duplicate-ip' }
    ]
  },
  { id: 'files', name: '配置文件', path: '/console/files', adminOnly: true },
  { id: 'settings', name: '设置', path: '/console/settings', adminOnly: true },

  // ═══ 用户 ═══
  { id: 'guide', name: '公告', path: '/console/guide' },
  { id: 'profile', name: '个人资料', path: '/console/profile', icon: '👤' },
  { id: 'progress', name: '世界进度', path: '/console/progress' },
  {
    id: 'tools', name: '工具', path: '/console/tools',
    children: [
      { id: 'item-search', name: '物品查询', path: '/console/tools/item-search' },
      { id: 'gradient-text', name: '彩色文字', path: '/console/tools/gradient-text' },
      { id: 'resources', name: '资源下载', path: '/console/tools/resources' }
    ]
  },
  { id: 'about', name: '关于', path: '/console/about', adminOnly: true }
]

const visibleItems = computed(() => {
  return sidebarItems.filter(item => {
    if (item.adminOnly && !isAdmin()) return false
    if (item.id === 'about' && !aboutVisible.value) return false
    return true
  })
})

// ── 桌面端侧边栏 ──
const isActive = (path) => route.path === path
const isActiveParent = (path) => route.path.startsWith(path)
const isExpanded = (path) => route.path.startsWith(path)

const handleParentClick = (item) => {
  if (item.children && item.children.length > 0) {
    router.push(item.children[0].path)
  } else {
    router.push(item.path)
  }
}

// ── 移动端底部导航 ──
const mainTabs = computed(() => {
  const admin = isAdmin()

  if (admin) {
    return [
      { id: 'online', name: '总览', path: '/console/online' },
      { id: 'players', name: '玩家', path: '/console/players' },
      { id: 'terminal', name: '控制台', path: '/console/terminal' },
      { id: 'more', name: '管理', isMore: true },
      { id: 'other', name: '其它', isOther: true },
    ]
  } else {
    return [
      { id: 'guide', name: '公告', path: '/console/guide' },
      { id: 'profile', name: '个人资料', path: '/console/profile' },
      { id: 'progress', name: '世界进度', path: '/console/progress' },
      { id: 'tools', name: '工具', isTools: true },
    ]
  }
})

const handleTabClick = (tab) => {
  if (tab.isMore) openMoreMenu()
  else if (tab.isOther) openOtherMenu()
  else if (tab.isTools) openToolsMenu()
  else router.push(tab.path)
}

const isTabActive = (tab) => {
  if (tab.isMore) return showMoreMenu.value
  if (tab.isOther) return showOtherMenu.value
  if (tab.isTools) return showToolsMenu.value
  return route.path === tab.path
}

// ── 管理弹出菜单 (admin) ──
const showMoreMenu = ref(false)
const expandedMoreItem = ref(null)

const openMoreMenu = () => { showMoreMenu.value = true; expandedMoreItem.value = null }
const closeMoreMenu = () => { showMoreMenu.value = false; expandedMoreItem.value = null }

const moreItems = computed(() => {
  return visibleItems.value.filter(item =>
    !['online', 'terminal', 'players', 'profile', 'guide', 'progress', 'tools'].includes(item.id) &&
    item.adminOnly
  )
})

const toggleMoreItem = (item) => {
  if (item.children && item.children.length > 0) {
    expandedMoreItem.value = expandedMoreItem.value === item.id ? null : item.id
  } else {
    closeMoreMenu()
    router.push(item.path)
  }
}

const navigateMoreChild = (childPath) => {
  closeMoreMenu()
  router.push(childPath)
}

const hasChildren = (item) => item.children && item.children.length > 0

// ── 其它弹出菜单展开状态 ──
const expandedOtherItem = ref(null)
const toggleOtherItem = (item) => {
  if (hasChildren(item)) {
    expandedOtherItem.value = expandedOtherItem.value === item.id ? null : item.id
  } else {
    closeOtherMenu()
    router.push(item.path)
  }
}

// ── 其它弹出菜单 (admin) / 工具弹出菜单 (non-admin) ──
const showOtherMenu = ref(false)
const showToolsMenu = ref(false)

const openOtherMenu = () => { showOtherMenu.value = true; expandedOtherItem.value = null }
const closeOtherMenu = () => { showOtherMenu.value = false; expandedOtherItem.value = null }
const openToolsMenu = () => { showToolsMenu.value = true }
const closeToolsMenu = () => { showToolsMenu.value = false }

const otherItems = computed(() => {
  return visibleItems.value.filter(item =>
    ['guide', 'profile', 'progress', 'tools'].includes(item.id)
  )
})

const toolsItems = computed(() => {
  const toolsItem = sidebarItems.find(item => item.id === 'tools')
  return toolsItem?.children || []
})
</script>

<template>
  <!-- ═══ 桌面侧边栏 ═══ -->
  <aside v-if="!isMobile" class="sidebar glass">
    <nav class="sidebar-nav">
      <template v-for="(item, idx) in visibleItems" :key="item.id">
        <div v-if="idx > 0 && !item.adminOnly && visibleItems[idx - 1]?.adminOnly" class="sidebar-divider"></div>

        <!-- 有子项目的组 -->
        <div v-if="item.children && item.children.length > 0" class="sidebar-item-group">
          <div class="sidebar-item parent-item" :class="{ active: isActiveParent(item.path) }"
            @click="handleParentClick(item)">
            <span class="sidebar-name">{{ item.name }}</span>
            <span class="expand-icon" :class="{ rotated: isExpanded(item.path) }">▼</span>
            <div v-if="isActiveParent(item.path)" class="active-indicator"></div>
          </div>
          <div v-if="isExpanded(item.path)" class="sidebar-submenu">
            <router-link v-for="child in item.children" :key="child.id" :to="child.path"
              class="sidebar-item child-item" :class="{ active: isActive(child.path) }">
              <span class="sidebar-name">{{ child.name }}</span>
              <div v-if="isActive(child.path)" class="active-indicator"></div>
            </router-link>
          </div>
        </div>

        <!-- 普通路由 -->
        <router-link v-else :to="item.path" class="sidebar-item"
          :class="{ active: isActive(item.path) }">
          <span class="sidebar-name">{{ item.name }}</span>
          <div v-if="isActive(item.path)" class="active-indicator"></div>
        </router-link>
      </template>
    </nav>
  </aside>

  <!-- ═══ 移动端底部导航栏 ═══ -->
  <nav v-else class="mobile-bottom-nav">
    <button v-for="tab in mainTabs" :key="tab.id" class="mobile-tab"
      :class="{ active: isTabActive(tab) }"
      @click="handleTabClick(tab)">
      <span class="tab-icon">
        <!-- 总览: 网格仪表盘 -->
        <svg v-if="tab.id === 'online'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/>
        </svg>
        <!-- 玩家: 双人 -->
        <svg v-else-if="tab.id === 'players'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>
        </svg>
        <!-- 控制台: 终端/命令 -->
        <svg v-else-if="tab.id === 'terminal'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/>
        </svg>
        <!-- 管理: 齿轮 -->
        <svg v-else-if="tab.id === 'more'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/>
        </svg>
        <!-- 公告: 铃铛 -->
        <svg v-else-if="tab.id === 'guide'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/>
        </svg>
        <!-- 个人资料: 单人 -->
        <svg v-else-if="tab.id === 'profile'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>
        </svg>
        <!-- 世界进度: 奖杯/进度 -->
        <svg v-else-if="tab.id === 'progress'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M6 9H4.5a2.5 2.5 0 0 1 0-5C7 4 6 9 6 9"/><path d="M18 9h1.5a2.5 2.5 0 0 0 0-5C17 4 18 9 18 9"/><path d="M4 22h16"/><path d="M10 22V2h4v20"/><path d="M4 9h.01"/><path d="M20 9h.01"/>
        </svg>
        <!-- 工具: 扳手 -->
        <svg v-else-if="tab.id === 'tools'" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
        </svg>
        <!-- 其它: 更多(圆点) -->
        <svg v-else width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>
        </svg>
      </span>
      <span class="tab-label">{{ tab.name }}</span>
    </button>
  </nav>

  <!-- ═══ 移动端管理弹出菜单 ═══ -->
  <Teleport to="body">
    <div v-if="showMoreMenu" class="mobile-more-overlay" @click="closeMoreMenu">
      <div class="mobile-more-panel" @click.stop>
        <div class="more-header">
          <h3>管理</h3>
          <button class="more-close" @click="closeMoreMenu">✕</button>
        </div>
        <div class="more-list">
          <template v-for="item in moreItems" :key="item.id">
            <div class="more-item-wrapper">
              <button class="more-item" :class="{ active: isActive(item.path), hasChildren: hasChildren(item) }"
                @click="toggleMoreItem(item)">
                <span class="more-item-name">{{ item.name }}</span>
                <span v-if="hasChildren(item)" class="more-expand-icon" :class="{ rotated: expandedMoreItem === item.id }">▼</span>
              </button>
              <!-- 子菜单 -->
              <div v-if="hasChildren(item) && expandedMoreItem === item.id" class="more-submenu">
                <button v-for="child in item.children" :key="child.id" class="more-child-item"
                  :class="{ active: isActive(child.path) }"
                  @click="navigateMoreChild(child.path)">
                  <span class="more-child-name">{{ child.name }}</span>
                </button>
              </div>
            </div>
          </template>
        </div>
      </div>
    </div>
  </Teleport>

  <!-- ═══ 移动端其它弹出菜单 (admin) ═══ -->
  <Teleport to="body">
    <div v-if="showOtherMenu" class="mobile-more-overlay" @click="closeOtherMenu">
      <div class="mobile-more-panel" @click.stop>
        <div class="more-header">
          <h3>其它</h3>
          <button class="more-close" @click="closeOtherMenu">✕</button>
        </div>
        <div class="more-list">
          <template v-for="item in otherItems" :key="item.id">
            <button class="more-item"
              :class="{ active: isActive(item.path) }"
              @click="toggleOtherItem(item)">
              <span class="more-item-name">{{ item.name }}</span>
              <span v-if="hasChildren(item)" class="more-arrow" :class="{ rotated: expandedOtherItem === item.id }">›</span>
            </button>
            <div v-if="hasChildren(item) && expandedOtherItem === item.id" class="more-submenu">
              <button v-for="child in item.children" :key="child.id" class="more-subitem"
                :class="{ active: isActive(child.path) }"
                @click="closeOtherMenu(); router.push(child.path)">
                <span class="more-item-name">{{ child.name }}</span>
              </button>
            </div>
          </template>
        </div>
      </div>
    </div>
  </Teleport>

  <!-- ═══ 移动端工具弹出菜单 (non-admin) ═══ -->
  <Teleport to="body">
    <div v-if="showToolsMenu" class="mobile-more-overlay" @click="closeToolsMenu">
      <div class="mobile-more-panel" @click.stop>
        <div class="more-header">
          <h3>工具</h3>
          <button class="more-close" @click="closeToolsMenu">✕</button>
        </div>
        <div class="more-list">
          <button v-for="item in toolsItems" :key="item.id" class="more-item"
            :class="{ active: isActive(item.path) }"
            @click="closeToolsMenu(); router.push(item.path)">
            <span class="more-item-name">{{ item.name }}</span>
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* ── 桌面侧边栏 ── */
.sidebar {
  width: 200px;
  flex-shrink: 0;
  height: 100%;
  background: var(--bg-card);
  border-radius: 14px;
  border: 1px solid var(--border-light);
  padding: 16px 0;
  overflow-y: auto;
}
.sidebar-nav { display: flex; flex-direction: column; gap: 2px; padding: 0 8px; }
.sidebar-divider { height: 1px; background: var(--border-light); margin: 8px 14px 6px; flex-shrink: 0; }
.sidebar-item {
  display: flex; align-items: center; padding: 10px 14px; cursor: pointer;
  border-radius: 10px; transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
  position: relative; color: var(--text-secondary); text-decoration: none; font-size: 0.9rem;
}
.sidebar-item:hover { background: var(--bg-hover); color: var(--text-primary); }
.sidebar-item.active {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white; box-shadow: 0 2px 8px rgba(99, 102, 241, 0.2);
}
.sidebar-item.active:hover { transform: translateX(4px); }
.sidebar-name { font-size: 0.9rem; font-weight: 500; }
.active-indicator {
  position: absolute; right: 6px; top: 50%; transform: translateY(-50%);
  width: 3px; height: 18px; background: rgba(255, 255, 255, 0.9); border-radius: 2px;
}
.parent-item { justify-content: space-between; }
.expand-icon { font-size: 0.6rem; transition: transform 0.25s ease; color: inherit; }
.expand-icon.rotated { transform: rotate(180deg); }
.sidebar-submenu { padding-left: 16px; overflow: hidden; }
.child-item { padding-left: 24px; font-size: 0.85rem; opacity: 0.9; }
.child-item:hover { padding-left: 28px; }
.child-item.active { background: rgba(99, 102, 241, 0.6); }

/* ── 移动端底部导航栏 ── */
.mobile-bottom-nav {
  position: fixed; bottom: 0; left: 0; right: 0; z-index: 9999;
  display: flex; align-items: center; justify-content: space-around;
  height: 60px; padding: 0; background: var(--bg-card);
  border-top: 1px solid var(--border-light);
  backdrop-filter: blur(12px); -webkit-backdrop-filter: blur(12px);
}
.mobile-tab {
  flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: 2px; height: 100%; border: none; background: transparent;
  color: var(--text-muted); cursor: pointer; transition: all 0.2s;
  padding: 4px 0; -webkit-tap-highlight-color: transparent;
}
.mobile-tab.active { color: var(--accent-primary); }
.tab-icon { display: flex; align-items: center; justify-content: center; height: 22px; }
.tab-icon svg { display: block; }
.tab-label { font-size: 0.65rem; font-weight: 600; line-height: 1; }

/* ── 移动端管理弹出菜单 ── */
.mobile-more-overlay {
  position: fixed; inset: 0; z-index: 10000;
  background: rgba(0, 0, 0, 0.4); display: flex; align-items: flex-end;
  animation: fadeIn 0.2s ease;
}
.mobile-more-panel {
  width: 100%; max-height: 55vh; overflow-y: auto;
  background: var(--bg-primary); border-radius: 16px 16px 0 0;
  padding: 0 0 env(safe-area-inset-bottom, 0);
  box-shadow: 0 -8px 30px rgba(0, 0, 0, 0.15);
  animation: slideUp 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
}
.more-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 18px 20px 12px; border-bottom: 1px solid var(--border-light);
}
.more-header h3 { margin: 0; font-size: 1.1rem; color: var(--text-primary); }
.more-close {
  width: 32px; height: 32px; border-radius: 10px; border: 1px solid var(--border-light);
  background: var(--bg-tertiary); color: var(--text-secondary); font-size: 0.9rem;
  cursor: pointer; display: flex; align-items: center; justify-content: center;
}
.more-list { padding: 8px 12px; display: flex; flex-direction: column; gap: 2px; }
.more-item {
  display: flex; align-items: center; justify-content: space-between;
  padding: 14px 16px; border: none; background: transparent; border-radius: 10px;
  color: var(--text-primary); font-size: 0.9rem; font-weight: 500;
  cursor: pointer; transition: all 0.15s; text-align: left;
  -webkit-tap-highlight-color: transparent;
}
.more-item:hover { background: var(--bg-hover); }
.more-item.active { background: rgba(99, 102, 241, 0.1); color: var(--accent-primary); }
.more-item.hasChildren { font-weight: 600; }
.more-expand-icon { color: var(--text-muted); font-size: 0.7rem; transition: transform 0.2s ease; margin-left: 8px; }
.more-expand-icon.rotated { transform: rotate(180deg); }
.more-arrow { color: var(--text-muted); font-size: 1.2rem; transition: transform 0.2s ease; }
.more-arrow.rotated { transform: rotate(90deg); }

.more-submenu {
  padding: 0 12px 4px 24px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.more-subitem {
  display: flex; align-items: center; padding: 10px 16px;
  border: none; background: transparent; border-radius: 8px;
  color: var(--text-secondary); font-size: 0.85rem; font-weight: 400;
  cursor: pointer; transition: all 0.15s; text-align: left;
  -webkit-tap-highlight-color: transparent;
}
.more-subitem:hover { background: var(--bg-hover); color: var(--text-primary); }
.more-subitem.active { color: var(--accent-primary); }

.more-child-item {
  display: flex; align-items: center; padding: 10px 16px;
  border: none; background: transparent; border-radius: 8px;
  color: var(--text-secondary); font-size: 0.85rem; font-weight: 400;
  cursor: pointer; transition: all 0.15s; text-align: left; width: 100%;
  -webkit-tap-highlight-color: transparent;
}
.more-child-item:hover { background: var(--bg-hover); color: var(--text-primary); }
.more-child-item.active { color: var(--accent-primary); background: rgba(99, 102, 241, 0.08); }
.more-child-name { }

@keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
@keyframes slideUp { from { transform: translateY(100%); } to { transform: translateY(0); } }
</style>

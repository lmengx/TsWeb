<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isAdmin, isManager } from '../utils/authHelper.js'
import { getServers, getCurrentServer, fetchServers } from '../utils/serverStore.js'

const route = useRoute()
const router = useRouter()

const isLoggedIn = computed(() => !!localStorage.getItem('user'))

const isMobile = ref(false)
let mql = null

// ── 服务器切换按钮（点击跳转到服务器管理页） ──
const servers = ref([])
const currentServer = ref(null)
const noPermTip = ref(false)

const loadServers = async () => {
  await fetchServers()
  servers.value = getServers()
  currentServer.value = getCurrentServer()
}

const goServerManage = () => {
  // 仅唯一管理员可管理服务器
  if (!isAdmin()) {
    noPermTip.value = true
    setTimeout(() => { noPermTip.value = false }, 2200)
    return
  }
  router.push('/console/servers')
}

let statusTimer = null
const refreshServerStatus = () => { loadServers() }

// 切换当前服务器：直接读取最新选中服务器更新徽标，避免全量重拉列表造成闪烁
const onServerChanged = () => {
  currentServer.value = getCurrentServer()
}

// ── 移动端检测 ──
onMounted(() => {
  loadServers()

  mql = window.matchMedia('(max-width: 767px)')
  isMobile.value = mql.matches
  mql.addEventListener('change', onMediaChange)

  // 定时刷新服务器在线状态（与后端心跳 15s 同频），保持状态点颜色同步
  statusTimer = setInterval(refreshServerStatus, 15000)
  // 切换服务器后只更新当前服务器徽标（不重拉列表）
  window.addEventListener('server-changed', onServerChanged)
})

onUnmounted(() => {
  if (statusTimer) clearInterval(statusTimer)
  window.removeEventListener('server-changed', onServerChanged)
  if (mql) mql.removeEventListener('change', onMediaChange)
})

const onMediaChange = (e) => { isMobile.value = e.matches }

// ── 侧边栏图标（feather 风格线性图标内嵌） ──
const icons = {
  grid: '<rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect>',
  terminal: '<polyline points="4 17 10 11 4 5"></polyline><line x1="12" y1="19" x2="20" y2="19"></line>',
  globe: '<circle cx="12" cy="12" r="10"></circle><line x1="2" y1="12" x2="22" y2="12"></line><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"></path>',
  users: '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M23 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path>',
  layers: '<polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline>',
  home: '<path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path><polyline points="9 22 9 12 15 12 15 22"></polyline>',
  clock: '<circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline>',
  shield: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>',
  box: '<path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path><polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline><line x1="12" y1="22.08" x2="12" y2="12"></line>',
  crosshair: '<circle cx="12" cy="12" r="10"></circle><line x1="22" y1="12" x2="18" y2="12"></line><line x1="6" y1="12" x2="2" y2="12"></line><line x1="12" y1="6" x2="12" y2="2"></line><line x1="12" y1="22" x2="12" y2="18"></line>',
  copy: '<rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>',
  folder: '<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>',
  settings: '<circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path>',
  wrench: '<path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"></path>',
  search: '<circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line>',
  aperture: '<circle cx="12" cy="12" r="10"></circle><line x1="14.31" y1="8" x2="20.05" y2="17.94"></line><line x1="9.69" y1="8" x2="21.17" y2="8"></line><line x1="7.38" y1="12" x2="13.12" y2="2.06"></line><line x1="9.69" y1="16" x2="3.95" y2="6.06"></line><line x1="14.31" y1="16" x2="2.83" y2="16"></line><line x1="16.62" y1="12" x2="10.88" y2="21.94"></line>',
  key: '<path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path>',
  message: '<path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"></path>',
  vote: '<polyline points="9 11 12 14 22 4"></polyline><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"></path>',
  file: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line>'
}

// ── 侧边栏配置（分两个分区） ──
// 服务器设置：服务器内操作（managerOnly = admin+subadmin，adminOnly = 仅 admin）
// 后端设置：后端级配置（仅 admin）
const serverSection = [
  { id: 'online', name: '服务器总览', path: '/console/online', managerOnly: true, icon: 'grid' },
  { id: 'terminal', name: '控制台', path: '/console/terminal', managerOnly: true, icon: 'terminal' },
  { id: 'progress', name: '世界信息', path: '/console/progress', managerOnly: true, icon: 'globe' },
  { id: 'players', name: '玩家管理', path: '/console/players', managerOnly: true, icon: 'users' },
  { id: 'groups', name: '组管理', path: '/console/groups', managerOnly: true, icon: 'layers' },
  { id: 'houses', name: '房屋与建筑', path: '/console/houses', managerOnly: true, icon: 'home' },
  { id: 'tasks', name: '自动任务', path: '/console/tasks', managerOnly: true, icon: 'clock' },
  {
    id: 'anticheat', name: '反作弊', path: '/console/anticheat', managerOnly: true, icon: 'shield',
    children: [
      { id: 'item-restrict', name: '物品限制配置', path: '/console/anticheat/item-restrict', icon: 'box' },
      { id: 'proj-restrict', name: '弹幕限制配置', path: '/console/anticheat/proj-restrict', icon: 'crosshair' },
      { id: 'duplicate-ip', name: '共享IP检测', path: '/console/anticheat/duplicate-ip', icon: 'copy' }
    ]
  },
  { id: 'files', name: '文件管理', path: '/console/files', adminOnly: true, icon: 'folder' },
  { id: 'settings', name: '插件设置', path: '/console/settings', adminOnly: true, icon: 'settings' },
  {
    id: 'tools', name: '工具', path: '/console/tools', managerOnly: true, icon: 'wrench',
    children: [
      { id: 'item-search', name: '物品查询', path: '/console/tools/item-search', icon: 'search' },
      { id: 'gradient-text', name: '彩色文字', path: '/console/tools/gradient-text', icon: 'aperture' }
    ]
  }
]

const backendSection = [
  { id: 'accounts', name: '账户管理', path: '/console/accounts', adminOnly: true, icon: 'key' },
  { id: 'qq', name: 'QQ 配置', path: '/console/qq', adminOnly: true, icon: 'message' },
  { id: 'vote', name: '投票管理', path: '/console/vote', adminOnly: true, icon: 'vote' },
  { id: 'audit', name: '系统日志', path: '/console/audit', adminOnly: true, icon: 'file' }
]

const filterVisible = (item) => {
  if (item.adminOnly && !isAdmin()) return false
  if (item.managerOnly && !isManager()) return false
  return true
}

const serverItems = computed(() => serverSection.filter(filterVisible))
const backendItems = computed(() => backendSection.filter(filterVisible))
const visibleItems = computed(() => [...serverItems.value, ...backendItems.value])
// 过滤空分区：subadmin 无后端设置项时，不显示“后端设置”分区标题与分割线
const sections = computed(() => [
  { label: '服务器设置', items: serverItems.value },
  { label: '后端设置', items: backendItems.value }
].filter(sec => sec.items.length > 0))

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
      { id: 'progress', name: '世界信息', path: '/console/progress' },
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
    !['online', 'terminal', 'players', 'progress', 'tools'].includes(item.id) &&
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
    ['progress', 'tools'].includes(item.id)
  )
})

const toolsItems = computed(() => {
  const toolsItem = serverSection.find(item => item.id === 'tools')
  return toolsItem?.children || []
})
</script>

<template>
  <!-- ═══ 桌面侧边栏 ═══ -->
  <aside v-if="!isMobile" class="sidebar glass">
    <!-- 服务器切换按钮（多服，仅登录用户；点击跳转服务器管理页） -->
    <div v-if="isLoggedIn" class="server-switcher">
      <button class="server-switcher-btn" @click="goServerManage" title="服务器管理">
        <span class="ss-dot" :class="{ online: currentServer?.connected }"></span>
        <span class="ss-name">{{ currentServer?.name || '暂无服务器' }}</span>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="ss-arrow">
          <polyline points="9 18 15 12 9 6"></polyline>
        </svg>
      </button>
      <transition name="dropdown">
        <div v-if="noPermTip" class="ss-noperm">仅管理员可管理服务器</div>
      </transition>
    </div>

    <nav class="sidebar-nav">
      <template v-for="sec in sections" :key="sec.label">
        <div class="sidebar-section-label">
          <span class="section-dot"></span>{{ sec.label }}
        </div>
        <template v-for="item in sec.items" :key="item.id">
          <!-- 有子项目的组 -->
          <div v-if="item.children && item.children.length > 0" class="sidebar-item-group">
            <div class="sidebar-item parent-item" :class="{ active: isActiveParent(item.path) }"
              @click="handleParentClick(item)">
              <span class="item-icon" v-html="icons[item.icon] || ''"></span>
              <span class="sidebar-name">{{ item.name }}</span>
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="expand-icon" :class="{ rotated: isExpanded(item.path) }">
                <polyline points="6 9 12 15 18 9"></polyline>
              </svg>
            </div>
            <div v-if="isExpanded(item.path)" class="sidebar-submenu">
              <router-link v-for="child in item.children" :key="child.id" :to="child.path"
                class="sidebar-item child-item" :class="{ active: isActive(child.path) }">
                <span class="child-icon" v-html="icons[child.icon] || ''"></span>
                <span class="sidebar-name">{{ child.name }}</span>
              </router-link>
            </div>
          </div>

          <!-- 普通路由 -->
          <router-link v-else :to="item.path" class="sidebar-item"
            :class="{ active: isActive(item.path) }">
            <span class="item-icon" v-html="icons[item.icon] || ''"></span>
            <span class="sidebar-name">{{ item.name }}</span>
          </router-link>
        </template>
        <div v-if="sec !== sections[sections.length - 1]" class="sidebar-divider"></div>
      </template>
    </nav>
  </aside>

  <!-- ═══ 移动端底部导航栏 ═══ -->
  <nav v-else class="mobile-bottom-nav glass">
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
          <button class="more-close" @click="closeMoreMenu">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
          </button>
        </div>
        <div class="more-list">
          <template v-for="item in moreItems" :key="item.id">
            <div class="more-item-wrapper">
              <button class="more-item" :class="{ active: isActive(item.path), hasChildren: hasChildren(item) }"
                @click="toggleMoreItem(item)">
                <span class="more-item-name">{{ item.name }}</span>
                <svg v-if="hasChildren(item)" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="more-expand-icon" :class="{ rotated: expandedMoreItem === item.id }">
                  <polyline points="6 9 12 15 18 9"></polyline>
                </svg>
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
          <button class="more-close" @click="closeOtherMenu">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
          </button>
        </div>
        <div class="more-list">
          <template v-for="item in otherItems" :key="item.id">
            <button class="more-item"
              :class="{ active: isActive(item.path) }"
              @click="toggleOtherItem(item)">
              <span class="more-item-name">{{ item.name }}</span>
              <svg v-if="hasChildren(item)" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="more-arrow" :class="{ rotated: expandedOtherItem === item.id }">
                <polyline points="9 18 15 12 9 6"></polyline>
              </svg>
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
          <button class="more-close" @click="closeToolsMenu">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
          </button>
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
  width: 224px;
  flex-shrink: 0;
  height: 100%;
  border-radius: var(--radius-lg);
  padding: 14px 0;
  overflow-y: auto;
  overflow-x: hidden;
}

/* ═══ 特色服务器切换器 ═══ */
.server-switcher {
  position: relative;
  padding: 0 12px 12px;
  border-bottom: 1px solid var(--border-light);
  margin: 0 8px 12px;
}
.server-switcher-btn {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 11px 12px;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.16), rgba(139, 92, 246, 0.1));
  border: 1px solid rgba(99, 102, 241, 0.28);
  color: var(--text-primary);
  transition: all 0.25s var(--ease-out);
}
.server-switcher-btn:hover {
  border-color: var(--accent-primary);
  box-shadow: var(--glow-primary);
}
.ss-dot {
  width: 9px; height: 9px; border-radius: 50%;
  background: var(--accent-error); flex-shrink: 0;
  box-shadow: 0 0 0 2px rgba(244, 63, 94, 0.15);
}
.ss-dot.online {
  background: var(--accent-secondary);
  box-shadow: 0 0 8px rgba(16, 185, 129, 0.7);
}
.ss-name {
  flex: 1; text-align: left;
  font-size: 0.86rem; font-weight: 700;
  color: var(--text-primary);
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.ss-arrow { color: var(--accent-primary); flex-shrink: 0; }
.ss-noperm {
  position: absolute;
  top: calc(100% + 6px);
  left: 0; right: 0;
  padding: 8px 12px;
  text-align: center;
  font-size: 0.76rem;
  color: var(--accent-warning);
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(245, 158, 11, 0.3);
  border-radius: 10px;
  box-shadow: var(--shadow-lg);
  z-index: 300;
}
.dropdown-enter-active, .dropdown-leave-active { transition: all 0.2s ease; }
.dropdown-enter-from, .dropdown-leave-to { opacity: 0; transform: translateY(-6px); }

.sidebar-nav { display: flex; flex-direction: column; gap: 2px; padding: 0 10px; }
.sidebar-section-label {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 10px 6px;
  font-size: 0.68rem;
  font-weight: 700;
  letter-spacing: 1.2px;
  text-transform: uppercase;
  color: var(--text-muted);
}
.section-dot {
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: var(--gradient-primary);
  box-shadow: var(--glow-primary);
  flex-shrink: 0;
}
.sidebar-divider { height: 1px; background: var(--border-light); margin: 10px 10px 6px; flex-shrink: 0; }

.sidebar-item {
  display: flex; align-items: center; gap: 10px;
  padding: 9px 12px; cursor: pointer;
  border-radius: var(--radius-sm);
  transition: all 0.22s var(--ease-out);
  position: relative;
  color: var(--text-secondary);
  text-decoration: none;
  font-size: 0.88rem;
}
.sidebar-item:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
  transform: translateX(2px);
}
.sidebar-item.active {
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.9), rgba(139, 92, 246, 0.85));
  color: #fff;
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.35);
}
.sidebar-item.active::before {
  content: '';
  position: absolute;
  left: 0;
  top: 50%;
  transform: translateY(-50%);
  width: 3px;
  height: 60%;
  border-radius: 0 3px 3px 0;
  background: #fff;
  box-shadow: 0 0 8px rgba(255, 255, 255, 0.8);
}
.sidebar-item.active:hover { transform: translateX(2px); }

.item-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  flex-shrink: 0;
}
.item-icon svg {
  width: 17px;
  height: 17px;
  stroke-width: 2;
}
.sidebar-item.active .item-icon svg { stroke-width: 2.2; }

.sidebar-name { flex: 1; font-size: 0.88rem; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.sidebar-item.active .sidebar-name { font-weight: 600; }

.parent-item { justify-content: space-between; }
.expand-icon {
  width: 12px; height: 12px;
  color: var(--text-muted);
  transition: transform 0.25s var(--ease-out);
  flex-shrink: 0;
}
.expand-icon.rotated { transform: rotate(180deg); }

.sidebar-submenu {
  padding-left: 10px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.child-item {
  padding: 8px 12px 8px 16px;
  font-size: 0.84rem;
  opacity: 0.92;
}
.child-item:hover { padding-left: 18px; }
.child-item.active {
  background: rgba(99, 102, 241, 0.16);
  color: var(--accent-primary);
  box-shadow: none;
}
.child-item.active::before { display: none; }
.child-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 14px;
  height: 14px;
  flex-shrink: 0;
}
.child-icon svg { width: 13px; height: 13px; opacity: 0.8; }

/* ── 移动端底部导航栏 ── */
.mobile-bottom-nav {
  position: fixed; bottom: 0; left: 0; right: 0; z-index: 9999;
  display: flex; align-items: center; justify-content: space-around;
  height: 60px; padding: 0;
  border-top: 1px solid var(--border-light);
  border-radius: 0;
  box-shadow: 0 -4px 20px rgba(0, 0, 0, 0.3);
}
.mobile-tab {
  flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: 2px; height: 100%;
  color: var(--text-muted); cursor: pointer; transition: all 0.2s;
  padding: 4px 0; -webkit-tap-highlight-color: transparent;
}
.mobile-tab.active { color: var(--accent-primary); }
.mobile-tab.active .tab-label { text-shadow: 0 0 8px rgba(99, 102, 241, 0.5); }
.tab-icon { display: flex; align-items: center; justify-content: center; height: 22px; }
.tab-icon svg { display: block; }
.tab-label { font-size: 0.65rem; font-weight: 600; line-height: 1; }

/* ── 移动端管理弹出菜单 ── */
.mobile-more-overlay {
  position: fixed; inset: 0; z-index: 10000;
  background: rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(4px);
  display: flex; align-items: flex-end;
  animation: fadeIn 0.2s ease;
}
.mobile-more-panel {
  width: 100%; max-height: 58vh; overflow-y: auto;
  background: var(--bg-primary);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-xl) var(--radius-xl) 0 0;
  padding: 0 0 env(safe-area-inset-bottom, 0);
  box-shadow: 0 -8px 40px rgba(0, 0, 0, 0.4);
  animation: slideUp 0.3s var(--ease-out);
}
.more-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 18px 20px 12px;
  border-bottom: 1px solid var(--border-light);
}
.more-header h3 { margin: 0; font-size: 1.05rem; color: var(--text-primary); }
.more-close {
  width: 32px; height: 32px; border-radius: 10px;
  border: 1px solid var(--border-light);
  background: var(--bg-tertiary); color: var(--text-secondary);
  cursor: pointer; display: flex; align-items: center; justify-content: center;
  transition: all 0.15s;
}
.more-close:hover { color: var(--accent-error); border-color: var(--accent-error); }
.more-list { padding: 8px 12px; display: flex; flex-direction: column; gap: 2px; }
.more-item {
  display: flex; align-items: center; justify-content: space-between;
  padding: 14px 16px; border-radius: var(--radius-sm);
  color: var(--text-primary); font-size: 0.9rem; font-weight: 500;
  cursor: pointer; transition: all 0.15s; text-align: left; width: 100%;
  -webkit-tap-highlight-color: transparent;
}
.more-item:hover { background: var(--bg-hover); }
.more-item.active { background: rgba(99, 102, 241, 0.14); color: var(--accent-primary); }
.more-item.hasChildren { font-weight: 600; }
.more-expand-icon { color: var(--text-muted); transition: transform 0.2s ease; margin-left: 8px; flex-shrink: 0; }
.more-expand-icon.rotated { transform: rotate(180deg); }
.more-arrow { color: var(--text-muted); transition: transform 0.2s ease; flex-shrink: 0; }
.more-arrow.rotated { transform: rotate(90deg); }

.more-submenu {
  padding: 0 12px 4px 24px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.more-subitem {
  display: flex; align-items: center; padding: 10px 16px;
  border-radius: 8px;
  color: var(--text-secondary); font-size: 0.85rem; font-weight: 400;
  cursor: pointer; transition: all 0.15s; text-align: left; width: 100%;
  -webkit-tap-highlight-color: transparent;
}
.more-subitem:hover { background: var(--bg-hover); color: var(--text-primary); }
.more-subitem.active { color: var(--accent-primary); }

.more-child-item {
  display: flex; align-items: center; padding: 10px 16px;
  border-radius: 8px;
  color: var(--text-secondary); font-size: 0.85rem; font-weight: 400;
  cursor: pointer; transition: all 0.15s; text-align: left; width: 100%;
  -webkit-tap-highlight-color: transparent;
}
.more-child-item:hover { background: var(--bg-hover); color: var(--text-primary); }
.more-child-item.active { color: var(--accent-primary); background: rgba(99, 102, 241, 0.08); }

@keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
@keyframes slideUp { from { transform: translateY(100%); } to { transform: translateY(0); } }
</style>

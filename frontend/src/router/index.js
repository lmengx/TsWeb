import { createRouter, createWebHistory } from 'vue-router'
import Home from '../views/Home.vue'
import Login from '../views/Login.vue'
import Console from '../views/Console.vue'
import PlayersView from '../views/console/PlayersView.vue'
import SettingsView from '../views/SettingsView.vue'
import ServerError from '../views/ServerError.vue'
import Setup from '../views/Setup.vue'
import SetupIntro from '../views/SetupIntro.vue'
import PluginSetup from '../views/PluginSetup.vue'
import BackendView from '../views/BackendView.vue'
import BackendInit from '../views/BackendInit.vue'
import NotFound from '../views/NotFound.vue'
import { isAdmin, isManager } from '../utils/authHelper.js'
import { fetchServers } from '../utils/serverStore.js'

const routes = [
  {
    path: '/',
    name: 'Home',
    component: Home
  },
  {
    path: '/backend/init',
    name: 'BackendInit',
    component: BackendInit
  },
  {
    path: '/backend',
    name: 'Backend',
    component: BackendView
  },
  {
    path: '/setup',
    name: 'Setup',
    component: Setup
  },
  {
    path: '/setup/intro',
    name: 'SetupIntro',
    component: SetupIntro
  },
  {
    path: '/setup/plugin',
    name: 'PluginSetup',
    component: PluginSetup
  },
  {
    path: '/login',
    name: 'Login',
    component: Login
  },
  {
    path: '/vote',
    name: 'Vote',
    // 玩家（QQ 台账）投票中心：登录表单 + 我的信息门户；不挂 requiresAuth（未登录显示登录表单）
    // 玩家 token 存 user_player，与管理端 'user' 完全隔离，天然进不了 /console 管理路由
    component: () => import('../views/VoteView.vue')
  },
  {
    path: '/change-password',
    name: 'ChangePassword',
    component: () => import('../views/ChangePasswordView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/error/server',
    name: 'ServerError',
    component: ServerError
  },
  {
    path: '/console',
    name: 'Console',
    component: Console,
    meta: { requiresAuth: true },
    children: [
      {
        path: '',
        name: 'ConsoleHome',
        redirect: '/console/online'
      },
      {
        path: 'progress',
        name: 'Progress',
        component: () => import('../views/console/ProgressView.vue'),
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'settings',
        name: 'Settings',
        component: SettingsView,
        meta: { requiresAuth: true, requiresAdmin: true },
        redirect: '/console/settings/register',
        children: [
          {
            path: 'register',
            name: 'RegisterLoginSettings',
            component: () => import('../views/console/settings/RegisterLoginSettingsView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'boss',
            name: 'BossLimitSettings',
            component: () => import('../views/console/settings/BossLimitSettingsView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'backup',
            name: 'BackupSettings',
            component: () => import('../views/console/settings/BackupSettingsView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'promotion',
            name: 'PromotionConfig',
            component: () => import('../views/console/PromotionConfigView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'statuspanel',
            name: 'StatusPanelSettings',
            component: () => import('../views/console/settings/StatusPanelSettingsView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'emoji',
            name: 'EmoteCommandConfig',
            component: () => import('../views/console/settings/EmoteCommandView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'shopui',
            name: 'ShopUIConfig',
            component: () => import('../views/console/settings/ShopUISettingsView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'permissions',
            name: 'Permissions',
            component: () => import('../views/console/PermissionView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'crosstransfer',
            name: 'CrossTransfer',
            component: () => import('../views/console/CrossTransferView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'risk-control',
            name: 'RiskControl',
            component: () => import('../views/console/settings/RiskControlSettingsView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          }
        ]
      },
      {
        path: 'terminal',
        name: 'ConsoleTerminal',
        component: () => import('../components/ConsoleTerminal.vue'),
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'tasks',
        name: 'Tasks',
        component: () => import('../views/console/TasksView.vue'),
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'players',
        name: 'Players',
        component: PlayersView,
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'online',
        name: 'OnlineStats',
        component: () => import('../views/console/OnlineStatsView.vue'),
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'users/:username',
        name: 'UserDetail',
        component: () => import('../views/console/UserDetailView.vue'),
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'unverified/:nickname',
        name: 'UnverifiedDetail',
        component: () => import('../views/console/UnverifiedDetail.vue'),
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'server',
        name: 'ServerSettings',
        component: () => import('../views/console/ServerSettingsView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'servers',
        name: 'Servers',
        component: () => import('../views/console/ServersView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'audit',
        name: 'Audit',
        component: () => import('../views/console/AuditView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'qq',
        name: 'QQConfig',
        component: () => import('../views/console/QQConfigView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'accounts',
        name: 'Accounts',
        component: () => import('../views/console/BackendSettingsView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'vote',
        name: 'VoteAdmin',
        // 投票管理：直属后端（数据在后端本地 votes.json，独立于任何服务器）
        component: () => import('../views/console/VoteSettingsView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'tools',
        name: 'Tools',
        redirect: '/console/tools/home',
        meta: { requiresAuth: true, requiresManager: true },
        children: [
          {
            path: 'home',
            name: 'ToolsHome',
            component: () => import('../views/console/tools/ToolsHome.vue'),
            meta: { requiresAuth: true, requiresManager: true }
          },
          {
            path: 'item-search',
            name: 'ItemSearch',
            component: () => import('../views/console/tools/ItemSearch.vue'),
            meta: { requiresAuth: true, requiresManager: true }
          },
          {
            path: 'gradient-text',
            name: 'GradientText',
            component: () => import('../views/console/tools/GradientText.vue'),
            meta: { requiresAuth: true, requiresManager: true }
          }
        ]
      },
          {
            path: 'files',
            name: 'FileManager',
            component: () => import('../views/console/FileManagerView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
      {
        path: 'groups',
        name: 'Groups',
        component: () => import('../views/console/GroupsView.vue'),
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'houses',
        name: 'Houses',
        component: () => import('../views/console/HouseManagementView.vue'),
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'banlist',
        name: 'BanList',
        component: () => import('../views/console/BanListView.vue'),
        meta: { requiresAuth: true, requiresManager: true }
      },
      {
        path: 'anticheat',
        name: 'AntiCheat',
        redirect: '/console/anticheat/item-restrict',
        meta: { requiresAuth: true, requiresManager: true },
        children: [
          {
            path: 'duplicate-ip',
            name: 'DuplicateIP',
            component: () => import('../views/console/DuplicateIPView.vue'),
            meta: { requiresAuth: true, requiresManager: true }
          },
          {
            path: 'proj-restrict',
            name: 'ProjRestrict',
            component: () => import('../views/console/ProjRestrictView.vue'),
            meta: { requiresAuth: true, requiresManager: true }
          },
          {
            path: 'item-restrict',
            name: 'ItemRestrict',
            component: () => import('../views/console/ItemRestrictView.vue'),
            meta: { requiresAuth: true, requiresManager: true }
          }
        ]
      },
    ]
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'NotFound',
    component: NotFound
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

let serverStatusCache = null
let lastCheckTime = 0

// 检查后端是否已初始化（是否有任何已配置服务器）
const checkServerStatus = async () => {
  const now = Date.now()
  if (serverStatusCache && now - lastCheckTime < 10000) {
    return serverStatusCache
  }

  try {
    const servers = await fetchServers()
    serverStatusCache = servers.length > 0
    lastCheckTime = now
    return serverStatusCache
  } catch {
    serverStatusCache = false
    lastCheckTime = now
    return false
  }
}

router.beforeEach(async (to, from) => {
  // 检测 URL 中是否有 ?token=xxx（Setup Token）
  //  - 首次（无账户）→ 强制引导到 /backend/init 设置管理员密码
  //  - 已有账户 → token 不再用于登录，去登录页
  if (to.query.token && to.path !== '/backend/init' && to.path !== '/setup' && !to.path.startsWith('/setup/')) {
    try {
      const res = await fetch('/api/setup/check?token=' + encodeURIComponent(to.query.token))
      const data = await res.json()
      if (data.needToken || !data.setupToken) {
        // token 无效 → 登录页
        return { path: '/login', query: {}, replace: true }
      }
      if (data.hasAccounts === false) {
        // 无账户（首次初始化）→ 引导设置管理员密码
        return { path: '/backend/init', query: { token: to.query.token }, replace: true }
      }
      // 已有账户 → token 不再用于登录
      return { path: '/login', query: {}, replace: true }
    } catch {
      return { path: '/login', query: {}, replace: true }
    }
  }

  if (to.path === '/error/server' || to.path === '/setup' || to.path === '/backend' || to.path === '/backend/init' || to.path === '/login') {
    return true
  }

  const isLoggedIn = localStorage.getItem('user') !== null

  if (to.meta.requiresAuth && !isLoggedIn) {
    return '/login'
  }
  if (to.path === '/login' && isLoggedIn) {
    return '/console'
  }

  // 需 admin 的页面（仅唯一管理员）
  if (to.meta.requiresAdmin && isLoggedIn) {
    if (isAdmin()) return true
    return '/console'
  }

  // 需管理角色（admin/subadmin）的页面
  if (to.meta.requiresManager && isLoggedIn) {
    if (isManager()) return true
    return '/console'
  }

  return true
})

export default router
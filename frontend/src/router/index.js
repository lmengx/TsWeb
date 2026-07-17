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
import { isAdmin } from '../utils/authHelper.js'

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
        redirect: (to) => {
          return isAdmin() ? '/console/online' : '/console/guide'
        }
      },
      {
        path: 'profile',
        name: 'Profile',
        component: () => import('../views/console/ProfileView.vue'),
        meta: { requiresAuth: true }
      },
      {
        path: 'progress',
        name: 'Progress',
        component: () => import('../views/console/ProgressView.vue'),
        meta: { requiresAuth: true }
      },
      {
        path: 'settings',
        name: 'Settings',
        component: SettingsView,
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'settings/promotion',
        name: 'PromotionConfig',
        component: () => import('../views/console/PromotionConfigView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'terminal',
        name: 'ConsoleTerminal',
        component: () => import('../components/ConsoleTerminal.vue'),
        meta: { requiresAuth: true }
      },
      {
        path: 'players',
        name: 'Players',
        component: PlayersView,
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'online',
        name: 'OnlineStats',
        component: () => import('../views/console/OnlineStatsView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'users/:username',
        name: 'UserDetail',
        component: () => import('../views/console/UserDetailView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'unverified/:nickname',
        name: 'UnverifiedDetail',
        component: () => import('../views/console/UnverifiedDetail.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'server',
        name: 'ServerSettings',
        component: () => import('../views/console/ServerSettingsView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'guide',
        name: 'Guide',
        component: () => import('../views/console/GuideView.vue'),
        meta: { requiresAuth: true }
      },
      {
        path: 'tools',
        name: 'Tools',
        redirect: '/console/tools/home',
        meta: { requiresAuth: true },
        children: [
          {
            path: 'home',
            name: 'ToolsHome',
            component: () => import('../views/console/tools/ToolsHome.vue'),
            meta: { requiresAuth: true }
          },
          {
            path: 'item-search',
            name: 'ItemSearch',
            component: () => import('../views/console/tools/ItemSearch.vue'),
            meta: { requiresAuth: true }
          },
          {
            path: 'gradient-text',
            name: 'GradientText',
            component: () => import('../views/console/tools/GradientText.vue'),
            meta: { requiresAuth: true }
          },
          {
            path: 'resources',
            name: 'ResourceDownload',
            component: () => import('../views/console/tools/ResourceDownload.vue'),
            meta: { requiresAuth: true }
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
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'banlist',
        name: 'BanList',
        component: () => import('../views/console/BanListView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'anticheat',
        name: 'AntiCheat',
        redirect: '/console/anticheat/item-restrict',
        meta: { requiresAuth: true, requiresAdmin: true },
        children: [
          {
            path: 'duplicate-ip',
            name: 'DuplicateIP',
            component: () => import('../views/console/DuplicateIPView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'proj-restrict',
            name: 'ProjRestrict',
            component: () => import('../views/console/ProjRestrictView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          },
          {
            path: 'item-restrict',
            name: 'ItemRestrict',
            component: () => import('../views/console/ItemRestrictView.vue'),
            meta: { requiresAuth: true, requiresAdmin: true }
          }
        ]
      },
      {
        path: 'about',
        name: 'About',
        component: () => import('../views/console/AboutView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      }
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

const checkServerStatus = async () => {
  const now = Date.now()
  if (serverStatusCache && now - lastCheckTime < 10000) {
    return serverStatusCache
  }

  try {
    const response = await fetch('/api/status')
    const result = await response.json()
    serverStatusCache = result.connected
    lastCheckTime = now
    return result.connected
  } catch {
    serverStatusCache = false
    lastCheckTime = now
    return false
  }
}

router.beforeEach(async (to, from) => {
  // 检测 URL 中是否有 ?token=xxx（Setup Token），自动登录为 superadmin
  if (to.query.token && to.path !== '/setup' && !to.path.startsWith('/setup/') && to.path !== '/setup/intro' && to.path !== '/backend' && to.path !== '/backend/init') {
    try {
      const res = await fetch('/api/auth/setup-login?token=' + encodeURIComponent(to.query.token))
      const data = await res.json()
      if (data.success) {
        localStorage.setItem('user', JSON.stringify({
          username: 'admin',
          usergroup: 'superadmin',
          token: data.token
        }))
        // 去掉 URL 中的 token 参数
        const path = to.path
        return { path, query: {}, replace: true }
      }
    } catch {}
  }

  if (to.path === '/error/server' || to.path === '/setup' || to.path === '/backend' || to.path === '/backend/init') {
    return true
  }

  // 关于页许可检查
  if (to.path === '/console/about') {
    try {
      const res = await fetch('/api/config/license-check')
      if (res.ok) {
        const data = await res.json()
        if (data.hidden) {
          return '/console'
        }
      }
    } catch {}
  }

  const isConnected = await checkServerStatus()
  
  if (!isConnected) {
    return '/error/server'
  }

  const isLoggedIn = localStorage.getItem('user') !== null

  if (to.meta.requiresAuth && !isLoggedIn) {
    return '/login'
  }
  if (to.path === '/login' && isLoggedIn) {
    return '/console'
  }
  if (to.meta.requiresAdmin && isLoggedIn) {
    if (isAdmin()) {
      return true
    }
    return '/console'
  }
})

export default router
import { createRouter, createWebHistory } from 'vue-router'
import Home from '../views/Home.vue'
import Login from '../views/Login.vue'
import Console from '../views/Console.vue'
import PlayersView from '../views/console/PlayersView.vue'
import SettingsView from '../views/SettingsView.vue'
import AppSettingsView from '../views/AppSettingsView.vue'
import ServerError from '../views/ServerError.vue'
import { isAdmin } from '../utils/authHelper.js'

const routes = [
  {
    path: '/',
    name: 'Home',
    component: Home
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
          return isAdmin() ? '/console/terminal' : '/console/profile'
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
        path: 'settings/app',
        name: 'AppSettings',
        component: AppSettingsView,
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
        path: 'server',
        name: 'ServerSettings',
        component: () => import('../views/console/ServerSettingsView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'logs',
        name: 'Logs',
        component: () => import('../views/console/LogsView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      },
      {
        path: 'plugins',
        name: 'Plugins',
        component: () => import('../views/console/PluginsView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
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
      }
    ]
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
  if (to.path === '/error/server') {
    return true
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
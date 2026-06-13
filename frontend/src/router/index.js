import { createRouter, createWebHistory } from 'vue-router'
import Home from '../views/Home.vue'
import Login from '../views/Login.vue'
import Console from '../views/Console.vue'
import PlayersView from '../views/console/PlayersView.vue'
import SettingsView from '../views/SettingsView.vue'

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
    path: '/settings',
    name: 'Settings',
    component: SettingsView,
    meta: { requiresAuth: true, requiresAdmin: true }
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
        redirect: '/console/terminal'
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
        meta: { requiresAuth: true }
      },
      {
        path: 'users/:username',
        name: 'UserDetail',
        component: () => import('../views/console/UserDetailView.vue'),
        meta: { requiresAuth: true }
      },
      {
        path: 'server',
        name: 'ServerSettings',
        component: () => import('../views/console/ServerSettingsView.vue'),
        meta: { requiresAuth: true }
      },
      {
        path: 'logs',
        name: 'Logs',
        component: () => import('../views/console/LogsView.vue'),
        meta: { requiresAuth: true }
      },
      {
        path: 'plugins',
        name: 'Plugins',
        component: () => import('../views/console/PluginsView.vue'),
        meta: { requiresAuth: true }
      },
      {
        path: 'groups',
        name: 'Groups',
        component: () => import('../views/console/GroupsView.vue'),
        meta: { requiresAuth: true, requiresAdmin: true }
      }
    ]
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach((to, from) => {
  const isLoggedIn = localStorage.getItem('user') !== null

  if (to.meta.requiresAuth && !isLoggedIn) {
    return '/login'
  }
  if (to.path === '/login' && isLoggedIn) {
    return '/console'
  }
  if (to.meta.requiresAdmin && isLoggedIn) {
    const user = JSON.parse(localStorage.getItem('user'))
    const usergroup = user?.usergroup?.toLowerCase() || ''
    if (usergroup.includes('owner') || usergroup.includes('superadmin')) {
      return true
    }
    return '/console'
  }
})

export default router

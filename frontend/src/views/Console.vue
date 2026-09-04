<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import AppHeader from '../components/AppHeader.vue'
import ConsoleSidebar from '../components/ConsoleSidebar.vue'
import '../styles/theme.css'

const router = useRouter()

const user = ref(null)
const isMobile = ref(false)
let mql = null

const isAdmin = computed(() => {
  if (!user.value?.usergroup) return false
  const usergroup = user.value.usergroup.toLowerCase()
  return usergroup.includes('admin') || usergroup.includes('owner') || usergroup.includes('superadmin')
})

const loadUser = () => {
  const saved = localStorage.getItem('user')
  if (saved) {
    try {
      user.value = JSON.parse(saved)
    } catch (e) {
      console.error('Failed to load user')
    }
  }
}

const logout = () => {
  localStorage.removeItem('user')
  router.push('/')
}

const goHome = () => {
  router.push('/')
}

onMounted(() => {
  loadUser()
  if (!user.value) {
    router.push('/login')
  }
  mql = window.matchMedia('(max-width: 767px)')
  isMobile.value = mql.matches
  mql.addEventListener('change', (e) => { isMobile.value = e.matches })
})

onUnmounted(() => {
  if (mql) mql.removeEventListener('change', () => {})
})
</script>

<template>
  <div class="console-page" :class="{ mobile: isMobile }">
    <div class="tech-bg"></div>
    <AppHeader />
    
    <main class="console-main">
      <ConsoleSidebar />
      
      <div class="content-area glass" :class="{ mobile: isMobile }">
        <router-view />
      </div>
    </main>
  </div>
</template>

<style scoped>
.console-page {
  position: relative;
  display: flex;
  flex-direction: column;
  height: 100vh;
  overflow: hidden;
  background-color: var(--bg-base);
  color: var(--text-primary);
}

.console-main {
  flex: 1;
  display: flex;
  overflow: hidden;
  margin-top: 68px;
  padding: 0 16px 16px;
  position: relative;
  z-index: 1;
}

.console-page.mobile .console-main {
  padding: 0 0 60px; /* 底部留出导航栏高度 */
  margin-top: 52px;
}

.content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  padding: 24px;
  border-radius: var(--radius-lg);
  margin-left: 16px;
  min-width: 0;
}

.content-area.mobile {
  margin-left: 0;
  border-radius: 0;
  border-left: none;
  border-right: none;
  border-top: 1px solid var(--border-light);
  border-bottom: none;
  padding: 14px 14px 0;
  background: transparent;
  backdrop-filter: none;
  box-shadow: none;
  border: none;
}

.console-page.mobile {
  background: var(--bg-base);
}
</style>

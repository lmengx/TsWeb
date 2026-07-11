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
    <AppHeader />
    
    <main class="console-main">
      <ConsoleSidebar />
      
      <div class="content-area" :class="{ mobile: isMobile }">
        <router-view />
      </div>
    </main>
  </div>
</template>

<style scoped>
.console-page {
  display: flex;
  flex-direction: column;
  height: 100vh;
  overflow: hidden;
  background-color: var(--bg-primary);
  color: var(--text-primary);
}

.console-main {
  flex: 1;
  display: flex;
  overflow: hidden;
  margin-top: 64px;
  padding: 0 12px 12px;
}

.console-page.mobile .console-main {
  padding: 0 0 60px; /* 底部留出导航栏高度 */
  margin-top: 48px;
}

.content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  padding: 20px;
  background: var(--bg-card);
  border-radius: 14px;
  border: 1px solid var(--border-light);
  margin-left: 12px;
}

.content-area.mobile {
  margin-left: 0;
  border-radius: 0;
  border-left: none;
  border-right: none;
  border-top: 1px solid var(--border-light);
  border-bottom: none;
  padding: 12px 12px 0;
}

.console-page.mobile {
  background: var(--bg-primary);
}
</style>

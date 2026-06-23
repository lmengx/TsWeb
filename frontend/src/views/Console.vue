<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import AppHeader from '../components/AppHeader.vue'
import ConsoleSidebar from '../components/ConsoleSidebar.vue'
import '../styles/theme.css'

const router = useRouter()

const user = ref(null)

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
})
</script>

<template>
  <div class="console-page">
    <AppHeader />
    
    <main class="console-main">
      <ConsoleSidebar />
      
      <div class="content-area">
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
  margin-top: 60px;
}

.content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  padding: 20px;
}
</style>

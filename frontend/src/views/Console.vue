<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import AppHeader from '../components/AppHeader.vue'
import ConsoleSidebar from '../components/ConsoleSidebar.vue'
import '../styles/theme.css'

const router = useRouter()
const route = useRoute()

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
        <router-view v-slot="{ Component }">
          <!-- 警告：mode="out-in" 下路由组件必须只有一个根元素！
               任何路由页若为多根（fragment），过渡会无法动画且 isLeaving 永久卡死，
               内容区永久空白（黑屏），只能整页刷新恢复（ServersView/UserDetailView 已踩坑修复） -->
          <transition name="fade-slide" mode="out-in">
            <component :is="Component" :key="route.path" />
          </transition>
        </router-view>
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
  /* 实心背景：路由切换过渡期间（out-in 离开/进入间隙）避免透明露底黑屏，
     桌面端同样需要（合成层黑屏修复，见 theme.css fade-slide 说明） */
  background: var(--bg-primary);
}

.content-area.mobile {
  margin-left: 0;
  border-radius: 0;
  border-left: none;
  border-right: none;
  border-top: 1px solid var(--border-light);
  border-bottom: none;
  padding: 14px 14px 0;
  /* 实心背景已在 .content-area 基础样式中全局设置（防过渡期黑屏），此处无需重复 */
  backdrop-filter: none;
  box-shadow: none;
  border: none;
}

.console-page.mobile {
  background: var(--bg-base);
}
</style>

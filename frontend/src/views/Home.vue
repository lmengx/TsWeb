<script setup>
import { computed, ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import AppHeader from '../components/AppHeader.vue'
import { get } from '../utils/api.js'
import '../styles/theme.css'

const router = useRouter()

const isLoggedIn = computed(() => localStorage.getItem('user') !== null)
const isAdmin = computed(() => {
  const user = localStorage.getItem('user')
  if (!user) return false
  const usergroup = JSON.parse(user)?.usergroup?.toLowerCase() || ''
  return usergroup.includes('owner') || usergroup.includes('superadmin')
})

const checkAndRedirectToSettings = async () => {
  if (!isAdmin.value) return false
  try {
    const res = await get('/api/config/status')
    const data = await res.json()
    return !data.configured
  } catch {
    return false
  }
}

const goToLogin = () => {
  router.push('/login')
}

const goToConsole = async () => {
  if (isAdmin.value) {
    const needSetup = await checkAndRedirectToSettings()
    if (needSetup) {
      router.push('/settings')
      return
    }
  }
  router.push('/console')
}

const getUserInfo = () => {
  const user = localStorage.getItem('user')
  if (user) {
    return JSON.parse(user)
  }
  return null
}

const logout = () => {
  if (typeof localStorage !== 'undefined') {
    localStorage.removeItem('user')
  }
  window.location.reload()
}

onMounted(async () => {
  if (isLoggedIn.value && isAdmin.value) {
    const needSetup = await checkAndRedirectToSettings()
    if (needSetup) {
      router.push('/settings')
    }
  }
})
</script>

<template>
  <div class="home">
    <AppHeader />
    
    <main class="main-content">
      <div class="hero">
        <h2>欢迎使用 TShock 管理面板</h2>
        <p>基于 Vue + Node.js 构建的现代化服务器管理工具</p>
      </div>
      
      <div class="features">
        <div class="feature-card">
          <div class="feature-icon">🔐</div>
          <h3>安全登录</h3>
          <p>采用非对称加密技术，密码全程加密传输</p>
        </div>
        <div class="feature-card">
          <div class="feature-icon">⚡</div>
          <h3>实时管理</h3>
          <p>在线执行命令，实时查看服务器状态</p>
        </div>
        <div class="feature-card">
          <div class="feature-icon">🛡️</div>
          <h3>权限控制</h3>
          <p>基于用户组的访问权限管理</p>
        </div>
      </div>
      
      <div class="actions">
        <button v-if="!isLoggedIn" @click="goToLogin" class="btn btn-primary">
          登录
        </button>
        <button v-else @click="goToConsole" class="btn btn-primary">
          进入控制台
        </button>
        <button v-if="isLoggedIn" @click="logout" class="btn btn-secondary">
          退出登录
        </button>
      </div>
      
      <div v-if="getUserInfo()" class="user-info card">
        <div class="info-row">
          <span class="label">当前登录用户:</span>
          <span class="value">{{ getUserInfo().username }}</span>
        </div>
        <div class="info-row">
          <span class="label">用户组:</span>
          <span class="value">{{ getUserInfo().usergroup }}</span>
        </div>
      </div>
    </main>
    
    <footer class="footer">
      <p>&copy; 2024 TShock 管理面板</p>
    </footer>
  </div>
</template>

<style scoped>
.home {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background-color: var(--bg-primary);
  color: var(--text-primary);
}

.main-content {
  flex: 1;
  padding: 80px 40px 40px;
  max-width: 900px;
  margin: 0 auto;
  width: 100%;
  box-sizing: border-box;
}

.hero {
  text-align: center;
  margin-bottom: 50px;
}

.hero h2 {
  font-size: 2rem;
  color: var(--text-primary);
  margin: 0 0 16px 0;
  font-weight: 700;
}

.hero p {
  color: var(--text-secondary);
  margin: 0;
  font-size: 1.1rem;
}

.features {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 24px;
  margin-bottom: 50px;
}

.feature-card {
  background: var(--bg-card);
  padding: 32px 24px;
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-md);
  text-align: center;
  border: 1px solid var(--border-light);
  transition: all 0.25s ease;
}

.feature-card:hover {
  transform: translateY(-4px);
  box-shadow: var(--shadow-lg);
}

.feature-icon {
  font-size: 2.5rem;
  margin-bottom: 16px;
}

.feature-card h3 {
  margin: 0 0 12px 0;
  color: var(--text-primary);
  font-size: 1.2rem;
  font-weight: 600;
}

.feature-card p {
  margin: 0;
  color: var(--text-secondary);
  font-size: 0.95rem;
}

.actions {
  text-align: center;
  margin-bottom: 40px;
  display: flex;
  justify-content: center;
  gap: 16px;
}

.btn-primary {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
}

.btn-primary:hover {
  transform: translateY(-2px);
  box-shadow: var(--shadow-lg);
}

.btn-secondary {
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 2px solid var(--border-color);
}

.btn-secondary:hover {
  background: var(--bg-hover);
  border-color: var(--accent-primary);
}

.user-info {
  text-align: center;
  padding: 24px;
  max-width: 400px;
  margin: 0 auto;
}

.info-row {
  display: flex;
  justify-content: space-between;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  margin-bottom: 10px;
}

.info-row:last-child {
  margin-bottom: 0;
}

.label {
  color: var(--text-secondary);
  font-weight: 500;
}

.value {
  color: var(--text-primary);
  font-weight: 600;
}

.footer {
  text-align: center;
  padding: 24px;
  color: var(--text-muted);
  font-size: 0.9rem;
  border-top: 1px solid var(--border-light);
}
</style>
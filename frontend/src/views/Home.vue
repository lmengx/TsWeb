<script setup>
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { get } from '../utils/api.js'

const router = useRouter()

// ── 用户状态（与 AppHeader 一致） ──
const user = ref(null)
const showUserMenu = ref(false)
const userMenuRef = ref(null)
let closeTimer = null

const serverConnected = ref(false)
const userOnline = ref(false)
const qqBound = ref(false)
const qqNumber = ref('')
let statusTimer = null

const isLoggedIn = computed(() => user.value !== null)
const currentUser = computed(() => user.value?.username || '未登录')
const userGroup = computed(() => user.value?.usergroup || '')
const displayGroup = computed(() => {
  const g = userGroup.value.toLowerCase()
  if (g.includes('owner')) return 'Owner'
  if (g.includes('superadmin')) return 'SuperAdmin'
  return userGroup.value || '未知'
})
const isAdmin = computed(() => {
  const g = userGroup.value.toLowerCase()
  return g.includes('owner') || g.includes('superadmin')
})

const loadUser = () => {
  const saved = localStorage.getItem('user')
  if (saved) {
    try { user.value = JSON.parse(saved) } catch { user.value = null }
  }
}

const fetchStatus = async () => {
  try {
    const res = await get('/api/status')
    const data = await res.json()
    serverConnected.value = data.connected
  } catch { serverConnected.value = false }
  if (user.value?.username) {
    try {
      const res = await get('/api/tshock/activeusers')
      const data = await res.json()
      if (data.activeusers) {
        const names = data.activeusers.split('\t').filter(n => n.trim())
        userOnline.value = names.some(n => n.toLowerCase() === user.value.username.toLowerCase())
      } else { userOnline.value = false }
    } catch { userOnline.value = false }
  }
  if (user.value?.username) {
    try {
      const res = await get('/api/tshock/userlist?username=' + encodeURIComponent(user.value.username))
      const data = await res.json()
      if (data.status === '200' && data.users?.[0]) {
        qqNumber.value = data.users[0].QQ || ''
        qqBound.value = !!qqNumber.value
      }
    } catch { qqBound.value = false }
  }
}

const goHome = () => router.push('/')
const goLogin = () => router.push('/login')
const goToConsole = () => router.push('/console')
const toggleUserMenu = () => { showUserMenu.value = !showUserMenu.value }

const handleMouseEnter = () => {
  if (closeTimer) { clearTimeout(closeTimer); closeTimer = null }
  showUserMenu.value = true
}
const handleMouseLeave = () => {
  closeTimer = setTimeout(() => { showUserMenu.value = false; closeTimer = null }, 150)
}

const logout = () => {
  localStorage.removeItem('user')
  user.value = null
  showUserMenu.value = false
  router.push('/')
}

const handleClickOutside = (event) => {
  if (userMenuRef.value && !userMenuRef.value.contains(event.target)) {
    showUserMenu.value = false
  }
}

// ── 主题切换 ──
const isDark = ref(false)
const toggleTheme = () => {
  isDark.value = !isDark.value
  document.documentElement.setAttribute('data-theme', isDark.value ? 'dark' : 'light')
  localStorage.setItem('theme', isDark.value ? 'dark' : 'light')
}

// ── 滚动数字 ──
const stats = [
  { label: '管理玩家', target: 1284, suffix: '' },
  { label: '当前在线', target: 12, suffix: '' },
  { label: '封禁处理', target: 342, suffix: '' }
]
const statValues = ref(stats.map(() => 0))
const statsRef = ref(null)
let statsAnimated = false

const animateStats = () => {
  if (statsAnimated) return
  statsAnimated = true
  stats.forEach((s, i) => {
    const start = performance.now()
    const dur = [2000, 1500, 1800][i]
    const from = 0, to = s.target
    const tick = (now) => {
      const elapsed = now - start
      const progress = Math.min(elapsed / dur, 1)
      const eased = progress === 1 ? 1 : 1 - Math.pow(2, -10 * progress)
      statValues.value[i] = Math.floor(from + (to - from) * eased)
      if (progress < 1) requestAnimationFrame(tick)
    }
    requestAnimationFrame(tick)
  })
}

let scrollObs = null

// ── 功能卡片 ──
const features = [
  { title: '玩家管理', desc: '查看在线玩家、角色详情与背包物品，支持物品编辑和数据管理', size: 'md' },
  { title: '封禁管理', desc: '封禁 / 解封玩家，批量操作，UUID / IP 记录溯源', size: 'lg' },
  { title: 'Boss 进度', desc: '追踪 Boss 击败进度，配置召唤限制规则', size: 'sm' },
  { title: '反作弊系统', desc: '物品限制、弹幕拦截、UUID 检测、重复 IP 排查、自动扫描', size: 'lg' },
  { title: 'QQ 绑定', desc: '玩家绑定 QQ 号，支持注册 / 改密 / 身份关联', size: 'sm' },
  { title: '在线统计', desc: '每小时在线数据记录，玩家活跃排行与在线日历', size: 'md' },
  { title: '文件管理', desc: '服务端文件浏览与编辑，资源打包下载', size: 'sm' },
  { title: '进服策略', desc: '三种注册模式 + SSC 开荒 + BossLimit', size: 'md' }
]

const cardGlows = ref(new Array(features.length).fill({ x: 50, y: 50 }))
const onCardMove = (e, i) => {
  const rect = e.currentTarget.getBoundingClientRect()
  cardGlows.value[i] = { x: ((e.clientX - rect.left) / rect.width) * 100, y: ((e.clientY - rect.top) / rect.height) * 100 }
}

const techStack = [
  { name: 'Vue.js', color: '#4fc08d' },
  { name: 'Node.js', color: '#339933' },
  { name: 'TShock', color: '#6366f1' },
  { name: 'SQLite', color: '#003b57' }
]

onMounted(() => {
  loadUser()
  fetchStatus()
  statusTimer = setInterval(fetchStatus, 10000)
  document.addEventListener('click', handleClickOutside)

  const saved = localStorage.getItem('theme')
  if (saved === 'dark') {
    isDark.value = true
    document.documentElement.setAttribute('data-theme', 'dark')
  }

  if (statsRef.value) {
    scrollObs = new IntersectionObserver((entries) => {
      if (entries[0].isIntersecting) animateStats()
    }, { threshold: 0.3 })
    scrollObs.observe(statsRef.value)
  }
})

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
  if (statusTimer) clearInterval(statusTimer)
  scrollObs?.disconnect()
})
</script>

<template>
  <div class="home-page" :class="{ dark: isDark }">
    <!-- 导航 -->
    <nav class="home-nav">
      <div class="nav-brand" @click="goHome">
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
          <line x1="3" y1="9" x2="21" y2="9"></line>
          <line x1="9" y1="21" x2="9" y2="9"></line>
        </svg>
        <span class="nav-title">TSWeb</span>
      </div>
      <div class="nav-actions">
        <button class="theme-btn" @click="toggleTheme" :title="isDark ? '切换白天' : '切换黑夜'">
          <svg v-if="isDark" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="5"></circle>
            <line x1="12" y1="1" x2="12" y2="3"></line>
            <line x1="12" y1="21" x2="12" y2="23"></line>
            <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"></line>
            <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"></line>
            <line x1="1" y1="12" x2="3" y2="12"></line>
            <line x1="21" y1="12" x2="23" y2="12"></line>
            <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"></line>
            <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"></line>
          </svg>
          <svg v-else width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>
          </svg>
        </button>

        <!-- 登录状态（与 AppHeader 完全一致） -->
        <div class="user-menu-wrapper" ref="userMenuRef" @mouseenter="handleMouseEnter" @mouseleave="handleMouseLeave">
          <div class="user-status" @click="isLoggedIn ? toggleUserMenu() : goLogin()" :class="{ active: showUserMenu }">
            <span class="status-dot" :class="{ online: serverConnected }"></span>
            <span class="username">{{ currentUser }}</span>
            <span class="expand-icon">{{ isLoggedIn ? (showUserMenu ? '▲' : '▼') : '' }}</span>
          </div>

          <transition name="dropdown">
            <div v-if="showUserMenu && isLoggedIn" class="user-dropdown">
              <div class="dropdown-header">
                <div class="user-info">
                  <div class="user-name">{{ currentUser }}</div>
                  <div class="user-meta">
                    <span class="meta-item">
                      <span class="meta-label">权限组</span>
                      <span class="meta-value tag">{{ displayGroup }}</span>
                    </span>
                    <span class="meta-item">
                      <span class="meta-label">游戏状态</span>
                      <span class="meta-value" :class="userOnline ? 'online' : 'offline'">{{ userOnline ? '游戏中' : '离线' }}</span>
                    </span>
                    <span class="meta-item">
                      <span class="meta-label">QQ 绑定</span>
                      <span class="meta-value qq-glow" :class="qqBound ? 'bound' : 'unbound'">{{ qqBound ? qqNumber : '未绑定' }}</span>
                    </span>
                  </div>
                </div>
              </div>
              <div class="dropdown-divider"></div>
              <div class="dropdown-actions">
                <button class="logout-btn" @click="logout">退出登录</button>
              </div>
            </div>
          </transition>
        </div>
      </div>
    </nav>

    <main class="home-main">
      <!-- 英雄区 -->
      <section class="hero-section">
        <div class="hero-glow"></div>
        <div class="hero-badge">v1.0.0</div>
        <h1 class="hero-title">TShock Web 管理面板</h1>
        <p class="hero-desc">基于 Web 的 TShock 服务器管理工具，提供玩家管理、反作弊、在线统计、<br>QQ 绑定、Boss 限制等全方位功能。</p>
        <div class="hero-actions">
          <button v-if="!isLoggedIn" @click="goLogin" class="hero-btn primary">
            登录管理面板
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
              <line x1="5" y1="12" x2="19" y2="12"></line>
              <polyline points="12 5 19 12 12 19"></polyline>
            </svg>
          </button>
          <button v-else @click="goToConsole" class="hero-btn primary">
            进入控制台
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
              <line x1="5" y1="12" x2="19" y2="12"></line>
              <polyline points="12 5 19 12 12 19"></polyline>
            </svg>
          </button>
        </div>
      </section>

      <!-- 统计数字 -->
      <section ref="statsRef" class="stats-section">
        <div v-for="(s, i) in stats" :key="i" class="stat-card">
          <span class="stat-value">{{ statValues[i] }}{{ s.suffix }}</span>
          <span class="stat-label">{{ s.label }}</span>
        </div>
      </section>

      <!-- 功能 Bento 网格 -->
      <section class="bento-section">
        <div v-for="(f, i) in features" :key="i" class="bento-card" :class="f.size" @mousemove="(e) => onCardMove(e, i)" @mouseleave="cardGlows[i] = { x: 50, y: 50 }">
          <div class="bento-glow" :style="{ background: `radial-gradient(circle at ${cardGlows[i].x}% ${cardGlows[i].y}%, rgba(99,102,241,0.15), transparent 70%)` }"></div>
          <div class="bento-num">{{ String(i + 1).padStart(2, '0') }}</div>
          <h3 class="bento-title">{{ f.title }}</h3>
          <p class="bento-desc">{{ f.desc }}</p>
        </div>
      </section>

      <!-- 技术栈 -->
      <section class="tech-section">
        <p class="tech-label">技术栈</p>
        <div class="tech-row">
          <div v-for="t in techStack" :key="t.name" class="tech-item">
            <span class="tech-dot" :style="{ background: t.color }"></span>
            {{ t.name }}
          </div>
        </div>
      </section>
    </main>

    <footer class="home-footer">
      <p>TSWeb &mdash; TShock Web Management Panel</p>
    </footer>
  </div>
</template>

<style scoped>
/* ═══════════════════════════════════ 主题变量 ═══════════════════════════════════ */
.home-page {
  --bg-gradient: linear-gradient(135deg, #e0e7ff, #c7d2fe, #a5b4fc, #c7d2fe, #e0e7ff);
  --card-bg: rgba(255, 255, 255, 0.7);
  --card-border: rgba(0, 0, 0, 0.06);
  --card-hover-border: rgba(99, 102, 241, 0.3);
  --text-primary: #0f0a3a;
  --text-secondary: #4b5563;
  --text-muted: #6b7280;
  --nav-bg: rgba(255, 255, 255, 0.75);
  --nav-border: rgba(255, 255, 255, 0.4);
  --btn-bg: rgba(255, 255, 255, 0.8);
  --btn-border: rgba(0, 0, 0, 0.08);
  --stat-bg: rgba(255, 255, 255, 0.65);
  --bg-tertiary: rgba(255, 255, 255, 0.8);
  --border-color: rgba(0, 0, 0, 0.08);
  --bg-hover: rgba(99, 102, 241, 0.08);
  --accent-primary: #6366f1;
  --accent-error: #ef4444;
  --bg-card: rgba(255, 255, 255, 0.9);
  --shadow-lg: 0 8px 32px rgba(99, 102, 241, 0.12);
  --border-light: rgba(0, 0, 0, 0.06);

  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: var(--bg-gradient);
  background-size: 400% 400%;
  animation: bgFlow 8s ease infinite;
  transition: background 0.4s ease;
  color: var(--text-primary);
}

.home-page.dark {
  --bg-gradient: linear-gradient(135deg, #0f0a3a, #1e1b4b, #312e81, #1e1b4b, #0f0a3a);
  --card-bg: rgba(30, 27, 75, 0.6);
  --card-border: rgba(255, 255, 255, 0.06);
  --card-hover-border: rgba(129, 140, 248, 0.3);
  --text-primary: #e0e7ff;
  --text-secondary: #a5b4fc;
  --text-muted: #6b7280;
  --nav-bg: rgba(15, 10, 58, 0.75);
  --nav-border: rgba(255, 255, 255, 0.08);
  --btn-bg: rgba(255, 255, 255, 0.08);
  --btn-border: rgba(255, 255, 255, 0.12);
  --stat-bg: rgba(30, 27, 75, 0.5);
  --bg-tertiary: rgba(255, 255, 255, 0.06);
  --border-color: rgba(255, 255, 255, 0.1);
  --bg-hover: rgba(99, 102, 241, 0.15);
  --bg-card: rgba(30, 27, 75, 0.8);
  --shadow-lg: 0 8px 32px rgba(0, 0, 0, 0.3);
  --border-light: rgba(255, 255, 255, 0.06);
}

@keyframes bgFlow {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

/* ═══════════════════════════════════ 导航 ═══════════════════════════════════ */
.home-nav {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 28px;
  margin: 16px 24px;
  background: var(--nav-bg);
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(16px);
  border-radius: 16px;
  border: 1px solid var(--nav-border);
}

.nav-brand {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #6366f1;
  cursor: pointer;
}

.nav-title {
  font-size: 1.05rem;
  font-weight: 800;
  color: var(--text-primary);
}

.nav-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

/* ── 主题按钮 ── */
.theme-btn {
  width: 36px;
  height: 36px;
  border-radius: 10px;
  border: 1.5px solid var(--btn-border);
  background: var(--btn-bg);
  color: var(--text-muted);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;
}

.theme-btn:hover {
  border-color: #6366f1;
  color: #6366f1;
}

/* ── 用户状态（与 AppHeader 一致） ── */
.user-menu-wrapper {
  position: relative;
}

.user-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  background: var(--bg-tertiary);
  border-radius: 10px;
  border: 1px solid var(--border-color);
  cursor: pointer;
  transition: all 0.2s ease;
}

.user-status:hover { background: var(--bg-hover); border-color: var(--accent-primary); }
.user-status.active { background: var(--bg-hover); border-color: var(--accent-primary); }

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #6b7280;
}

.status-dot.online {
  background: #22c55e;
  box-shadow: 0 0 8px rgba(34, 197, 94, 0.6);
}

.username { font-size: 0.9rem; color: var(--text-primary); font-weight: 500; }

.expand-icon { font-size: 0.7rem; color: var(--text-secondary); margin-left: 4px; }

.user-dropdown {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  width: 260px;
  background: var(--bg-card);
  border-radius: 14px;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
  overflow: hidden;
  z-index: 1001;
}

.dropdown-enter-active, .dropdown-leave-active { transition: all 0.25s ease; }
.dropdown-enter-from, .dropdown-leave-to { opacity: 0; transform: translateY(-10px); }

.dropdown-header { padding: 20px; }
.user-info { flex: 1; }
.user-name { font-size: 1.1rem; font-weight: 600; color: var(--text-primary); margin-bottom: 16px; }
.user-meta { display: flex; flex-direction: column; gap: 10px; }
.meta-item { display: flex; justify-content: space-between; align-items: center; }
.meta-label { font-size: 0.82rem; color: var(--text-muted); }
.meta-value { font-size: 0.85rem; font-weight: 600; color: var(--text-primary); }
.meta-value.tag {
  color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.15);
  padding: 2px 10px;
  border-radius: 10px;
  font-size: 0.78rem;
}
.meta-value.online { color: #22c55e; }
.meta-value.offline { color: #6b7280; }
.meta-value.bound { color: #22c55e; }
.meta-value.unbound { color: #6b7280; }

.dropdown-divider { height: 1px; background: var(--border-light); margin: 0 20px; }
.dropdown-actions { padding: 16px 20px; }

.logout-btn {
  width: 100%;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
}

.logout-btn:hover { background: rgba(239, 68, 68, 0.1); border-color: var(--accent-error); color: var(--accent-error); }

.meta-value.qq-glow.bound {
  font-weight: 800;
  color: #22c55e;
  background: linear-gradient(90deg, #22c55e, #4ade80, #22c55e);
  background-size: 200% 100%;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  animation: qq-shimmer 2.5s ease-in-out infinite;
}
@keyframes qq-shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

/* ═══════════════════════════════════ 主内容 ═══════════════════════════════════ */
.home-main {
  flex: 1;
  max-width: 820px;
  margin: 0 auto;
  width: 100%;
  padding: 40px 24px 40px;
  box-sizing: border-box;
}

/* ═══════════════════════════════════ 英雄区 ═══════════════════════════════════ */
.hero-section {
  text-align: center;
  margin-bottom: 48px;
  position: relative;
}

.hero-glow {
  position: absolute;
  top: -60px;
  left: 50%;
  transform: translateX(-50%);
  width: 400px;
  height: 200px;
  background: radial-gradient(ellipse, rgba(99, 102, 241, 0.2), transparent 70%);
  pointer-events: none;
}

.hero-badge {
  display: inline-block;
  padding: 4px 14px;
  border-radius: 20px;
  background: rgba(99, 102, 241, 0.12);
  border: 1px solid rgba(99, 102, 241, 0.2);
  color: #6366f1;
  font-size: 0.78rem;
  font-weight: 700;
  margin-bottom: 16px;
  position: relative;
}

.hero-title {
  margin: 0 0 14px;
  font-size: clamp(2rem, 5vw, 3rem);
  font-weight: 800;
  background: linear-gradient(135deg, #4f46e5, #7c3aed);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  line-height: 1.15;
}

.hero-desc {
  margin: 0 auto 28px;
  max-width: 560px;
  font-size: 0.95rem;
  color: var(--text-secondary);
  line-height: 1.7;
}

.hero-actions { display: flex; justify-content: center; gap: 12px; }

.hero-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 12px 28px;
  border-radius: 12px;
  font-size: 0.95rem;
  font-weight: 700;
  cursor: pointer;
  transition: all 0.3s cubic-bezier(0.34, 1.56, 0.64, 1);
  border: none;
}

.hero-btn.primary {
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.25);
}

.hero-btn.primary:hover {
  transform: translateY(-3px) scale(1.03);
  box-shadow: 0 8px 28px rgba(99, 102, 241, 0.4);
}

.hero-btn.primary:active { transform: translateY(0) scale(0.97); }

/* ═══════════════════════════════════ 统计数字 ═══════════════════════════════════ */
.stats-section {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
  margin-bottom: 40px;
}

.stat-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 28px 20px;
  background: var(--stat-bg);
  border: 1px solid var(--card-border);
  border-radius: 16px;
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  transition: all 0.25s ease;
}

.stat-card:hover {
  border-color: var(--card-hover-border);
  transform: translateY(-3px);
  box-shadow: 0 8px 24px rgba(99, 102, 241, 0.1);
}

.stat-value {
  font-size: 2.2rem;
  font-weight: 800;
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  font-variant-numeric: tabular-nums;
}

.stat-label { font-size: 0.85rem; color: var(--text-muted); font-weight: 500; }

/* ═══════════════════════════════════ Bento 网格 ═══════════════════════════════════ */
.bento-section {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
  margin-bottom: 40px;
}

.bento-card {
  position: relative;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 22px;
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  border-radius: 16px;
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  transition: border-color 0.3s ease, transform 0.4s cubic-bezier(0.34, 1.56, 0.64, 1), box-shadow 0.3s ease;
  cursor: default;
}

.bento-card:hover {
  border-color: var(--card-hover-border);
  transform: translateY(-4px) scale(1.02);
  box-shadow: 0 12px 32px rgba(99, 102, 241, 0.12);
}

.bento-glow {
  position: absolute;
  inset: 0;
  pointer-events: none;
  opacity: 0;
  transition: opacity 0.3s ease;
  border-radius: 16px;
}

.bento-card:hover .bento-glow { opacity: 1; }

.bento-card.lg { grid-column: span 2; grid-row: span 1; }
.bento-card.md { grid-column: span 1; grid-row: span 1; }
.bento-card.sm { grid-column: span 1; grid-row: span 1; }

.bento-num { font-size: 0.7rem; font-weight: 800; color: #a5b4fc; font-family: monospace; }
.bento-title { margin: 0; font-size: 0.95rem; font-weight: 700; color: var(--text-primary); }
.bento-desc { margin: 0; font-size: 0.8rem; color: var(--text-secondary); line-height: 1.5; }
.bento-card.sm .bento-desc { font-size: 0.75rem; }

/* ═══════════════════════════════════ 技术栈 ═══════════════════════════════════ */
.tech-section { text-align: center; margin-bottom: 40px; }

.tech-label {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 2px;
  color: var(--text-muted);
  margin: 0 0 14px;
}

.tech-row { display: flex; justify-content: center; gap: 24px; flex-wrap: wrap; }

.tech-item {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-muted);
}

.tech-dot { width: 8px; height: 8px; border-radius: 50%; }

/* ═══════════════════════════════════ 底部 ═══════════════════════════════════ */
.home-footer {
  text-align: center;
  padding: 20px;
  color: var(--text-muted);
  font-size: 0.85rem;
}

/* ═══════════════════════════════════ 响应式 ═══════════════════════════════════ */
@media (max-width: 640px) {
  .home-nav {
    margin: 12px;
    padding: 10px 16px;
    flex-wrap: wrap;
    gap: 8px;
  }
  .bento-section { grid-template-columns: repeat(2, 1fr); }
  .bento-card.lg { grid-column: span 2; }
  .stats-section { grid-template-columns: repeat(3, 1fr); }
  .stat-value { font-size: 1.6rem; }
  .hero-title { font-size: 1.8rem; }
}
</style>

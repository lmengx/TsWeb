<script setup>
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useTheme } from '../composables/useTheme'
import { get } from '../utils/api.js'

const router = useRouter()
const { isDark, toggleTheme } = useTheme()

// ── 用户状态 ──
const user = ref(null)
const showUserMenu = ref(false)
const userMenuRef = ref(null)
let closeTimer = null
const serverConnected = ref(false)
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
  if (user.value?.username) {
    try {
      await get('/api/user/selfinfo')
      serverConnected.value = true
    } catch { serverConnected.value = false }
  }
}

const goHome = () => router.push('/')
const goLogin = () => router.push('/login')
const goToConsole = () => router.push('/console')
const goVote = () => router.push('/vote')
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

// ── 项目统计数据 ──
const stats = [
  { label: '管理功能模块', value: 20, suffix: '+' },
  { label: 'REST 管理接口', value: 90, suffix: '+' },
  { label: '玩家保护能力', value: 5, suffix: ' 大类' }
]
const statValues = ref(stats.map(() => 0))
const statsRef = ref(null)
let statsAnimated = false
let scrollObs = null

// 3D 卡片
const cardRefs = ref([])
const cardVisible = ref([false, false, false])

const setCardRef = (el) => {
  if (el) cardRefs.value.push(el)
}

const animateStats = () => {
  if (statsAnimated) return
  statsAnimated = true
  stats.forEach((s, i) => {
    const start = performance.now()
    const dur = [2000, 1800, 1200][i]
    const from = 0, to = s.value
    if (to === 0) {
      statValues.value[i] = 0
      return
    }
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

// ── 功能卡片 ──
const features = [
  { title: '玩家管理', desc: '查看在线玩家、角色详情与背包物品，支持物品编辑和数据管理', size: 'md', icon: 'users' },
  { title: '封禁管理', desc: '封禁 / 解封玩家，批量操作，UUID / IP 记录溯源', size: 'lg', icon: 'ban' },
  { title: 'Boss 进度', desc: '追踪 Boss 击败进度，配置召唤限制规则', size: 'sm', icon: 'globe' },
  { title: '反作弊系统', desc: '物品限制、弹幕拦截、UUID 检测、重复 IP 排查、自动扫描', size: 'lg', icon: 'shield' },
  { title: 'QQ 绑定', desc: '玩家绑定 QQ 号，支持注册 / 改密 / 身份关联', size: 'sm', icon: 'message' },
  { title: '在线统计', desc: '每小时在线数据记录，玩家活跃排行与在线日历', size: 'md', icon: 'chart' },
  { title: '文件管理', desc: '服务端文件浏览与编辑，资源打包下载', size: 'sm', icon: 'folder' },
  { title: '进服策略', desc: '三种注册模式 + SSC 开荒 + BossLimit', size: 'md', icon: 'key' }
]

const cardGlows = ref(new Array(features.length).fill({ x: 50, y: 50 }))
const onCardMove = (e, i) => {
  const rect = e.currentTarget.getBoundingClientRect()
  cardGlows.value[i] = { x: ((e.clientX - rect.left) / rect.width) * 100, y: ((e.clientY - rect.top) / rect.height) * 100 }
}

const techStack = [
  { name: 'Vue.js', color: '#4fc08d' },
  { name: 'Node.js', color: '#339933' },
  { name: 'C#', color: '#9b59b6' }
]

// ── 特性图标 ──
const featureIcons = {
  users: '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M23 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path>',
  ban: '<circle cx="12" cy="12" r="10"></circle><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"></line>',
  globe: '<circle cx="12" cy="12" r="10"></circle><line x1="2" y1="12" x2="22" y2="12"></line><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"></path>',
  shield: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>',
  message: '<path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"></path>',
  chart: '<line x1="18" y1="20" x2="18" y2="10"></line><line x1="12" y1="20" x2="12" y2="4"></line><line x1="6" y1="20" x2="6" y2="14"></line>',
  folder: '<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>',
  key: '<path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path>'
}

onMounted(() => {
  loadUser()
  fetchStatus()
  statusTimer = setInterval(fetchStatus, 10000)
  document.addEventListener('click', handleClickOutside)

  // 统计数字滚动
  if (statsRef.value) {
    scrollObs = new IntersectionObserver((entries) => {
      if (entries[0].isIntersecting) {
        animateStats()
      }
    }, { threshold: 0.3 })
    scrollObs.observe(statsRef.value)
  }

  // 3D 卡片滚动观察
  setTimeout(() => {
    cardRefs.value.forEach((el, i) => {
      if (!el) return
      const obs = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
          if (entry.isIntersecting) {
            cardVisible.value[i] = true
            obs.unobserve(el)
          }
        })
      }, { threshold: 0.2 })
      obs.observe(el)
    })
  }, 100)
})

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
  if (statusTimer) clearInterval(statusTimer)
  if (scrollObs) scrollObs.disconnect()
})
</script>

<template>
  <div class="home-page">
    <div class="tech-bg"></div>
    <div class="home-orb orb-1"></div>
    <div class="home-orb orb-2"></div>

    <!-- 导航 -->
    <nav class="home-nav glass">
      <div class="nav-brand" @click="goHome">
        <div class="brand-logo">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
            <line x1="3" y1="9" x2="21" y2="9"></line>
            <line x1="9" y1="21" x2="9" y2="9"></line>
          </svg>
        </div>
        <span class="nav-title text-gradient">TSWeb</span>
        <span class="nav-sub">管理面板</span>
      </div>
      <div class="nav-actions">
        <button class="theme-btn" @click="toggleTheme" :title="isDark ? '切换白天' : '切换黑夜'">
          <svg v-if="isDark" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
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
          <svg v-else width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>
          </svg>
        </button>

        <div class="user-menu-wrapper" ref="userMenuRef" @mouseenter="handleMouseEnter" @mouseleave="handleMouseLeave">
          <div class="user-status" @click="isLoggedIn ? toggleUserMenu() : goLogin()" :class="{ active: showUserMenu }">
            <span class="status-dot" :class="{ online: serverConnected }"></span>
            <span class="username">{{ currentUser }}</span>
            <svg v-if="isLoggedIn" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="expand-icon" :class="{ rotated: showUserMenu }">
              <polyline points="6 9 12 15 18 9"></polyline>
            </svg>
          </div>

          <transition name="dropdown">
            <div v-if="showUserMenu && isLoggedIn" class="user-dropdown">
              <div class="dropdown-header">
                <div class="user-avatar">{{ currentUser.charAt(0).toUpperCase() }}</div>
                <div class="user-info">
                  <div class="user-name">{{ currentUser }}</div>
                  <div class="user-meta">
                    <span class="meta-item">
                      <span class="meta-label">权限组</span>
                      <span class="meta-value tag tag-primary">{{ displayGroup }}</span>
                    </span>
                  </div>
                </div>
              </div>
              <div class="dropdown-divider"></div>
              <div class="dropdown-actions"><button class="logout-btn" @click="logout">退出登录</button></div>
            </div>
          </transition>
        </div>
      </div>
    </nav>

    <main class="home-main">
      <!-- 英雄区 -->
      <section class="hero-section">
        <div class="hero-badge">
          <span class="badge-dot"></span>
          v1.0.0
        </div>
        <h1 class="hero-title">TShock <span class="text-gradient">Web</span> 管理面板</h1>
        <p class="hero-desc">
          基于 Web 的 TShock 服务器管理工具，提供玩家管理、反作弊、在线统计、
          QQ 绑定、Boss 限制等全方位功能。
        </p>
        <div class="hero-actions">
          <button @click="goVote" class="hero-btn secondary">
            玩家投票入口
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
              <polyline points="12 5 19 12 12 19"></polyline>
              <line x1="5" y1="12" x2="19" y2="12"></line>
            </svg>
          </button>
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

      <!-- 项目进度 -->
      <section ref="statsRef" class="progress-section">
        <div class="progress-left">
          <h2 class="progress-title">项目概览</h2>
          <p class="progress-desc">TSWeb 管理面板核心能力数据</p>
        </div>
        <div class="progress-cards">
          <div v-for="(s, i) in stats" :key="i" :ref="setCardRef" class="progress-card glass" :class="{ visible: cardVisible[i] }">
            <span class="progress-value text-gradient">{{ statValues[i] }}{{ s.suffix }}</span>
            <span class="progress-label">{{ s.label }}</span>
          </div>
        </div>
      </section>

      <!-- 功能 Bento 网格 -->
      <section class="bento-section">
        <div v-for="(f, i) in features" :key="i" class="bento-card glass" :class="f.size"
          @mousemove="(e) => onCardMove(e, i)" @mouseleave="cardGlows[i] = { x: 50, y: 50 }">
          <div class="bento-glow" :style="{ background: `radial-gradient(circle at ${cardGlows[i].x}% ${cardGlows[i].y}%, rgba(99,102,241,0.18), transparent 70%)` }"></div>
          <div class="bento-icon">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" v-html="featureIcons[f.icon] || ''"></svg>
          </div>
          <h3 class="bento-title">{{ f.title }}</h3>
          <p class="bento-desc">{{ f.desc }}</p>
        </div>
      </section>

      <!-- 技术栈 -->
      <section class="tech-section">
        <p class="tech-label">技术栈</p>
        <div class="tech-row">
          <div v-for="t in techStack" :key="t.name" class="tech-item" :style="{ '--dot-color': t.color }">
            <span class="tech-dot" :style="{ background: t.color, boxShadow: `0 0 8px ${t.color}` }"></span>
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
.home-page {
  position: relative;
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background-color: var(--bg-base);
  color: var(--text-primary);
  overflow-x: hidden;
}

/* 漂浮光球 */
.home-orb {
  position: fixed;
  border-radius: 50%;
  filter: blur(90px);
  pointer-events: none;
  z-index: 0;
  opacity: 0.5;
}
.orb-1 {
  width: 420px;
  height: 420px;
  top: -120px;
  right: -100px;
  background: radial-gradient(circle, rgba(99, 102, 241, 0.5), transparent 70%);
  animation: float 12s ease-in-out infinite;
}
.orb-2 {
  width: 380px;
  height: 380px;
  bottom: -140px;
  left: -120px;
  background: radial-gradient(circle, rgba(34, 211, 238, 0.35), transparent 70%);
  animation: float 16s ease-in-out infinite reverse;
}

/* 导航 */
.home-nav {
  position: relative;
  z-index: 10;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 20px;
  margin: 16px 24px;
  border-radius: var(--radius-lg);
  animation: fadeUp 0.6s var(--ease-out);
}
.nav-brand { display: flex; align-items: center; gap: 10px; color: var(--accent-primary); cursor: pointer; user-select: none; }
.brand-logo {
  display: flex; align-items: center; justify-content: center;
  width: 34px; height: 34px; border-radius: 10px;
  background: var(--gradient-primary); color: #fff;
  box-shadow: var(--glow-primary);
}
.nav-title { font-size: 1.05rem; font-weight: 800; letter-spacing: 0.5px; }
.nav-sub {
  font-size: 0.72rem; color: var(--text-muted);
  border-left: 1px solid var(--border-color); padding-left: 10px; margin-left: 2px;
}
.nav-actions { display: flex; align-items: center; gap: 10px; }

.theme-btn {
  width: 36px; height: 36px; border-radius: 10px;
  border: 1px solid var(--border-color);
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  cursor: pointer; display: flex; align-items: center; justify-content: center;
  transition: all var(--dur-fast) var(--ease-out);
}
.theme-btn:hover { border-color: var(--border-glow); color: var(--accent-primary); box-shadow: var(--glow-primary); }

.user-menu-wrapper { position: relative; }
.user-status {
  display: flex; align-items: center; gap: 8px;
  padding: 8px 14px; background: var(--bg-tertiary);
  border-radius: 10px; border: 1px solid var(--border-color);
  cursor: pointer; transition: all var(--dur-fast) var(--ease-out);
}
.user-status:hover { background: var(--bg-hover); border-color: var(--border-glow); }
.user-status.active { background: var(--bg-hover); border-color: var(--accent-primary); }
.status-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--text-muted); }
.status-dot.online { background: var(--accent-secondary); box-shadow: 0 0 8px rgba(16, 185, 129, 0.7); }
.username { font-size: 0.9rem; color: var(--text-primary); font-weight: 600; }
.expand-icon { color: var(--text-secondary); transition: transform var(--dur-fast) var(--ease-out); }
.expand-icon.rotated { transform: rotate(180deg); }

.user-dropdown {
  position: absolute; top: calc(100% + 10px); right: 0;
  width: 280px;
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
  overflow: hidden; z-index: 1001;
}
.dropdown-enter-active, .dropdown-leave-active { transition: all 0.22s var(--ease-out); }
.dropdown-enter-from, .dropdown-leave-to { opacity: 0; transform: translateY(-8px) scale(0.98); }
.dropdown-header { padding: 20px; display: flex; gap: 14px; align-items: center; }
.user-avatar {
  width: 44px; height: 44px; border-radius: 12px;
  background: var(--gradient-primary); color: #fff;
  font-size: 1.2rem; font-weight: 700;
  display: flex; align-items: center; justify-content: center; flex-shrink: 0;
  box-shadow: var(--glow-primary);
}
.user-info { flex: 1; min-width: 0; }
.user-name { font-size: 1.05rem; font-weight: 700; color: var(--text-primary); margin-bottom: 10px; }
.user-meta { display: flex; flex-direction: column; gap: 8px; }
.meta-item { display: flex; justify-content: space-between; align-items: center; }
.meta-label { font-size: 0.78rem; color: var(--text-muted); }
.dropdown-divider { height: 1px; background: var(--border-light); margin: 0 20px; }
.dropdown-actions { padding: 16px 20px; }
.logout-btn {
  width: 100%; padding: 11px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color); border-radius: var(--radius-sm);
  color: var(--text-primary); font-size: 0.9rem; font-weight: 600;
  cursor: pointer; transition: all var(--dur-fast) var(--ease-out);
}
.logout-btn:hover { background: rgba(244, 63, 94, 0.12); border-color: var(--accent-error); color: var(--accent-error); }

/* 主内容 */
.home-main {
  position: relative;
  z-index: 1;
  flex: 1;
  max-width: 920px;
  margin: 0 auto;
  width: 100%;
  padding: 48px 24px 40px;
  box-sizing: border-box;
}

/* 英雄区 */
.hero-section { text-align: center; margin-bottom: 56px; position: relative; animation: fadeUp 0.7s var(--ease-out) 0.1s both; }
.hero-badge {
  display: inline-flex; align-items: center; gap: 8px;
  padding: 6px 16px; border-radius: 999px;
  background: rgba(99, 102, 241, 0.1);
  border: 1px solid rgba(99, 102, 241, 0.3);
  color: #a5b4fc;
  font-size: 0.78rem; font-weight: 700;
  margin-bottom: 20px;
  box-shadow: var(--glow-primary);
}
.badge-dot {
  width: 6px; height: 6px; border-radius: 50%;
  background: var(--accent-cyan);
  box-shadow: 0 0 8px var(--accent-cyan);
  animation: pulseGlow 2s ease-in-out infinite;
}
.hero-title {
  margin: 0 0 16px;
  font-size: clamp(2.2rem, 5.5vw, 3.2rem);
  font-weight: 800;
  line-height: 1.15;
  letter-spacing: -0.5px;
  color: var(--text-primary);
}
.hero-desc {
  margin: 0 auto 32px;
  max-width: 600px;
  font-size: 0.98rem;
  color: var(--text-secondary);
  line-height: 1.8;
}
.hero-actions { display: flex; justify-content: center; gap: 14px; flex-wrap: wrap; }
.hero-btn {
  display: inline-flex; align-items: center; gap: 8px;
  padding: 13px 30px; border-radius: var(--radius-md);
  font-size: 0.95rem; font-weight: 700; cursor: pointer;
  transition: all 0.3s var(--ease-out);
}
.hero-btn.primary {
  background: var(--gradient-primary); color: white;
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.35);
}
.hero-btn.primary:hover { transform: translateY(-3px); box-shadow: 0 8px 32px rgba(99, 102, 241, 0.55), var(--glow-primary); }
.hero-btn.primary:active { transform: translateY(0) scale(0.97); }
.hero-btn.secondary {
  background: var(--bg-tertiary);
  color: var(--accent-primary);
  border: 1.5px solid rgba(99, 102, 241, 0.4);
}
.hero-btn.secondary:hover { transform: translateY(-3px); border-color: var(--accent-primary); box-shadow: 0 8px 24px rgba(99, 102, 241, 0.2); }
.hero-btn.secondary:active { transform: translateY(0) scale(0.97); }

/* 项目概览（分栏 + 3D 滚动） */
.progress-section { display: flex; gap: 40px; align-items: center; margin-bottom: 56px; animation: fadeUp 0.7s var(--ease-out) 0.2s both; }
.progress-left { flex-shrink: 0; width: 200px; }
.progress-title { margin: 0 0 8px; font-size: 1.4rem; font-weight: 800; color: var(--text-primary); }
.progress-desc { margin: 0; font-size: 0.85rem; color: var(--text-muted); line-height: 1.6; }
.progress-cards { display: flex; gap: 16px; flex: 1; }

.progress-card {
  flex: 1; display: flex; flex-direction: column; align-items: center; gap: 8px;
  padding: 30px 16px;
  border-radius: var(--radius-lg);
  transition: all 0.5s var(--ease-out);
  transform: perspective(600px) rotateY(25deg) translateX(30px) scale(0.9);
  opacity: 0;
}
.progress-card.visible {
  transform: perspective(600px) rotateY(0deg) translateX(0) scale(1);
  opacity: 1;
}
.progress-card:nth-child(2) { transition-delay: 0.12s; }
.progress-card:nth-child(3) { transition-delay: 0.24s; }
.progress-card:hover {
  border-color: rgba(99, 102, 241, 0.45);
  transform: perspective(600px) rotateY(0deg) translateX(0) scale(1.03) !important;
  box-shadow: 0 8px 32px rgba(99, 102, 241, 0.15);
}
.progress-value { font-size: 2.4rem; font-weight: 800; font-variant-numeric: tabular-nums; line-height: 1; }
.progress-label { font-size: 0.82rem; color: var(--text-muted); font-weight: 500; text-align: center; }

/* Bento 网格 */
.bento-section {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 14px;
  margin-bottom: 48px;
  animation: fadeUp 0.7s var(--ease-out) 0.3s both;
}
.bento-card {
  position: relative; overflow: hidden;
  display: flex; flex-direction: column; gap: 10px;
  padding: 24px;
  border-radius: var(--radius-lg);
  transition: border-color 0.3s ease, transform 0.4s var(--ease-out), box-shadow 0.3s ease;
  cursor: default;
}
.bento-card:hover {
  border-color: rgba(99, 102, 241, 0.45);
  transform: translateY(-4px);
  box-shadow: 0 16px 40px rgba(99, 102, 241, 0.18);
}
.bento-glow { position: absolute; inset: 0; pointer-events: none; opacity: 0; transition: opacity 0.3s ease; }
.bento-card:hover .bento-glow { opacity: 1; }
.bento-card.lg { grid-column: span 2; }
.bento-icon {
  width: 38px; height: 38px;
  display: flex; align-items: center; justify-content: center;
  border-radius: 10px;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.18), rgba(139, 92, 246, 0.12));
  border: 1px solid rgba(99, 102, 241, 0.3);
  color: #a5b4fc;
  margin-bottom: 4px;
}
.bento-icon svg { width: 20px; height: 20px; }
.bento-title { margin: 0; font-size: 0.98rem; font-weight: 700; color: var(--text-primary); }
.bento-desc { margin: 0; font-size: 0.8rem; color: var(--text-secondary); line-height: 1.6; }
.bento-card.sm .bento-desc { font-size: 0.76rem; }

/* 技术栈 */
.tech-section { text-align: center; margin-bottom: 40px; }
.tech-label {
  font-size: 0.75rem; font-weight: 700;
  text-transform: uppercase; letter-spacing: 3px;
  color: var(--text-muted); margin: 0 0 16px;
}
.tech-row { display: flex; justify-content: center; gap: 32px; flex-wrap: wrap; }
.tech-item { display: flex; align-items: center; gap: 8px; font-size: 0.88rem; font-weight: 600; color: var(--text-secondary); }
.tech-dot { width: 8px; height: 8px; border-radius: 50%; }
.tech-item:hover { color: var(--text-primary); }
.tech-item:hover .tech-dot { box-shadow: 0 0 12px var(--dot-color, currentColor), 0 0 24px var(--dot-color, currentColor); }

/* 底部 */
.home-footer { position: relative; z-index: 1; text-align: center; padding: 20px; color: var(--text-muted); font-size: 0.85rem; }

/* 响应式 */
@media (max-width: 768px) {
  .home-nav { margin: 12px; padding: 10px 14px; flex-wrap: wrap; gap: 8px; }
  .progress-section { flex-direction: column; gap: 20px; }
  .progress-left { width: 100%; text-align: center; }
  .progress-cards { flex-direction: column; }
  .bento-section { grid-template-columns: repeat(2, 1fr); }
  .bento-card.lg { grid-column: span 2; }
  .hero-title { font-size: 1.8rem; }
  .home-main { padding: 36px 16px 32px; }
}
</style>

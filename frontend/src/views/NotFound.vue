<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'

const router = useRouter()

const goHome = () => router.push('/')
const goBack = () => router.back()

const isDark = ref(false)

onMounted(() => {
  const saved = localStorage.getItem('theme')
  if (saved === 'dark') {
    isDark.value = true
    document.documentElement.setAttribute('data-theme', 'dark')
  }
})
</script>

<template>
  <div class="not-found-page" :class="{ dark: isDark }">
    <div class="not-found-glow"></div>

    <nav class="nf-nav">
      <div class="nf-brand" @click="goHome">
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
          <line x1="3" y1="9" x2="21" y2="9"></line>
          <line x1="9" y1="21" x2="9" y2="9"></line>
        </svg>
        <span class="nf-brand-text">TSWeb</span>
      </div>
    </nav>

    <main class="nf-main">
      <div class="nf-code">
        <span class="nf-digit">4</span>
        <span class="nf-digit nf-zero">0</span>
        <span class="nf-digit">4</span>
      </div>

      <div class="nf-divider"></div>

      <h1 class="nf-title">页面不存在</h1>
      <p class="nf-desc">
        你访问的地址没有对应页面，可能已被移动或删除。
      </p>

      <div class="nf-actions">
        <button class="nf-btn primary" @click="goHome">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path>
            <polyline points="9 22 9 12 15 12 15 22"></polyline>
          </svg>
          返回首页
        </button>
        <button class="nf-btn secondary" @click="goBack">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <line x1="19" y1="12" x2="5" y2="12"></line>
            <polyline points="12 19 5 12 12 5"></polyline>
          </svg>
          后退
        </button>
      </div>
    </main>

    <footer class="nf-footer">
      <p>TSWeb &mdash; TShock Web Management Panel</p>
    </footer>
  </div>
</template>

<style scoped>
.not-found-page {
  --bg-gradient: linear-gradient(135deg, #e0e7ff, #c7d2fe, #a5b4fc, #c7d2fe, #e0e7ff);
  --text-primary: #0f0a3a;
  --text-secondary: #4b5563;
  --text-muted: #6b7280;
  --nav-bg: rgba(255, 255, 255, 0.75);
  --nav-border: rgba(255, 255, 255, 0.4);
  --btn-bg: rgba(255, 255, 255, 0.8);
  --btn-border: rgba(0, 0, 0, 0.08);
  --digit-color: #6366f1;
  --digit-shadow: rgba(99, 102, 241, 0.25);
  --card-bg: rgba(255, 255, 255, 0.7);

  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: var(--bg-gradient);
  background-size: 400% 400%;
  animation: bgFlow 8s ease infinite;
  color: var(--text-primary);
}

.not-found-page.dark {
  --bg-gradient: linear-gradient(135deg, #0f0a3a, #1e1b4b, #312e81, #1e1b4b, #0f0a3a);
  --text-primary: #e0e7ff;
  --text-secondary: #a5b4fc;
  --text-muted: #6b7280;
  --nav-bg: rgba(15, 10, 58, 0.75);
  --nav-border: rgba(255, 255, 255, 0.08);
  --btn-bg: rgba(255, 255, 255, 0.08);
  --btn-border: rgba(255, 255, 255, 0.12);
  --digit-color: #818cf8;
  --digit-shadow: rgba(129, 140, 248, 0.3);
  --card-bg: rgba(30, 27, 75, 0.6);
}

@keyframes bgFlow {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

/* 导航 */
.nf-nav {
  display: flex;
  align-items: center;
  padding: 12px 28px;
  margin: 16px 24px;
  background: var(--nav-bg);
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(16px);
  border-radius: 16px;
  border: 1px solid var(--nav-border);
}

.nf-brand {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #6366f1;
  cursor: pointer;
}

.nf-brand-text {
  font-size: 1.05rem;
  font-weight: 800;
  color: var(--text-primary);
}

/* 主区域 */
.nf-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 24px;
  text-align: center;
  position: relative;
}

.not-found-glow {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -55%);
  width: 500px;
  height: 300px;
  background: radial-gradient(ellipse, rgba(99, 102, 241, 0.12), transparent 70%);
  pointer-events: none;
}

/* 404 数字 */
.nf-code {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  position: relative;
}

.nf-digit {
  font-size: clamp(5rem, 15vw, 9rem);
  font-weight: 900;
  line-height: 1;
  color: var(--digit-color);
  text-shadow: 0 4px 24px var(--digit-shadow);
  letter-spacing: -4px;
  animation: digitPulse 3s ease-in-out infinite;
}

.nf-zero {
  animation-delay: 0.5s;
  opacity: 0.85;
}

@keyframes digitPulse {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(-8px); }
}

.nf-divider {
  width: 48px;
  height: 3px;
  border-radius: 2px;
  background: linear-gradient(90deg, #6366f1, #8b5cf6);
  margin: 8px 0 20px;
}

/* 文本 */
.nf-title {
  margin: 0 0 12px;
  font-size: clamp(1.3rem, 3vw, 1.8rem);
  font-weight: 700;
}

.nf-desc {
  margin: 0 auto 32px;
  max-width: 420px;
  font-size: 0.95rem;
  color: var(--text-secondary);
  line-height: 1.7;
}

/* 按钮组 */
.nf-actions {
  display: flex;
  gap: 12px;
}

.nf-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 11px 24px;
  border-radius: 12px;
  font-size: 0.92rem;
  font-weight: 700;
  cursor: pointer;
  transition: all 0.3s cubic-bezier(0.34, 1.56, 0.64, 1);
  border: none;
  text-decoration: none;
}

.nf-btn.primary {
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.25);
}

.nf-btn.primary:hover {
  transform: translateY(-3px) scale(1.03);
  box-shadow: 0 8px 28px rgba(99, 102, 241, 0.4);
}

.nf-btn.primary:active {
  transform: translateY(0) scale(0.97);
}

.nf-btn.secondary {
  background: var(--btn-bg);
  border: 1px solid var(--btn-border);
  color: var(--text-primary);
}

.nf-btn.secondary:hover {
  border-color: #6366f1;
  color: #6366f1;
  transform: translateY(-3px);
}

.nf-btn.secondary:active {
  transform: translateY(0);
}

/* 底部 */
.nf-footer {
  text-align: center;
  padding: 20px;
  color: var(--text-muted);
  font-size: 0.85rem;
}

/* 响应式 */
@media (max-width: 480px) {
  .nf-nav { margin: 12px; padding: 10px 16px; }
  .nf-actions { flex-direction: column; align-items: center; }
}
</style>

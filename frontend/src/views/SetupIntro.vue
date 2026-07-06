<script setup>
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const features = [
  {
    title: '玩家管理',
    desc: '查看在线玩家、角色详情、背包物品，支持物品编辑和角色数据管理'
  },
  {
    title: '封禁管理',
    desc: '封禁 / 解封玩家，批量操作，查看封禁历史和 UUID / IP 记录'
  },
  {
    title: 'Boss 进度',
    desc: '追踪服务器 Boss 击败进度，配置召唤限制规则，防止恶意召唤'
  },
  {
    title: '反作弊系统',
    desc: '物品限制、弹幕拦截、UUID 检测、重复 IP 排查、自动扫描'
  },
  {
    title: 'QQ 绑定',
    desc: '玩家绑定 QQ 号，支持注册 / 改密 / 身份关联，搭配机器人使用'
  },
  {
    title: '在线统计',
    desc: '每小时在线数据记录，玩家活跃排行，在线日历一目了然'
  },
  {
    title: '文件管理',
    desc: '服务端文件浏览与编辑，资源文件打包下载'
  },
  {
    title: '进服策略',
    desc: '三种注册模式（默认 / 自动注册 / 白名单），SSC 开荒配置'
  }
]

const startInit = () => {
  const token = route.query.token || ''
  router.push(token ? `/setup?token=${encodeURIComponent(token)}` : '/setup')
}
</script>

<template>
  <div class="intro-page">
    <div class="intro-card">
      <!-- 顶部 -->
      <div class="intro-hero">
        <div class="hero-icon">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
            <line x1="3" y1="9" x2="21" y2="9"></line>
            <line x1="9" y1="21" x2="9" y2="9"></line>
          </svg>
        </div>
        <h1>TSWeb</h1>
        <p class="hero-sub">TShock 服务端 Web 管理面板</p>
        <p class="hero-desc">
          基于 Web 的 TShock 服务器管理工具，提供玩家管理、反作弊、在线统计、
          QQ 绑定、Boss 限制等全方位功能，让服务器管理更便捷。
        </p>
      </div>

      <!-- 功能列表 -->
      <div class="feature-grid">
        <div v-for="(f, i) in features" :key="i" class="feature-card">
          <div class="feature-num">{{ String(i + 1).padStart(2, '0') }}</div>
          <div class="feature-body">
            <span class="feature-title">{{ f.title }}</span>
            <span class="feature-desc">{{ f.desc }}</span>
          </div>
        </div>
      </div>

      <!-- 底部操作 -->
      <div class="intro-action">
        <p class="action-hint">准备好开始配置了吗？</p>
        <button class="start-btn" @click="startInit">
          <span>开始初始化</span>
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <line x1="5" y1="12" x2="19" y2="12"></line>
            <polyline points="12 5 19 12 12 19"></polyline>
          </svg>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.intro-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
  background: linear-gradient(135deg, #e0e7ff, #c7d2fe, #a5b4fc, #c7d2fe, #e0e7ff);
  background-size: 400% 400%;
  animation: bgFlow 8s ease infinite;
}

@keyframes bgFlow {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

.intro-card {
  width: 100%;
  max-width: 820px;
  background: rgba(255, 255, 255, 0.85);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border-radius: 24px;
  padding: 48px 40px;
  box-shadow: 0 8px 40px rgba(99, 102, 241, 0.12);
  border: 1px solid rgba(255, 255, 255, 0.6);
}

/* 顶部英雄区 */
.intro-hero {
  text-align: center;
  margin-bottom: 36px;
}

.hero-icon {
  margin-bottom: 12px;
  color: #6366f1;
  display: flex;
  justify-content: center;
}

.intro-hero h1 {
  margin: 0 0 4px;
  font-size: 2rem;
  font-weight: 800;
  background: linear-gradient(135deg, #4f46e5, #7c3aed);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.hero-sub {
  margin: 0 0 12px;
  font-size: 1rem;
  color: #6b7280;
  font-weight: 500;
}

.hero-desc {
  margin: 0 auto;
  max-width: 560px;
  font-size: 0.9rem;
  color: #4b5563;
  line-height: 1.7;
}

/* 功能网格 */
.feature-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 12px;
  margin-bottom: 36px;
}

.feature-card {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 16px;
  background: rgba(255, 255, 255, 0.7);
  border: 1px solid rgba(0, 0, 0, 0.06);
  border-radius: 14px;
  transition: all 0.25s ease;
}

.feature-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.08);
  transform: translateY(-2px);
}

.feature-num {
  font-size: 0.8rem;
  font-weight: 800;
  color: #a5b4fc;
  flex-shrink: 0;
  margin-top: 1px;
  font-family: monospace;
}

.feature-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.feature-title {
  font-weight: 700;
  font-size: 0.9rem;
  color: #0f0a3a;
}

.feature-desc {
  font-size: 0.8rem;
  color: #4b5563;
  line-height: 1.4;
}

/* 底部操作 */
.intro-action {
  text-align: center;
}

.action-hint {
  margin: 0 0 16px;
  font-size: 0.95rem;
  color: #6b7280;
}

.start-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 14px 40px;
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
  border: none;
  border-radius: 12px;
  font-size: 1.05rem;
  font-weight: 700;
  cursor: pointer;
  transition: all 0.3s ease;
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.25);
}

.start-btn:hover {
  transform: translateY(-3px);
  box-shadow: 0 8px 28px rgba(99, 102, 241, 0.4);
}

.start-btn:active {
  transform: translateY(0);
  box-shadow: 0 2px 8px rgba(99, 102, 241, 0.2);
}
</style>

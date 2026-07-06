<script setup>
import { computed } from 'vue'

const services = [
  {
    title: '小型服务器+QQ 机器人代部署',
    price: '100 起',
    items: [
      '完整部署当前版本的TSWeb，跑通QQ机器人',
      '现有功能技术支持，使用说明'
    ]
  },
  {
    title: '机器人进阶开发',
    price: '100 起',
    items: [
      '自定义指令开发（查装、查进度、绑定的拓展）',
      '多服务器管理、跨群广播',
      '定时任务、自动封禁通知',
      '专属机器人定制'
    ]
  },
  {
    title: '中大型服务器完整软件服务',
    price: '2000 起',
    items: [
      '优选推荐服务商',
      '域名注册推荐，备案指导',
      '私域特色网站搭建设计，UI风格化',
      '站群搭建（多世界/分流等），稳定性维护'
    ]
  },
  {
    title: '灵活项目开发',
    price: '100 / 小时',
    items: [
      'TShock 插件定制',
      'Web 前后端功能开发，私有功能定制',
      '按需报价，灵活沟通'
    ]
  }
]

const highlightKeywords = (text) => {
  const keywords = ['部署', '定制', '开发', '设计', '指导', '保障', '对接', '建议', '监控', '广播', '通知', '定制']
  let result = text
  for (const kw of keywords) {
    result = result.replace(new RegExp(`(${kw})`, 'g'), '<span class="glow-text">$1</span>')
  }
  return result
}
const openSourceUrl = 'https://github.com/lmx12330/TsWeb'
const authorName = 'lmx12330'
</script>

<template>
  <div class="about-page">
    <div class="about-card">
      <h1 class="about-title">关于 TSWeb</h1>
      <p class="about-desc">TShock Web 管理面板 — 开源、可定制、持续维护</p>

      <div class="about-author">
        <div class="author-avatar">{{ authorName.charAt(0).toUpperCase() }}</div>
        <div class="author-meta">
          <span class="author-name">{{ authorName }}</span>
          <span class="author-label">开发者 / 维护者</span>
        </div>
      </div>

      <div class="about-section">
        <h2 class="section-title">技术栈</h2>
        <div class="tech-tags">
          <span class="tech-tag">Vue.js</span>
          <span class="tech-tag">Node.js</span>
          <span class="tech-tag">C#</span>
          <span class="tech-tag">TShock</span>
          <span class="tech-tag">SQLite</span>
        </div>
      </div>

      <div class="about-section">
        <h2 class="section-title">源码</h2>
        <a :href="openSourceUrl" target="_blank" class="source-link">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M9 19c-5 1.5-5-2.5-7-3m14 6v-3.87a3.37 3.37 0 0 0-.94-2.61c3.14-.35 6.44-1.54 6.44-7A5.44 5.44 0 0 0 20 4.77 5.07 5.07 0 0 0 19.91 1S18.73.65 16 2.48a13.38 13.38 0 0 0-7 0C6.27.65 5.09 1 5.09 1A5.07 5.07 0 0 0 5 4.77a5.44 5.44 0 0 0-1.5 3.78c0 5.42 3.3 6.61 6.44 7A3.37 3.37 0 0 0 9 18.13V22"></path>
          </svg>
          <span>{{ openSourceUrl }}</span>
        </a>
      </div>

      <div class="about-divider"></div>

      <div class="about-section">
        <h2 class="section-title section-title-lg">
          有偿定制
          <span class="glow-badge glow-badge-lg">接单中</span>
        </h2>
        <p class="section-desc section-desc-lg">作者提供以下付费服务，详情请通过 
          <a class="qq-link" href="https://qm.qq.com/cgi-bin/qm/qr?k=3375213435" target="_blank">QQ-3375213435</a>
          联系
        </p>

        <div class="services-grid">
          <div v-for="(svc, i) in services" :key="i" class="service-card">
            <div class="service-header">
              <span class="service-title">{{ svc.title }}</span>
              <span class="service-price">{{ svc.price }}</span>
            </div>
            <ul class="service-items">
              <li v-for="(item, j) in svc.items" :key="j" class="service-item">
                <span class="item-bullet"></span>
                <span v-html="highlightKeywords(item)"></span>
              </li>
            </ul>
          </div>
        </div>
      </div>

      <p class="about-footer">开源协议 MIT &mdash; 感谢使用 TSWeb</p>
    </div>
  </div>
</template>

<style scoped>
.about-page {
  display: flex;
  justify-content: center;
  padding: 8px;
}

.about-card {
  width: 100%;
  max-width: 760px;
}

.about-title {
  margin: 0 0 4px;
  font-size: 1.4rem;
  font-weight: 800;
  color: var(--text-primary);
}

.about-desc {
  margin: 0 0 24px;
  color: var(--text-muted);
  font-size: 0.85rem;
}

.about-author {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px;
  background: var(--bg-tertiary);
  border-radius: 12px;
  border: 1px solid var(--border-color);
  margin-bottom: 24px;
}

.author-avatar {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  background: linear-gradient(135deg, #6366f1, #7c3aed);
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1rem;
  font-weight: 800;
  flex-shrink: 0;
}

.author-meta {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.author-name {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--text-primary);
}

.author-label {
  font-size: 0.78rem;
  color: var(--text-muted);
}

.about-section {
  margin-bottom: 24px;
}

.section-title {
  margin: 0 0 8px;
  font-size: 1rem;
  font-weight: 700;
  color: var(--text-primary);
  display: flex;
  align-items: center;
  gap: 8px;
}

.section-title-lg {
  font-size: 1.3rem;
}

.glow-badge-lg {
  font-size: 0.85rem !important;
  padding: 3px 14px !important;
}

.section-desc {
  margin: 0 0 16px;
  color: var(--text-muted);
  font-size: 0.85rem;
}

.section-desc-lg {
  font-size: 1rem;
}

.qq-link {
  font-weight: 800;
  color: #22c55e;
  background: linear-gradient(90deg, #22c55e, #4ade80, #22c55e);
  background-size: 200% 100%;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  animation: shimmer 2.5s ease-in-out infinite;
  text-decoration: none;
  cursor: pointer;
}

.qq-link:hover {
  text-decoration: underline;
}

.glow-badge {
  display: inline-block;
  padding: 2px 10px;
  border-radius: 10px;
  font-size: 0.7rem;
  font-weight: 700;
  color: #22c55e;
  background: linear-gradient(90deg, #22c55e, #4ade80, #22c55e);
  background-size: 200% 100%;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  animation: shimmer 2.5s ease-in-out infinite;
  border: 1px solid rgba(34, 197, 94, 0.3);
}

@keyframes shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

.tech-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.tech-tag {
  padding: 4px 14px;
  border-radius: 8px;
  background: rgba(99, 102, 241, 0.1);
  border: 1px solid rgba(99, 102, 241, 0.2);
  color: #6366f1;
  font-size: 0.8rem;
  font-weight: 600;
}

.source-link {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  border-radius: 10px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  color: var(--accent-primary);
  font-size: 0.85rem;
  font-weight: 600;
  text-decoration: none;
  transition: all 0.2s ease;
}

.source-link:hover {
  border-color: var(--accent-primary);
  background: var(--bg-hover);
}

.about-divider {
  height: 1px;
  background: var(--border-color);
  margin-bottom: 24px;
}

/* 服务卡片 */
.services-grid {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.service-card {
  padding: 22px 24px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  transition: all 0.25s ease;
}

.service-card:hover {
  border-color: var(--accent-primary);
}

.service-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 14px;
}

.service-title {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--text-primary);
}

.service-price {
  padding: 4px 14px;
  border-radius: 8px;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.15), rgba(139, 92, 246, 0.1));
  border: 1px solid rgba(99, 102, 241, 0.25);
  color: #818cf8;
  font-size: 0.95rem;
  font-weight: 800;
}

.service-items {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.service-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.92rem;
  color: var(--text-secondary);
  line-height: 1.6;
}

.item-bullet {
  width: 4px;
  height: 4px;
  border-radius: 50%;
  background: var(--accent-primary);
  flex-shrink: 0;
}

/* 流光关键词 */
:deep(.glow-text) {
  font-weight: 700;
  color: #818cf8;
  background: linear-gradient(90deg, #818cf8, #a5b4fc, #818cf8);
  background-size: 200% 100%;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  animation: shimmer 2.5s ease-in-out infinite;
}

.about-footer {
  text-align: center;
  margin-top: 32px;
  color: var(--text-muted);
  font-size: 0.8rem;
}
</style>

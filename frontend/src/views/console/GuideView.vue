<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { marked } from 'marked'

import rulesMd from '../../guides/server-rules.md?raw'
import pluginsMd from '../../guides/plugins-intro.md?raw'

const router = useRouter()
const route = useRoute()

const tabs = [
  { id: 'rules', label: '服务器规则', icon: '📜' },
  { id: 'plugins', label: '插件介绍', icon: '🔌' },
]

const sources = { rules: rulesMd, plugins: pluginsMd }

const activeTab = ref('rules')
const renderedHtml = ref('')

// 解析标题生成锚点
const headings = ref([])

const renderMarkdown = (text) => {
  const tokens = marked.lexer(text)
  const h2s = tokens.filter(t => t.type === 'heading' && t.depth === 2)
  headings.value = h2s.map((t, i) => ({
    text: t.text,
    id: `heading-${i}`
  }))

  // 给 h2 添加 id
  let idx = 0
  const processed = marked.lexer(text).map(t => {
    if (t.type === 'heading' && t.depth === 2) {
      const raw = marked.parser([t])
      return raw.replace('<h2', `<h2 id="heading-${idx++}"`)
    }
    return marked.parser([t])
  }).join('')

  return processed
}

const switchTab = (id) => {
  activeTab.value = id
  headings.value = []
  renderedHtml.value = ''
  // 使用 nextTick 等待 DOM 更新后渲染
  setTimeout(() => {
    renderedHtml.value = renderMarkdown(sources[id])
  }, 0)
}

const scrollToAnchor = (id) => {
  const el = document.getElementById(id)
  if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

// 检测 URL hash 切换 Tab
const checkHash = () => {
  const hash = window.location.hash.replace('#', '')
  const tabIds = tabs.map(t => t.id)
  if (tabIds.includes(hash)) {
    switchTab(hash)
  }
}

onMounted(() => {
  checkHash()
  switchTab(activeTab.value)
  window.addEventListener('hashchange', checkHash)
})
</script>

<template>
  <div class="guide-page">
    <!-- 顶部标题 -->
    <div class="guide-header">
      <h1 class="guide-title">📖 服务器说明</h1>
      <p class="guide-desc">了解服务器规则、当前特色与插件功能</p>
    </div>

    <!-- 分页 Tab -->
    <div class="tab-bar">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        class="tab-btn"
        :class="{ active: activeTab === tab.id }"
        @click="switchTab(tab.id)"
      >
        <span class="tab-icon">{{ tab.icon }}</span>
        <span class="tab-label">{{ tab.label }}</span>
      </button>
    </div>

    <!-- 内容区：侧边锚点 + 正文 -->
    <div class="content-layout">
      <!-- 锚点导航 -->
      <aside v-if="headings.length > 0" class="toc">
        <div class="toc-title">目录</div>
        <a
          v-for="h in headings"
          :key="h.id"
          class="toc-link"
          @click.prevent="scrollToAnchor(h.id)"
        >{{ h.text }}</a>
      </aside>

      <!-- Markdown 正文 -->
      <main class="markdown-body" v-html="renderedHtml"></main>
    </div>
  </div>
</template>

<style scoped>
.guide-page {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  max-width: 960px;
  margin: 0 auto;
  width: 100%;
  box-sizing: border-box;
  padding: 28px 32px 48px;
}

/* ── 头部 ── */
.guide-header { margin-bottom: 24px; }
.guide-title {
  margin: 0 0 6px;
  font-size: 1.6rem;
  font-weight: 800;
  color: var(--text-primary);
}
.guide-desc {
  margin: 0;
  font-size: 0.9rem;
  color: var(--text-secondary);
}

/* ── Tab 栏 ── */
.tab-bar {
  display: flex;
  gap: 6px;
  margin-bottom: 28px;
  padding: 4px;
  background: var(--bg-tertiary);
  border-radius: 12px;
  border: 1px solid var(--border-light);
}
.tab-btn {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  padding: 10px 16px;
  border: none;
  border-radius: 9px;
  background: transparent;
  color: var(--text-secondary);
  font-size: 0.88rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
}
.tab-btn:hover { color: var(--text-primary); background: rgba(34, 197, 94, 0.06); }
.tab-btn.active {
  background: var(--bg-card);
  color: #22c55e;
  box-shadow: 0 2px 8px rgba(0,0,0,0.06);
  border: 1px solid var(--border-light);
}
.tab-icon { font-size: 1.1rem; }

/* ── 双栏布局 ── */
.content-layout {
  display: flex;
  gap: 32px;
  align-items: flex-start;
}

/* ── 锚点侧栏 ── */
.toc {
  flex-shrink: 0;
  width: 170px;
  position: sticky;
  top: 20px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.toc-title {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--border-light);
}
.toc-link {
  font-size: 0.82rem;
  color: var(--text-secondary);
  text-decoration: none;
  padding: 4px 8px;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
  line-height: 1.4;
}
.toc-link:hover {
  color: #22c55e;
  background: rgba(34, 197, 94, 0.06);
}

/* ── Markdown 正文 ── */
.markdown-body {
  flex: 1;
  min-width: 0;
  color: var(--text-primary);
  line-height: 1.75;
  font-size: 0.95rem;
  font-family: -apple-system, 'PingFang SC', 'Microsoft YaHei', 'Noto Sans SC', system-ui, sans-serif;
}

/* deep 样式用于 v-html 内的元素 */
.markdown-body :deep(h2) {
  font-size: 1.25rem;
  font-weight: 700;
  margin: 32px 0 14px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--border-light);
  color: var(--text-primary);
  scroll-margin-top: 80px;
}

.markdown-body :deep(h3) {
  font-size: 1.05rem;
  font-weight: 700;
  margin: 24px 0 10px;
  color: var(--text-primary);
}

.markdown-body :deep(p) {
  margin: 0 0 12px;
  color: var(--text-secondary);
}

.markdown-body :deep(ul),
.markdown-body :deep(ol) {
  margin: 0 0 14px;
  padding-left: 22px;
}
.markdown-body :deep(li) {
  margin-bottom: 6px;
  color: var(--text-secondary);
}

.markdown-body :deep(strong) {
  color: #22c55e;
  font-weight: 700;
}

.markdown-body :deep(em) {
  font-style: italic;
  color: var(--text-primary);
}

.markdown-body :deep(blockquote) {
  margin: 16px 0;
  padding: 12px 18px;
  background: rgba(34, 197, 94, 0.06);
  border-left: 3px solid #22c55e;
  border-radius: 0 8px 8px 0;
  color: var(--text-secondary);
  font-size: 0.9rem;
}

.markdown-body :deep(table) {
  width: 100%;
  border-collapse: collapse;
  margin: 16px 0;
  font-size: 0.88rem;
}

.markdown-body :deep(th) {
  text-align: left;
  padding: 10px 14px;
  font-weight: 700;
  color: var(--text-primary);
  background: var(--bg-tertiary);
  border-bottom: 2px solid var(--border-light);
}

.markdown-body :deep(td) {
  padding: 9px 14px;
  border-bottom: 1px solid var(--border-light);
  color: var(--text-secondary);
}

.markdown-body :deep(tr:last-child td) {
  border-bottom: none;
}

.markdown-body :deep(code) {
  background: rgba(34, 197, 94, 0.1);
  padding: 2px 8px;
  border-radius: 5px;
  font-size: 0.85em;
  font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', Consolas, monospace;
  color: #16a34a;
}

.markdown-body :deep(pre) {
  background: var(--bg-tertiary);
  padding: 16px 20px;
  border-radius: 10px;
  overflow-x: auto;
  margin: 16px 0;
  border: 1px solid var(--border-light);
}

.markdown-body :deep(pre code) {
  background: none;
  padding: 0;
  color: var(--text-primary);
  font-size: 0.85rem;
}

.markdown-body :deep(hr) {
  border: none;
  height: 1px;
  background: var(--border-light);
  margin: 24px 0;
}

/* ── 移动端 ── */
@media (max-width: 767px) {
  .guide-page { padding: 16px; }
  .guide-title { font-size: 1.25rem; }
  .tab-bar { flex-direction: column; gap: 2px; }
  .tab-btn { padding: 10px 12px; }
  .content-layout { flex-direction: column; }
  .toc {
    position: static;
    width: 100%;
    flex-direction: row;
    flex-wrap: wrap;
    gap: 4px;
    margin-bottom: 16px;
    padding-bottom: 12px;
    border-bottom: 1px solid var(--border-light);
  }
  .toc-title { display: none; }
  .toc-link {
    font-size: 0.8rem;
    padding: 4px 10px;
    background: var(--bg-tertiary);
    border-radius: 6px;
  }
}
</style>

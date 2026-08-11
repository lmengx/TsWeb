<script setup>
import { useRoute } from 'vue-router'

const route = useRoute()

const tabs = [
  { path: '/console/settings/register', name: '注册与登录' },
  { path: '/console/settings/boss', name: 'Boss 限制' },
  { path: '/console/settings/promotion', name: '权限提升' },
  { path: '/console/settings/backup', name: '自动备份' }
]
</script>

<template>
  <div class="settings-page">
    <div class="page-header">
      <h2>服务器设置</h2>
    </div>

    <div class="settings-tabs">
      <router-link
        v-for="tab in tabs"
        :key="tab.path"
        :to="tab.path"
        class="tab"
        :class="{ active: route.path.startsWith(tab.path) }"
      >
        {{ tab.name }}
      </router-link>
    </div>

    <router-view />
  </div>
</template>

<style scoped>
.settings-page {
  padding: 20px;
  width: 100%;
}

.page-header {
  margin-bottom: 20px;
}

.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
}

.settings-tabs {
  display: flex;
  gap: 4px;
  margin-bottom: 20px;
  border-bottom: 1px solid var(--border-light);
}

.tab {
  padding: 10px 18px;
  color: var(--text-secondary);
  text-decoration: none;
  font-size: 0.95rem;
  font-weight: 500;
  border-radius: var(--radius-md) var(--radius-md) 0 0;
  border: 1px solid transparent;
  border-bottom: none;
  transition: all 0.2s ease;
  position: relative;
  top: 1px;
}

.tab:hover {
  color: var(--text-primary);
  background: var(--bg-hover);
}

.tab.active {
  color: var(--accent-primary);
  background: var(--bg-card);
  border-color: var(--border-light);
  font-weight: 600;
}
</style>

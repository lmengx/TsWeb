<script setup>
</script>

<template>
  <div class="server-settings-content">
    <div class="section-header">
      <h2>服务器设置</h2>
    </div>
    <div class="empty-state">
      <div class="empty-icon">⚙️</div>
      <h3>功能开发中</h3>
      <p>该功能正在开发中，敬请期待</p>
    </div>
  </div>
</template>

<style scoped>
.server-settings-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 0;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
  padding: 0 20px;
}

.section-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.4rem;
  font-weight: 600;
}

.empty-state {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 40px;
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
  margin: 0 20px 20px;
}

.empty-icon {
  font-size: 4rem;
  margin-bottom: 16px;
}

.empty-state h3 {
  font-size: 1.5rem;
  color: var(--text-primary);
  margin-bottom: 8px;
}

.empty-state p {
  color: var(--text-muted);
  font-size: 1rem;
}
</style>

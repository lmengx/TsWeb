<script setup>
import SystemSettingsPanel from '../../components/SystemSettingsPanel.vue'
</script>

<template>
  <div class="backend-settings-content">
    <div class="section-header">
      <h2>后端设置</h2>
    </div>
    <SystemSettingsPanel />
  </div>
</template>

<style scoped>
.backend-settings-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: auto;
  padding: 0 20px 20px;
}
.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  padding-top: 16px;
}
.section-header h2 { margin: 0; color: var(--text-primary); font-size: 1.4rem; }
</style>

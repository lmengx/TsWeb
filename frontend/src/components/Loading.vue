<script setup>
// 统一加载组件：渐变双色圆环 + 可选文字
// props:
//   text    - 显示文字（默认"加载中…"，传空字符串则不显示文字）
//   size    - sm | md（默认）| lg
//   overlay - 是否铺满父容器（玻璃蒙层，父容器需 position: relative）
defineProps({
  text: { type: String, default: '加载中…' },
  size: { type: String, default: 'md' },
  overlay: { type: Boolean, default: false }
})
</script>

<template>
  <div class="ts-loading" :class="[`size-${size}`, { overlay }]">
    <div class="ts-loading-spinner"></div>
    <span v-if="text" class="ts-loading-text">{{ text }}</span>
  </div>
</template>

<style scoped>
.ts-loading {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 40px;
  color: var(--text-muted);
}

.ts-loading.size-sm {
  padding: 16px;
  gap: 8px;
}
.ts-loading.size-sm .ts-loading-spinner {
  width: 20px;
  height: 20px;
  border-width: 2px;
}

.ts-loading.size-lg {
  padding: 56px;
}
.ts-loading.size-lg .ts-loading-spinner {
  width: 48px;
  height: 48px;
  border-width: 4px;
}

.ts-loading.overlay {
  position: absolute;
  inset: 0;
  padding: 0;
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border-radius: inherit;
  z-index: 50;
}

.ts-loading-spinner {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  border: 3px solid rgba(99, 102, 241, 0.15);
  border-top-color: var(--accent-primary);
  border-right-color: var(--accent-cyan);
  animation: ts-spin 0.9s linear infinite;
  box-shadow: 0 0 12px rgba(99, 102, 241, 0.12);
}

.ts-loading-text {
  font-size: 0.85rem;
  letter-spacing: 0.3px;
}

@keyframes ts-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>

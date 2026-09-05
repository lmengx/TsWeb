<script setup>
import { useProgress } from '../composables/useProgress.js'

const { visible, progress } = useProgress()
</script>

<template>
  <transition name="rp-fade">
    <div v-if="visible" class="route-progress">
      <div class="route-progress-bar" :style="{ width: progress + '%' }"></div>
    </div>
  </transition>
</template>

<style scoped>
.route-progress {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  height: 3px;
  z-index: 9999;
  pointer-events: none;
}

.route-progress-bar {
  height: 100%;
  max-width: 100%;
  background: var(--gradient-primary);
  background-size: 200% 100%;
  box-shadow: 0 0 12px rgba(99, 102, 241, 0.6);
  border-radius: 0 3px 3px 0;
  transition: width 0.3s var(--ease-out);
  animation: rp-shimmer 1.4s linear infinite;
}

@keyframes rp-shimmer {
  from {
    background-position: 200% 0;
  }
  to {
    background-position: -200% 0;
  }
}

.rp-fade-enter-active,
.rp-fade-leave-active {
  transition: opacity 0.3s ease;
}
.rp-fade-enter-from,
.rp-fade-leave-to {
  opacity: 0;
}
</style>

<script setup>
import { ref, watch, onUnmounted } from 'vue'

const props = defineProps({
  show: Boolean,
  text: { type: String, default: '保存成功' },
  duration: { type: Number, default: 750 }
})

const emit = defineEmits(['close'])

const visible = ref(false)
let timer = null

watch(() => props.show, (v) => {
  if (v) {
    visible.value = true
    clearTimeout(timer)
    timer = setTimeout(() => {
      visible.value = false
      emit('close')
    }, props.duration)
  }
})

onUnmounted(() => clearTimeout(timer))
</script>

<template>
  <Teleport to="body">
    <!-- 无遮罩浮动提示：不拦截底层交互，居中浮现成功图样 -->
    <Transition name="success-pop">
      <div v-if="visible" class="success-float">
        <div class="success-badge">
          <svg width="38" height="38" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
            <polyline points="20 6 9 17 4 12"></polyline>
          </svg>
        </div>
        <span class="success-text">{{ text }}</span>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.success-float {
  position: fixed;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  z-index: 10000;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
  padding: 30px 44px;
  background: var(--bg-card);
  border-radius: 18px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.35), 0 0 0 1px var(--border-light);
  pointer-events: none;
}

.success-badge {
  width: 64px;
  height: 64px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  background: linear-gradient(135deg, #22c55e, #16a34a);
  box-shadow: 0 8px 30px rgba(34, 197, 94, 0.5), inset 0 0 0 1px rgba(255, 255, 255, 0.2);
  animation: badge-pop 0.4s cubic-bezier(0.34, 1.56, 0.64, 1) both;
}

@keyframes badge-pop {
  0% { transform: scale(0) rotate(-40deg); opacity: 0; }
  60% { transform: scale(1.15) rotate(6deg); opacity: 1; }
  80% { transform: scale(0.95) rotate(-2deg); }
  100% { transform: scale(1) rotate(0); }
}

.success-text {
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
  letter-spacing: 0.5px;
  animation: text-in 0.3s ease-out 0.08s both;
}

@keyframes text-in {
  from { opacity: 0; transform: translateY(8px); }
  to { opacity: 1; transform: translateY(0); }
}

.success-pop-enter-active {
  animation: modal-in 0.25s cubic-bezier(0.34, 1.56, 0.64, 1) both;
}
.success-pop-leave-active {
  animation: modal-out 0.25s ease-in both;
}

@keyframes modal-in {
  from { opacity: 0; transform: translate(-50%, -50%) scale(0.7); }
  to { opacity: 1; transform: translate(-50%, -50%) scale(1); }
}
@keyframes modal-out {
  from { opacity: 1; transform: translate(-50%, -50%) scale(1); }
  to { opacity: 0; transform: translate(-50%, -50%) scale(0.85); }
}
</style>

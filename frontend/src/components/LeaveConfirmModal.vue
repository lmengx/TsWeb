<script setup>
import { watch } from 'vue'

const props = defineProps({
  show: Boolean,
  title: { type: String, default: '未保存的更改' },
  message: { type: String, default: '当前修改尚未保存，离开后更改将丢失。' },
  confirmText: { type: String, default: '离开' }
})

const emit = defineEmits(['cancel', 'leave'])

// ESC 关闭（视为取消）
watch(() => props.show, (v) => {
  if (!v) return
  const onKey = (e) => {
    if (e.key === 'Escape') emit('cancel')
  }
  window.addEventListener('keydown', onKey)
  const remove = () => window.removeEventListener('keydown', onKey)
  // 下次隐藏时移除
  const unwatch = watch(() => props.show, (nv) => {
    if (!nv) { remove(); unwatch() }
  })
})
</script>

<template>
  <Teleport to="body">
    <Transition name="confirm-fade">
      <div v-if="show" class="confirm-overlay" @click.self="emit('cancel')">
        <div class="confirm-modal">
          <div class="confirm-icon">
            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"></path>
              <line x1="12" y1="9" x2="12" y2="13"></line>
              <line x1="12" y1="17" x2="12.01" y2="17"></line>
            </svg>
          </div>
          <h3 class="confirm-title">{{ title }}</h3>
          <p class="confirm-message">{{ message }}</p>
          <div class="confirm-actions">
            <button class="confirm-cancel" @click="emit('cancel')">取消</button>
            <button class="confirm-leave" @click="emit('leave')">{{ confirmText }}</button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.confirm-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.55);
  backdrop-filter: blur(6px);
  -webkit-backdrop-filter: blur(6px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10001;
}

.confirm-fade-enter-active { animation: fade-in 0.2s ease-out both; }
.confirm-fade-leave-active { animation: fade-out 0.18s ease-in both; }
@keyframes fade-in { from { opacity: 0; } to { opacity: 1; } }
@keyframes fade-out { from { opacity: 1; } to { opacity: 0; } }

.confirm-modal {
  width: 90%;
  max-width: 400px;
  background: #11182a; /* 自身背景不透明（暗色主题） */
  border-radius: 18px;
  padding: 28px 26px 22px;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 8px;
  box-shadow: 0 24px 70px rgba(0, 0, 0, 0.4);
  animation: modal-in 0.28s cubic-bezier(0.34, 1.56, 0.64, 1) both;
}

.light .confirm-modal {
  background: #ffffff; /* 亮色主题不透明 */
}

@keyframes modal-in {
  from { opacity: 0; transform: scale(0.8) translateY(20px); }
  to { opacity: 1; transform: scale(1) translateY(0); }
}

.confirm-icon {
  width: 58px;
  height: 58px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #f97316;
  background: rgba(249, 115, 22, 0.12);
  border: 1px solid rgba(249, 115, 22, 0.25);
  margin-bottom: 6px;
}

.confirm-title {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
  color: var(--text-primary);
}

.confirm-message {
  margin: 0 0 18px;
  font-size: 0.85rem;
  color: var(--text-secondary);
  line-height: 1.6;
}

.confirm-actions {
  display: flex;
  gap: 12px;
  width: 100%;
}

.confirm-cancel,
.confirm-leave {
  flex: 1;
  padding: 11px 0;
  border-radius: 10px;
  font-size: 0.88rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.confirm-cancel {
  background: transparent;
  border: 1px solid var(--border-color);
  color: var(--text-primary);
}

.confirm-cancel:hover {
  border-color: var(--text-muted);
  background: rgba(255, 255, 255, 0.04);
}

.confirm-leave {
  background: linear-gradient(135deg, #ef4444, #dc2626);
  border: none;
  color: #fff;
}

.confirm-leave:hover {
  transform: translateY(-1px);
  box-shadow: 0 6px 18px rgba(239, 68, 68, 0.4);
}
</style>

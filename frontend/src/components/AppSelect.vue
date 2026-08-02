<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'

// 通用自定义下拉框：完全可控样式的弹出面板
const props = defineProps({
  modelValue: { type: [String, Number], default: '' },
  // [{ value, label }] 或 [string]
  options: { type: Array, default: () => [] },
  placeholder: { type: String, default: '请选择...' }
})

const emit = defineEmits(['update:modelValue'])

const open = ref(false)
const triggerRef = ref(null)
const panelRef = ref(null)
const panelStyle = ref({})

const normalized = computed(() =>
  props.options.map(o => (typeof o === 'string' ? { value: o, label: o } : o))
)

const selectedLabel = computed(() => {
  const hit = normalized.value.find(o => o.value === props.modelValue)
  return hit ? hit.label : ''
})

const toggle = async () => {
  if (open.value) { close(); return }
  open.value = true
  await nextTick()
  updatePosition()
}

const updatePosition = () => {
  const el = triggerRef.value
  if (!el) return
  const rect = el.getBoundingClientRect()
  panelStyle.value = {
    top: rect.bottom + 6 + 'px',
    left: rect.left + 'px',
    width: Math.max(rect.width, 160) + 'px'
  }
}

const close = () => { open.value = false }

const select = (val) => {
  emit('update:modelValue', val)
  close()
}

// 点击外部 / 滚动 / 窗口变化时关闭
// 但面板自身内部发生的点击与滚动不算“外部”，不关闭（面板可能需滚动选择选项）
const isInside = (target) => {
  if (!target || !(target instanceof Node)) return false
  if (triggerRef.value?.contains(target)) return true
  if (panelRef.value?.contains(target)) return true
  return false
}

const onDocClick = (e) => { if (!isInside(e.target)) close() }
const onDocScroll = (e) => {
  if (!open.value) return
  // 滚动发生在面板内部（如时/分选项滚动）→ 不关闭；页面其他区域滚动 → 关闭
  if (isInside(e.target)) return
  close()
}

onMounted(() => {
  document.addEventListener('click', onDocClick)
  document.addEventListener('scroll', onDocScroll, true)
  window.addEventListener('resize', onDocScroll)
})
onUnmounted(() => {
  document.removeEventListener('click', onDocClick)
  document.removeEventListener('scroll', onDocScroll, true)
  window.removeEventListener('resize', onDocScroll)
})
</script>

<template>
  <div class="app-select" ref="triggerRef" @click.stop>
    <button type="button" class="app-select-trigger" :class="{ open }" @click="toggle">
      <span class="app-select-value" :class="{ placeholder: !selectedLabel }">
        {{ selectedLabel || placeholder }}
      </span>
      <svg class="app-select-arrow" :class="{ open }" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="6 9 12 15 18 9" />
      </svg>
    </button>

    <Teleport to="body">
      <transition name="dropdown">
        <div v-if="open" ref="panelRef" class="app-select-panel" :style="panelStyle" @click.stop>
          <button
            v-for="opt in normalized"
            :key="opt.value"
            type="button"
            class="app-select-option"
            :class="{ selected: opt.value === modelValue }"
            @click="select(opt.value)"
          >{{ opt.label }}</button>
        </div>
      </transition>
    </Teleport>
  </div>
</template>

<style scoped>
.app-select { position: relative; display: inline-block; }

.app-select-trigger {
  display: flex; align-items: center; justify-content: space-between; gap: 8px;
  padding: 8px 12px;
  border-radius: 10px;
  border: 1px solid var(--border-light);
  background-color: var(--bg-tertiary);
  color: var(--text-primary);
  font-size: 0.9rem;
  font-family: inherit;
  cursor: pointer;
  outline: none;
  transition: border-color 0.2s, box-shadow 0.2s;
  min-width: 130px;
  user-select: none;
}
.app-select-trigger:hover { border-color: rgba(99, 102, 241, 0.4); }
.app-select-trigger.open,
.app-select-trigger:focus { border-color: var(--accent-primary); box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.12); }

.app-select-value { color: var(--text-primary); font-size: 0.9rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.app-select-value.placeholder { color: var(--text-muted); }

.app-select-arrow { color: var(--text-muted); flex-shrink: 0; transition: transform 0.2s; }
.app-select-arrow.open { transform: rotate(180deg); }

/* 弹出面板（Teleport 到 body，fixed 定位） */
.app-select-panel {
  position: fixed;
  z-index: 99999;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 12px;
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.25);
  padding: 4px;
  max-height: 240px;
  overflow-y: auto;
}

.app-select-option {
  display: block; width: 100%; text-align: left;
  padding: 9px 12px;
  border: none; background: transparent;
  border-radius: 8px;
  color: var(--text-secondary);
  font-size: 0.88rem;
  font-family: inherit;
  cursor: pointer;
  transition: all 0.12s;
  user-select: none;
}
.app-select-option:hover { background: var(--bg-hover); color: var(--text-primary); }
.app-select-option.selected { background: rgba(99, 102, 241, 0.12); color: #818cf8; font-weight: 600; }

.dropdown-enter-active, .dropdown-leave-active { transition: opacity 0.15s ease, transform 0.15s ease; }
.dropdown-enter-from, .dropdown-leave-to { opacity: 0; transform: translateY(-4px) scale(0.98); }
</style>

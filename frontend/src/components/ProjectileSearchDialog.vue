<script setup>
import { ref, computed, watch, onUnmounted } from 'vue'
import { loadProjectileData } from '../api/projectileDataApi.js'

const props = defineProps({
  show: Boolean,
  multi: { type: Boolean, default: false },
  // 显示右侧"执行方案"面板（multi 模式下使用）：批量添加时统一设置处理方法
  plan: { type: Boolean, default: false },
  defaultMethod: { type: String, default: 'log' }
})

const emit = defineEmits(['select', 'close'])

// ── 关闭动画状态机：idle → success → shrink → close ──
const closingPhase = ref('idle')
let closeTimer = null
const startSuccessClose = () => {
  if (closingPhase.value !== 'idle') return
  closingPhase.value = 'success'
  clearTimeout(closeTimer)
  closeTimer = setTimeout(() => {
    closingPhase.value = 'shrink'
    closeTimer = setTimeout(() => {
      closingPhase.value = 'idle'
      emit('close')
    }, 260)
  }, 440)
}
const startPlainClose = () => {
  if (closingPhase.value !== 'idle') return
  closingPhase.value = 'shrink'
  clearTimeout(closeTimer)
  closeTimer = setTimeout(() => {
    closingPhase.value = 'idle'
    emit('close')
  }, 240)
}
const requestClose = () => {
  if (closingPhase.value !== 'idle') return
  startPlainClose()
}

// 打开时重置状态
watch(() => props.show, (val) => {
  if (val) {
    closingPhase.value = 'idle'
    clearTimeout(closeTimer)
    searchQuery.value = ''
    imageErrors.value = {}
    selectedIds.value = new Set()
    planMethod.value = props.defaultMethod
    planCommand.value = ''
    if (!dataLoaded.value) {
      initData()
    }
  }
})

const selectedIds = ref(new Set())
const searchQuery = ref('')
const projData = ref({ list: [], dict: {} })
const imageErrors = ref({})
const dataLoaded = ref(false)

// ── 方案面板状态 ──
const planMethod = ref(props.defaultMethod)
const planCommand = ref('')
const planQuickCommands = [
  { label: '/banp "{playername}" "违规使用{projid}"', desc: '封禁玩家' },
  { label: '/kick "{playername}" "违规使用{projid}"', desc: '踢出玩家' },
  { label: '/bc "{playername}违规使用{projid}"', desc: '广播公告' }
]
const isQuickPlan = () => ['ban', 'kick', 'log'].includes(planMethod.value)
// 批量添加时实际写入的 method：命令方案且填了命令 → 命令字符串
const effectivePlanMethod = computed(() => {
  if (planMethod.value === 'command') {
    return planCommand.value.trim() || 'log'
  }
  return planMethod.value
})
watch(() => props.defaultMethod, (v) => { planMethod.value = v })

const numericList = computed(() => projData.value.list.filter(p => typeof p.id === 'number'))

const searchResults = computed(() => {
  if (!searchQuery.value.trim()) {
    return numericList.value.slice(0, 200).sort((a, b) => a.id - b.id)
  }

  const query = searchQuery.value.trim().toLowerCase()
  const keywords = query.split(/\s+/).filter(k => k.length > 0)

  const exactResults = numericList.value.filter(item => {
    const chinese = (item.chinese || '').toLowerCase()
    const english = (item.english || '').toLowerCase()
    const internal = (item.internal || '').toLowerCase()
    const id = String(item.id)
    return keywords.every(keyword => {
      return chinese.includes(keyword) ||
             english.includes(keyword) ||
             internal.includes(keyword) ||
             id.includes(keyword)
    })
  })

  if (exactResults.length > 0) {
    return exactResults.slice(0, 200)
  }

  return numericList.value
    .filter(item => {
      const chinese = (item.chinese || '').toLowerCase()
      const english = (item.english || '').toLowerCase()
      const internal = (item.internal || '').toLowerCase()
      const id = String(item.id)
      return keywords.every(keyword => {
        return fuzzyMatchOneMistake(chinese, keyword) ||
               fuzzyMatchOneMistake(english, keyword) ||
               fuzzyMatchOneMistake(internal, keyword) ||
               fuzzyMatchOneMistake(id, keyword)
      })
    })
    .slice(0, 200)
})

const levenshteinDistance = (a, b) => {
  const matrix = []
  for (let i = 0; i <= b.length; i++) matrix[i] = [i]
  for (let j = 0; j <= a.length; j++) matrix[0][j] = j
  for (let i = 1; i <= b.length; i++) {
    for (let j = 1; j <= a.length; j++) {
      if (b.charAt(i - 1) === a.charAt(j - 1)) {
        matrix[i][j] = matrix[i - 1][j - 1]
      } else {
        matrix[i][j] = Math.min(
          matrix[i - 1][j - 1] + 1,
          matrix[i][j - 1] + 1,
          matrix[i - 1][j] + 1
        )
      }
    }
  }
  return matrix[b.length][a.length]
}

const fuzzyMatchOneMistake = (text, keyword) => {
  if (!text || !keyword) return false
  const t = text.replace(/\s+/g, '')
  const k = keyword.replace(/\s+/g, '')
  if (t.includes(k)) return true
  const lenDiff = Math.abs(t.length - k.length)
  if (lenDiff > 1) return false
  return levenshteinDistance(t, k) <= 1
}

// 图标：本地 assets/img/proj/ 优先，错误后回退 wiki（png → gif 二级）
const getProjImage = (item) => {
  const err = imageErrors.value[item.id] || 0
  if (err >= 2) return ''
  if (err === 1) {
    if (item.english) {
      return `https://terraria.wiki.gg/images/${item.english.replace(/\s+/g, '_')}.gif`
    }
    return ''
  }
  if (err === 0 && item.image) {
    return `/assets/img/proj/${item.image}`
  }
  if (item.english) {
    return `https://terraria.wiki.gg/images/${item.english.replace(/\s+/g, '_')}.png`
  }
  return ''
}

const handleImageError = (item) => {
  imageErrors.value = { ...imageErrors.value, [item.id]: (imageErrors.value[item.id] || 0) + 1 }
}

const handleSelect = (item) => {
  if (props.multi) {
    const next = new Set(selectedIds.value)
    if (next.has(item.id)) next.delete(item.id)
    else next.add(item.id)
    selectedIds.value = next
  } else {
    emit('select', item)
    startSuccessClose()
  }
}

const confirmMulti = () => {
  const items = numericList.value.filter(i => selectedIds.value.has(i.id))
  emit('select', {
    multi: true,
    items,
    method: effectivePlanMethod.value
  })
  selectedIds.value = new Set()
  startSuccessClose()
}
const clearMulti = () => {
  selectedIds.value = new Set()
}

const initData = async () => {
  projData.value = await loadProjectileData()
  dataLoaded.value = true
}

onUnmounted(() => clearTimeout(closeTimer))
</script>

<template>
  <Teleport to="body">
    <div
      v-if="show"
      class="search-dialog-overlay"
      :class="{ 'overlay-leaving': closingPhase === 'shrink' }"
      @click.self="requestClose"
    >
      <div
        class="search-dialog"
        :class="{
          'with-plan': multi && plan,
          'phase-success': closingPhase === 'success',
          'phase-shrink': closingPhase === 'shrink'
        }"
      >
        <div class="search-dialog-header">
          <h3>{{ multi ? '批量添加弹幕' : '选择弹幕' }}</h3>
          <button @click="requestClose" class="close-btn" :disabled="closingPhase !== 'idle'">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>

        <!-- 成功图样覆盖层 -->
        <div v-if="closingPhase === 'success'" class="success-layer">
          <div class="success-badge">
            <svg width="46" height="46" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
              <polyline points="20 6 9 17 4 12"></polyline>
            </svg>
          </div>
          <span class="success-text">已添加</span>
        </div>

        <div class="search-dialog-body">
          <div class="pick-layout" :class="{ 'with-plan': multi && plan }">
            <!-- 左侧：搜索结果 -->
            <div class="pick-left">
              <input
                v-model="searchQuery"
                type="text"
                placeholder="搜索弹幕名称、内部名或ID（支持多词）..."
                class="search-input"
                autofocus
              />
              <div class="search-hint">
                找到 {{ searchResults.length }} 个匹配结果（共 {{ numericList.length }} 条）
              </div>

              <div class="results-grid" :class="{ 'results-grid-condensed': multi && plan }">
                <div
                  v-for="item in searchResults"
                  :key="item.id"
                  class="item-card"
                  :class="{ 'item-card-selected': multi && selectedIds.has(item.id) }"
                  @click="handleSelect(item)"
                >
                  <span v-if="multi" class="item-check" :class="{ checked: selectedIds.has(item.id) }">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                      <polyline points="20 6 9 17 4 12"></polyline>
                    </svg>
                  </span>
                  <div class="item-image-wrapper">
                    <img
                      :src="getProjImage(item)"
                      :alt="item.chinese"
                      class="item-image"
                      @error="handleImageError(item)"
                    />
                  </div>
                  <div class="item-info">
                    <span class="item-name">{{ item.chinese || item.internal || item.id }}</span>
                    <span class="item-id">ID: {{ item.id }}</span>
                    <span class="item-english">{{ item.english || item.internal }}</span>
                  </div>
                </div>
                <div v-if="searchResults.length === 0 && searchQuery.trim()" class="no-results">
                  未找到匹配的弹幕
                </div>
              </div>
            </div>

            <!-- 右侧：执行方案面板 -->
            <div v-if="multi && plan" class="pick-right">
              <div class="plan-panel">
                <h4 class="plan-title">执行方案</h4>

                <div class="plan-methods">
                  <button
                    @click="planMethod = 'log'"
                    class="plan-method-btn plan-log"
                    :class="{ active: planMethod === 'log' }"
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>
                      <polyline points="14 2 14 8 20 8"></polyline>
                      <line x1="16" y1="13" x2="8" y2="13"></line>
                      <line x1="16" y1="17" x2="8" y2="17"></line>
                    </svg>
                    记录
                  </button>
                  <button
                    @click="planMethod = 'kick'"
                    class="plan-method-btn plan-kick"
                    :class="{ active: planMethod === 'kick' }"
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M21 12a9 9 0 00-9-9 9.75 9.75 0 00-6.74 2.74L3 8"></path>
                      <path d="M16 3.13a9 9 0 010 17.74"></path>
                      <path d="M10 17l6-6"></path>
                    </svg>
                    踢出
                  </button>
                  <button
                    @click="planMethod = 'ban'"
                    class="plan-method-btn plan-ban"
                    :class="{ active: planMethod === 'ban' }"
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <circle cx="12" cy="12" r="10"></circle>
                      <line x1="15" y1="9" x2="9" y2="15"></line>
                      <line x1="9" y1="9" x2="15" y2="15"></line>
                    </svg>
                    封禁
                  </button>
                  <button
                    @click="planMethod = 'command'"
                    class="plan-method-btn plan-command"
                    :class="{ active: planMethod === 'command' }"
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polyline points="4 17 10 11 4 5"></polyline>
                      <line x1="12" y1="19" x2="20" y2="19"></line>
                    </svg>
                    命令
                  </button>
                </div>

                <div v-if="planMethod === 'command'" class="plan-command-box">
                  <input
                    v-model="planCommand"
                    type="text"
                    class="plan-command-input"
                    placeholder="支持 {playername}、{projid} 转义"
                  />
                  <div class="plan-quick">
                    <span class="plan-quick-label">快速:</span>
                    <button
                      v-for="cmd in planQuickCommands"
                      :key="cmd.label"
                      @click="planCommand = cmd.label"
                      class="plan-quick-btn"
                      :title="cmd.desc"
                    >
                      {{ cmd.label }}
                    </button>
                  </div>
                </div>

                <div class="plan-summary">
                  <span class="plan-summary-count">已选 {{ selectedIds.size }} 个弹幕</span>
                  <span class="plan-summary-method">
                    方案：{{ planMethod === 'command'
                      ? (planCommand.trim() || '自定义命令')
                      : planMethod === 'ban' ? '封禁' : planMethod === 'kick' ? '踢出' : '记录' }}
                  </span>
                </div>
              </div>
            </div>
          </div>

          <!-- 多选底部操作条 -->
          <div v-if="multi" class="multi-footer">
            <span class="multi-count">已选 {{ selectedIds.size }} 个</span>
            <div class="multi-actions">
              <button class="back-btn" @click="clearMulti" :disabled="selectedIds.size === 0">清空</button>
              <button class="multi-confirm" :disabled="selectedIds.size === 0" @click="confirmMulti">
                添加 {{ selectedIds.size }} 个弹幕
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.search-dialog-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
  animation: dialog-overlay-in 0.22s ease-out both;
}

.search-dialog {
  background: var(--bg-card);
  border-radius: 20px;
  width: 90%;
  max-width: 620px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
  position: relative;
  overflow: hidden;
  animation: dialog-bounce-in 0.4s cubic-bezier(0.34, 1.56, 0.64, 1) both;
}

@keyframes dialog-bounce-in {
  0% { opacity: 0; transform: scale(0.55) translateY(60px); }
  55% { opacity: 1; transform: scale(1.06) translateY(-12px); }
  75% { transform: scale(0.98) translateY(4px); }
  100% { opacity: 1; transform: scale(1) translateY(0); }
}

@keyframes dialog-overlay-in {
  from { opacity: 0; }
  to { opacity: 1; }
}

/* 成功阶段 */
.search-dialog.phase-success { animation: none; }
.search-dialog.phase-success .search-dialog-header,
.search-dialog.phase-success .search-dialog-body {
  filter: blur(6px);
  opacity: 0.25;
  transition: filter 0.35s ease, opacity 0.35s ease;
  pointer-events: none;
}

.success-layer {
  position: absolute;
  inset: 0;
  z-index: 5;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 14px;
  background: radial-gradient(circle at center,
    rgba(34, 197, 94, 0.12) 0%,
    rgba(34, 197, 94, 0.04) 45%,
    transparent 75%);
  animation: success-fade-in 0.28s ease-out both;
}

@keyframes success-fade-in {
  from { opacity: 0; backdrop-filter: blur(0px); }
  to { opacity: 1; backdrop-filter: blur(4px); }
}

.success-badge {
  width: 76px;
  height: 76px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  background: linear-gradient(135deg, #22c55e, #16a34a);
  box-shadow: 0 8px 30px rgba(34, 197, 94, 0.5), inset 0 0 0 1px rgba(255, 255, 255, 0.2);
  animation: badge-pop 0.4s cubic-bezier(0.34, 1.56, 0.64, 1) 0.05s both;
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
  animation: success-text-in 0.3s ease-out 0.12s both;
}

@keyframes success-text-in {
  from { opacity: 0; transform: translateY(10px); }
  to { opacity: 1; transform: translateY(0); }
}

/* 消失阶段 */
.search-dialog.phase-shrink {
  animation: dialog-shrink-out 0.26s ease-in both;
}

@keyframes dialog-shrink-out {
  0% { opacity: 1; transform: scale(1); filter: blur(0px); }
  100% { opacity: 0; transform: scale(0.78) translateY(24px); filter: blur(6px); }
}
.search-dialog-overlay.overlay-leaving {
  animation: dialog-overlay-out 0.26s ease-in both;
}
@keyframes dialog-overlay-out {
  from { opacity: 1; }
  to { opacity: 0; }
}

.search-dialog-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 20px 24px;
  border-bottom: 1px solid var(--border-light);
}

.search-dialog-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.1rem;
}

.close-btn {
  background: none;
  border: none;
  color: var(--text-muted);
  cursor: pointer;
  padding: 4px;
  border-radius: 6px;
  transition: all 0.2s;
}

.close-btn:hover {
  color: var(--text-primary);
  background: rgba(0, 0, 0, 0.1);
}

.search-dialog-body {
  padding: 20px 24px;
  overflow-y: auto;
  flex: 1;
}

.search-input {
  width: 100%;
  padding: 14px 18px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 12px;
  color: var(--text-primary);
  font-size: 1rem;
  box-sizing: border-box;
  transition: all 0.25s ease;
}

.search-input:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.search-hint {
  font-size: 0.85rem;
  color: var(--text-muted);
  margin: 8px 0 16px;
}

.results-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 10px;
}

.item-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 12px;
  background: var(--bg-primary);
  border: 2px solid var(--border-light);
  border-radius: 12px;
  cursor: pointer;
  transition: all 0.25s ease;
  position: relative;
}

.item-card-selected {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.08);
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.2);
}

.item-check {
  position: absolute;
  top: 6px;
  right: 6px;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  border: 2px solid var(--border-color);
  background: var(--bg-card);
  display: flex;
  align-items: center;
  justify-content: center;
  color: transparent;
  transition: all 0.2s ease;
}

.item-check.checked {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
  color: #fff;
}

.item-card:hover {
  border-color: var(--accent-primary);
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.15);
}

.item-image-wrapper {
  width: 48px;
  height: 48px;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.item-image {
  width: 90%;
  height: 90%;
  object-fit: contain;
  image-rendering: pixelated;
}

.item-info {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 2px;
}

.item-name {
  color: var(--text-primary);
  font-size: 0.8rem;
  font-weight: 600;
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-id {
  color: var(--accent-primary);
  font-size: 0.7rem;
  font-weight: 600;
}

.item-english {
  color: var(--text-muted);
  font-size: 0.65rem;
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.no-results {
  grid-column: 1 / -1;
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.multi-footer {
  position: sticky;
  bottom: 0;
  margin-top: 16px;
  padding: 12px 0 4px;
  background: var(--bg-card);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.multi-count {
  font-size: 0.85rem;
  color: var(--accent-primary);
  font-weight: 600;
}

.multi-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.multi-actions .back-btn { margin-top: 0; }

.multi-confirm {
  padding: 8px 16px;
  border: none;
  border-radius: 9px;
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  color: #fff;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.multi-confirm:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.35);
}

.multi-confirm:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.back-btn {
  margin-top: 12px;
  padding: 8px 16px;
  background: transparent;
  border: 1px solid var(--accent-primary);
  border-radius: 8px;
  color: var(--accent-primary);
  cursor: pointer;
  font-size: 0.85rem;
  transition: all 0.2s;
  align-self: center;
}

.back-btn:hover {
  background: rgba(99, 102, 241, 0.1);
}

/* ═══════ 左右分栏 + 方案面板 ═══════ */
.pick-layout {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.pick-layout.with-plan {
  flex-direction: row;
  align-items: stretch;
  gap: 16px;
}
.pick-left {
  flex: 1;
  min-width: 0;
}
.pick-right {
  width: 240px;
  flex-shrink: 0;
  display: flex;
}
.results-grid-condensed {
  grid-template-columns: repeat(auto-fill, minmax(110px, 1fr));
  gap: 8px;
}

.search-dialog.with-plan {
  max-width: 860px;
}

.plan-panel {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px;
  background: var(--bg-primary);
  border: 1px solid var(--border-light);
  border-radius: 14px;
  align-self: flex-start;
  position: sticky;
  top: 0;
}
.plan-title {
  margin: 0;
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--text-primary);
}
.plan-methods {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}
.plan-method-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 9px 10px;
  border: 1px solid var(--border-light);
  border-radius: 10px;
  background: transparent;
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 500;
  color: var(--text-secondary);
  transition: all 0.2s var(--ease-out);
}
.plan-method-btn:hover {
  border-color: var(--accent-primary);
  color: var(--text-primary);
}
.plan-method-btn.active {
  color: #fff;
  border-color: transparent;
  transform: scale(1.02);
}
.plan-log.active { background: #eab308; }
.plan-kick.active { background: #f97316; }
.plan-ban.active { background: #ef4444; }
.plan-command { color: #8b5cf6; border-color: rgba(139, 92, 246, 0.3); }
.plan-command.active { background: #8b5cf6; color: #fff; }

.plan-command-box {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.plan-command-input {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--border-light);
  border-radius: 8px;
  background: rgba(139, 92, 246, 0.08);
  color: var(--text-primary);
  font-size: 0.75rem;
  font-family: 'SF Mono', 'Consolas', monospace;
  box-sizing: border-box;
}
.plan-command-input:focus {
  outline: none;
  border-color: #8b5cf6;
}
.plan-quick {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
}
.plan-quick-label {
  font-size: 0.7rem;
  color: var(--text-muted);
}
.plan-quick-btn {
  padding: 4px 8px;
  background: rgba(99, 102, 241, 0.1);
  border: 1px solid rgba(99, 102, 241, 0.2);
  border-radius: 6px;
  font-size: 0.68rem;
  color: var(--accent-primary);
  cursor: pointer;
  transition: all 0.2s;
}
.plan-quick-btn:hover {
  background: rgba(99, 102, 241, 0.2);
}

.plan-summary {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px;
  background: rgba(99, 102, 241, 0.06);
  border: 1px solid rgba(99, 102, 241, 0.15);
  border-radius: 10px;
}
.plan-summary-count {
  font-size: 0.78rem;
  font-weight: 700;
  color: var(--accent-primary);
}
.plan-summary-method {
  font-size: 0.72rem;
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 640px) {
  .pick-layout.with-plan {
    flex-direction: column;
  }
  .pick-right {
    width: 100%;
  }
}
</style>

<script setup>
import { ref, computed, watch, onMounted } from 'vue'

const inputText = ref('')
const outputCode = ref('')
const selectedColorIndex = ref(0)
const toastMessage = ref('')
const showToast = ref(false)

const colors = ref([
  { id: 1, r: 255, g: 0, b: 0 },
  { id: 2, r: 255, g: 165, b: 0 },
  { id: 3, r: 255, g: 255, b: 0 },
  { id: 4, r: 0, g: 255, b: 0 },
  { id: 5, r: 0, g: 0, b: 255 },
  { id: 6, r: 75, g: 0, b: 130 },
  { id: 7, r: 148, g: 0, b: 211 }
])

const presets = [
  { name: 'Rainbow', colors: [
    { r: 255, g: 0, b: 0 },
    { r: 255, g: 165, b: 0 },
    { r: 255, g: 255, b: 0 },
    { r: 0, g: 255, b: 0 },
    { r: 0, g: 0, b: 255 },
    { r: 75, g: 0, b: 130 },
    { r: 148, g: 0, b: 211 }
  ]},
  { name: 'Heat Map', colors: [
    { r: 0, g: 0, b: 255 },
    { r: 0, g: 255, b: 255 },
    { r: 0, g: 255, b: 0 },
    { r: 255, g: 255, b: 0 },
    { r: 255, g: 128, b: 0 },
    { r: 255, g: 0, b: 0 }
  ]},
  { name: 'Terminal', colors: [
    { r: 0, g: 0, b: 0 },
    { r: 0, g: 128, b: 0 },
    { r: 0, g: 255, b: 0 }
  ]},
  { name: 'Mantle', colors: [
    { r: 255, g: 255, b: 255 },
    { r: 200, g: 200, b: 200 },
    { r: 150, g: 150, b: 150 },
    { r: 100, g: 100, b: 100 },
    { r: 50, g: 50, b: 50 },
    { r: 0, g: 0, b: 0 }
  ]},
  { name: 'Combi', colors: [
    { r: 255, g: 0, b: 128 },
    { r: 128, g: 0, b: 255 },
    { r: 0, g: 128, b: 255 },
    { r: 0, g: 255, b: 128 },
    { r: 128, g: 255, b: 0 },
    { r: 255, g: 128, b: 0 }
  ]}
]

const selectedPreset = ref('Rainbow')

const selectedColor = computed(() => {
  if (colors.value.length === 0) return { id: 0, r: 0, g: 0, b: 0 }
  return colors.value[selectedColorIndex.value] || colors.value[0]
})

const hexValue = computed({
  get: () => {
    const c = selectedColor.value
    return '#' + [c.r, c.g, c.b].map(x => x.toString(16).padStart(2, '0')).join('')
  },
  set: (val) => {
    if (val.length === 7 && val.startsWith('#')) {
      const r = parseInt(val.slice(1, 3), 16)
      const g = parseInt(val.slice(3, 5), 16)
      const b = parseInt(val.slice(5, 7), 16)
      if (!isNaN(r) && !isNaN(g) && !isNaN(b)) {
        colors.value[selectedColorIndex.value].r = r
        colors.value[selectedColorIndex.value].g = g
        colors.value[selectedColorIndex.value].b = b
      }
    }
  }
})

const inputCharCount = computed(() => inputText.value.length)
const outputCharCount = computed(() => outputCode.value.length)
const colorCount = computed(() => colors.value.length)

const gradientPreviewStyle = computed(() => {
  if (colors.value.length === 0) return 'background: #ccc'
  const stops = colors.value.map(c => {
    const hex = '#' + [c.r, c.g, c.b].map(x => x.toString(16).padStart(2, '0')).join('')
    return hex
  })
  return `background: linear-gradient(to right, ${stops.join(', ')})`
})

const interpolateColor = (color1, color2, factor) => {
  const r = Math.round(color1.r + (color2.r - color1.r) * factor)
  const g = Math.round(color1.g + (color2.g - color1.g) * factor)
  const b = Math.round(color1.b + (color2.b - color1.b) * factor)
  return { r, g, b }
}

const getGradientColor = (position, total) => {
  if (colors.value.length === 0) return { r: 128, g: 128, b: 128 }
  if (colors.value.length === 1) return colors.value[0]
  
  const segmentSize = total / (colors.value.length - 1)
  const segmentIndex = Math.floor(position / segmentSize)
  const localPosition = position - segmentIndex * segmentSize
  const factor = localPosition / segmentSize
  
  if (segmentIndex >= colors.value.length - 1) return colors.value[colors.value.length - 1]
  
  return interpolateColor(colors.value[segmentIndex], colors.value[segmentIndex + 1], factor)
}

const generateGradientText = () => {
  if (!inputText.value) {
    outputCode.value = ''
    return
  }
  
  const chars = inputText.value.split('')
  const result = chars.map((char, index) => {
    const color = getGradientColor(index, chars.length - 1 || 1)
    const hex = '#' + [color.r, color.g, color.b].map(x => x.toString(16).padStart(2, '0')).join('')
    return `[c/${hex}:${char}]`
  }).join('')
  
  outputCode.value = result
}

const previewChars = computed(() => {
  if (!inputText.value) return []
  
  const chars = inputText.value.split('')
  return chars.map((char, index) => {
    const color = getGradientColor(index, chars.length - 1 || 1)
    const hex = '#' + [color.r, color.g, color.b].map(x => x.toString(16).padStart(2, '0')).join('')
    return { char, color: hex }
  })
})

const addColor = () => {
  const newId = colors.value.length > 0 ? Math.max(...colors.value.map(c => c.id)) + 1 : 1
  const lastColor = colors.value.length > 0 ? colors.value[colors.value.length - 1] : { r: 255, g: 255, b: 255 }
  colors.value.push({ id: newId, r: lastColor.r, g: lastColor.g, b: lastColor.b })
  selectedColorIndex.value = colors.value.length - 1
}

const deleteColor = () => {
  if (colors.value.length <= 1) return
  colors.value.splice(selectedColorIndex.value, 1)
  if (selectedColorIndex.value >= colors.value.length) {
    selectedColorIndex.value = colors.value.length - 1
  }
}

const changeColor = () => {
  const c = colors.value[selectedColorIndex.value]
  c.r = Math.floor(Math.random() * 256)
  c.g = Math.floor(Math.random() * 256)
  c.b = Math.floor(Math.random() * 256)
}

const clearAll = () => {
  colors.value = [{ id: 1, r: 128, g: 128, b: 128 }]
  selectedColorIndex.value = 0
  inputText.value = ''
  outputCode.value = ''
}

const reverseGradient = () => {
  colors.value.reverse()
  colors.value.forEach((c, i) => c.id = i + 1)
}

const applyPreset = (presetName) => {
  const preset = presets.find(p => p.name === presetName)
  if (preset) {
    colors.value = preset.colors.map((c, i) => ({ id: i + 1, r: c.r, g: c.g, b: c.b }))
    selectedColorIndex.value = 0
  }
}

const selectColor = (index) => {
  selectedColorIndex.value = index
}

const updateRGB = (channel, value) => {
  const num = parseInt(value) || 0
  const clamped = Math.max(0, Math.min(255, num))
  colors.value[selectedColorIndex.value][channel] = clamped
}

const copyToClipboard = async (text) => {
  try {
    await navigator.clipboard.writeText(text)
    showToastMessage('已复制到剪贴板')
  } catch {
    showToastMessage('复制失败')
  }
}

const pasteFromClipboard = async () => {
  try {
    const text = await navigator.clipboard.readText()
    inputText.value = text
    showToastMessage('已粘贴')
  } catch {
    showToastMessage('粘贴失败')
  }
}

const showToastMessage = (msg) => {
  toastMessage.value = msg
  showToast.value = true
  setTimeout(() => {
    showToast.value = false
  }, 2000)
}

watch(inputText, generateGradientText)
watch(colors, generateGradientText, { deep: true })

onMounted(() => {
  generateGradientText()
})
</script>

<template>
  <div class="gradient-text-page">
    <div class="page-header">
      <h2>彩色渐变文字生成器</h2>
      <p class="page-desc">生成泰拉瑞亚游戏内可用的彩色渐变文字代码</p>
    </div>

    <div class="main-content">
      <div class="top-section">
        <div class="input-panel">
          <div class="panel-header">
            <span class="panel-title">输入文本</span>
            <span class="char-count">{{ inputCharCount }} 字符</span>
          </div>
          <textarea 
            v-model="inputText"
            class="input-textarea"
            placeholder="输入要转换的文字..."
          ></textarea>
          <div class="input-actions">
            <button class="action-btn" @click="pasteFromClipboard">
              <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
              </svg>
              粘贴
            </button>
            <button class="action-btn" @click="copyToClipboard(inputText)">
              <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
              </svg>
              复制输入
            </button>
          </div>
        </div>

        <div class="output-panel">
          <div class="preview-section">
            <div class="panel-header">
              <span class="panel-title">彩色预览</span>
            </div>
            <div class="preview-area">
              <span 
                v-for="(item, index) in previewChars" 
                :key="index"
                :style="{ color: item.color }"
              >{{ item.char }}</span>
            </div>
          </div>
          
          <div class="code-section">
            <div class="panel-header">
              <span class="panel-title">输出代码</span>
              <span class="char-count">{{ outputCharCount }} 字符</span>
            </div>
            <textarea 
              v-model="outputCode"
              class="output-textarea"
              readonly
              placeholder="生成的彩色代码..."
            ></textarea>
            <div class="output-actions">
              <button class="action-btn primary" @click="copyToClipboard(outputCode)">
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                  <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
                </svg>
                复制结果
              </button>
            </div>
          </div>
        </div>
      </div>

      <div class="gradient-preview-bar">
        <div class="gradient-bar" :style="gradientPreviewStyle"></div>
      </div>

      <div class="bottom-section">
        <div class="color-info-panel">
          <div class="panel-header">
            <span class="panel-title">颜色信息</span>
            <span class="color-count">共 {{ colorCount }} 个颜色</span>
          </div>
          
          <div class="color-preview-box" :style="{ background: hexValue }"></div>
          
          <div class="color-inputs">
            <div class="input-group">
              <label>ID</label>
              <input 
                type="number" 
                :value="selectedColor.id"
                min="1"
                class="color-input id-input"
                readonly
              />
            </div>
            
            <div class="rgb-inputs">
              <div class="input-group">
                <label>R</label>
                <input 
                  type="number" 
                  :value="selectedColor.r"
                  min="0"
                  max="255"
                  class="color-input"
                  @input="updateRGB('r', $event.target.value)"
                />
              </div>
              <div class="input-group">
                <label>G</label>
                <input 
                  type="number" 
                  :value="selectedColor.g"
                  min="0"
                  max="255"
                  class="color-input"
                  @input="updateRGB('g', $event.target.value)"
                />
              </div>
              <div class="input-group">
                <label>B</label>
                <input 
                  type="number" 
                  :value="selectedColor.b"
                  min="0"
                  max="255"
                  class="color-input"
                  @input="updateRGB('b', $event.target.value)"
                />
              </div>
            </div>
            
            <div class="input-group hex-group">
              <label>HEX</label>
              <input 
                type="text" 
                v-model="hexValue"
                class="color-input hex-input"
                maxlength="7"
              />
            </div>
          </div>
          
          <div class="color-list">
            <div 
              v-for="(color, index) in colors"
              :key="color.id"
              class="color-item"
              :class="{ selected: index === selectedColorIndex }"
              :style="{ background: '#' + [color.r, color.g, color.b].map(x => x.toString(16).padStart(2, '0')).join('') }"
              @click="selectColor(index)"
            ></div>
          </div>
        </div>

        <div class="control-buttons">
          <button class="control-btn" @click="addColor">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"></line>
              <line x1="5" y1="12" x2="19" y2="12"></line>
            </svg>
            添加颜色
          </button>
          <button class="control-btn" @click="deleteColor" :disabled="colors.length <= 1">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="5" y1="12" x2="19" y2="12"></line>
            </svg>
            删除颜色
          </button>
          <button class="control-btn" @click="changeColor">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21.5 2v6h-6M2.5 22v-6h6M2 11.5a10 10 0 0 1 18.5-4M22 12.5a10 10 0 0 1-18.5 4"></path>
            </svg>
            更换颜色
          </button>
          <button class="control-btn danger" @click="clearAll">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="3 6 5 6 21 6"></polyline>
              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
            </svg>
            清除所有
          </button>
          <button class="control-btn" @click="reverseGradient">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="17 1 21 5 17 9"></polyline>
              <path d="M3 11V9a4 4 0 0 1 4-4h14"></path>
              <polyline points="7 23 3 19 7 15"></polyline>
              <path d="M21 13v2a4 4 0 0 1-4 4H3"></path>
            </svg>
            反向渐变
          </button>
        </div>

        <div class="preset-panel">
          <div class="panel-header">
            <span class="panel-title">渐变预设</span>
          </div>
          <select 
            v-model="selectedPreset"
            class="preset-select"
            @change="applyPreset(selectedPreset)"
          >
            <option v-for="preset in presets" :key="preset.name" :value="preset.name">
              {{ preset.name }}
            </option>
          </select>
        </div>
      </div>
    </div>

    <div v-if="showToast" class="toast">
      {{ toastMessage }}
    </div>
  </div>
</template>

<style scoped>
.gradient-text-page {
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

.page-desc {
  margin: 8px 0 0;
  color: var(--text-muted);
  font-size: 0.9rem;
}

.main-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.top-section {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

.input-panel,
.output-panel {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 16px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.panel-title {
  color: var(--text-primary);
  font-weight: 600;
  font-size: 0.95rem;
}

.char-count {
  color: var(--text-muted);
  font-size: 0.85rem;
}

.input-textarea,
.output-textarea {
  width: 100%;
  min-height: 120px;
  padding: 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  resize: vertical;
  transition: border-color 0.25s ease;
}

.input-textarea:focus,
.output-textarea:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.input-textarea::placeholder,
.output-textarea::placeholder {
  color: var(--text-muted);
}

.output-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.preview-section,
.code-section {
  flex: 1;
}

.preview-area {
  width: 100%;
  min-height: 60px;
  padding: 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  font-size: 1.1rem;
  line-height: 1.6;
  word-wrap: break-word;
  white-space: pre-wrap;
  font-weight: 500;
}

.output-textarea {
  min-height: 80px;
}

.input-actions,
.output-actions {
  display: flex;
  gap: 8px;
  margin-top: 12px;
}

.action-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.25s ease;
}

.action-btn:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.action-btn.primary {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  border-color: var(--accent-primary);
  color: white;
}

.action-btn.primary:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.4);
}

.gradient-preview-bar {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 16px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.gradient-bar {
  height: 24px;
  border-radius: var(--radius-md);
  width: 100%;
}

.bottom-section {
  display: grid;
  grid-template-columns: 280px 180px 1fr;
  gap: 20px;
}

.color-info-panel,
.control-buttons,
.preset-panel {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 16px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.color-preview-box {
  width: 100%;
  height: 48px;
  border-radius: var(--radius-md);
  margin-bottom: 16px;
  border: 2px solid var(--border-color);
}

.color-inputs {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.input-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.input-group label {
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-weight: 500;
  min-width: 32px;
}

.color-input {
  flex: 1;
  padding: 8px 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  transition: border-color 0.25s ease;
}

.color-input:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.id-input {
  max-width: 60px;
  background: var(--bg-secondary);
  cursor: not-allowed;
}

.rgb-inputs {
  display: flex;
  gap: 8px;
}

.rgb-inputs .input-group {
  flex: 1;
}

.rgb-inputs .color-input {
  width: 100%;
}

.hex-input {
  text-transform: uppercase;
}

.color-count {
  color: var(--accent-primary);
  font-size: 0.85rem;
  font-weight: 500;
}

.color-list {
  display: flex;
  gap: 6px;
  margin-top: 16px;
  flex-wrap: wrap;
}

.color-item {
  width: 28px;
  height: 28px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  border: 2px solid transparent;
  transition: all 0.2s ease;
}

.color-item:hover {
  transform: scale(1.1);
}

.color-item.selected {
  border-color: var(--text-primary);
  box-shadow: 0 0 0 2px var(--bg-card);
}

.control-buttons {
  display: flex;
  flex-direction: column;
  gap: 10px;
  justify-content: center;
}

.control-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 10px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.25s ease;
}

.control-btn:hover:not(:disabled) {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.control-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.control-btn.danger {
  border-color: rgba(239, 68, 68, 0.3);
}

.control-btn.danger:hover {
  border-color: #ef4444;
  color: #ef4444;
}

.preset-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.preset-select {
  width: 100%;
  padding: 10px 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  cursor: pointer;
  transition: border-color 0.25s ease;
}

.preset-select:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.toast {
  position: fixed;
  bottom: 80px;
  left: 50%;
  transform: translateX(-50%);
  padding: 12px 24px;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  box-shadow: var(--shadow-lg);
  z-index: 1000;
  animation: fadeIn 0.3s ease;
}

@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateX(-50%) translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateX(-50%) translateY(0);
  }
}
</style>
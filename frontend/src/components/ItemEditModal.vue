<script setup>
import { ref, computed, watch } from 'vue'

const props = defineProps({
  show: {
    type: Boolean,
    default: false
  },
  mode: {
    type: String,
    default: 'give'
  },
  slotIndex: {
    type: Number,
    default: -1
  },
  initialItemId: {
    type: Number,
    default: 0
  },
  initialStack: {
    type: Number,
    default: 1
  },
  initialPrefix: {
    type: Number,
    default: 0
  },
  title: {
    type: String,
    default: '编辑物品'
  }
})

const emit = defineEmits(['close', 'submit'])

const itemId = ref(props.initialItemId || 0)
const stack = ref(props.initialStack || 1)
const prefixId = ref('')
const loading = ref(false)
const error = ref('')
const success = ref('')
const warning = ref('')
const showPrefixDropdown = ref(false)

const prefixMap = {
  '大': 1, '巨大': 2, '危险': 3, '凶残': 4, '锋利': 5, '尖锐': 6, '微小': 7, '可怕': 8,
  '小': 9, '钝': 10, '倒霉': 11, '笨重': 12, '可耻': 13, '重': 14, '轻': 15, '精准': 16,
  '迅速': 17, '急速': 18, '恐怖': 19, '致命': 20, '可靠': 21, '讨厌': 22, '无力': 23,
  '粗笨': 24, '强大': 25, '神秘': 26, '精巧': 27, '精湛': 28, '笨拙': 29, '无知': 30,
  '错乱': 31, '威猛': 32, '禁忌': 33, '天界': 34, '狂怒': 35, '锐利': 36, '高端': 37,
  '强力': 38, '碎裂': 39, '破损': 40, '粗劣': 41, '迅捷': 42, '灵活': 43, '灵巧': 44,
  '残暴': 45, '缓慢': 46, '迟钝': 47, '呆滞': 48, '惹恼': 49, '凶险': 50, '狂躁': 51,
  '致伤': 52, '强劲': 53, '粗鲁': 54, '虚弱': 55, '无情': 56, '暴怒': 57, '神级': 58,
  '恶魔': 59, '狂热': 60, '坚硬': 61, '守护': 62, '装甲': 63, '护佑': 64, '奥秘': 65,
  '精确': 66, '幸运': 67, '锯齿': 68, '尖刺': 69, '愤怒': 70, '险恶': 71, '轻快': 72,
  '快速': 73, '弹道': 74, '虬结': 75
}

watch(() => props.show, (newVal) => {
  if (newVal) {
    itemId.value = props.initialItemId || 0
    stack.value = props.initialStack || 1
    prefixId.value = props.initialPrefix ? Object.keys(prefixMap).find(k => prefixMap[k] === props.initialPrefix) || '' : ''
    error.value = ''
    success.value = ''
    warning.value = ''
    showPrefixDropdown.value = false
  }
})

const itemImageUrl = computed(() => {
  if (!itemId.value || isNaN(parseInt(itemId.value))) return null
  return `/assets/img/img/Item_${itemId.value}.png`
})

const filteredPrefixes = computed(() => {
  const input = prefixId.value.trim()
  if (!input) return []
  const isNumericInput = /^\d+$/.test(input)
  return Object.entries(prefixMap)
    .filter(([name, id]) => {
      if (isNumericInput) {
        return String(id).includes(input)
      }
      return name.includes(input)
    })
    .map(([name, id]) => ({ name, id }))
})

const selectPrefix = (prefix) => {
  prefixId.value = prefix.name
  showPrefixDropdown.value = false
}

const getPrefixIdValue = () => {
  const input = prefixId.value.trim()
  if (!input) return 0
  if (prefixMap[input] !== undefined) {
    return prefixMap[input]
  }
  if (/^\d+$/.test(input)) {
    return parseInt(input)
  }
  return 0
}

const handleSubmit = () => {
  if (!itemId.value || itemId.value <= 0) {
    error.value = '请输入有效的物品ID'
    return
  }

  const prefixValue = getPrefixIdValue()
  if (prefixId.value.trim() && prefixValue === 0 && !/^\d+$/.test(prefixId.value.trim())) {
    warning.value = `前缀"${prefixId.value}"无法识别，将使用默认值0`
  }

  emit('submit', {
    itemId: parseInt(itemId.value),
    stack: parseInt(stack.value),
    prefix: prefixValue,
    slotIndex: props.slotIndex
  })
}

const close = () => {
  emit('close')
}
</script>

<template>
  <div v-if="show" class="modal-overlay" @click.self="close">
    <div class="modal">
      <div class="modal-header">
        <h3>{{ title }}</h3>
        <button @click="close" class="close-btn">×</button>
      </div>
      <div class="modal-body">
        <div class="edit-form">
          <div class="form-row">
            <label>物品ID</label>
            <div class="item-input-row">
              <input
                v-model="itemId"
                type="number"
                min="0"
                placeholder="输入物品ID"
                class="form-input"
              />
              <div v-if="itemImageUrl" class="item-preview">
                <img
                  :src="itemImageUrl"
                  :alt="'Item ' + itemId"
                  class="item-image"
                  @error="(e) => e.target.style.display = 'none'"
                />
              </div>
            </div>
          </div>
          <div class="form-row">
            <label>堆叠数</label>
            <input
              v-model="stack"
              type="number"
              min="1"
              class="form-input"
            />
          </div>
          <div class="form-row">
            <label>前缀ID</label>
            <input
              v-model="prefixId"
              type="text"
              placeholder="可选，默认无，可输入中文搜索"
              class="form-input"
              @input="warning = ''; showPrefixDropdown = true"
              @focus="showPrefixDropdown = true"
              @blur="setTimeout(() => showPrefixDropdown = false, 150)"
            />
            <div v-if="showPrefixDropdown && filteredPrefixes.length > 0" class="prefix-dropdown">
              <div
                v-for="prefix in filteredPrefixes"
                :key="prefix.id"
                class="prefix-option"
                @click="selectPrefix(prefix)"
              >
                <span class="prefix-name">{{ prefix.name }}</span>
                <span class="prefix-id">ID: {{ prefix.id }}</span>
              </div>
            </div>
          </div>
        </div>

        <div v-if="error" class="edit-error">
          {{ error }}
        </div>
        <div v-if="warning" class="edit-warning">
          {{ warning }}
        </div>
        <div v-if="success" class="edit-success">
          {{ success }}
        </div>
      </div>
      <div class="modal-footer">
        <button @click="close" class="cancel-btn">取消</button>
        <button @click="handleSubmit" :disabled="loading" class="submit-btn">
          {{ loading ? '执行中...' : '确认' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.7);
  backdrop-filter: blur(8px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  width: 90%;
  max-width: 400px;
  max-height: 85vh;
  overflow: hidden;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
  animation: modalIn 0.25s ease;
}

@keyframes modalIn {
  from {
    opacity: 0;
    transform: scale(0.95) translateY(-20px);
  }
  to {
    opacity: 1;
    transform: scale(1) translateY(0);
  }
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px;
  background: var(--bg-tertiary);
  border-bottom: 1px solid var(--border-light);
}

.modal-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.25rem;
  font-weight: 600;
}

.close-btn {
  background: var(--bg-hover);
  border: none;
  color: var(--text-secondary);
  font-size: 1.25rem;
  cursor: pointer;
  padding: 6px 10px;
  border-radius: var(--radius-sm);
  transition: all 0.2s ease;
  line-height: 1;
}

.close-btn:hover {
  color: var(--text-primary);
  background: var(--bg-card);
}

.modal-body {
  padding: 24px;
}

.edit-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
  position: relative;
}

.form-row label {
  color: var(--text-secondary);
  font-weight: 600;
  font-size: 0.9rem;
}

.item-input-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.form-input {
  flex: 1;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  transition: all 0.25s ease;
}

.form-input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.form-input::placeholder {
  color: var(--text-muted);
}

.item-preview {
  width: 44px;
  height: 44px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
}

.item-image {
  width: 85%;
  height: 85%;
  object-fit: contain;
  image-rendering: pixelated;
}

.prefix-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  background: var(--bg-card);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  margin-top: 4px;
  max-height: 200px;
  overflow-y: auto;
  z-index: 10;
  box-shadow: var(--shadow-md);
}

.prefix-option {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  cursor: pointer;
  transition: background 0.2s ease;
  border-bottom: 1px solid var(--border-light);
}

.prefix-option:last-child {
  border-bottom: none;
}

.prefix-option:hover {
  background: var(--bg-hover);
}

.prefix-name {
  color: var(--text-primary);
  font-weight: 500;
}

.prefix-id {
  color: var(--text-muted);
  font-size: 0.85rem;
}

.edit-error {
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border-radius: var(--radius-md);
  margin-top: 16px;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.edit-warning {
  padding: 12px 16px;
  background: rgba(234, 179, 8, 0.15);
  color: #ca8a04;
  border-radius: var(--radius-md);
  margin-top: 16px;
  border: 1px solid rgba(234, 179, 8, 0.3);
}

.edit-success {
  padding: 12px 16px;
  background: rgba(34, 197, 94, 0.15);
  color: var(--accent-secondary);
  border-radius: var(--radius-md);
  margin-top: 16px;
  border: 1px solid rgba(34, 197, 94, 0.3);
}

.modal-footer {
  padding: 16px 24px;
  background: var(--bg-tertiary);
  border-top: 1px solid var(--border-light);
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

.cancel-btn {
  padding: 12px 24px;
  background: var(--bg-hover);
  color: var(--text-primary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 500;
  transition: all 0.25s ease;
}

.cancel-btn:hover {
  background: var(--bg-card);
  border-color: var(--accent-primary);
}

.submit-btn {
  padding: 12px 24px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.submit-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}

.submit-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>